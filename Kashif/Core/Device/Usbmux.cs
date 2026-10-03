using System.Net.Sockets;

namespace Kashif.Device;

/// <summary>جهاز موصول كما تراه خدمة Apple Mobile Device</summary>
public sealed record UsbDevice(long DeviceId, string Udid, string ConnectionType);

/// <summary>
/// التخاطب مع usbmuxd: خدمة Apple Mobile Device (تأتي مع iTunes أو تطبيق Apple Devices في ويندوز)
/// تستمع على 127.0.0.1:27015 وتفتح نفقًا إلى منفذ داخل الآيفون الموصول بالكيبل.
/// الرسالة: رأس 16 بايت (الطول، الإصدار 1، النوع 8 = plist، رقم) ثم plist بصيغة XML.
/// </summary>
public sealed class Usbmux
{
    public const int DefaultPort = 27015;
    readonly string host;
    readonly int port;
    int tag;

    public Usbmux(string host = "127.0.0.1", int port = DefaultPort) { this.host = host; this.port = port; }

    public static TimeSpan Timeout = TimeSpan.FromSeconds(15);

    TcpClient Open()
    {
        var c = new TcpClient { NoDelay = true, ReceiveTimeout = (int)Timeout.TotalMilliseconds, SendTimeout = (int)Timeout.TotalMilliseconds };
        try { c.Connect(host, port); }
        catch (SocketException ex)
        {
            DeviceTrace.Log($"usbmuxd: لا اتصال بـ {host}:{port} ({ex.SocketErrorCode}) — خدمة Apple Mobile Device غير مشغّلة؟");
            c.Dispose();
            throw new DeviceException("خدمة Apple Mobile Device غير مشغّلة على هذا الكمبيوتر.\n" +
                "ثبّت iTunes أو تطبيق Apple Devices من متجر مايكروسوفت، ثم صِل الآيفون بالكيبل.", ex);
        }
        return c;
    }

    Dictionary<string, object> Request(Stream s, Dictionary<string, object> msg)
    {
        msg.TryAdd("ClientVersionString", "Kashif");
        msg.TryAdd("ProgName", "Kashif");
        msg.TryAdd("kLibUSBMuxVersion", 3L);
        var body = Plist.ToXml(msg);
        var head = new byte[16];
        BitConverter.TryWriteBytes(head.AsSpan(0), (uint)(16 + body.Length));
        BitConverter.TryWriteBytes(head.AsSpan(4), 1u);
        BitConverter.TryWriteBytes(head.AsSpan(8), 8u);
        BitConverter.TryWriteBytes(head.AsSpan(12), (uint)++tag);
        s.Write(head);
        s.Write(body);
        s.Flush();
        var rh = Io.ReadExact(s, 16);
        uint len = BitConverter.ToUInt32(rh, 0);
        if (len < 16 || len > 16 * 1024 * 1024) { DeviceTrace.Log($"usbmuxd: رد بطول غير صالح {len}"); throw new DeviceException("رد غير صالح من خدمة Apple Mobile Device"); }
        var reply = Plist.ParseDict(Io.ReadExact(s, (int)len - 16));
        DeviceTrace.Log($"usbmuxd: {msg.Str("MessageType")} ← رد {reply.Str("MessageType")}" + (reply.ContainsKey("Number") ? $" رقم {reply.Long("Number")}" : "") +
            (reply.TryGetValue("DeviceList", out var dl) && dl is List<object> l ? $" ({l.Count} جهاز)" : ""));
        return reply;
    }

    static void CheckResult(Dictionary<string, object> r, string what)
    {
        if (r.Str("MessageType") == "Result" && r.Long("Number") is var n && n != 0)
            throw new DeviceException(n switch
            {
                2 => "الآيفون غير موصول (فُصل الكيبل؟).",
                3 => $"الآيفون رفض الاتصال ({what}). افتح قفل الشاشة وأعد المحاولة.",
                _ => $"خطأ من خدمة Apple Mobile Device رقم {n} ({what}).",
            });
    }

    /// <summary>الأجهزة الموصولة: الموصولة بالكيبل أولًا</summary>
    public List<UsbDevice> ListDevices()
    {
        using var c = Open();
        var r = Request(c.GetStream(), new() { ["MessageType"] = "ListDevices" });
        var list = new List<UsbDevice>();
        if (r.TryGetValue("DeviceList", out var dl) && dl is List<object> items)
            foreach (var it in items.OfType<Dictionary<string, object>>())
            {
                var props = it.TryGetValue("Properties", out var p) ? p as Dictionary<string, object> : null;
                long id = props?.Long("DeviceID", -1) is long v && v >= 0 ? v : it.Long("DeviceID", -1);
                var udid = props.Str("SerialNumber");
                if (id >= 0 && udid != "") list.Add(new UsbDevice(id, udid, props.Str("ConnectionType")));
            }
        foreach (var d in list) DeviceTrace.Log($"جهاز: {DeviceTrace.Mask(d.Udid)} ({d.ConnectionType})");
        return list.OrderBy(d => d.ConnectionType == "USB" ? 0 : 1).ToList();
    }

    /// <summary>سجل الاقتران (الثقة) المحفوظ لهذا الجهاز — يُنشأ عندما يضغط المستخدم «ثق بهذا الكمبيوتر»</summary>
    public Dictionary<string, object> ReadPairRecord(string udid)
    {
        try
        {
            using var c = Open();
            var r = Request(c.GetStream(), new() { ["MessageType"] = "ReadPairRecord", ["PairRecordID"] = udid });
            if (r.Data("PairRecordData") is { Length: > 0 } data) { DeviceTrace.Log("سجل الاقتران: من usbmuxd"); return Plist.ParseDict(data); }
            DeviceTrace.Log("سجل الاقتران: usbmuxd لم يعطه");
        }
        catch (DeviceException) { throw; }
        catch (Exception ex) { DeviceTrace.Error("ReadPairRecord", ex); }
        // احتياط: ملف الاقتران الذي تحفظه خدمة Apple في ويندوز
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Apple", "Lockdown", udid + ".plist");
        if (File.Exists(path)) { DeviceTrace.Log("سجل الاقتران: من ملف Lockdown"); return Plist.ParseDict(File.ReadAllBytes(path)); }
        DeviceTrace.Log("سجل الاقتران: غير موجود — الجهاز لم يثق بهذا الكمبيوتر");
        throw new DeviceException("هذا الكمبيوتر غير موثوق لدى الآيفون.\n" +
            "افتح iTunes أو تطبيق Apple Devices وصِل الآيفون، ثم اضغط «ثق» على شاشة الآيفون وأدخل رمزه، وأعد المحاولة.");
    }

    /// <summary>نفق إلى منفذ داخل الجهاز (المنفذ بترتيب الشبكة كما يطلبه usbmuxd)</summary>
    public TcpClient Connect(long deviceId, int devicePort)
    {
        var c = Open();
        try
        {
            int swapped = ((devicePort & 0xFF) << 8) | ((devicePort >> 8) & 0xFF);
            var r = Request(c.GetStream(), new() { ["MessageType"] = "Connect", ["DeviceID"] = deviceId, ["PortNumber"] = (long)swapped });
            CheckResult(r, "المنفذ " + devicePort);
            DeviceTrace.Log($"نفق إلى المنفذ {devicePort}: مفتوح");
            return c;
        }
        catch { c.Dispose(); throw; }
    }
}

/// <summary>خطأ برسالة عربية مفهومة للفني</summary>
public sealed class DeviceException : Exception
{
    public DeviceException(string message, Exception inner = null) : base(message, inner) { }
}

static class Io
{
    public static byte[] ReadExact(Stream s, int n)
    {
        var buf = new byte[n];
        int got = 0;
        while (got < n)
        {
            int k = s.Read(buf, got, n - got);
            if (k <= 0) throw new DeviceException("انقطع الاتصال بالآيفون (فُصل الكيبل أو أُغلق الاتصال).");
            got += k;
        }
        return buf;
    }
}
