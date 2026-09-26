using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Kashif;

/// <summary>المستخدم الحالي والصلاحيات</summary>
public static class Session
{
    public static long UserId;
    public static string UserName = "";
    public static bool IsAdmin;
    public static HashSet<string> Perms = new();

    public static readonly (string Key, string Title)[] AllPerms =
    {
        ("analyze", "تحليل سجلات البانك"), ("history", "سجل الفحوصات (عرض وحفظ)"), ("kb", "خبرة المحل (إضافة وتعديل)"),
        ("reports", "التقارير"), ("print", "الطباعة والتصدير"), ("delete", "الحذف"),
        ("users", "المستخدمون"), ("settings", "الإعدادات"), ("backup", "النسخ الاحتياطي"),
    };

    public static bool Can(string perm) => IsAdmin || perm == null || Perms.Contains(perm);

    public static bool Guard(string perm)
    {
        if (Can(perm)) return true;
        Dialogs.Warn("ليس لديك صلاحية لتنفيذ هذه العملية. اطلب من مدير النظام منحك الصلاحية.", "لا توجد صلاحية");
        return false;
    }

    /// <summary>كلمة المرور الافتراضية ما زالت مستخدمة؟ (تُطلب إعادة تعيينها عند أول دخول)</summary>
    public static bool UsingDefaultPassword;

    // PBKDF2-SHA256 مع ملح عشوائي لكل مستخدم: pbkdf2$التكرارات$الملح$الناتج
    const int Iterations = 120_000;

    public static string HashPassword(string pass)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Pbkdf2(pass, salt, Iterations, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    static byte[] Pbkdf2(string pass, byte[] salt, int iterations, int length)
    {
        try { return Rfc2898DeriveBytes.Pbkdf2(pass, salt, iterations, HashAlgorithmName.SHA256, length); }
        catch (CryptographicException) { return Pbkdf2Managed(pass, salt, iterations, length); }
    }

    /// <summary>نفس خوارزمية PBKDF2-HMAC-SHA256 (RFC 8018) بتنفيذ داخلي، احتياطًا إن لم يدعمها النظام</summary>
    static byte[] Pbkdf2Managed(string pass, byte[] salt, int iterations, int length)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pass));
        var result = new byte[length];
        var block = new byte[salt.Length + 4];
        salt.CopyTo(block, 0);
        for (int i = 1, offset = 0; offset < length; i++, offset += 32)
        {
            block[^4] = (byte)(i >> 24); block[^3] = (byte)(i >> 16); block[^2] = (byte)(i >> 8); block[^1] = (byte)i;
            var u = hmac.ComputeHash(block);
            var t = (byte[])u.Clone();
            for (int j = 1; j < iterations; j++)
            {
                u = hmac.ComputeHash(u);
                for (int k = 0; k < t.Length; k++) t[k] ^= u[k];
            }
            Array.Copy(t, 0, result, offset, Math.Min(t.Length, length - offset));
        }
        return result;
    }

    public static bool Verify(string pass, string stored)
    {
        var parts = (stored ?? "").Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out int it)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Pbkdf2(pass, salt, it, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    public static bool Login(string user, string pass)
    {
        var dt = Db.Query("SELECT id,username,full_name,is_admin,pass_hash FROM users WHERE username=@p0 COLLATE NOCASE AND active=1", user.Trim());
        if (dt.Rows.Count == 0) return false;
        var r = dt.Rows[0];
        if (!Verify(pass, Db.S(r["pass_hash"]))) return false;
        UserId = Db.L(r["id"]);
        UserName = Db.S(r["full_name"]) != "" ? Db.S(r["full_name"]) : Db.S(r["username"]);
        IsAdmin = Db.L(r["is_admin"]) == 1;
        Perms = Db.Query("SELECT perm FROM user_perms WHERE user_id=@p0", UserId)
                  .Rows.Cast<DataRow>().Select(x => Db.S(x["perm"])).ToHashSet();
        UsingDefaultPassword = pass == "admin" || string.Equals(pass, Db.S(r["username"]), StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public static void Logout()
    {
        UserId = 0; UserName = ""; IsAdmin = false; Perms = new(); UsingDefaultPassword = false;
    }
}

/// <summary>إعدادات البرنامج (مفتاح/قيمة)</summary>
public static class Settings
{
    public static readonly (string Key, string Caption, string Def)[] All =
    {
        ("shop_name", "اسم المحل", "محلي"),
        ("shop_phone", "هاتف المحل", ""),
        ("shop_address", "عنوان المحل (يظهر في الطباعة)", ""),
        ("shop_city", "المدينة", ""),
        ("shop_activity", "النشاط التجاري", "صيانة الهواتف"),
        ("ui_theme", "مظهر البرنامج (الألوان)", "classic"),
        ("invoice_footer", "تذييل المطبوعات", "التشخيص مبني على سجل الجهاز ويُؤكَّد بالفحص العملي"),
        ("print_mode", "حجم ورق الطباعة", "A4"),
        ("print_preview", "معاينة قبل الطباعة", "1"),
        ("printer_name", "الطابعة", ""),
        ("analyze_autosave", "حفظ كل تحليل في السجل تلقائيًا", "0"),
        ("analyze_combine", "تجميع سجلات نفس الجهاز في تحليل واحد", "1"),
        ("backup_dir", "مجلد النسخ الاحتياطي المحلي", ""),
        ("cloud_dir", "مجلد النسخ السحابي (Google Drive / OneDrive)", ""),
        ("backup_keep", "عدد النسخ المحتفظ بها", "30"),
        ("backup_on_exit", "نسخة تلقائية عند إغلاق البرنامج", "1"),
    };

    // الإعدادات تُقرأ من الذاكرة بعد أول قراءة؛ null = المفتاح غير موجود في قاعدة البيانات
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> cache = new();

    public static string Get(string key, string def = "")
    {
        if (!cache.TryGetValue(key, out var v))
        {
            var o = Db.Scalar("SELECT value FROM settings WHERE key=@p0", key);
            v = o == null || o is DBNull ? null : Convert.ToString(o, CultureInfo.InvariantCulture);
            cache[key] = v;
        }
        return v ?? def;
    }

    public static int Int(string key, int def) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    public static double Dbl(string key, double def) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : def;
    public static bool On(string key) => Get(key) == "1";

    public static void Set(string key, string value)
    {
        Db.Exec("INSERT OR REPLACE INTO settings(key,value) VALUES(@p0,@p1)", key, value);
        cache[key] = value;
    }

    /// <summary>تفريغ الذاكرة (بعد استعادة نسخة احتياطية مثلًا)</summary>
    public static void Reload() => cache.Clear();
}
