using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Kashif.Device;

/// <summary>
/// الجهاز لا يقلع: في أي وضع يراه ويندوز على الكيبل؟ رمز المنتج في USB لشركة Apple (VID 05AC):
/// 0x1280–0x1283 وضع الاستعادة (iBoot)، 0x1227 وضع DFU، 0x1290–0x12AF جهاز يعمل بالنظام
/// (المصدر: libirecovery من libimobiledevice، و The Apple Wiki «USB Product IDs»).
/// </summary>
public static class BootMode
{
    public enum Mode { None, Normal, Recovery, Dfu }

    static readonly Regex Pnp = new(@"VID_05AC&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);

    /// <summary>أهم وضع بين أجهزة Apple الموصولة (Recovery/DFU أولًا لأنها سبب الفحص)</summary>
    public static Mode FromPnpIds(IEnumerable<string> instanceIds)
    {
        var modes = new List<Mode>();
        foreach (var id in instanceIds ?? Enumerable.Empty<string>())
        {
            if (Pnp.Match(id ?? "") is not { Success: true } m) continue;
            int pid = Convert.ToInt32(m.Groups[1].Value, 16);
            if (pid == 0x1227) modes.Add(Mode.Dfu);
            else if (pid is >= 0x1280 and <= 0x1283) modes.Add(Mode.Recovery);
            else if (pid is >= 0x1290 and <= 0x12AF) modes.Add(Mode.Normal);
        }
        return modes.Contains(Mode.Dfu) ? Mode.Dfu : modes.Contains(Mode.Recovery) ? Mode.Recovery : modes.Contains(Mode.Normal) ? Mode.Normal : Mode.None;
    }

    /// <summary>أجهزة USB الحاضرة من Apple في ويندوز (Get-PnpDevice)، أو null إن تعذر السؤال</summary>
    public static List<string> QueryWindows()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -Command \"Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -like 'USB\\VID_05AC*' } | ForEach-Object { $_.InstanceId }\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return null; }
            var ids = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            DeviceTrace.Log($"ويندوز: {ids.Count} جهاز USB من Apple");
            return ids;
        }
        catch (Exception ex) { DeviceTrace.Error("Get-PnpDevice", ex); return null; }
    }

    /// <summary>العنوان والخطوات لكل وضع؛ usbmuxSees: خدمة Apple ترى الجهاز (يعمل بالنظام وموثوق)</summary>
    public static (string Title, string[] Steps) Guidance(Mode mode, bool usbmuxSees) => mode switch
    {
        Mode.Normal when usbmuxSees => ("الجهاز يعمل بالنظام ويُرى على الكيبل", new[]
        {
            "استخدم «من الآيفون» لسحب سجلات البانك، و«فحص الجهاز بالكيبل» للبطارية والشحن.",
        }),
        Mode.Normal => ("ويندوز يرى آيفون، لكن خدمة Apple لا تتصل به", new[]
        {
            "افتح قفل الشاشة واضغط «ثق بهذا الكمبيوتر» إن ظهر.",
            "ثبّت iTunes أو تطبيق Apple Devices، أو أعد تشغيله، ثم أعد المحاولة.",
        }),
        Mode.Recovery => ("الجهاز في وضع الاستعادة (Recovery)", new[]
        {
            "الجهاز يصل إلى مرحلة iBoot: المعالج والذاكرة يعملان إلى هذا الحد.",
            "افتح iTunes أو Apple Devices واختر «تحديث» (Update): يعيد تثبيت iOS دون مسح البيانات.",
            "إن فشل التحديث: «استعادة» (Restore) — تمسح البيانات.",
            "إن ظهر رقم خطأ عند الاستعادة فاكتبه: يدل على نوع العطل.",
        }),
        Mode.Dfu => ("الجهاز في وضع DFU (الشاشة سوداء)", new[]
        {
            "أدنى وضع إقلاع: يتعرف عليه الكمبيوتر حتى لو تلف النظام.",
            "المتاح فيه «استعادة» (Restore) فقط، وتمسح البيانات.",
            "إن فشلت الاستعادة بكيبل وكمبيوتر سليمين، فالعطل غالبًا في البوردة (الذاكرة أو المعالج أو دائرة الطاقة) — اكتب رقم الخطأ.",
        }),
        _ => ("لا يظهر أي جهاز Apple على الكيبل", new[]
        {
            "جرّب كيبلًا أصليًا ومنفذ USB آخر في الكمبيوتر.",
            "اشحن الجهاز 15 دقيقة على شاحن سليم، ونظّف منفذ الشحن من الأوساخ والتأكسد.",
            "جرّب الدخول إلى وضع الاستعادة يدويًا بأزرار الموديل، ثم أعد الفحص.",
            "إن لم يتعرف عليه الكمبيوتر حتى في DFU: العطل في منفذ الشحن أو فلاتته أو دائرة الطاقة في البوردة.",
        }),
    };
}
