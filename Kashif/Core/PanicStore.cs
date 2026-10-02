using System.Data;

namespace Kashif;

/// <summary>حفظ التحليلات في سجل الفحوصات، والتعلّم من نتائج المحل، وقراءة خبرة المحل من قاعدة البيانات</summary>
public static class PanicStore
{
    /// <summary>سجل فحص محفوظ: نص السجلات الأصلي كما هو (يُعاد تحليله عند الفتح بأحدث قاعدة معرفة)</summary>
    public sealed class Record
    {
        public long Id;
        public string Customer = "", Phone = "", Notes = "", Status = "قيد الفحص", Date = "", FixedPart = "", FixedDate = "";
        public CaseFlags Flags = new();
        public List<(string Id, int Answer)> Answers = new();
        public List<(string Source, string Raw)> Logs = new();
    }

    public static string[] Statuses => StoreSql.Statuses;

    static List<CustomRule> rules;
    static long rulesVersion = -1;

    /// <summary>قواعد خبرة المحل الفعّالة (تُقرأ من جديد فقط إذا تغيّرت البيانات)</summary>
    public static List<CustomRule> Rules()
    {
        if (rules != null && rulesVersion == Db.Version) return rules;
        var list = new List<CustomRule>();
        foreach (DataRow r in Db.Query(StoreSql.RulesActive).Rows)
            list.Add(new CustomRule
            {
                Id = Db.L(r["id"]), Name = Db.S(r["name"]), Pattern = Db.S(r["pattern"]), Device = Db.S(r["device"]),
                Part = Db.S(r["part"]), Level = CustomRule.Levels.Contains(Db.S(r["level"])) ? Db.S(r["level"]) : "شائع", Note = Db.S(r["note"]),
                IsRegex = Db.L(r["is_regex"]) == 1, Priority = (int)Math.Clamp(Db.L(r["priority"]), -20, 20),
            });
        rules = list;
        rulesVersion = Db.Version;
        return rules;
    }

    /// <summary>حفظ تحليل جديد أو تحديث سجل موجود — يعيد رقم السجل</summary>
    public static long Save(long id, Diagnosis d, IEnumerable<PanicLog> logs, CaseFlags flags, string customer, string phone, string notes, string status, string fixedPart,
                            IEnumerable<(string Id, int Answer)> answers = null)
    {
        string fixedDate = "";
        if (id > 0 && (fixedPart ?? "").Trim() != "")
        {
            // تاريخ تسجيل النتيجة لا يتغير عند كل حفظ — فقط إذا تغيّرت القطعة
            var old = Db.Query("SELECT IFNULL(fixed_part,'') AS p, IFNULL(fixed_date,'') AS d FROM analyses WHERE id=@p0", id);
            if (old.Rows.Count == 1 && Db.S(old.Rows[0]["p"]) == fixedPart.Trim()) fixedDate = Db.S(old.Rows[0]["d"]);
        }
        if (fixedDate == "") fixedDate = Ui.Now;
        var p = StoreSql.Values(d, logs, flags, customer, phone, notes, status, fixedPart, fixedDate, PanicAnalyzer.Report(d), answers);
        if (id > 0 && Db.L(Db.Scalar("SELECT COUNT(*) FROM analyses WHERE id=@p0", id)) > 0)
        {
            Db.Exec(StoreSql.Update, p.Append(id).ToArray());
            return id;
        }
        return Db.Insert(StoreSql.Insert, p.Append(Ui.Now).Append(Session.UserId).ToArray());
    }

    public static Record Load(long id)
    {
        var dt = Db.Query(StoreSql.Select, id);
        if (dt.Rows.Count == 0) return null;
        var r = dt.Rows[0];
        return new Record
        {
            Id = id, Customer = Db.S(r["customer"]), Phone = Db.S(r["phone"]), Notes = Db.S(r["notes"]),
            Status = Db.S(r["status"]), Date = Db.S(r["date"]), Flags = CaseFlags.Decode(Db.S(r["flags"])),
            FixedPart = Db.S(r["fixed_part"]), FixedDate = Db.S(r["fixed_date"]), Logs = StoreSql.DecodeLogs(Db.S(r["raw"])),
            Answers = PanicAnalyzer.DecodeAnswers(Db.S(r["answers"])),
        };
    }

    /// <summary>الفحوصات السابقة لنفس الجهاز (بمفتاح التقارير) — يظهر أن الجهاز جاء من قبل</summary>
    public static DataTable Previous(string deviceKey, long exceptId) =>
        string.IsNullOrEmpty(deviceKey) ? new DataTable() : Db.Query(StoreSql.Previous, deviceKey, exceptId);

    /// <summary>عدد الأجهزة الأخرى التي ظهر عليها نفس النمط بنفس رقم بناء iOS</summary>
    /// <summary>خبرة المحل للتحليل المعروض: بنفس البصمة، وبنفس مصفوفة حساسات SMC على نفس الموديل</summary>
    public static (List<(string Part, int Count)> BySignature, List<(string Part, int Count)> BySensorArray) ShopHistory(Diagnosis d, long exceptId)
    {
        List<(string, int)> Rows(System.Data.DataTable t) => t.Rows.Cast<System.Data.DataRow>().Select(r => (Db.S(r["part"]), (int)Db.L(r["n"]))).ToList();
        var bySig = d == null || d.Signature == "" ? new() : Rows(Db.Query(StoreSql.HistoryBySignature, d.Signature, exceptId));
        var key = PanicAnalyzer.SensorArrayKey(d);
        var byArr = key == "" || d.Product == "" ? new() : Rows(Db.Query(StoreSql.HistoryBySensorArray, d.Product, key, exceptId));
        return (bySig, byArr);
    }

    public static long SameBuildDevices(Diagnosis d) =>
        d == null || d.Build == "" || d.Signature == "" ? 0 : Db.L(Db.Scalar(StoreSql.SameBuildDevices, d.Build, d.Signature, d.Log?.DeviceKey ?? ""));

    /// <summary>تجربة قاعدة على الفحوصات المحفوظة (قبل حفظها في خبرة المحل)</summary>
    public static StoreSql.RuleTest TestRule(CustomRule rule) =>
        StoreSql.TestRule(rule, Db.Query(StoreSql.RuleTestRows).Rows.Cast<DataRow>()
            .Select(r => (Db.L(r["id"]), Db.S(r["device"]), Db.S(r["product"]), Db.S(r["fixed_part"]), Db.S(r["raw"]))).ToList());
}

/// <summary>أرقام الرئيسية</summary>
public static class Stats
{
    public static long Today() => Db.L(Db.Scalar("SELECT COUNT(*) FROM analyses WHERE date>=@p0", Ui.Today));
    public static long Total() => Db.L(Db.Scalar("SELECT COUNT(*) FROM analyses"));
    public static long Open() => Db.L(Db.Scalar("SELECT COUNT(*) FROM analyses WHERE status IN ('قيد الفحص','بانتظار قطعة')"));
    public static long Rules() => Db.L(Db.Scalar("SELECT COUNT(*) FROM kb_rules WHERE active=1"));
    /// <summary>عدد التنبيهات على الجرس: الفحوصات المفتوحة</summary>
    public static long AlertsCount() => Open();

    /// <summary>دقة الأرجح في الفحوصات التي سُجّلت قطعتها المُصلِحة: (النسبة، العدد) — العدد 0 إن لم تُسجّل نتائج</summary>
    public static (double Percent, long Count) Accuracy()
    {
        var r = Db.Query("SELECT COUNT(*) AS n, SUM(CASE WHEN top_part=fixed_part THEN 1 ELSE 0 END) AS ok FROM analyses WHERE IFNULL(fixed_part,'')<>''").Rows[0];
        long n = Db.L(r["n"]);
        return (n == 0 ? 0 : 100.0 * Db.L(r["ok"]) / n, n);
    }

    public static string TopPart(int days = 30) =>
        Db.S(Db.Scalar("SELECT top_part FROM analyses WHERE IFNULL(top_part,'')<>'' AND date>=date('now','localtime',@p0) GROUP BY top_part ORDER BY COUNT(*) DESC LIMIT 1", $"-{days} day"));

    /// <summary>القطع الأكثر إصلاحًا: القطعة المُصلِحة إن سُجّلت، وإلا الأرجح</summary>
    public static List<(string, double)> PartsChart(int days = 90) =>
        Db.Query(@"SELECT CASE WHEN IFNULL(fixed_part,'')<>'' THEN fixed_part ELSE top_part END AS part, COUNT(*) AS n FROM analyses
                   WHERE IFNULL(top_part,'')<>'' AND date>=date('now','localtime',@p0) GROUP BY part ORDER BY n DESC LIMIT 8", $"-{days} day")
          .Rows.Cast<DataRow>().Select(r => (Db.S(r["part"]), Db.D(r["n"]))).ToList();
}
