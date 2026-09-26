using System.Globalization;

namespace Kashif;

static class Program
{
    /// <summary>وسيط التشغيل بعد الاستعادة (ينتظر إغلاق النسخة السابقة)</summary>
    public const string RestartArg = "--restarted";

    /// <summary>إعادة تشغيل البرنامج (بعد استعادة نسخة احتياطية)</summary>
    public static void Restart()
    {
        try { MainForm.Instance?.HideTray(); } catch { }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, RestartArg) { UseShellExecute = false }); }
        catch { }
        Environment.Exit(0);
    }

    /// <summary>ملفات البانك المُمرّرة عند التشغيل (فتح باستخدام كاشف، أو سحبها إلى أيقونة البرنامج)</summary>
    public static readonly List<string> StartupFiles = new();

    [STAThread]
    static void Main(string[] args)
    {
        foreach (var a in args ?? Array.Empty<string>())
            if (a != RestartArg && File.Exists(a)) StartupFiles.Add(a);

        // ثقافة ثابتة: تواريخ ميلادية وأرقام إنجليزية مهما كانت إعدادات ويندوز
        var ci = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = ci;
        Thread.CurrentThread.CurrentCulture = ci;

        // نسخة واحدة فقط: تشغيل نسختين معًا يسبب تعارض الكتابة على قاعدة البيانات
        using var mutex = new Mutex(true, @"Local\Kashif.SingleInstance", out bool first);
        // بعد استعادة نسخة احتياطية يُعاد تشغيل البرنامج والنسخة القديمة ما زالت تُغلق: ننتظرها قليلًا
        // (كان التشغيل الجديد يظهر «البرنامج يعمل مسبقًا» ويُغلق، فيبقى المستخدم بلا برنامج)
        if (!first && Environment.GetCommandLineArgs().Contains(RestartArg))
        {
            try { first = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            MessageBox.Show(StartupFiles.Count > 0 ? "البرنامج يعمل مسبقًا — اسحب الملف إلى نافذة «تحليل البانك» المفتوحة." : "البرنامج يعمل مسبقًا على هذا الجهاز.", "كاشف", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }

        ApplicationConfiguration.Initialize();
        FontKit.Init();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

        try { Db.Init(); }
        catch (Exception ex)
        {
            Log(ex);
            Dialogs.Error("تعذر فتح قاعدة البيانات:\n" + ex.Message, "كاشف");
            return;
        }
        // المظهر المختار من الإعدادات (يُطبَّق قبل بناء أي شاشة)
        try { Theme.Apply(Settings.Get("ui_theme", "classic")); } catch { Theme.Apply("classic"); }

        // حلقة الدخول: «تسجيل الخروج» يعيد إلى شاشة الدخول بدل إغلاق البرنامج
        while (true)
        {
            using (var login = new LoginForm())
                if (login.ShowDialog() != DialogResult.OK) return;

            if (Session.UsingDefaultPassword)
                using (var setup = new SetupDialog()) setup.ShowDialog();

            var main = new MainForm();
            Application.Run(main);
            if (!main.LoggedOut) return;
            Session.Logout();
        }
    }

    static void ReportError(Exception ex)
    {
        Log(ex);
        var msg = ex is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 }
            ? "قاعدة البيانات مشغولة حاليًا. انتظر لحظة ثم أعد المحاولة."
            : ex.Message;
        try { Dialogs.Error(msg + "\n\nتم تسجيل التفاصيل في ملف errors.log داخل مجلد البيانات.", "خطأ غير متوقع"); }
        catch { MessageBox.Show(msg, "خطأ غير متوقع", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>سجل الأخطاء: يساعد على معرفة سبب أي مشكلة لاحقًا</summary>
    static void Log(Exception ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Db.DataDir);
            File.AppendAllText(Path.Combine(Db.DataDir, "errors.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
