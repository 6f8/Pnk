using System.Net.Sockets;

namespace Kashif.Device;

/// <summary>ملف سجل من الجهاز</summary>
public sealed record DeviceLog(string Name, string Text);

/// <summary>نتيجة سحب السجلات: اسم الجهاز وموديله ونظامه، والسجلات</summary>
/// <param name="Failed">سجلات تعذرت قراءتها</param>
/// <param name="Older">سجلات أقدم لم تُسحب (يُسحب أحدث <see cref="CrashReports.MaxLogs"/> فقط)</param>
public sealed record DevicePull(string DeviceName, string ProductType, string Version, string Udid, List<DeviceLog> Logs, int Failed, int Older);

/// <summary>
/// سحب سجلات البانك من الآيفون الموصول بالكيبل — نفس ما يفعله iTunes عند المزامنة:
/// usbmuxd ← lockdownd (جلسة موثوقة) ← com.apple.crashreportmover (ينقل السجلات الجديدة ويرد «ping»)
/// ← com.apple.crashreportcopymobile (قراءة الملفات عبر AFC). قراءة فقط: لا يُحذف شيء من الجهاز.
/// </summary>
public static class CrashReports
{
    public const string Mover = "com.apple.crashreportmover", Copy = "com.apple.crashreportcopymobile";
    public const int MaxLogs = 40;

    /// <summary>سجل بانك؟ panic-full أولًا (فيه كل شيء)، و panic-base فقط إن لم يوجد full</summary>
    public static bool IsPanic(string name) =>
        name.StartsWith("panic-full", StringComparison.OrdinalIgnoreCase) || name.StartsWith("panic-base", StringComparison.OrdinalIgnoreCase);

    public static DevicePull Pull(Usbmux mux = null, string udid = null, IProgress<string> progress = null)
    {
        mux ??= new Usbmux();
        progress?.Report("البحث عن الآيفون…");
        var devices = mux.ListDevices();
        if (devices.Count == 0) throw new DeviceException("لا يوجد آيفون موصول. صِل الجهاز بالكيبل وافتح قفل شاشته، ثم أعد المحاولة.");
        var dev = udid == null ? devices[0] : devices.FirstOrDefault(d => d.Udid == udid) ?? throw new DeviceException("الجهاز المحدد لم يعد موصولًا.");

        var pair = mux.ReadPairRecord(dev.Udid);
        string name, product, version;
        int moverPort, copyPort;
        bool moverSsl, copySsl;
        progress?.Report("فتح جلسة مع الآيفون…");
        using (var ld = new Lockdown(mux.Connect(dev.DeviceId, Lockdown.Port), pair))
        {
            ld.StartSession();
            name = ld.GetValue("DeviceName");
            product = ld.GetValue("ProductType");
            version = ld.GetValue("ProductVersion");
            (moverPort, moverSsl) = ld.StartService(Mover);
            progress?.Report("تجهيز سجلات الأعطال في الآيفون…");
            using (var mover = mux.Connect(dev.DeviceId, moverPort))
            {
                Stream ms = mover.GetStream();
                if (moverSsl) ms = Lockdown.Tls(ms, pair);
                // الخدمة تنقل السجلات ثم ترسل «ping»؛ إن لم ترسل لا نتوقف: الملفات الموجودة تُقرأ على أي حال
                try { Io.ReadExact(ms, 4); } catch (DeviceException) { } catch (IOException) { }
            }
            (copyPort, copySsl) = ld.StartService(Copy);
        }

        progress?.Report("قراءة سجلات البانك…");
        using var copy = mux.Connect(dev.DeviceId, copyPort);
        Stream cs = copy.GetStream();
        if (copySsl) cs = Lockdown.Tls(cs, pair);
        var afc = new Afc(cs);
        var found = new List<string>();
        Collect(afc, "/", found, 0);
        // panic-full يكفي؛ panic-base فقط عند غياب full. الأحدث أولًا (الاسم يحمل التاريخ)
        var full = found.Where(p => Path.GetFileName(p).StartsWith("panic-full", StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = full.Count > 0 ? full : found;
        var chosen = pool.OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal).Take(MaxLogs).ToList();
        var logs = new List<DeviceLog>();
        int failed = 0;
        for (int i = 0; i < chosen.Count; i++)
        {
            progress?.Report($"قراءة السجل {i + 1} من {chosen.Count}…");
            try { logs.Add(new DeviceLog(Path.GetFileName(chosen[i]), System.Text.Encoding.UTF8.GetString(afc.ReadFile(chosen[i])))); }
            catch (AfcException) { failed++; }
        }
        return new DevicePull(name, product, version, dev.Udid, logs, failed, pool.Count - chosen.Count);
    }

    /// <summary>الملفات في الجذر ومجلداته الفرعية (حتى مستويين، مثل Retired)</summary>
    static void Collect(Afc afc, string dir, List<string> found, int depth)
    {
        List<string> names;
        try { names = afc.ReadDirectory(dir); }
        catch (AfcException) { return; }
        foreach (var n in names)
        {
            var path = dir == "/" ? "/" + n : dir + "/" + n;
            if (IsPanic(n)) { found.Add(path); continue; }
            if (depth >= 2 || n.Contains('.')) continue;
            try
            {
                if (afc.FileInfo(path).TryGetValue("st_ifmt", out var t) && t == "S_IFDIR") Collect(afc, path, found, depth + 1);
            }
            catch (AfcException) { }
        }
    }
}
