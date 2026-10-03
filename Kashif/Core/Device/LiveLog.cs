using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Kashif.Device;

/// <summary>سطر مهم من السجل المباشر: نوعه ومعناه، ورمز الحساس إن ذُكر</summary>
public sealed record LiveFlag(DateTime At, string Category, string Meaning, string Line, string Sensor);

/// <summary>
/// تصنيف أسطر سجل النظام المباشر: لا يغيّر ترتيب الأسباب وحده — يعرض للفني الأسطر التي تخص الحساسات و SMC و I2C و AOP
/// كما يكتبها الجهاز لحظة بلحظة، قبل أن يصل إلى البانك.
/// </summary>
public static class LiveLog
{
    static readonly (string Category, Regex Rx, string Meaning)[] Rules =
    {
        ("حساس", new(@"Missing\s*sensor", RegexOptions.IgnoreCase), "حساس مفقود — نفس سبب بانك thermalmonitord"),
        ("الحرارة", new(@"thermalmonitord.*\b(error|fail\w*|missing|unavailable|timeout)\b", RegexOptions.IgnoreCase), "خدمة الحرارة تشتكي من حساس"),
        ("SMC", new(@"\b(AppleSMC|SMC)\b.*\b(fail\w*|timeout|error|not ready|assert\w*)\b", RegexOptions.IgnoreCase), "خطأ في معالج الطاقة والحساسات SMC"),
        ("I2C", new(@"\bi2c\d*\b.*\b(timeout|stuck|nak|error|fail\w*)\b", RegexOptions.IgnoreCase), "خط I2C لا يستجيب — قطعة على الخط لا ترد"),
        ("AOP", new(@"\b(AOP|AppleSPU|AppleAOP)\b.*\b(error|fail\w*|timeout|crash\w*|panic)\b", RegexOptions.IgnoreCase), "المعالج دائم التشغيل (الحساسات) يشتكي"),
        ("انهيار", new(@"\b(panic|watchdog)\b", RegexOptions.IgnoreCase), "إشارة انهيار أو مراقبة"),
    };

    static readonly Regex SensorCodes = new(@"Missing\s*sensor\s*\(?\s*s?\s*\)?\s*:?\s*([A-Za-z][A-Za-z0-9]{1,5}(?:[\s,]+[A-Za-z][A-Za-z0-9]{1,5})*)", RegexOptions.IgnoreCase);

    public static LiveFlag Classify(string line, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        foreach (var (cat, rx, meaning) in Rules)
            if (rx.IsMatch(line))
            {
                string sensor = null;
                if (cat == "حساس" && SensorCodes.Match(line) is { Success: true } m)
                    sensor = string.Join(",", Regex.Split(m.Groups[1].Value.Trim(), @"[\s,]+").Where(t => t.Any(char.IsDigit)));
                return new LiveFlag(at, cat, meaning, line.Trim(), string.IsNullOrEmpty(sensor) ? null : sensor);
            }
        return null;
    }
}

/// <summary>
/// سجل النظام المباشر من الآيفون (com.apple.syslog_relay): الجهاز يرسل الرسائل نصًا متصلًا، كل رسالة تنتهي بصفر.
/// يعمل في الخلفية حتى الإيقاف أو انقطاع الجهاز (إعادة التشغيل تقطعه).
/// </summary>
public sealed class LiveSession : IDisposable
{
    public const string Service = "com.apple.syslog_relay";
    readonly CancellationTokenSource cts = new();
    readonly object gate = new();
    readonly List<LiveFlag> flags = new();
    public event Action<string> Line;
    public event Action<LiveFlag> Flagged;
    /// <summary>انتهت الجلسة: null عند الإيقاف أو الانقطاع العادي، وإلا رسالة الخطأ</summary>
    public event Action<string> Ended;
    public string DeviceName { get; private set; } = "";
    public bool Running { get; private set; }

    public IReadOnlyList<LiveFlag> Flags { get { lock (gate) return flags.ToList(); } }
    public int CountSince(DateTime t) { lock (gate) return flags.Count(f => f.At >= t); }

    public void Start(Usbmux mux = null)
    {
        mux ??= new Usbmux();
        DeviceTrace.Begin("السجل المباشر");
        Running = true;
        Task.Run(() =>
        {
            string error = null;
            try { Run(mux); }
            catch (DeviceException ex) { error = ex.Message; DeviceTrace.Error("السجل المباشر", ex); }
            catch (Exception ex) when (!cts.IsCancellationRequested) { error = ex.Message; }
            catch (Exception) { }
            Running = false;
            Ended?.Invoke(cts.IsCancellationRequested ? null : error);
        });
    }

    void Run(Usbmux mux)
    {
        var dev = mux.ListDevices().FirstOrDefault() ?? throw new DeviceException("لا يوجد آيفون موصول. صِل الجهاز بالكيبل وافتح قفل شاشته.");
        var pair = mux.ReadPairRecord(dev.Udid);
        int port; bool ssl;
        using (var ld = new Lockdown(mux.Connect(dev.DeviceId, Lockdown.Port), pair))
        {
            ld.StartSession();
            DeviceName = ld.GetValue("DeviceName");
            (port, ssl) = ld.StartService(Service);
        }
        using TcpClient c = mux.Connect(dev.DeviceId, port);
        c.ReceiveTimeout = 0;   // السجل قد يسكت دقائق
        using var reg = cts.Token.Register(() => { try { c.Dispose(); } catch { } });
        Stream s = c.GetStream();
        if (ssl) s = Lockdown.Tls(s, pair);
        DeviceTrace.Log("السجل المباشر: بدأ القراءة");
        Read(s, cts.Token);
        DeviceTrace.Log($"السجل المباشر: انتهى ({flags.Count} سطر مميّز)");
    }

    /// <summary>تقسيم التدفق إلى رسائل (الصفر أو السطر الجديد يفصلان) — منفصل للاختبار</summary>
    public void Read(Stream s, CancellationToken ct)
    {
        var buf = new byte[8192];
        var pending = new List<byte>();
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = s.Read(buf, 0, buf.Length); }
            catch (Exception) when (ct.IsCancellationRequested) { return; }
            if (n <= 0) break;
            for (int i = 0; i < n; i++)
            {
                byte b = buf[i];
                if (b == 0 || b == (byte)'\n')
                {
                    if (pending.Count > 0) Emit(Encoding.UTF8.GetString(pending.ToArray()));
                    pending.Clear();
                }
                else if (pending.Count < 16384) pending.Add(b);
            }
        }
        if (pending.Count > 0) Emit(Encoding.UTF8.GetString(pending.ToArray()));
    }

    void Emit(string line)
    {
        Line?.Invoke(line);
        if (LiveLog.Classify(line, DateTime.Now) is { } f)
        {
            lock (gate) { flags.Add(f); if (flags.Count > 2000) flags.RemoveRange(0, 500); }
            Flagged?.Invoke(f);
        }
    }

    public void Stop() { try { cts.Cancel(); } catch { } }

    public void Dispose() { Stop(); cts.Dispose(); }
}
