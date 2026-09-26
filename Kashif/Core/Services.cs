using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Kashif;

// ============================== النسخ الاحتياطي ==============================
public static class Backup
{
    public static string LocalDir
    {
        get
        {
            var d = Settings.Get("backup_dir");
            return string.IsNullOrWhiteSpace(d) ? Path.Combine(Db.DataDir, "Backups") : d;
        }
    }

    public static string Run()
    {
        Directory.CreateDirectory(LocalDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var file = Path.Combine(LocalDir, $"kashif_{stamp}.db");
        // نسختان في نفس الثانية (مثل نسخة الأمان قبل الاستعادة) لا تكتب إحداهما فوق الأخرى
        for (int k = 2; File.Exists(file); k++) file = Path.Combine(LocalDir, $"kashif_{stamp}_{k}.db");
        using (var src = Db.Open())
        using (var dst = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            dst.Open();
            src.BackupDatabase(dst);
        }

        int keep = Math.Max(3, Settings.Int("backup_keep", 30));
        Prune(LocalDir, keep);

        var cloud = Settings.Get("cloud_dir");
        if (!string.IsNullOrWhiteSpace(cloud))
        {
            Directory.CreateDirectory(cloud);
            File.Copy(file, Path.Combine(cloud, Path.GetFileName(file)), true);
            Prune(cloud, keep);
        }
        return file;
    }

    static void Prune(string dir, int keep)
    {
        foreach (var f in Directory.GetFiles(dir, "kashif_*.db").OrderByDescending(x => x).Skip(keep))
            try { File.Delete(f); } catch { }
    }

    /// <summary>يتحقق أن الملف نسخة سليمة من قاعدة بيانات كاشف قبل استبدال البيانات الحالية</summary>
    public static bool IsValidBackup(string file, out string error)
    {
        error = "";
        try
        {
            using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check";
            if (Convert.ToString(cmd.ExecuteScalar()) != "ok") { error = "الملف تالف ولا يمكن استعادته."; return false; }
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('analyses','kb_rules','users')";
            if (Convert.ToInt64(cmd.ExecuteScalar()) < 3) { error = "هذا الملف ليس نسخة احتياطية من برنامج كاشف."; return false; }
            return true;
        }
        catch (Exception ex) { error = "تعذر قراءة الملف: " + ex.Message; return false; }
    }

    public static void Restore(string file)
    {
        if (!IsValidBackup(file, out var err)) throw new InvalidOperationException(err);
        try { Run(); } catch { }           // نسخة أمان قبل الاستعادة
        // دمج سجل WAL في الملف ثم إغلاق كل الاتصالات، وحذف ملفات السجل القديمة حتى لا تُطبَّق على النسخة المستعادة
        try
        {
            using var c = Db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch { }
        SqliteConnection.ClearAllPools();
        File.Copy(file, Db.FilePath, true);
        foreach (var ext in new[] { "-wal", "-shm" })
            try { File.Delete(Db.FilePath + ext); } catch { }
        Settings.Reload();
    }
}
