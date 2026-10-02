using System.Net.Sockets;

namespace Kashif.Device;

/// <summary>صحة البطارية كما يقيسها الجهاز نفسه</summary>
/// <param name="DesignMah">السعة الأصلية (مللي أمبير ساعة)</param>
/// <param name="FullMah">السعة الكاملة الحالية</param>
public sealed record BatteryHealth(int CycleCount, int DesignMah, int FullMah, double? TemperatureC)
{
    /// <summary>السعة الحالية نسبةً إلى الأصلية (قريبة من «السعة القصوى» في إعدادات الآيفون، وقد تختلف ببضع درجات)، أو -1</summary>
    public int Percent => DesignMah > 0 && FullMah > 0 ? (int)Math.Round(100.0 * FullMah / DesignMah) : -1;
    public bool Valid => Percent > 0 && Percent <= 150;
    public override string ToString() =>
        $"{Percent}% من السعة الأصلية ({FullMah} من {DesignMah} mAh) · {CycleCount} دورة شحن" + (TemperatureC is double t ? $" · {t:0.#}°" : "");
}

/// <summary>
/// قراءة البطارية عبر خدمة التشخيص com.apple.mobile.diagnostics_relay (طلب IORegistry لـ AppleSmartBattery) —
/// نفس ما تقرؤه أدوات الصيانة. الرسائل بنفس صيغة lockdown (طول 4 بايت ثم plist).
/// </summary>
public static class BatteryReader
{
    public const string Service = "com.apple.mobile.diagnostics_relay";

    /// <summary>القراءة من جلسة lockdown مفتوحة؛ null إن لم يعطِ الجهاز قيمًا صالحة</summary>
    public static BatteryHealth Read(Lockdown ld, Usbmux mux, long deviceId, Dictionary<string, object> pair)
    {
        var (port, ssl) = ld.StartService(Service);
        using TcpClient c = mux.Connect(deviceId, port);
        Stream s = c.GetStream();
        if (ssl) s = Lockdown.Tls(s, pair);
        try
        {
            foreach (var query in new[] { ("EntryName", "AppleSmartBattery"), ("EntryClass", "IOPMPowerSource") })
            {
                var r = Lockdown.Send(s, new() { ["Request"] = "IORegistry", [query.Item1] = query.Item2 });
                if (r.Str("Status") != "Success") continue;
                var diag = r.TryGetValue("Diagnostics", out var dv) ? dv as Dictionary<string, object> : null;
                var reg = diag != null && diag.TryGetValue("IORegistry", out var iv) ? iv as Dictionary<string, object> : null;
                if (Parse(reg) is { Valid: true } b) return b;
            }
            return null;
        }
        finally
        {
            try { Lockdown.Send(s, new() { ["Request"] = "Goodbye" }); } catch { }
        }
    }

    /// <summary>قيم AppleSmartBattery: الأرقام في المستوى الأعلى أو داخل BatteryData حسب إصدار iOS</summary>
    public static BatteryHealth Parse(Dictionary<string, object> reg)
    {
        if (reg == null) return null;
        var data = reg.TryGetValue("BatteryData", out var bd) ? bd as Dictionary<string, object> : null;
        long Get(string key) => reg.Long(key, -1) is var v && v > 0 ? v : data.Long(key, -1);
        long design = Get("DesignCapacity");
        long full = Get("NominalChargeCapacity");
        if (full <= 0) full = Get("AppleRawMaxCapacity");
        if (full <= 0 && Get("MaxCapacity") is var mc && mc > 100) full = mc;   // بعض الإصدارات: MaxCapacity بالـ mAh لا نسبة
        long cycles = Get("CycleCount");
        long temp = reg.Long("Temperature", long.MinValue);
        return new BatteryHealth((int)Math.Max(0, cycles), (int)Math.Max(0, design), (int)Math.Max(0, full),
            temp == long.MinValue ? null : temp / 100.0);
    }
}
