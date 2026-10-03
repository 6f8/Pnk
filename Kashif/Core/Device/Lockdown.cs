using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Kashif.Device;

/// <summary>
/// lockdownd داخل الآيفون (المنفذ 62078): معلومات الجهاز، فتح جلسة موثوقة بسجل الاقتران (TLS)، وتشغيل الخدمات.
/// الرسالة: طول 4 بايت (ترتيب الشبكة) ثم plist بصيغة XML.
/// </summary>
public sealed class Lockdown : IDisposable
{
    public const int Port = 62078;
    readonly TcpClient client;
    readonly Dictionary<string, object> pair;
    Stream stream;

    public Lockdown(TcpClient tunnel, Dictionary<string, object> pairRecord)
    {
        client = tunnel;
        pair = pairRecord;
        stream = tunnel.GetStream();
    }

    public static Dictionary<string, object> Send(Stream s, Dictionary<string, object> msg)
    {
        var body = Plist.ToXml(msg);
        var len = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, (uint)body.Length);
        s.Write(len);
        s.Write(body);
        s.Flush();
        uint n = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(Io.ReadExact(s, 4));
        if (n == 0 || n > 16 * 1024 * 1024) throw new DeviceException("رد غير صالح من الآيفون");
        return Plist.ParseDict(Io.ReadExact(s, (int)n));
    }

    Dictionary<string, object> Request(string request, Dictionary<string, object> extra = null)
    {
        var msg = new Dictionary<string, object> { ["Label"] = "Kashif", ["Request"] = request };
        if (extra != null) foreach (var kv in extra) msg[kv.Key] = kv.Value;
        var r = Send(stream, msg);
        var error = r.Str("Error");
        var label = request == "GetValue" && extra != null ? $"GetValue {extra.GetValueOrDefault("Key")}" : request;
        DeviceTrace.Log($"lockdown: {label} ← {(error != "" ? "خطأ " + error : "تم")}");
        if (error != "") throw new DeviceException(Explain(error), null);
        return r;
    }

    static string Explain(string error) => error switch
    {
        "PasswordProtected" => "الآيفون مقفل: افتح قفل الشاشة وأعد المحاولة.",
        "InvalidHostID" or "InvalidPairRecord" or "PairingDialogResponsePending" or "UserDeniedPairing" =>
            "الآيفون لا يثق بهذا الكمبيوتر. افتح iTunes أو Apple Devices، صِل الجهاز واضغط «ثق» على شاشته، ثم أعد المحاولة.",
        "SessionInactive" => "انتهت الجلسة مع الآيفون. أعد المحاولة.",
        "InvalidService" or "ServiceProhibited" => "الآيفون رفض خدمة سجلات الأعطال.",
        _ => "خطأ من الآيفون: " + error,
    };

    public string GetValue(string key)
    {
        try { return Request("GetValue", new() { ["Key"] = key }).TryGetValue("Value", out var v) ? v as string ?? "" : ""; }
        catch (DeviceException) { return ""; }
    }

    /// <summary>جلسة موثوقة بسجل الاقتران؛ بعدها تمر الرسائل عبر TLS إن طلب الجهاز ذلك</summary>
    public void StartSession()
    {
        var r = Request("StartSession", new() { ["HostID"] = pair.Str("HostID"), ["SystemBUID"] = pair.Str("SystemBUID") });
        DeviceTrace.Log("lockdown: الجلسة " + (r.Bool("EnableSessionSSL") ? "تطلب TLS" : "بلا TLS"));
        if (r.Bool("EnableSessionSSL")) stream = Tls(stream, pair);
    }

    /// <summary>تشغيل خدمة داخل الجهاز: يعيد منفذها وهل تحتاج TLS</summary>
    public (int Port, bool Ssl) StartService(string name)
    {
        var r = Request("StartService", new() { ["Service"] = name });
        long port = r.Long("Port");
        DeviceTrace.Log($"lockdown: الخدمة {name} ← المنفذ {port}" + (r.Bool("EnableServiceSSL") ? " (TLS)" : ""));
        if (port <= 0 || port > 65535) throw new DeviceException("الآيفون لم يشغّل الخدمة " + name);
        return ((int)port, r.Bool("EnableServiceSSL"));
    }

    /// <summary>TLS بشهادة الكمبيوتر من سجل الاقتران (شهادة الجهاز ذاتية التوقيع فلا يُتحقق منها)</summary>
    public static Stream Tls(Stream inner, Dictionary<string, object> pair)
    {
        var certPem = pair.Data("HostCertificate") ?? pair.Data("RootCertificate");
        var keyPem = pair.Data("HostPrivateKey") ?? pair.Data("RootPrivateKey");
        if (certPem == null || keyPem == null) throw new DeviceException("سجل الاقتران ناقص (لا توجد شهادة الكمبيوتر). أعد «الثقة» بالكمبيوتر من iTunes أو Apple Devices.");
        using var pem = X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(certPem), Encoding.ASCII.GetString(keyPem));
        // ويندوز لا يستخدم مفتاحًا مؤقتًا في TLS: يُعاد تحميل الشهادة مع مفتاحها
        var cert = new X509Certificate2(pem.Export(X509ContentType.Pkcs12));
        var ssl = new SslStream(inner, false);
        try
        {
            ssl.AuthenticateAsClient(new SslClientAuthenticationOptions
            {
                TargetHost = "iPhone",
                ClientCertificates = new X509CertificateCollection { cert },
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (s, c, ch, e) => true,
            });
        }
        catch (Exception ex) { DeviceTrace.Error("مصافحة TLS", ex); throw; }
        DeviceTrace.Log($"TLS: تم ({ssl.SslProtocol}, {ssl.NegotiatedCipherSuite})");
        return ssl;
    }

    public void Dispose()
    {
        try { stream?.Dispose(); } catch { }
        client.Dispose();
    }
}
