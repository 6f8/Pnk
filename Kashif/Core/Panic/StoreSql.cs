using System.Text.Json;

namespace Kashif;

/// <summary>
/// جداول قاعدة البيانات واستعلامات سجل الفحوصات وترميز السجلات المحفوظة — بلا اعتماد على الواجهة،
/// فتُختبر مباشرة على SQLite (Kashif.Tests) ويستخدمها <see cref="PanicStore"/> في البرنامج.
/// المعاملات بالأسماء @p0، @p1 ... بنفس ترتيب المصفوفات التي تبنيها الدوال هنا.
/// </summary>
public static class StoreSql
{
    public const string Schema = @"
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY, username TEXT UNIQUE NOT NULL, pass_hash TEXT NOT NULL,
    full_name TEXT, is_admin INTEGER DEFAULT 0, active INTEGER DEFAULT 1);
CREATE TABLE IF NOT EXISTS user_perms(user_id INTEGER, perm TEXT, PRIMARY KEY(user_id,perm));
CREATE TABLE IF NOT EXISTS audit_log(id INTEGER PRIMARY KEY, date TEXT, user_id INTEGER, action TEXT, details TEXT);

-- سجل الفحوصات: كل تحليل محفوظ مع نص السجلات الأصلي (يُعاد تحليله لاحقًا بقاعدة معرفة أحدث)
CREATE TABLE IF NOT EXISTS analyses(id INTEGER PRIMARY KEY, date TEXT NOT NULL, user_id INTEGER,
    customer TEXT, phone TEXT, device TEXT, product TEXT, device_key TEXT, ios TEXT, panic_time TEXT,
    kind TEXT, title TEXT, top_part TEXT, confidence TEXT, logs INTEGER DEFAULT 1, flags TEXT,
    result TEXT, notes TEXT, raw TEXT, status TEXT DEFAULT 'قيد الفحص');

-- خبرة المحل: نص يظهر في السجل (أو رمز حساس أو تعبير منتظم) ← القطعة التي كانت السبب فعلًا
CREATE TABLE IF NOT EXISTS kb_rules(id INTEGER PRIMARY KEY, name TEXT NOT NULL, pattern TEXT NOT NULL, device TEXT,
    part TEXT NOT NULL, level TEXT DEFAULT 'شائع', note TEXT, active INTEGER DEFAULT 1);
";

    /// <summary>أعمدة أُضيفت بعد الإصدار الأول (تُضاف لقواعد البيانات القديمة عند التشغيل)</summary>
    public static readonly (string Table, string Col, string Def)[] Migrations =
    {
        // الإصدار 2: التعلّم من نتائج المحل
        ("analyses", "signature", "TEXT"),        // بصمة النمط (sensor:Prs0، smc:...، service:wifid)
        ("analyses", "learn_pattern", "TEXT"),    // نص تطابقه قاعدة مقترحة
        ("analyses", "top3", "TEXT"),             // أول 3 أسباب مفصولة بسطر جديد (لحساب الدقة)
        ("analyses", "build", "TEXT"),            // رقم بناء iOS (لكشف البانك البرمجي المشترك)
        ("analyses", "fixed_part", "TEXT"),       // القطعة التي أصلحت الجهاز فعلًا
        ("analyses", "fixed_date", "TEXT"),
        ("kb_rules", "is_regex", "INTEGER DEFAULT 0"),
        // الإصدار 3: أجوبة الفحص التفاعلي (id=رقم الجواب;...)
        ("analyses", "answers", "TEXT"),
        ("kb_rules", "priority", "INTEGER DEFAULT 0"),
        // الإصدار 4: مصفوفة حساسات SMC (الخانات غير الصفرية) — يتعلّم المحل معنى أرقامها من الحالات المؤكدة
        ("analyses", "smc_array", "TEXT"),
    };

    public const string Indexes = @"
CREATE INDEX IF NOT EXISTS ix_analyses_date ON analyses(date);
CREATE INDEX IF NOT EXISTS ix_analyses_device_key ON analyses(device_key);
CREATE INDEX IF NOT EXISTS ix_analyses_kind ON analyses(kind);
CREATE INDEX IF NOT EXISTS ix_analyses_signature ON analyses(signature);
CREATE INDEX IF NOT EXISTS ix_analyses_build ON analyses(build);
CREATE INDEX IF NOT EXISTS ix_audit_date ON audit_log(date);";

    // ------------------------------------------------------------------ حفظ وقراءة الفحص
    const string Cols = "customer, phone, device, product, device_key, ios, panic_time, kind, title, top_part, confidence, logs, flags, result, notes, raw, status, " +
                        "signature, learn_pattern, top3, build, fixed_part, fixed_date, answers, smc_array";
    const int ColCount = 25;

    public static readonly string Insert =
        $"INSERT INTO analyses({Cols}, date, user_id) VALUES({string.Join(",", Enumerable.Range(0, ColCount + 2).Select(i => "@p" + i))})";

    public static readonly string Update =
        "UPDATE analyses SET " + string.Join(", ", Cols.Split(',').Select((c, i) => $"{c.Trim()}=@p{i}")) + $" WHERE id=@p{ColCount}";

    public const string Select = "SELECT * FROM analyses WHERE id=@p0";

    public static readonly string[] Statuses = { "قيد الفحص", "بانتظار قطعة", "جاهز", "تم التسليم", "لا يصلح" };

    /// <summary>قيم الأعمدة بترتيب Cols (المعاملات @p0 ... @p23)</summary>
    public static object[] Values(Diagnosis d, IEnumerable<PanicLog> logs, CaseFlags flags, string customer, string phone, string notes, string status,
                                  string fixedPart, string fixedDate, string report, IEnumerable<(string Id, int Answer)> answers = null)
    {
        var top3 = string.Join("\n", d.Candidates.Take(3).Select(c => c.Part));
        return new object[]
        {
            (customer ?? "").Trim(), (phone ?? "").Trim(), d.Device, d.Product, d.Log?.DeviceKey ?? "", d.Ios + (d.Build != "" ? $" ({d.Build})" : ""), d.Time,
            d.Kind, d.Title, d.TopPart, d.Confidence, d.LogCount, (flags ?? new CaseFlags()).Encode(),
            report, (notes ?? "").Trim(), EncodeLogs(logs), Statuses.Contains(status) ? status : Statuses[0],
            d.Signature, d.LearnPattern, top3, d.Build, (fixedPart ?? "").Trim(), (fixedPart ?? "").Trim() == "" ? "" : fixedDate ?? "",
            PanicAnalyzer.EncodeAnswers(answers), PanicAnalyzer.SensorArrayKey(d),
        };
    }

    /// <summary>نص السجلات الأصلي كما هو: [[المصدر، النص], ...]</summary>
    public static string EncodeLogs(IEnumerable<PanicLog> logs) => JsonSerializer.Serialize(logs.Select(l => new[] { l.Source, l.Raw }).ToList());

    public static List<(string Source, string Raw)> DecodeLogs(string raw)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        try
        {
            foreach (var x in JsonSerializer.Deserialize<List<string[]>>(raw) ?? new())
                if (x != null && x.Length == 2) list.Add((x[0] ?? "", x[1] ?? ""));
        }
        catch (JsonException) { list.Add(("سجل محفوظ", raw)); }
        return list;
    }

    // ------------------------------------------------------------------ التعلّم من النتائج
    /// <summary>الفحوصات السابقة لنفس الجهاز: @p0 مفتاح الجهاز، @p1 الفحص الحالي (يُستثنى)</summary>
    public const string Previous = @"SELECT id, date AS [التاريخ], title AS [التشخيص], top_part AS [الأرجح], IFNULL(fixed_part,'') AS [القطعة المُصلِحة], status AS [الحالة]
        FROM analyses WHERE device_key=@p0 AND id<>@p1 ORDER BY id DESC LIMIT 20";

    /// <summary>
    /// نفس البصمة على أجهزة أخرى بنفس رقم بناء iOS: @p0 البناء، @p1 البصمة، @p2 مفتاح الجهاز الحالي.
    /// إذا تكرر على أكثر من جهاز فالسبب قد يكون برمجيًا في هذا الإصدار.
    /// </summary>
    /// <summary>خبرة المحل بنفس البصمة: القطع التي أصلحت فعلًا فحوصات سابقة بنفس النمط (@p0 البصمة، @p1 الفحص الحالي يُستثنى)</summary>
    public const string HistoryBySignature = @"SELECT TRIM(fixed_part) AS part, COUNT(*) AS n FROM analyses
        WHERE signature=@p0 AND @p0<>'' AND id<>@p1 AND TRIM(IFNULL(fixed_part,''))<>'' GROUP BY TRIM(fixed_part) ORDER BY n DESC";

    /// <summary>نفس مصفوفة حساسات SMC على نفس الموديل (@p0 الموديل، @p1 المصفوفة، @p2 الفحص الحالي)</summary>
    public const string HistoryBySensorArray = @"SELECT TRIM(fixed_part) AS part, COUNT(*) AS n FROM analyses
        WHERE product=@p0 AND smc_array=@p1 AND @p1<>'' AND id<>@p2 AND TRIM(IFNULL(fixed_part,''))<>'' GROUP BY TRIM(fixed_part) ORDER BY n DESC";

    public const string SameBuildDevices = @"SELECT COUNT(DISTINCT device_key) FROM analyses
        WHERE IFNULL(build,'')<>'' AND build=@p0 AND IFNULL(signature,'')<>'' AND signature=@p1 AND IFNULL(device_key,'')<>@p2";

    /// <summary>دقة التشخيص حسب النوع (للفحوصات التي سُجّلت قطعتها المُصلِحة): @p0 من، @p1 إلى</summary>
    public const string Accuracy = @"
SELECT 0 AS id, kind AS [النوع], COUNT(*) AS [فحوصات بنتيجة],
       SUM(CASE WHEN top_part=fixed_part THEN 1 ELSE 0 END) AS [الأرجح كان صحيحًا],
       SUM(CASE WHEN instr(char(10)||IFNULL(top3,'')||char(10), char(10)||fixed_part||char(10))>0 THEN 1 ELSE 0 END) AS [ضمن أول 3],
       ROUND(100.0*SUM(CASE WHEN top_part=fixed_part THEN 1 ELSE 0 END)/COUNT(*),1) AS [الدقة %]
FROM analyses WHERE IFNULL(fixed_part,'')<>'' AND date BETWEEN @p0 AND @p1 GROUP BY kind
UNION ALL
SELECT 0, 'الكل', COUNT(*), SUM(CASE WHEN top_part=fixed_part THEN 1 ELSE 0 END),
       SUM(CASE WHEN instr(char(10)||IFNULL(top3,'')||char(10), char(10)||fixed_part||char(10))>0 THEN 1 ELSE 0 END),
       ROUND(100.0*SUM(CASE WHEN top_part=fixed_part THEN 1 ELSE 0 END)/MAX(COUNT(*),1),1)
FROM analyses WHERE IFNULL(fixed_part,'')<>'' AND date BETWEEN @p0 AND @p1
ORDER BY [فحوصات بنتيجة] DESC";

    /// <summary>
    /// قواعد مقترحة: نفس النمط (نص قابل للمطابقة) ونفس القطعة المُصلِحة 3 مرات على الأقل، أخطأ فيها البرنامج مرة على الأقل،
    /// ولا توجد لها قاعدة في خبرة المحل. @p0 الحد الأدنى للتكرار.
    /// </summary>
    public const string Suggestions = @"
SELECT 0 AS id, learn_pattern AS [النمط], IFNULL(device,'') AS [الجهاز], fixed_part AS [القطعة المُصلِحة], COUNT(*) AS [مرات],
       SUM(CASE WHEN top_part<>fixed_part THEN 1 ELSE 0 END) AS [أخطأ البرنامج], MAX(date) AS [آخر مرة]
FROM analyses a
WHERE IFNULL(fixed_part,'')<>'' AND IFNULL(learn_pattern,'')<>''
  AND NOT EXISTS (SELECT 1 FROM kb_rules r WHERE r.active=1 AND r.pattern=a.learn_pattern AND r.part=a.fixed_part)
GROUP BY learn_pattern, device, fixed_part
HAVING COUNT(*)>=@p0 AND SUM(CASE WHEN top_part<>fixed_part THEN 1 ELSE 0 END)>0
ORDER BY [مرات] DESC";

    /// <summary>الفحوصات التي تُجرَّب عليها قاعدة جديدة (آخر 2000): النص المحفوظ والجهاز والقطعة المُصلِحة</summary>
    public const string RuleTestRows = "SELECT id, device, product, IFNULL(fixed_part,'') AS fixed_part, raw FROM analyses ORDER BY id DESC LIMIT 2000";

    public const string RulesActive = "SELECT id, name, pattern, device, part, level, note, IFNULL(is_regex,0) AS is_regex, IFNULL(priority,0) AS priority FROM kb_rules WHERE active=1 ORDER BY id";

    /// <summary>نتيجة تجربة قاعدة على الفحوصات المحفوظة</summary>
    public sealed record RuleTest(int Checked, int Matched, int SamePart, int OtherPart, int NoOutcome, List<long> Ids);

    /// <summary>تجربة قاعدة على الفحوصات المحفوظة: كم فحصًا تطابقه، وكم منها أُصلح بنفس القطعة فعلًا</summary>
    public static RuleTest TestRule(CustomRule rule, IEnumerable<(long Id, string Device, string Product, string FixedPart, string Raw)> rows)
    {
        int checkedN = 0, matched = 0, same = 0, other = 0, none = 0;
        var ids = new List<long>();
        foreach (var r in rows)
        {
            checkedN++;
            bool hit = false;
            foreach (var (source, text) in DecodeLogs(r.Raw))
            {
                var log = PanicParser.Parse(text, source);
                var sensors = PanicAnalyzer.Analyze(log).MissingSensors;
                if (rule.Matches(log.PanicString, sensors, r.Device, r.Product)) { hit = true; break; }
            }
            if (!hit) continue;
            matched++;
            ids.Add(r.Id);
            if (r.FixedPart == "") none++;
            else if (r.FixedPart.Trim() == rule.Part.Trim()) same++;
            else other++;
        }
        return new RuleTest(checkedN, matched, same, other, none, ids);
    }
}
