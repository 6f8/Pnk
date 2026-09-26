using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kashif;

/// <summary>
/// سجل بانك واحد: الحقول الأساسية من رأس ملف ‎.ips‎ وجسمه، ونص البانك (panicString) بعد فك رموز الهروب.
/// يُقرأ من الملف الأصلي (JSON سليم) أو من نص منسوخ أو ناقص أو مأخوذ من صورة (استخراج مرن).
/// </summary>
public sealed class PanicLog
{
    /// <summary>اسم الملف أو «نص ملصوق»</summary>
    public string Source = "";
    /// <summary>النص كما وصل (يُحفظ في السجل ليُعاد تحليله لاحقًا)</summary>
    public string Raw = "";
    public string BugType = "", Timestamp = "", OsVersion = "", Product = "", Kernel = "", IncidentId = "", CrashReporterKey = "", SocRevision = "";
    public string PanicString = "";
    /// <summary>قُرئ كـ JSON سليم (ملف أصلي) — وإلا فبالاستخراج المرن</summary>
    public bool FromJson;
    /// <summary>ملاحظات القراءة (نص ناقص، ليس بانك، ...)</summary>
    public readonly List<string> Notes = new();

    public bool IsEmpty => PanicString.Trim() == "" && Product == "";

    /// <summary>إصدار iOS ورقم البناء من «iPhone OS 26.5.2 (23F84)»</summary>
    public string IosVersion => Rx.OsVer.Match(OsVersion) is { Success: true } m ? m.Groups[1].Value : "";
    public string IosBuild => Rx.OsVer.Match(OsVersion) is { Success: true } m && m.Groups[2].Success ? m.Groups[2].Value : "";

    /// <summary>رمز المعالج من سطر النواة: RELEASE_ARM64_T8030 ← T8030</summary>
    public string SocCode => Rx.Soc.Match(Kernel + "\n" + PanicString) is { Success: true } m ? m.Groups[1].Value.ToUpperInvariant() : "";

    /// <summary>هوية الجهاز لتجميع سجلات الجهاز نفسه: مفتاح التقارير (ثابت للجهاز) وإلا الموديل</summary>
    public string DeviceKey => CrashReporterKey != "" ? CrashReporterKey.ToLowerInvariant() : Product != "" ? Product : "";

    static class Rx
    {
        public static readonly Regex OsVer = new(@"(\d+(?:\.\d+){0,2})\s*(?:\(([0-9A-Za-z]+)\))?", RegexOptions.Compiled);
        public static readonly Regex Soc = new(@"RELEASE_ARM64_([TS]\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}

/// <summary>قراءة سجلات البانك من ملف أو نص (سجل واحد أو عدة سجلات ملصوقة معًا)</summary>
public static class PanicParser
{
    /// <summary>بداية سجل جديد: رأس ملف ‎.ips‎ الذي يحتوي bug_type (يقبل «(» بدل «{» من نسخ الصور)</summary>
    static readonly Regex HeaderStart = new(@"[\{\(]\s*""?\s*bug_type\s*""?\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>علامات نص البانك عندما يُلصق وحده بلا رأس الملف</summary>
    static readonly Regex PanicMarkers = new(@"panic\s*\(cpu|Missing sensor|SMC PANIC|watchdog timeout|Debugger message|panicString|Kernel (?:data|instruction)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>كل السجلات في النص: يُقسَّم عند كل رأس bug_type</summary>
    public static List<PanicLog> ParseMany(string text, string source)
    {
        text = Normalize(text);
        var starts = HeaderStart.Matches(text).Select(m => m.Index).ToList();
        var list = new List<PanicLog>();
        if (starts.Count <= 1)
        {
            var one = Parse(text, source);
            if (!one.IsEmpty) list.Add(one);
            return list;
        }
        // نص قبل أول رأس (مثل سطر عنوان) لا يُعد سجلًا مستقلًا إلا إذا كان فيه نص بانك
        if (starts[0] > 0 && PanicMarkers.IsMatch(text[..starts[0]]))
        {
            var pre = Parse(text[..starts[0]], source);
            if (!pre.IsEmpty) list.Add(pre);
        }
        for (int i = 0; i < starts.Count; i++)
        {
            int end = i + 1 < starts.Count ? starts[i + 1] : text.Length;
            var log = Parse(text[starts[i]..end], starts.Count > 1 ? $"{source} ({i + 1})" : source);
            if (!log.IsEmpty) list.Add(log);
        }
        return list;
    }

    /// <summary>قراءة سجل واحد: JSON أولًا (الملف الأصلي)، ثم الاستخراج المرن</summary>
    public static PanicLog Parse(string text, string source)
    {
        text = Normalize(text ?? "");
        var log = new PanicLog { Source = source ?? "", Raw = text };
        if (!TryJson(text, log)) Loose(text, log);
        Finish(log);
        return log;
    }

    /// <summary>توحيد النص: إزالة علامات الاتجاه والمسافات الصفرية، وتوحيد علامات التنصيص ونهايات الأسطر</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\uFEFF' or '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F' or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E':
                    continue;
                case '\u201C' or '\u201D' or '\u201E' or '\u201F' or '\u2033' or '\u00AB' or '\u00BB': sb.Append('"'); break;
                case '\u2018' or '\u2019' or '\u201A' or '\u2032': sb.Append('\''); break;
                case '\u00A0': sb.Append(' '); break;
                case '\r': break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ JSON
    static readonly JsonDocumentOptions JsonOpts = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 };

    static bool TryJson(string text, PanicLog log)
    {
        var t = text.Trim();
        if (t.Length == 0 || t[0] != '{') return false;
        // الملف الأصلي: سطر رأس JSON ثم جسم JSON. بعض الأدوات تحفظه كائنًا واحدًا
        var parts = new List<string>();
        int nl = t.IndexOf('\n');
        if (nl > 0) { parts.Add(t[..nl]); parts.Add(t[(nl + 1)..]); }
        else parts.Add(t);

        bool any = false, body = false;
        void Take(JsonDocument doc)
        {
            using (doc)
            {
                any = true;
                Read(doc.RootElement, log);
                if (doc.RootElement.TryGetProperty("panicString", out _)) body = true;
            }
        }
        // كائن واحد (قد يكون منسقًا على عدة أسطر)، وإلا فسطر الرأس ثم الجسم
        if (TryParseObject(t, out var whole)) Take(whole);
        else
            foreach (var p in parts)
                if (TryParseObject(p, out var root)) Take(root);
        if (!any) return false;
        // سطر الرأس سليم لكن الجسم ليس JSON (منسوخ ناقص): نكمل بالاستخراج المرن
        if (!body) { Loose(text, log, keepExisting: true); return true; }
        log.FromJson = true;
        return true;
    }

    static bool TryParseObject(string s, out JsonDocument doc)
    {
        doc = null;
        s = s.Trim();
        if (s.Length < 2 || s[0] != '{') return false;
        try
        {
            doc = JsonDocument.Parse(s, JsonOpts);
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return true;
            doc.Dispose();
            doc = null;
            return false;
        }
        catch (JsonException) { return false; }
    }

    static void Read(JsonElement o, PanicLog log)
    {
        string Str(params string[] keys)
        {
            foreach (var k in keys)
                foreach (var p in o.EnumerateObject())
                    if (string.Equals(p.Name, k, StringComparison.OrdinalIgnoreCase))
                        return p.Value.ValueKind switch
                        {
                            JsonValueKind.String => p.Value.GetString() ?? "",
                            JsonValueKind.Number => p.Value.GetRawText(),
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => "",
                        };
            return "";
        }
        Set(ref log.BugType, Str("bug_type"));
        Set(ref log.Timestamp, Str("timestamp", "date"));
        Set(ref log.OsVersion, Str("os_version"));
        Set(ref log.Product, Str("product"));
        Set(ref log.Kernel, Str("kernel"));
        Set(ref log.IncidentId, Str("incident_id", "incident"));
        Set(ref log.CrashReporterKey, Str("crashReporterKey"));
        Set(ref log.SocRevision, Str("socRevision"));
        Set(ref log.PanicString, Str("panicString"));
        // بعض الإصدارات تضع رقم البناء في «build» بدل os_version
        if (log.OsVersion == "") Set(ref log.OsVersion, Str("build"));
    }

    static void Set(ref string field, string value)
    {
        if (field == "" && !string.IsNullOrWhiteSpace(value)) field = value;
    }

    // ------------------------------------------------------------------ الاستخراج المرن
    /// <summary>
    /// نص منسوخ من الشاشة أو من صورة: أقواس ناقصة، أسطر مكسورة داخل القيم، مسافات داخل أسماء المفاتيح،
    /// و«\n» مكتوبة حرفيًا. يُقرأ كل مفتاح بمفرده، والنص الكامل للبانك حتى المفتاح التالي أو نهاية النص.
    /// </summary>
    static void Loose(string text, PanicLog log, bool keepExisting = false)
    {
        string Short(string key) => Collapse(Unescape(ValueOf(text, key, longValue: false)));
        Set(ref log.BugType, Short("bug_type"));
        Set(ref log.Timestamp, Short("timestamp"));
        Set(ref log.OsVersion, Short("os_version"));
        Set(ref log.Product, Short("product"));
        Set(ref log.Kernel, Short("kernel"));
        Set(ref log.IncidentId, Short("incident_id"));
        if (log.IncidentId == "") Set(ref log.IncidentId, Short("incident"));
        Set(ref log.CrashReporterKey, Short("crashReporterKey"));
        Set(ref log.SocRevision, Short("socRevision"));
        if (log.OsVersion == "") Set(ref log.OsVersion, Short("build"));

        var ps = ValueOf(text, "panicString", longValue: true);
        if (ps != "") Set(ref log.PanicString, Unescape(ps));
        else if (log.PanicString == "" && PanicMarkers.IsMatch(text))
        {
            // نص البانك وحده بلا رأس الملف (مثل ما يُنسخ من «بيانات التحليلات»)
            log.PanicString = Unescape(text.Trim());
        }
        if (!keepExisting) log.FromJson = false;
    }

    /// <summary>قيمة مفتاح: اسم المفتاح يقبل مسافات بين حروفه وحالة أحرف مختلفة (نسخ الصور)</summary>
    static string ValueOf(string text, string key, bool longValue)
    {
        var name = string.Join(@"\s?", key.Select(c => Regex.Escape(c.ToString())));
        var m = Regex.Match(text, "\"\\s*" + name + "\\s*\"\\s*:\\s*", RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        int i = m.Index + m.Length;
        if (i >= text.Length) return "";
        if (text[i] != '"')
        {
            // قيمة بلا تنصيص (رقم مثل bug_type:210)
            var raw = Regex.Match(text[i..], @"^[^,\}\)\n]*");
            return raw.Value.Trim();
        }
        i++;
        var sb = new StringBuilder();
        for (; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\\' && i + 1 < text.Length) { sb.Append(ch).Append(text[i + 1]); i++; continue; }
            if (ch == '"')
            {
                if (!longValue) return sb.ToString();
                // نص البانك طويل وقد يحتوي علامة تنصيص شاردة من النسخ: ينتهي فقط عند «",» يتبعها مفتاح جديد أو «}» أو نهاية النص
                int j = SkipSpace(text, i + 1);
                if (j >= text.Length || text[j] == '}') return sb.ToString();
                if (text[j] == ',')
                {
                    int k = SkipSpace(text, j + 1);
                    if (k >= text.Length || text[k] == '"') return sb.ToString();
                }
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    static int SkipSpace(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    /// <summary>فك رموز الهروب في نصوص JSON (\n و \/ و \" و \uXXXX) — يعمل على النص المنسوخ الذي فيه «\n» حرفيًا</summary>
    public static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('\\')) return s ?? "";
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch != '\\' || i + 1 >= s.Length) { sb.Append(ch); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': break;
                case 'b' or 'f': break;
                case '/' or '\\' or '"' or '\'': sb.Append(n); break;
                case 'u' when i + 4 < s.Length && int.TryParse(s.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code):
                    sb.Append((char)code);
                    i += 4;
                    break;
                default: sb.Append('\\').Append(n); break;
            }
        }
        return sb.ToString();
    }

    static string Collapse(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    static void Finish(PanicLog log)
    {
        log.BugType = Collapse(log.BugType);
        log.Timestamp = Collapse(log.Timestamp);
        log.OsVersion = Collapse(log.OsVersion);
        log.Product = Collapse(log.Product).Replace(" ", "");
        log.Kernel = Collapse(log.Kernel);
        log.IncidentId = Collapse(log.IncidentId);
        log.CrashReporterKey = Collapse(log.CrashReporterKey).Replace(" ", "");
        log.SocRevision = Collapse(log.SocRevision);
        log.PanicString = log.PanicString.Trim();

        if (!log.FromJson && !log.IsEmpty)
            log.Notes.Add("قُرئ السجل بالاستخراج المرن (نص منسوخ أو ناقص أو من صورة) — النتيجة صحيحة ما دامت الأسطر المهمة مقروءة، والملف الأصلي ‎.ips‎ أدق.");
        if (log.PanicString == "" && !log.IsEmpty)
            log.Notes.Add("لا يوجد نص البانك (panicString) في هذا السجل — انسخ الملف كاملًا.");
    }
}
