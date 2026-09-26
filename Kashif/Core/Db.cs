using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Kashif;

/// <summary>طبقة قاعدة البيانات (SQLite) — ملف واحد محلي سهل النسخ الاحتياطي.</summary>
public static class Db
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kashif");

    public static string FilePath => Path.Combine(DataDir, "kashif.db");

    // الاتصالات تُعاد من مجمّع الاتصالات (Pooling) فلا يُفتح الملف من جديد مع كل استعلام
    static readonly string connStr = new SqliteConnectionStringBuilder
    {
        DataSource = FilePath, Pooling = true, ForeignKeys = true
    }.ToString();

    public static SqliteConnection Open()
    {
        var c = new SqliteConnection(connStr);
        c.Open();
        return c;
    }

    /// <summary>
    /// رقم يزيد مع كل عملية كتابة محفوظة: الشاشات تعرف به هل تغيّرت البيانات منذ آخر تحميل (بدل إعادة التحميل في كل مرة)
    /// </summary>
    static long version;
    public static long Version => Interlocked.Read(ref version);
    internal static void Changed() => Interlocked.Increment(ref version);

    internal static void Bind(SqliteCommand cmd, object[] p)
    {
        if (p == null) return;
        for (int i = 0; i < p.Length; i++)
            cmd.Parameters.AddWithValue("@p" + i, p[i] ?? DBNull.Value);
    }

    public static int Exec(string sql, params object[] p) { using var t = new Tx(); var r = t.Exec(sql, p); t.Commit(); return r; }
    public static long Insert(string sql, params object[] p) { using var t = new Tx(); var id = t.Insert(sql, p); t.Commit(); return id; }
    // القراءة تتم بلا معاملة كتابة: لا تحجز قفل الكتابة، فتعمل حتى أثناء معاملة مفتوحة على اتصال آخر
    public static object Scalar(string sql, params object[] p) { using var t = new Tx(write: false); return t.Scalar(sql, p); }
    public static DataTable Query(string sql, params object[] p) { using var t = new Tx(write: false); return t.Query(sql, p); }

    // تحويلات آمنة: لا ترمي استثناءً مهما كانت القيمة (نص فارغ، نص غير رقمي، رقم عشري في حقل صحيح...)
    public static double D(object o)
    {
        switch (o)
        {
            case null or DBNull: return 0;
            case double d: return double.IsFinite(d) ? d : 0;
            case long l: return l;
            case int i: return i;
            case decimal m: return (double)m;
            case float f: return double.IsFinite(f) ? f : 0;
            case bool b: return b ? 1 : 0;
            case string s:
                s = s.Replace(",", "").Replace("‎", "").Trim();
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : 0;
            default:
                try { var v2 = Convert.ToDouble(o, CultureInfo.InvariantCulture); return double.IsFinite(v2) ? v2 : 0; }
                catch { return 0; }
        }
    }

    public static long L(object o)
    {
        switch (o)
        {
            case null or DBNull: return 0;
            case long l: return l;
            case int i: return i;
            case string s when long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v): return v;
            default:
                double d = D(o);
                return d >= long.MaxValue ? long.MaxValue : d <= long.MinValue ? long.MinValue : (long)Math.Round(d);
        }
    }
    public static string S(object o) => o == null || o is DBNull ? "" : Convert.ToString(o, CultureInfo.InvariantCulture);
    public static object N(long id) => id > 0 ? (object)id : DBNull.Value;

    public static void Init()
    {
        Directory.CreateDirectory(DataDir);
        // WAL: القراءة لا تنتظر الكتابة، والحفظ أسرع (لا مزامنة كاملة للقرص مع كل عملية)
        using (var c = Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            try { cmd.ExecuteNonQuery(); } catch { /* قرص أو مجلد لا يدعم WAL: يبقى الوضع الافتراضي */ }
        }
        using (var t = new Tx())
        {
            t.Exec(Schema);
            foreach (var s in Settings.All)
                t.Exec("INSERT OR IGNORE INTO settings(key,value) VALUES(@p0,@p1)", s.Key, s.Def);
            Migrate(t);
            t.Exec(Indexes);
            if (L(t.Scalar("SELECT COUNT(*) FROM users")) == 0) Seed(t);
            t.Commit();
        }
    }

    /// <summary>ترقية قواعد البيانات القديمة: إضافة الأعمدة الجديدة إن لم تكن موجودة</summary>
    static void Migrate(Tx t)
    {
        var cols = new (string Table, string Col, string Def)[]
        {
            // لا ترقيات بعد — الإصدار الأول. أضف هنا أي عمود جديد: (الجدول، العمود، النوع)
        };
        foreach (var (table, col, def) in cols)
        {
            bool exists = t.Query($"PRAGMA table_info({table})").Rows.Cast<DataRow>().Any(r => S(r["name"]) == col);
            if (!exists) t.Exec($"ALTER TABLE {table} ADD COLUMN {col} {def}");
        }
    }

    /// <summary>سجل العمليات الحساسة (حذف، تعديل خبرة المحل، استعادة ...)</summary>
    public static void Audit(string action, string details)
    {
        try { Exec("INSERT INTO audit_log(date,user_id,action,details) VALUES(@p0,@p1,@p2,@p3)", Ui.Now, Session.UserId, action, details); } catch { }
    }

    /// <summary>أول تشغيل: المدير فقط (قاعدة المعرفة مدمجة في البرنامج)</summary>
    static void Seed(Tx t)
    {
        t.Exec("INSERT INTO users(username,pass_hash,full_name,is_admin) VALUES('admin',@p0,'المدير',1)", Session.HashPassword("admin"));
    }

    const string Indexes = @"
CREATE INDEX IF NOT EXISTS ix_analyses_date ON analyses(date);
CREATE INDEX IF NOT EXISTS ix_analyses_device_key ON analyses(device_key);
CREATE INDEX IF NOT EXISTS ix_analyses_kind ON analyses(kind);
CREATE INDEX IF NOT EXISTS ix_audit_date ON audit_log(date);";

    const string Schema = @"
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

-- خبرة المحل: نص يظهر في السجل (أو رمز حساس) ← القطعة التي كانت السبب فعلًا
CREATE TABLE IF NOT EXISTS kb_rules(id INTEGER PRIMARY KEY, name TEXT NOT NULL, pattern TEXT NOT NULL, device TEXT,
    part TEXT NOT NULL, level TEXT DEFAULT 'شائع', note TEXT, active INTEGER DEFAULT 1);
";
}

/// <summary>معاملة (Transaction) — كل عملية مركبة تُحفظ كاملة أو لا تُحفظ.</summary>
public sealed class Tx : IDisposable
{
    readonly SqliteConnection c;
    readonly SqliteTransaction t;
    bool wrote;

    /// <param name="write">false = اتصال للقراءة فقط بلا معاملة (لا يحجز قفل الكتابة)</param>
    public Tx(bool write = true)
    {
        c = Db.Open();
        if (!write) return;
        try { t = c.BeginTransaction(); }
        catch { c.Dispose(); throw; }
    }

    SqliteCommand Cmd(string sql, object[] p)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        Db.Bind(cmd, p);
        return cmd;
    }

    public int Exec(string sql, params object[] p)
    {
        using var cmd = Cmd(sql, p);
        int n = cmd.ExecuteNonQuery();
        wrote = true;
        return n;
    }
    public object Scalar(string sql, params object[] p) { using var cmd = Cmd(sql, p); return cmd.ExecuteScalar(); }
    public long Insert(string sql, params object[] p) { Exec(sql, p); return Db.L(Scalar("SELECT last_insert_rowid()")); }

    /// <summary>قراءة يدوية إلى DataTable (تتجنب مشاكل القيود في DataTable.Load)</summary>
    public DataTable Query(string sql, params object[] p)
    {
        var dt = new DataTable();
        using var cmd = Cmd(sql, p);
        using var r = cmd.ExecuteReader();
        int n = r.FieldCount;
        var rows = new List<object[]>();
        while (r.Read()) { var v = new object[n]; r.GetValues(v); rows.Add(v); }

        for (int i = 0; i < n; i++)
        {
            Type type = null;
            foreach (var row in rows)
            {
                var v = row[i];
                if (v is DBNull) continue;
                var vt = v is long ? typeof(long) : v is double ? typeof(double) : typeof(string);
                if (type == null) type = vt;
                else if (type != vt)
                    type = (type != typeof(string) && vt != typeof(string)) ? typeof(double) : typeof(string);
            }
            string name = r.GetName(i), nm = name; int k = 1;
            while (dt.Columns.Contains(nm)) nm = name + (++k);
            dt.Columns.Add(nm, type ?? typeof(string));
        }

        foreach (var row in rows)
        {
            var dr = dt.NewRow();
            for (int i = 0; i < n; i++)
            {
                var v = row[i];
                var ct = dt.Columns[i].DataType;
                dr[i] = v is DBNull ? DBNull.Value
                      : ct == typeof(string) ? Convert.ToString(v, CultureInfo.InvariantCulture)
                      : ct == typeof(double) ? Convert.ToDouble(v, CultureInfo.InvariantCulture)
                      : v;
            }
            dt.Rows.Add(dr);
        }
        return dt;
    }

    public void Commit()
    {
        if (t == null) return;
        t.Commit();
        if (wrote) Db.Changed();
    }
    public void Dispose() { t?.Dispose(); c.Dispose(); }
}
