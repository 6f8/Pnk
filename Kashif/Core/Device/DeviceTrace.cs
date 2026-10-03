using System.Runtime.InteropServices;
using System.Text;

namespace Kashif.Device;

/// <summary>
/// سجل تشخيص الاتصال بالآيفون: خطوات البروتوكول ونتائجها ورموز الأخطاء، لإرساله عند فشل ميزة على جهاز حقيقي.
/// لا يُكتب فيه شيء من بيانات الزبون: لا رقم تسلسلي ولا IMEI ولا شهادات، والـ UDID مختصر.
/// </summary>
public static class DeviceTrace
{
    static readonly object gate = new();
    static List<string> lines = new();
    static string title = "";

    /// <summary>بدء سجل جديد لعملية (سحب، فحص، سجل مباشر...)</summary>
    public static void Begin(string what)
    {
        lock (gate) { lines = new(); title = what; }
        Log("بدء: " + what);
        Log($"ويندوز: {RuntimeInformation.OSDescription} · .NET {Environment.Version} · {RuntimeInformation.ProcessArchitecture}");
    }

    public static void Log(string msg)
    {
        lock (gate)
        {
            lines.Add($"{DateTime.Now:HH:mm:ss.fff}  {msg}");
            if (lines.Count > 3000) lines.RemoveRange(0, 1000);
        }
    }

    public static void Error(string where, Exception ex) =>
        Log($"خطأ في {where}: {ex.GetType().Name}: {ex.Message}" + (ex.InnerException != null ? $" ← {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : ""));

    /// <summary>معرّف مختصر لا يكشف الجهاز (أول 8 أحرف)</summary>
    public static string Mask(string id) => string.IsNullOrEmpty(id) ? "(فارغ)" : id.Length <= 8 ? id : id[..8] + "…";

    public static string Text
    {
        get
        {
            lock (gate)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"كاشف — سجل تشخيص الاتصال: {title}");
                sb.AppendLine("لا يحتوي بيانات الزبون (لا رقم تسلسلي ولا IMEI ولا شهادات).");
                sb.AppendLine(new string('-', 60));
                foreach (var l in lines) sb.AppendLine(l);
                return sb.ToString();
            }
        }
    }

    public static bool Empty { get { lock (gate) return lines.Count == 0; } }

    /// <summary>يحفظ السجل في المجلد ويعيد مسار الملف</summary>
    public static string Save(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"kashif-device-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, Text, new UTF8Encoding(true));
        return path;
    }
}
