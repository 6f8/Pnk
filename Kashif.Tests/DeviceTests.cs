using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kashif.Device;

namespace Kashif.Tests;

/// <summary>
/// سحب السجلات من الآيفون: plist (XML والثنائي)، وحزم AFC، والمسار كاملًا على آيفون وهمي
/// (usbmuxd ← lockdownd ← crashreportmover ← crashreportcopymobile) يعمل على منفذ محلي.
/// ما لا يُختبر هنا: TLS مع جهاز حقيقي — يُجرَّب على آيفون فعلي.
/// </summary>
static partial class Program
{
    static void RunDevice()
    {
        Run("الآيفون: plist بصيغتي XML والثنائية", PlistFormats);
        Run("الآيفون: حزم AFC", AfcPackets);
        Run("الآيفون: سحب السجلات من جهاز وهمي", PullFromFakeDevice);
        Run("الآيفون: رسائل الأخطاء المفهومة", DeviceErrors);
        Run("الآيفون: TLS بشهادة سجل الاقتران (PEM)", PairTls);
        Run("البطارية المقاسة واختبار العزل في الترتيب", BatteryAndIsolation);
        Run("مراقبة إعادة التشغيل بالكيبل", RebootWatching);
        Run("الشحن مقاسًا بالكيبل", ChargingChecks);
        Run("السجل المباشر من الآيفون", LiveSyslog);
        Run("التنبيه بنسخة جديدة", UpdateCheck);
    }

    static void PlistFormats()
    {
        var src = new Dictionary<string, object>
        {
            ["MessageType"] = "Connect", ["DeviceID"] = 7L, ["Ok"] = true, ["Data"] = new byte[] { 1, 2, 250 },
            ["List"] = new List<object> { "a&b<c>", 3L }, ["Nested"] = new Dictionary<string, object> { ["x"] = -5L },
        };
        var back = Plist.ParseDict(Plist.ToXml(src));
        Check(back.Str("MessageType") == "Connect" && back.Long("DeviceID") == 7 && back.Bool("Ok"), "قيم بسيطة");
        Check(back.Data("Data").SequenceEqual(new byte[] { 1, 2, 250 }), "data");
        Check(back["List"] is List<object> l && (string)l[0] == "a&b<c>" && (long)l[1] == 3, "array مع رموز XML");
        Check(((Dictionary<string, object>)back["Nested"]).Long("x") == -5, "قاموس داخل قاموس");

        var bin = BinaryPlist(new() { ["HostID"] = "ABC-123", ["SystemBUID"] = "BUID-9", ["Name"] = "آيفون" });
        var d = Plist.ParseDict(bin);
        Check(d.Str("HostID") == "ABC-123" && d.Str("SystemBUID") == "BUID-9", "bplist: نصوص ASCII");
        Check(d.Str("Name") == "آيفون", "bplist: نص UTF-16");

        bool threw = false;
        try { Plist.Parse(Encoding.ASCII.GetBytes("bplist00 broken")); } catch (FormatException) { threw = true; }
        Check(threw, "bplist تالف يُرفض بخطأ واضح");
    }

    static void AfcPackets()
    {
        var p = Afc.Packet(Afc.OpReadDir, 5, Encoding.UTF8.GetBytes("/\0"), new byte[] { 9, 9 });
        Check(p.Length == Afc.HeaderSize + 2 + 2, "الطول");
        Check(Encoding.ASCII.GetString(p, 0, 8) == "CFA6LPAA", "التوقيع");
        Check(BinaryPrimitives.ReadUInt64LittleEndian(p.AsSpan(16)) == Afc.HeaderSize + 2, "طول الرأس مع المعاملات");
        var r = Afc.Read(new MemoryStream(p));
        Check(r.Op == Afc.OpReadDir && r.Header.Length == 2 && r.Payload.SequenceEqual(new byte[] { 9, 9 }), "القراءة تطابق الكتابة");
        Check(CrashReports.IsPanic("panic-full-2026-09-26-205345.0002.ips") && !CrashReports.IsPanic("JetsamEvent-2026.ips"), "تمييز سجلات البانك");
    }

    static void PullFromFakeDevice()
    {
        var big = new string('x', 150_000);   // أكبر من قطعة القراءة (64 كيلوبايت)
        var files = new Dictionary<string, string>
        {
            ["/panic-full-2026-09-26-205345.ips"] = "{\"bug_type\":\"210\"}\npanic newest " + big,
            ["/panic-full-2026-09-20-101010.ips"] = "panic older",
            ["/panic-base-2026-09-26-205345.ips"] = "base",
            ["/JetsamEvent-2026.ips"] = "not a panic",
            ["/Retired/panic-full-2025-01-01-000000.ips"] = "retired panic",
        };
        using var fake = new FakeIPhone(files);
        var pull = CrashReports.Pull(new Usbmux("127.0.0.1", fake.Port));
        Check(pull.DeviceName == "iPhone يوسف" && pull.ProductType == "iPhone14,3" && pull.Version == "26.0", $"معلومات الجهاز: {pull.DeviceName} {pull.ProductType} {pull.Version}");
        Check(pull.Udid == "00008110-000A1B2C3D4E5F6A", "UDID");
        var names = pull.Logs.Select(l => l.Name).ToList();
        Check(names.SequenceEqual(new[] { "panic-full-2026-09-26-205345.ips", "panic-full-2026-09-20-101010.ips", "panic-full-2025-01-01-000000.ips" }),
            "panic-full فقط، الأحدث أولًا، ومن مجلد Retired أيضًا: " + string.Join(", ", names));
        Check(pull.Logs[0].Text.EndsWith(big) && pull.Logs[0].Text.Length == files["/panic-full-2026-09-26-205345.ips"].Length, "ملف أكبر من قطعة القراءة يُقرأ كاملًا");
        Check(fake.MoverPinged, "خدمة نقل السجلات شُغّلت");
        Check(fake.Writes == 0, "لا كتابة ولا حذف في الجهاز");
        Check(pull.Failed == 0 && pull.Older == 0, $"failed={pull.Failed} older={pull.Older}");
        Check(pull.Battery is { Valid: true, Percent: 90, CycleCount: 412, DesignMah: 4352, FullMah: 3918 } && pull.Battery.TemperatureC == 29.5,
            "البطارية من خدمة التشخيص: " + pull.Battery);
        Check(pull.Check?.Identity is { Serial: "F2LXK0Q1ABCD", Imei: "356000000000001", ProductType: "iPhone14,3" }, "هوية الجهاز: " + pull.Check?.Identity);
        Check(pull.Check?.Charge is { External: true, AmperageMa: 850, VoltageMv: 4120, Watts: 5, Percent: 64, PathWorks: true }, "الشحن: " + pull.Check?.Charge);
        var only = CrashReports.Inspect(new Usbmux("127.0.0.1", fake.Port));
        Check(only.Battery?.Percent == 90 && only.Identity.ProductType == "iPhone14,3" && only.Charge?.PathWorks == true, "فحص الجهاز بلا سحب");
    }

    static void DeviceErrors()
    {
        // لا خدمة Apple على المنفذ
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        try { CrashReports.Pull(new Usbmux("127.0.0.1", port)); Check(false, "كان يجب أن يفشل"); }
        catch (DeviceException ex) { Check(ex.Message.Contains("Apple Mobile Device"), "رسالة: " + ex.Message); }

        using (var fake = new FakeIPhone(new(), devices: 0))
        {
            try { CrashReports.Pull(new Usbmux("127.0.0.1", fake.Port)); Check(false, "كان يجب أن يفشل"); }
            catch (DeviceException ex) { Check(ex.Message.Contains("لا يوجد آيفون"), "رسالة: " + ex.Message); }
        }
        using (var fake = new FakeIPhone(new(), lockdownError: "PasswordProtected"))
        {
            try { CrashReports.Pull(new Usbmux("127.0.0.1", fake.Port)); Check(false, "كان يجب أن يفشل"); }
            catch (DeviceException ex) { Check(ex.Message.Contains("افتح قفل"), "رسالة: " + ex.Message); }
        }
    }

    static void BatteryAndIsolation()
    {
        var b = BatteryReader.Parse(new Dictionary<string, object> { ["CycleCount"] = 900L, ["DesignCapacity"] = 3000L, ["AppleRawMaxCapacity"] = 2250L });
        Check(b is { Percent: 75, CycleCount: 900 }, "AppleRawMaxCapacity عند غياب NominalChargeCapacity: " + b);
        Check(BatteryReader.Parse(new()) is { Valid: false }, "بلا قيم: غير صالحة");
        Check(BatteryReader.Parse(null) == null, "null");

        // حالة iPhone 13 Pro Max: البطارية الأخيرة بلا دليل؛ بطارية سليمة بالقياس تنخفض أكثر ولا تتقدم
        Diagnosis Fresh() => PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("smc_bsc_d64_screen_sensor.ips"), "x")[0]);
        var d = Fresh();
        int before = d.Candidates.First(c => c.Part == Parts.Battery).Score;
        PanicAnalyzer.ApplyBattery(d, 94, 210);
        Check(d.Candidates.First(c => c.Part == Parts.Battery).Score == Math.Max(1, before - 25), "بطارية سليمة ← -25");
        Check(d.Evidence.Any(e => e.What == "البطارية مقاسة بالكيبل" && e.IsExam), "دليل البطارية من الفحص");
        Check(d.TopPart != Parts.Battery, "البطارية ليست الأرجح");

        var weak = Fresh();
        PanicAnalyzer.ApplyBattery(weak, 71, 1100);
        Check(weak.Candidates.First(c => c.Part == Parts.Battery).Score == before + 10, "بطارية ضعيفة ← +10 فقط");
        Check(weak.TopPart != Parts.Battery, "ضعف السعة وحده لا يجعلها الأرجح في انهيار يذكر حساسًا آخر");

        // اختبار العزل: فصل حساس الشاشة أوقف الانهيار ← يتقدم؛ فصل فلاتة الشحن لم يوقفه ← تنخفض
        var iso = Fresh();
        int charge = iso.Candidates.First(c => c.Part == Parts.ChargingFlex).Score;
        PanicAnalyzer.ApplyIsolation(iso, Parts.ChargingFlex, false, "02:58");
        PanicAnalyzer.ApplyIsolation(iso, Parts.FrontFlex, true, "06:00");
        Check(iso.TopPart == Parts.FrontFlex && iso.Candidates[0].Label == "الأرجح", "الحساس الأمامي الأرجح بعد العزل");
        Check(iso.Candidates.First(c => c.Part == Parts.ChargingFlex).Score == Math.Max(1, charge - 30), "فلاتة الشحن -30");
        PanicAnalyzer.ApplyIsolation(iso, "فلاتة لم تُذكر", true, "06:00");
        Check(iso.Evidence.Count(e => e.What == "اختبار العزل") == 3, "نص حر يُسجَّل دليلًا");
    }

    static void UpdateCheck()
    {
        var r = Updates.Parse("{\"name\":\"كاشف 1.0.12\",\"tag_name\":\"latest\",\"html_url\":\"https://github.com/6f8/Pnk/releases/tag/latest\"}");
        Check(r?.Version == new Version(1, 0, 12) && r.Url.EndsWith("/latest"), "الإصدار من الاسم: " + r?.Version);
        Check(Updates.IsNewer(r, "1.0.5+a98dd4d") && !Updates.IsNewer(r, "1.0.12") && !Updates.IsNewer(r, "1.1.0"), "المقارنة (1.0.12 أحدث من 1.0.5 وليس من 1.1.0)");
        Check(!Updates.IsNewer(r, "1.0.12+abc"), "نفس النسخة");
        Check(Updates.Parse("{\"message\":\"Not Found\"}") == null && Updates.Parse("not json") == null, "رد 404 أو نص تالف ← لا شيء");
        Check(!Updates.IsNewer(null, "1.0.0"), "null ← لا تنبيه");
    }

    static void LiveSyslog()
    {
        var t = DateTime.Now;
        Check(LiveLog.Classify("thermalmonitord[93] <Error>: Missing sensor(s): Prs0", t) is { Category: "حساس", Sensor: "Prs0" }, "حساس مفقود مع رمزه");
        Check(LiveLog.Classify("kernel: i2c2: _checkBusStatus SCL is stuck low", t)?.Category == "I2C", "I2C عالق");
        Check(LiveLog.Classify("SpringBoard[55] <Notice>: hello", t) == null, "سطر عادي لا يُعلَّم");
        Check(LiveLog.Classify("", t) == null, "سطر فارغ");

        using var fake = new FakeIPhone(new());
        using var live = new LiveSession();
        var lines = new List<string>();
        var done = new ManualResetEventSlim();
        string endError = "unset";
        live.Line += l => { lock (lines) lines.Add(l); };
        live.Ended += e => { endError = e; done.Set(); };
        live.Start(new Usbmux("127.0.0.1", fake.Port));
        Check(done.Wait(10000), "انتهت الجلسة عند إغلاق الجهاز للاتصال");
        Check(endError == null, "انقطاع عادي بلا خطأ: " + endError);
        Check(lines.Count == 3 && lines[2].EndsWith("SMC BSC failure timeout"), "الرسالة المقسومة بين دفعتين تُجمع: " + string.Join(" | ", lines));
        var flags = live.Flags;
        Check(flags.Count == 2 && flags[0].Sensor == "Prs0,mic1" && flags[1].Category == "SMC", "الأخطاء: " + string.Join(", ", flags.Select(f => f.Category + ":" + f.Sensor)));
        Check(live.DeviceName == "iPhone يوسف", "اسم الجهاز");
    }

    static void ChargingChecks()
    {
        ChargeState C(bool ext, long amp, long pct) => BatteryReader.ParseCharge(new() { ["ExternalConnected"] = ext, ["InstantAmperage"] = amp, ["CurrentCapacity"] = pct });
        Check(C(true, 900, 50).PathWorks == true, "يدخل تيار ← يعمل");
        Check(C(false, -300, 50).PathWorks == false && C(false, -300, 50).Verdict.Contains("منفذ الشحن"), "لا مصدر طاقة ← المنفذ أو الفلاتة");
        Check(C(true, 0, 40).PathWorks == false && C(true, 0, 40).Verdict.Contains("آيسي الشحن"), "مصدر بلا تيار ← آيسي الشحن");
        Check(C(true, 0, 100).PathWorks == null, "ممتلئة ← لا حكم");
        Check(C(true, 4294966996, 50).AmperageMa == -300, "تيار سالب مكتوب بلا إشارة (32 بت)");
        Check(C(true, unchecked((long)18446744073709551316UL), 50).AmperageMa == -300, "تيار سالب (64 بت)");

        Diagnosis Fresh() => PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("prs0_iphone11_ocr.txt"), "x")[0]);
        var ok = Fresh();
        int flex = ok.Candidates.First(c => c.Part == Parts.ChargingFlex).Score;
        PanicAnalyzer.ApplyCharging(ok, true, C(true, 900, 50).Verdict);
        Check(ok.Candidates.First(c => c.Part == Parts.ChargingFlex).Score == flex && ok.TopPart == Parts.ChargingFlex,
            "الشحن يعمل لا يبرّئ فلاتة الشحن من حساس Prs0");
        var bad = Fresh();
        PanicAnalyzer.ApplyCharging(bad, false, C(true, 0, 40).Verdict);
        Check(bad.Candidates.First(c => c.Part == Parts.ChargingFlex).Score == Math.Min(99, flex + 10), "لا يشحن ← فلاتة الشحن +10");
    }

    static void RebootWatching()
    {
        Check(RebootWatch.LimitFor(180) == TimeSpan.FromSeconds(360), "3 دقائق ← 6");
        Check(RebootWatch.LimitFor(60) == TimeSpan.FromSeconds(240), "الحد الأدنى 4 دقائق");
        Check(RebootWatch.LimitFor(null) == TimeSpan.FromMinutes(6), "غير معروف ← 6");
        Check(RebootWatch.LimitFor(3600) == TimeSpan.FromMinutes(15), "الحد الأعلى 15");

        var t0 = new DateTime(2026, 10, 2, 12, 0, 0);
        var present = new List<string> { "U1" };
        var w = new RebootWatch(() => present.ToList());
        w.Start(t0, TimeSpan.FromMinutes(6));
        Check(w.Watching && w.Udid == "U1", "يراقب الجهاز الموصول");
        Check(w.Tick(t0.AddSeconds(100)) == RebootWatch.State.Running, "ما زال يعمل");
        present.Clear();
        Check(w.Tick(t0.AddSeconds(182)) == RebootWatch.State.Rebooted && w.Result == TimeSpan.FromSeconds(182), "انقطع ← أعاد التشغيل بعد 3:02");
        Check(RebootWatch.Clock(w.Result) == "03:02", "الساعة " + RebootWatch.Clock(w.Result));

        present.Add("U1");
        w.Start(t0, TimeSpan.FromMinutes(6));
        Check(w.Tick(t0.AddMinutes(6)) == RebootWatch.State.Passed, "تجاوز المهلة ← بقي يعمل");

        var none = new RebootWatch(() => throw new InvalidOperationException("no service"));
        none.Start(t0, TimeSpan.FromMinutes(6));
        Check(!none.Watching && none.Tick(t0.AddSeconds(30)) == RebootWatch.State.Running, "بلا خدمة Apple: بلا مراقبة ولا خطأ");
        Check(none.Mark(true, t0.AddSeconds(200)) == RebootWatch.State.Rebooted && RebootWatch.Clock(none.Result) == "03:20", "الحكم اليدوي");
    }

    static void PairTls()
    {
        using var hostKey = System.Security.Cryptography.RSA.Create(2048);
        var hostReq = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Kashif Host", hostKey,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var hostCert = hostReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        // سجل الاقتران يحمل الشهادة والمفتاح بصيغة PEM (المفتاح PKCS#1 «RSA PRIVATE KEY»)
        var pair = new Dictionary<string, object>
        {
            ["HostCertificate"] = Encoding.ASCII.GetBytes(hostCert.ExportCertificatePem()),
            ["HostPrivateKey"] = Encoding.ASCII.GetBytes(hostKey.ExportRSAPrivateKeyPem()),
        };
        using var devKey = System.Security.Cryptography.RSA.Create(2048);
        var devReq = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Device", devKey,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var devTmp = devReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var devCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(devTmp.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12));

        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        string seenClient = null, echoed = null;
        var server = new Thread(() =>
        {
            using var c = l.AcceptTcpClient();
            using var ssl = new System.Net.Security.SslStream(c.GetStream(), false, (o, cert, ch, e) => { seenClient = cert?.Subject; return true; });
            ssl.AuthenticateAsServer(devCert, clientCertificateRequired: true, checkCertificateRevocation: false);
            var buf = new byte[4];
            ssl.ReadExactly(buf);
            ssl.Write(buf);
        }) { IsBackground = true };
        server.Start();
        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, ((IPEndPoint)l.LocalEndpoint).Port);
            using var tls = Lockdown.Tls(client.GetStream(), pair);
            tls.Write(Encoding.ASCII.GetBytes("ping"));
            var back = new byte[4];
            tls.ReadExactly(back);
            echoed = Encoding.ASCII.GetString(back);
        }
        server.Join(5000);
        l.Stop();
        Check(seenClient == "CN=Kashif Host", "الجهاز يرى شهادة الكمبيوتر: " + seenClient);
        Check(echoed == "ping", "البيانات تمر عبر TLS");
        bool threw = false;
        try { Lockdown.Tls(new MemoryStream(), new Dictionary<string, object>()); } catch (DeviceException) { threw = true; }
        Check(threw, "سجل اقتران بلا شهادة: رسالة واضحة");
    }

    /// <summary>bplist00 لقاموس نصوص (لاختبار قراءة سجل الاقتران الثنائي)</summary>
    static byte[] BinaryPlist(Dictionary<string, string> d)
    {
        var objs = new List<byte[]>();
        byte[] Str(string s)
        {
            bool ascii = s.All(c => c < 128);
            var bytes = ascii ? Encoding.ASCII.GetBytes(s) : Encoding.BigEndianUnicode.GetBytes(s);
            int len = ascii ? bytes.Length : bytes.Length / 2;
            return new[] { (byte)((ascii ? 0x50 : 0x60) | len) }.Concat(bytes).ToArray();
        }
        objs.Add(null);   // 0: القاموس
        foreach (var k in d.Keys) objs.Add(Str(k));
        foreach (var v in d.Values) objs.Add(Str(v));
        int n = d.Count;
        objs[0] = new[] { (byte)(0xD0 | n) }.Concat(Enumerable.Range(1, n * 2).Select(i => (byte)i)).ToArray();
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("bplist00"));
        var offsets = new List<int>();
        foreach (var o in objs) { offsets.Add((int)ms.Length); ms.Write(o); }
        int table = (int)ms.Length;
        foreach (var o in offsets) ms.WriteByte((byte)o);
        var trailer = new byte[32];
        trailer[6] = 1; trailer[7] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(8), (ulong)objs.Count);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(24), (ulong)table);
        ms.Write(trailer);
        return ms.ToArray();
    }

    /// <summary>آيفون وهمي: usbmuxd على منفذ محلي، وخلفه lockdownd وخدمتا سجلات الأعطال (بلا TLS)</summary>
    sealed class FakeIPhone : IDisposable
    {
        const int MoverPort = 1001, CopyPort = 1002, DiagPort = 1003, SyslogPort = 1004;
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly Dictionary<string, string> files;
        readonly int devices;
        readonly string lockdownError;
        public int Port { get; }
        public volatile bool MoverPinged;
        public int Writes;

        public FakeIPhone(Dictionary<string, string> files, int devices = 1, string lockdownError = null)
        {
            this.files = files;
            this.devices = devices;
            this.lockdownError = lockdownError;
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            new Thread(Accept) { IsBackground = true }.Start();
        }

        void Accept()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch { return; }
                new Thread(() => { try { using (c) Serve(c.GetStream()); } catch { } }) { IsBackground = true }.Start();
            }
        }

        static Dictionary<string, object> ReadMux(Stream s)
        {
            var h = new byte[16];
            s.ReadExactly(h);
            var body = new byte[BitConverter.ToUInt32(h, 0) - 16];
            s.ReadExactly(body);
            return Plist.ParseDict(body);
        }

        static void WriteMux(Stream s, Dictionary<string, object> msg)
        {
            var body = Plist.ToXml(msg);
            var h = new byte[16];
            BitConverter.TryWriteBytes(h.AsSpan(0), (uint)(16 + body.Length));
            BitConverter.TryWriteBytes(h.AsSpan(4), 1u);
            BitConverter.TryWriteBytes(h.AsSpan(8), 8u);
            s.Write(h);
            s.Write(body);
        }

        void Serve(Stream s)
        {
            var m = ReadMux(s);
            switch (m.Str("MessageType"))
            {
                case "ListDevices":
                    var list = new List<object>();
                    for (int i = 0; i < devices; i++)
                        list.Add(new Dictionary<string, object>
                        {
                            ["DeviceID"] = 3L, ["MessageType"] = "Attached",
                            ["Properties"] = new Dictionary<string, object> { ["DeviceID"] = 3L, ["SerialNumber"] = "00008110-000A1B2C3D4E5F6A", ["ConnectionType"] = "USB" },
                        });
                    WriteMux(s, new() { ["DeviceList"] = list });
                    break;
                case "ReadPairRecord":
                    WriteMux(s, new() { ["PairRecordData"] = BinaryPlist(new() { ["HostID"] = "HOST-1", ["SystemBUID"] = "BUID-1" }) });
                    break;
                case "Connect":
                    long swapped = m.Long("PortNumber");
                    int port = (int)(((swapped & 0xFF) << 8) | ((swapped >> 8) & 0xFF));
                    WriteMux(s, new() { ["MessageType"] = "Result", ["Number"] = 0L });
                    if (port == Lockdown.Port) ServeLockdown(s);
                    else if (port == MoverPort) { s.Write(Encoding.ASCII.GetBytes("ping")); MoverPinged = true; }
                    else if (port == CopyPort) ServeAfc(s);
                    else if (port == DiagPort) ServeDiagnostics(s);
                    else if (port == SyslogPort)
                    {
                        // رسائل منتهية بصفر، وآخرها مقسوم بين دفعتين
                        s.Write(Encoding.UTF8.GetBytes("Oct  2 21:00:01 iPhone SpringBoard[55] <Notice>: hello\0" +
                            "Oct  2 21:00:02 iPhone thermalmonitord[93] <Error>: Missing sensor(s): Prs0 mic1\0Oct  2 21:00:03 iPhone kernel[0] <Notice>: AppleSMC: SMC "));
                        s.Flush();
                        Thread.Sleep(50);
                        s.Write(Encoding.UTF8.GetBytes("BSC failure timeout\0"));
                    }
                    break;
            }
        }

        void ServeLockdown(Stream s)
        {
            while (true)
            {
                var len = new byte[4];
                try { s.ReadExactly(len); } catch { return; }
                var body = new byte[BinaryPrimitives.ReadUInt32BigEndian(len)];
                s.ReadExactly(body);
                var req = Plist.ParseDict(body);
                var r = new Dictionary<string, object> { ["Request"] = req.Str("Request") };
                switch (req.Str("Request"))
                {
                    case "StartSession":
                        if (lockdownError != null) r["Error"] = lockdownError;
                        else if (req.Str("HostID") != "HOST-1") r["Error"] = "InvalidHostID";
                        else { r["SessionID"] = "S1"; r["EnableSessionSSL"] = false; }
                        break;
                    case "GetValue":
                        r["Value"] = req.Str("Key") switch
                        {
                            "DeviceName" => "iPhone يوسف", "ProductType" => "iPhone14,3", "ProductVersion" => "26.0",
                            "SerialNumber" => "F2LXK0Q1ABCD", "InternationalMobileEquipmentIdentity" => "356000000000001", _ => "",
                        };
                        break;
                    case "StartService":
                        r["Port"] = req.Str("Service") switch
                        {
                            CrashReports.Mover => (long)MoverPort, CrashReports.Copy => (long)CopyPort, BatteryReader.Service => (long)DiagPort,
                            LiveSession.Service => (long)SyslogPort, _ => 0L,
                        };
                        r["EnableServiceSSL"] = false;
                        break;
                }
                var outBody = Plist.ToXml(r);
                var outLen = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(outLen, (uint)outBody.Length);
                s.Write(outLen);
                s.Write(outBody);
            }
        }

        /// <summary>خدمة التشخيص: AppleSmartBattery بقيم iOS حديث (السعة الأصلية داخل BatteryData)</summary>
        void ServeDiagnostics(Stream s)
        {
            while (true)
            {
                var len = new byte[4];
                try { s.ReadExactly(len); } catch { return; }
                var body = new byte[BinaryPrimitives.ReadUInt32BigEndian(len)];
                s.ReadExactly(body);
                var req = Plist.ParseDict(body);
                Dictionary<string, object> r = req.Str("Request") == "IORegistry" && req.Str("EntryName") == "AppleSmartBattery"
                    ? new()
                    {
                        ["Status"] = "Success",
                        ["Diagnostics"] = new Dictionary<string, object>
                        {
                            ["IORegistry"] = new Dictionary<string, object>
                            {
                                ["CycleCount"] = 412L, ["NominalChargeCapacity"] = 3918L, ["Temperature"] = 2950L,
                                ["ExternalConnected"] = true, ["IsCharging"] = true, ["InstantAmperage"] = 850L, ["Voltage"] = 4120L, ["CurrentCapacity"] = 64L,
                                ["AdapterDetails"] = new Dictionary<string, object> { ["Watts"] = 5L, ["Description"] = "usb host" },
                                ["BatteryData"] = new Dictionary<string, object> { ["DesignCapacity"] = 4352L },
                            },
                        },
                    }
                    : new() { ["Status"] = req.Str("Request") == "Goodbye" ? "Success" : "UnknownRequest" };
                var outBody = Plist.ToXml(r);
                var outLen = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(outLen, (uint)outBody.Length);
                s.Write(outLen);
                s.Write(outBody);
            }
        }

        void ServeAfc(Stream s)
        {
            var open = new Dictionary<ulong, (string Path, int Pos)>();
            ulong next = 1;
            while (true)
            {
                (ulong Op, byte[] Header, byte[] Payload) p;
                try { p = Afc.Read(s); } catch { return; }
                string PathArg(int at) => Encoding.UTF8.GetString(p.Header, at, p.Header.Length - at).TrimEnd('\0');
                void Reply(ulong op, byte[] header, byte[] payload = null) => s.Write(Afc.Packet(op, 0, header, payload));
                void Status(ulong code) { var b = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, code); Reply(Afc.OpStatus, b); }
                bool IsDir(string path) => path == "/" || files.Keys.Any(f => f.StartsWith(path.TrimEnd('/') + "/"));
                switch (p.Op)
                {
                    case Afc.OpReadDir:
                    {
                        var dir = PathArg(0).TrimEnd('/');
                        if (!IsDir(dir == "" ? "/" : dir)) { Status(8); break; }
                        var children = files.Keys.Where(f => f.StartsWith(dir + "/")).Select(f => f[(dir.Length + 1)..].Split('/')[0]).Distinct();
                        Reply(Afc.OpData, null, Encoding.UTF8.GetBytes(string.Join("\0", new[] { ".", ".." }.Concat(children)) + "\0"));
                        break;
                    }
                    case Afc.OpGetFileInfo:
                    {
                        var path = PathArg(0);
                        if (IsDir(path)) Reply(Afc.OpData, null, Encoding.UTF8.GetBytes("st_ifmt\0S_IFDIR\0"));
                        else if (files.ContainsKey(path)) Reply(Afc.OpData, null, Encoding.UTF8.GetBytes("st_ifmt\0S_IFREG\0"));
                        else Status(8);
                        break;
                    }
                    case Afc.OpFileOpen:
                    {
                        ulong mode = BinaryPrimitives.ReadUInt64LittleEndian(p.Header);
                        if (mode != 1) Interlocked.Increment(ref Writes);
                        var path = PathArg(8);
                        if (!files.ContainsKey(path)) { Status(8); break; }
                        open[next] = (path, 0);
                        var h = new byte[8];
                        BinaryPrimitives.WriteUInt64LittleEndian(h, next++);
                        Reply(Afc.OpFileOpenRes, h);
                        break;
                    }
                    case Afc.OpFileRead:
                    {
                        ulong handle = BinaryPrimitives.ReadUInt64LittleEndian(p.Header);
                        int want = (int)BinaryPrimitives.ReadUInt64LittleEndian(p.Header.AsSpan(8));
                        var (path, pos) = open[handle];
                        var all = Encoding.UTF8.GetBytes(files[path]);
                        int n = Math.Min(want, all.Length - pos);
                        open[handle] = (path, pos + n);
                        Reply(Afc.OpData, null, all.AsSpan(pos, n).ToArray());
                        break;
                    }
                    case Afc.OpFileClose: open.Remove(BinaryPrimitives.ReadUInt64LittleEndian(p.Header)); Status(0); break;
                    default: Interlocked.Increment(ref Writes); Status(1); break;
                }
            }
        }

        public void Dispose() => listener.Stop();
    }
}
