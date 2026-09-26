using System.Data;
using System.Text.Json;

namespace Kashif;

/// <summary>حفظ التحليلات في سجل الفحوصات وقراءة خبرة المحل من قاعدة البيانات</summary>
public static class PanicStore
{
    /// <summary>سجل فحص محفوظ: نص السجلات الأصلي كما هو (يُعاد تحليله عند الفتح بأحدث قاعدة معرفة)</summary>
    public sealed class Record
    {
        public long Id;
        public string Customer = "", Phone = "", Notes = "", Status = "قيد الفحص", Date = "";
        public CaseFlags Flags = new();
        public List<(string Source, string Raw)> Logs = new();
    }

    public static readonly string[] Statuses = { "قيد الفحص", "بانتظار قطعة", "جاهز", "تم التسليم", "لا يصلح" };

    static List<CustomRule> rules;
    static long rulesVersion = -1;

    /// <summary>قواعد خبرة المحل الفعّالة (تُقرأ من جديد فقط إذا تغيّرت البيانات)</summary>
    public static List<CustomRule> Rules()
    {
        if (rules != null && rulesVersion == Db.Version) return rules;
        var list = new List<CustomRule>();
        foreach (DataRow r in Db.Query("SELECT id, name, pattern, device, part, level, note FROM kb_rules WHERE active=1 ORDER BY id").Rows)
            list.Add(new CustomRule
            {
                Id = Db.L(r["id"]), Name = Db.S(r["name"]), Pattern = Db.S(r["pattern"]), Device = Db.S(r["device"]),
                Part = Db.S(r["part"]), Level = CustomRule.Levels.Contains(Db.S(r["level"])) ? Db.S(r["level"]) : "شائع", Note = Db.S(r["note"]),
            });
        rules = list;
        rulesVersion = Db.Version;
        return rules;
    }

    /// <summary>حفظ تحليل جديد أو تحديث سجل موجود — يعيد رقم السجل</summary>
    public static long Save(long id, Diagnosis d, IEnumerable<PanicLog> logs, CaseFlags flags, string customer, string phone, string notes, string status)
    {
        var raw = JsonSerializer.Serialize(logs.Select(l => new[] { l.Source, l.Raw }).ToList());
        var key = d.Log?.DeviceKey ?? "";
        object[] p =
        {
            customer.Trim(), phone.Trim(), d.Device, d.Product, key, d.Ios + (d.Build != "" ? $" ({d.Build})" : ""), d.Time,
            d.Kind, d.Title, d.TopPart, d.Confidence, d.LogCount, (flags ?? new CaseFlags()).Encode(),
            PanicAnalyzer.Report(d), notes.Trim(), raw, Statuses.Contains(status) ? status : Statuses[0],
        };
        if (id > 0 && Db.L(Db.Scalar("SELECT COUNT(*) FROM analyses WHERE id=@p0", id)) > 0)
        {
            Db.Exec(@"UPDATE analyses SET customer=@p0, phone=@p1, device=@p2, product=@p3, device_key=@p4, ios=@p5, panic_time=@p6,
                kind=@p7, title=@p8, top_part=@p9, confidence=@p10, logs=@p11, flags=@p12, result=@p13, notes=@p14, raw=@p15, status=@p16
                WHERE id=@p17", p.Append(id).ToArray());
            return id;
        }
        return Db.Insert(@"INSERT INTO analyses(customer, phone, device, product, device_key, ios, panic_time, kind, title, top_part, confidence,
                logs, flags, result, notes, raw, status, date, user_id)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16,@p17,@p18)",
            p.Append(Ui.Now).Append(Session.UserId).ToArray());
    }

    public static Record Load(long id)
    {
        var dt = Db.Query("SELECT * FROM analyses WHERE id=@p0", id);
        if (dt.Rows.Count == 0) return null;
        var r = dt.Rows[0];
        var rec = new Record
        {
            Id = id, Customer = Db.S(r["customer"]), Phone = Db.S(r["phone"]), Notes = Db.S(r["notes"]),
            Status = Db.S(r["status"]), Date = Db.S(r["date"]), Flags = CaseFlags.Decode(Db.S(r["flags"])),
        };
        try
        {
            var list = JsonSerializer.Deserialize<List<string[]>>(Db.S(r["raw"])) ?? new();
            foreach (var x in list.Where(x => x != null && x.Length == 2)) rec.Logs.Add((x[0] ?? "", x[1] ?? ""));
        }
        catch (JsonException) { rec.Logs.Add(("سجل محفوظ", Db.S(r["raw"]))); }
        return rec;
    }

    /// <summary>عدد الفحوصات السابقة لنفس الجهاز (بمفتاح التقارير) — يظهر تنبيهًا بأن الجهاز جاء من قبل</summary>
    public static DataTable Previous(string deviceKey, long exceptId) =>
        string.IsNullOrEmpty(deviceKey) ? new DataTable()
        : Db.Query(@"SELECT id, date AS [التاريخ], title AS [التشخيص], top_part AS [الأرجح], status AS [الحالة]
            FROM analyses WHERE device_key=@p0 AND id<>@p1 ORDER BY id DESC LIMIT 20", deviceKey, exceptId);
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

    public static string TopPart(int days = 30) =>
        Db.S(Db.Scalar("SELECT top_part FROM analyses WHERE IFNULL(top_part,'')<>'' AND date>=date('now','localtime',@p0) GROUP BY top_part ORDER BY COUNT(*) DESC LIMIT 1", $"-{days} day"));

    public static List<(string, double)> PartsChart(int days = 90) =>
        Db.Query("SELECT top_part, COUNT(*) AS n FROM analyses WHERE IFNULL(top_part,'')<>'' AND date>=date('now','localtime',@p0) GROUP BY top_part ORDER BY n DESC LIMIT 8", $"-{days} day")
          .Rows.Cast<DataRow>().Select(r => (Db.S(r["top_part"]), Db.D(r["n"]))).ToList();
}
