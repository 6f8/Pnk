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

/// <summary>حالة الشحن لحظة القراءة (الكيبل الموصول بالكمبيوتر مصدر طاقة أيضًا)</summary>
/// <param name="AmperageMa">التيار الداخل للبطارية (موجب = شحن، سالب = تفريغ)</param>
public sealed record ChargeState(bool External, bool Charging, int AmperageMa, int VoltageMv, int Watts, string Adapter, int Percent)
{
    /// <summary>هل مسار الشحن يعمل: true يدخل تيار، false لا يرى مصدرًا أو لا يدخل تيار، null لا يُحكم (البطارية ممتلئة)</summary>
    public bool? PathWorks => !External ? false : AmperageMa > 20 ? true : Percent >= 95 ? null : false;

    public string Verdict => PathWorks switch
    {
        true => $"يشحن: يدخل {AmperageMa} mA" + (Watts > 0 ? $" من مصدر {Watts} واط" : "") + " — مسار الشحن (المنفذ وآيسي الشحن) يعمل.",
        null => "البطارية ممتلئة تقريبًا فلا يدخل تيار — طبيعي، لا يُحكم على الشحن الآن.",
        false when !External => "الجهاز لا يرى أي مصدر طاقة على الكيبل الموصول: منفذ الشحن أو فلاتته أو الكيبل.",
        _ => "يرى مصدر الطاقة لكن لا يدخل تيار للبطارية: آيسي الشحن أو البطارية أو موصلها.",
    };

    public override string ToString() =>
        $"{(External ? "مصدر طاقة موصول" : "لا مصدر طاقة")} · {AmperageMa} mA · {VoltageMv / 1000.0:0.00} V · الشحن {Percent}%" + (Adapter != "" ? $" · {Adapter}" : "");
}

/// <summary>
/// قراءة البطارية عبر خدمة التشخيص com.apple.mobile.diagnostics_relay (طلب IORegistry لـ AppleSmartBattery) —
/// نفس ما تقرؤه أدوات الصيانة. الرسائل بنفس صيغة lockdown (طول 4 بايت ثم plist).
/// </summary>
public static class BatteryReader
{
    public const string Service = "com.apple.mobile.diagnostics_relay";

    /// <summary>القراءة من جلسة lockdown مفتوحة؛ null إن لم يعطِ الجهاز قيمًا صالحة</summary>
    public static BatteryHealth Read(Lockdown ld, Usbmux mux, long deviceId, Dictionary<string, object> pair) =>
        Parse(Query(ld, mux, deviceId, pair));

    /// <summary>قيم AppleSmartBattery الخام (للصحة والشحن معًا)، أو null</summary>
    public static Dictionary<string, object> Query(Lockdown ld, Usbmux mux, long deviceId, Dictionary<string, object> pair)
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
                if (Parse(reg) is { Valid: true }) return reg;
            }
            return null;
        }
        finally
        {
            try { Lockdown.Send(s, new() { ["Request"] = "Goodbye" }); } catch { }
        }
    }

    /// <summary>حالة الشحن من نفس القيم: ExternalConnected، IsCharging، InstantAmperage (أو Amperage)، Voltage، AdapterDetails، CurrentCapacity</summary>
    public static ChargeState ParseCharge(Dictionary<string, object> reg)
    {
        if (reg == null) return null;
        bool B(string k) => reg.TryGetValue(k, out var v) && v is bool b && b;
        long L(string k, long def = 0) => reg.TryGetValue(k, out var v) && v is long l ? l : def;
        long amp = reg.ContainsKey("InstantAmperage") ? L("InstantAmperage") : L("Amperage");
        // التيار السالب قد يُكتب عددًا بلا إشارة: 64 بت يصبح سالبًا عند القراءة، و32 بت يُصحَّح هنا
        if (amp > int.MaxValue && amp <= uint.MaxValue) amp -= 1L << 32;
        var adapter = reg.TryGetValue("AdapterDetails", out var av) ? av as Dictionary<string, object> : null;
        long watts = adapter.Long("Watts", 0);
        var desc = adapter.Str("Description");
        if (desc == "") desc = adapter.Str("Name");
        long pct = L("CurrentCapacity", -1);
        if (pct < 0 || pct > 100) { long mx = L("MaxCapacity", 0); pct = mx > 0 && mx <= 100 && pct > 0 ? pct * 100 / mx : -1; }
        return new ChargeState(B("ExternalConnected"), B("IsCharging"), (int)Math.Clamp(amp, int.MinValue, int.MaxValue), (int)L("Voltage"), (int)watts, desc, (int)pct);
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
