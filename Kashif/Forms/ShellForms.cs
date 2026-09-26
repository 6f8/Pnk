using System.Data;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Kashif;

/// <summary>شعار البرنامج: مربع دائري بتدرج برتقالي/كهرماني وحرف «ك»</summary>
public static class Brand
{
    public static void DrawMark(Graphics g, RectangleF r)
    {
        Gfx.Hq(g);
        using (var p = Gfx.Round(r, r.Width * 0.28f))
        using (var b = new LinearGradientBrush(r, Theme.Amber, Theme.Orange, 60f))
            g.FillPath(b, p);
        using (var p = Gfx.Round(RectangleF.Inflate(r, -1, -1), r.Width * 0.27f))
        using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1)) g.DrawPath(pen, p);
        TextRenderer.DrawText(g, "ك", FontKit.GetPx(r.Height * 0.53f, FontStyle.Bold), Rectangle.Round(new RectangleF(r.X, r.Y - r.Height * 0.06f, r.Width, r.Height)), Color.White, Gfx.Center);
    }
}

// ============================== تسجيل الدخول ==============================
public class LoginForm : BaseForm
{
    readonly TextBox user = new() { Width = 340 };
    readonly TextBox pass = new() { Width = 340, UseSystemPasswordChar = true };
    readonly Label err = new() { AutoSize = false, Width = 340, Height = 26, ForeColor = Theme.Danger, Font = Theme.F(9.5f), TextAlign = ContentAlignment.MiddleLeft };
    Point drag;

    public LoginForm()
    {
        Text = "تسجيل الدخول — كاشف";
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(940, 580);
        BackColor = Theme.Surface;
        KeyPreview = true;

        var brand = new BrandPanel { Dock = DockStyle.Right, Width = 420 };
        var close = new ModernButton { Kind = BtnKind.Glass, IconName = "x", Size = new Size(36, 36), Location = new Point(16, 16), TabStop = false };
        close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        brand.Controls.Add(close);

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(70, 70, 70, 30) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(new Label { Text = "تسجيل الدخول", AutoSize = false, Width = 360, Height = 46, Font = Theme.FS(20), ForeColor = Theme.Ink, TextAlign = ContentAlignment.MiddleLeft });
        flow.Controls.Add(new Label { Text = "مرحبًا بعودتك، أدخل بياناتك للمتابعة", AutoSize = false, Width = 360, Height = 34, Font = Theme.F(10.5f), ForeColor = Theme.Muted, TextAlign = ContentAlignment.TopLeft, Margin = new Padding(3, 0, 3, 18) });

        flow.Controls.Add(Caption("اسم المستخدم"));
        var userBox = new InputBox(user, 348, "user") { Height = 46, Margin = new Padding(3, 0, 3, 14) };
        flow.Controls.Add(userBox);
        flow.Controls.Add(Caption("كلمة المرور"));
        var passBox = new InputBox(pass, 348, "lock") { Height = 46, Margin = new Padding(3, 0, 3, 4) };
        var eye = new ModernButton { Kind = BtnKind.Ghost, IconName = "eye", Size = new Size(32, 32), TabStop = false };
        eye.Click += (s, e) => { pass.UseSystemPasswordChar = !pass.UseSystemPasswordChar; eye.IconName = pass.UseSystemPasswordChar ? "eye" : "eye-off"; eye.Invalidate(); pass.Focus(); };
        passBox.Trailing = eye;
        flow.Controls.Add(passBox);
        flow.Controls.Add(err);

        var b = new ModernButton { Text = "دخول", IconName = "log-in", Width = 348, Height = 46, Font = Theme.FS(11), Margin = new Padding(3, 8, 3, 10) };
        b.Click += (s, e) => TryLogin();
        flow.Controls.Add(b);
        AcceptButton = b;

        if (DefaultAdminActive())
            flow.Controls.Add(new Label
            {
                Text = "أول مرة؟ اسم المستخدم admin وكلمة المرور admin — ستُطلب منك كلمة مرور جديدة بعد الدخول.",
                AutoSize = false, Width = 348, Height = 44, Font = Theme.F(9), ForeColor = Theme.Muted, TextAlign = ContentAlignment.TopLeft
            });

        host.Controls.Add(flow);
        Controls.Add(host);
        Controls.Add(brand);

        foreach (var c in new Control[] { brand, host, flow })
        {
            c.MouseDown += (s, e) => drag = e.Location;
            c.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) Location = new Point(Location.X + e.X - drag.X, Location.Y + e.Y - drag.Y); };
        }
        pass.KeyUp += (s, e) => CapsHint();
        user.TextChanged += (s, e) => err.Text = "";
        pass.TextChanged += (s, e) => err.Text = "";
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += (s, e) => { if (user.Text == "") user.Focus(); else pass.Focus(); };
    }

    static Label Caption(string t) => new()
    {
        Text = t, AutoSize = false, Width = 348, Height = 26, Font = Theme.FS(9.5f), ForeColor = Theme.Text2,
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 0, 3, 2)
    };

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
    }

    static bool DefaultAdminActive()
    {
        try
        {
            var dt = Db.Query("SELECT username, pass_hash FROM users WHERE username='admin' AND active=1");
            return dt.Rows.Count == 1 && Session.Verify("admin", Db.S(dt.Rows[0]["pass_hash"]));
        }
        catch { return false; }
    }

    void CapsHint()
    {
        if (Control.IsKeyLocked(Keys.CapsLock)) { err.ForeColor = Theme.Warning; err.Text = "تنبيه: زر Caps Lock مفعّل"; }
        else if (err.ForeColor == Theme.Warning) err.Text = "";
    }

    void TryLogin()
    {
        if (user.Text.Trim() == "") { err.ForeColor = Theme.Danger; err.Text = "أدخل اسم المستخدم."; user.Focus(); return; }
        Cursor = Cursors.WaitCursor;
        bool ok = Session.Login(user.Text, pass.Text);
        Cursor = Cursors.Default;
        if (ok) { DialogResult = DialogResult.OK; Close(); return; }
        err.ForeColor = Theme.Danger;
        err.Text = "اسم المستخدم أو كلمة المرور غير صحيحة.";
        pass.SelectAll();
        pass.Focus();
    }

    /// <summary>لوحة الهوية الجانبية في شاشة الدخول</summary>
    sealed class BrandPanel : Panel
    {
        public BrandPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Orange;   // لون التدرج عند زر الإغلاق (الأزرار تُرسم فوق لون الحاوية)
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Hq(g);
            var rect = ClientRectangle;
            int S(int v) => Dpi.S(v);
            using (var bg = new LinearGradientBrush(rect, Theme.Orange, Gfx.Mix(Theme.Orange, Theme.Amber, 0.65f), 70f))
                g.FillRectangle(bg, rect);
            // دوائر زخرفية ناعمة
            using (var b1 = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillEllipse(b1, -S(120), Height - S(260), S(380), S(380));
            using (var b2 = new SolidBrush(Color.FromArgb(26, 255, 255, 255))) g.FillEllipse(b2, Width - S(170), -S(110), S(300), S(300));
            using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255), 1)) g.DrawEllipse(pen, Width - S(230), -S(170), S(420), S(420));

            int right = Width - S(48);
            // الشعار بخلفية بيضاء ليتميز عن التدرج
            var mark = new RectangleF(right - S(58), S(70), S(58), S(58));
            Gfx.FillRound(g, mark, S(16), Color.White);
            TextRenderer.DrawText(g, "ك", FontKit.GetPx(mark.Height * 0.52f, FontStyle.Bold), Rectangle.Round(new RectangleF(mark.X, mark.Y - S(3), mark.Width, mark.Height)), Theme.Orange, Gfx.Center);
            TextRenderer.DrawText(g, "كاشف", Theme.FS(30), new Rectangle(S(40), S(142), right - S(40), S(60)), Color.White, Gfx.RtlStart);
            TextRenderer.DrawText(g, "تحليل بانك الآيفون وتشخيص أعطاله", Theme.F(12), new Rectangle(S(40), S(202), right - S(40), S(32)), Color.FromArgb(235, 255, 255, 255), Gfx.RtlStart);

            var features = new[]
            {
                ("scan-line", "حلّل ملفات panic-full أو النص المنسوخ"),
                ("list-ordered", "الأسباب مرتبة مع خطوات الفحص"),
                ("history", "سجل فحوصات لكل جهاز"),
                ("lightbulb", "أضف خبرة محلك ليتعلم منها البرنامج"),
            };
            int y = S(272), box = S(34);
            foreach (var (icon, text) in features)
            {
                var ir = new RectangleF(right - box, y, box, box);
                Gfx.FillRound(g, ir, S(10), Color.FromArgb(56, 255, 255, 255));
                Icons.Draw(g, icon, ir, Color.White, 17);
                TextRenderer.DrawText(g, text, Theme.F(10.5f), new Rectangle(S(40), y, right - box - S(14) - S(40), box), Color.White, Gfx.RtlStart);
                y += S(50);
            }
            TextRenderer.DrawText(g, "الإصدار " + Application.ProductVersion.Split('+')[0], Theme.F(9), new Rectangle(S(40), Height - S(50), right - S(40), S(24)), Color.FromArgb(215, 255, 255, 255), Gfx.RtlStart);
        }
    }
}

// ============================== أول تشغيل ==============================
/// <summary>ترحيب لمرة واحدة: بيانات المحل وتغيير كلمة المرور الافتراضية</summary>
public class SetupDialog : DialogShell
{
    public SetupDialog() : base("مرحبًا بك في كاشف", 600, 560, "sparkles")
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        flow.Controls.Add(new Label
        {
            Text = "خطوة واحدة قبل البدء: أدخل بيانات محلك لتظهر في تقارير الفحص المطبوعة، واختر كلمة مرور جديدة بدل الافتراضية لحماية بياناتك.",
            AutoSize = false, Width = 530, Height = 52, ForeColor = Theme.Text2, Font = Theme.F(10), TextAlign = ContentAlignment.TopLeft
        });
        var shop = new TextBox { Width = 522, Text = Settings.Get("shop_name") == "محلي" ? "" : Settings.Get("shop_name"), PlaceholderText = "مثال: مركز النور للصيانة" };
        var phone = new TextBox { Width = 253, Text = Settings.Get("shop_phone"), PlaceholderText = "07xx xxx xxxx" };
        var addr = new TextBox { Width = 253, Text = Settings.Get("shop_address"), PlaceholderText = "المدينة — الشارع" };
        var p1 = new TextBox { Width = 253, UseSystemPasswordChar = true };
        var p2 = new TextBox { Width = 253, UseSystemPasswordChar = true };
        // بيانات المحل يدخلها المدير فقط؛ المستخدم العادي يغيّر كلمة مروره فقط
        if (Session.IsAdmin)
        {
            flow.Controls.Add(Ui.Labeled("اسم المحل", shop));
            flow.Controls.Add(Ui.Labeled("الهاتف", phone));
            flow.Controls.Add(Ui.Labeled("العنوان", addr));
        }
        else Height = 440;
        flow.Controls.Add(new Label { Text = "كلمة المرور الجديدة (4 أحرف على الأقل)", AutoSize = false, Width = 522, Height = 34, Font = Theme.FS(10.5f), ForeColor = Theme.Ink, TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(6, 10, 6, 0) });
        flow.Controls.Add(Ui.Labeled("كلمة المرور", p1));
        flow.Controls.Add(Ui.Labeled("تأكيد كلمة المرور", p2));
        Body.Controls.Add(flow);

        AddButton("لاحقًا", DialogResult.Cancel, BtnKind.Secondary, "clock");
        var ok = AddButton("حفظ والبدء", DialogResult.None, BtnKind.Primary, "check");
        AcceptButton = ok;
        ok.Click += (s, e) =>
        {
            if (p1.Text.Length < 4) { Dialogs.Warn("كلمة المرور قصيرة جدًا (4 أحرف على الأقل)."); p1.Focus(); return; }
            if (p1.Text != p2.Text) { Dialogs.Warn("تأكيد كلمة المرور غير مطابق."); p2.Focus(); return; }
            if (p1.Text == "admin") { Dialogs.Warn("اختر كلمة مرور مختلفة عن الافتراضية."); p1.Focus(); return; }
            if (Session.IsAdmin)
            {
                if (shop.Text.Trim() != "") Settings.Set("shop_name", shop.Text.Trim());
                Settings.Set("shop_phone", phone.Text.Trim());
                Settings.Set("shop_address", addr.Text.Trim());
            }
            Db.Exec("UPDATE users SET pass_hash=@p0 WHERE id=@p1", Session.HashPassword(p1.Text), Session.UserId);
            Session.UsingDefaultPassword = false;
            DialogResult = DialogResult.OK;
            Close();
        };
        Shown += (s, e) => { if (Session.IsAdmin) shop.Focus(); else p1.Focus(); };
    }
}

// ============================== النافذة الرئيسية ==============================
public class MainForm : BaseForm
{
    public record Page(string Text, string Perm, string Icon, string Group, string Desc, Func<Form> Make);
    /// <summary>قسم في القائمة: أيقونته ولونه، والمجموعة التي يظهر تحتها</summary>
    record Section(string Name, string Icon, Color Tint, string Group);

    const string HomeKey = "الرئيسية";
    const int MaxTabs = 8;
    /// <summary>عرض القائمة الجانبية: كاملة، أو شريط أيقونات مصغّر</summary>
    const int SideWide = 264, SideRail = 76;

    static readonly Color Teal = ColorTranslator.FromHtml("#0F8B8D"), Report = ColorTranslator.FromHtml("#D9485F");
    const string GOps = "العمل اليومي", GData = "المتابعة", GAdmin = "النظام";

    static readonly Section[] Sections =
    {
        new("التحليل", "scan-line", Theme.Orange, GOps),
        new("سجل الفحوصات", "history", Theme.Success, GOps),
        new("قاعدة المعرفة", "book-open", Theme.Info, GOps),
        new("التقارير", "chart-column", Report, GData),
        new("الإدارة", "settings", Teal, GAdmin),
    };

    /// <summary>لون القسم (لبطاقات الوصول السريع)</summary>
    public static Color TintOf(string group) => Sections.FirstOrDefault(x => x.Name == group) is { } s ? SecTint(s) : Theme.Brand;

    /// <summary>لون القسم حسب المظهر (المظهر البسيط: لون الهوية لكل الأقسام)</summary>
    static Color SecTint(Section s) => Theme.MonoSections ? Theme.Brand : s.Tint;

    /// <summary>لون «الرئيسية» ولون الصورة الرمزية: الهوية، أو لون التمييز إذا كان الشريط داكنًا</summary>
    static Color HomeTint => Theme.DarkSidebar ? Theme.Orange : Theme.Brand;

    readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 18), BackColor = Theme.Bg };
    readonly TopBar top;
    readonly DocTabs tabs = new() { Dock = DockStyle.Fill };
    readonly Panel side = new() { Dock = DockStyle.Left, Width = SideWide, BackColor = Theme.Sidebar };
    readonly ScrollHost nav = new() { Dock = DockStyle.Fill, BackColor = Theme.Sidebar };
    readonly List<NavLabel> navLabels = new();
    readonly ToolTip navTip = new();
    Panel logo, userBox;
    Control[] userButtons;
    bool rail;
    readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Visible = true, Text = "كاشف" };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    readonly Dictionary<string, (Form Form, Page Page)> open = new();
    readonly List<(NavSection Head, List<NavItem> Items)> sections = new();
    readonly List<(NavItem Btn, Page Page)> navItems = new();
    NavSection homeHead;
    string activeKey;
    public bool LoggedOut { get; private set; }
    public static MainForm Instance { get; private set; }

    public List<Page> Pages { get; }

    List<Page> BuildPages()
    {
        var list = new List<Page>
        {
            new(HomeKey, null, "house", "", "نظرة سريعة على الفحوصات والأعطال الأكثر تكرارًا", () => new DashboardForm(this)),

            new(AnalyzeForm.PageTitle, "analyze", "scan-line", "التحليل", "افتح ملفات panic-full أو الصق النص — التشخيص والأسباب وخطوات الفحص (F2)", () => new AnalyzeForm()),

            new(HistoryForm.PageTitle, "history", "history", "سجل الفحوصات", "كل الفحوصات المحفوظة: الزبون، الجهاز، التشخيص، الحالة", () => new HistoryForm()),

            new(ReferenceForm.PageTitle, "analyze", "book-open", "قاعدة المعرفة", "رموز الحساسات وأنواع البانك وخدمات النظام ومعنى كل منها", () => new ReferenceForm()),
            new("خبرة المحل", "kb", "lightbulb", "قاعدة المعرفة", "أضف ما تعلمته: نص يظهر في السجل ← القطعة التي كانت السبب", () => new CrudForm(Defs.KbRules())),

            new("الأعطال الأكثر تكرارًا", "reports", "trending-up", "التقارير", "القطعة الأرجح في كل فحص — ما يستحق التخزين أولًا", () => new ReportsForm("الأعطال الأكثر تكرارًا")),
            new("حسب نوع البانك", "reports", "chart-pie", "التقارير", "حساس مفقود، SMC، مراقب النظام، التخزين ...", () => new ReportsForm("حسب نوع البانك")),
            new("حسب الجهاز", "reports", "smartphone", "التقارير", "الموديلات الأكثر وصولًا بالبانك", () => new ReportsForm("حسب الجهاز")),

            new("الإعدادات والبيانات", "settings", "settings", "الإدارة", "بيانات المحل، التحليل، الطباعة، النسخ الاحتياطي", () => new SettingsForm()),
            new("المستخدمون والصلاحيات", "users", "shield-check", "الإدارة", "حسابات الدخول وصلاحيات كل مستخدم", () => new UsersForm()),
        };
        if (Session.IsAdmin)
            list.Insert(list.FindIndex(p => p.Group == "الإدارة"),
                new("سجل العمليات", "users", "shield-check", "التقارير", "من غيّر ماذا ومتى (حفظ، حذف، تعديل خبرة المحل)", () => new ReportsForm("سجل العمليات")));
        return list;
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainForm()
    {
        Instance = this;
        Text = $"كاشف — {Settings.Get("shop_name")}";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(960, 600);
        KeyPreview = true;
        // الشاشات حسب صلاحيات المستخدم
        Pages = BuildPages().Where(p => Session.Can(p.Perm)).ToList();

        // ---------- الشعار ----------
        logo = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Sidebar };
        logo.Paint += (s, e) =>
        {
            var g = e.Graphics;
            int m = Dpi.S(rail ? 0 : 20), box = Dpi.S(40);
            var mark = rail ? new RectangleF((logo.Width - box) / 2f, (logo.Height - box) / 2f, box, box) : new RectangleF(logo.Width - m - box, (logo.Height - box) / 2f, box, box);
            Brand.DrawMark(g, mark);
            if (!rail)
            {
                int tx = (int)mark.X - Dpi.S(12);
                TextRenderer.DrawText(g, "كاشف", Theme.FS(15), new Rectangle(Dpi.S(12), (int)mark.Y - Dpi.S(4), tx - Dpi.S(12), Dpi.S(28)), Theme.SidebarText, Gfx.RtlStart);
                TextRenderer.DrawText(g, "تحليل البانك • التشخيص • السجل", Theme.F(8.5f), new Rectangle(Dpi.S(12), (int)mark.Y + Dpi.S(22), tx - Dpi.S(12), Dpi.S(20)), Theme.SidebarMuted, Gfx.RtlStart);
            }
            using var pen = new Pen(Theme.SidebarBorder);
            g.DrawLine(pen, Dpi.S(14), logo.Height - 1, logo.Width - Dpi.S(14), logo.Height - 1);
        };

        // ---------- المستخدم ----------
        userBox = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = Theme.Sidebar };
        userBox.Paint += (s, e) =>
        {
            var g = e.Graphics;
            using (var pen = new Pen(Theme.SidebarBorder)) g.DrawLine(pen, Dpi.S(14), 0, userBox.Width - Dpi.S(14), 0);
            int a = Dpi.S(36);
            var av = rail ? new RectangleF((userBox.Width - a) / 2f, (userBox.Height - a) / 2f, a, a) : new RectangleF(userBox.Width - Dpi.S(20) - a, (userBox.Height - a) / 2f, a, a);
            Avatar.Draw(g, av, Session.UserName, HomeTint);
            if (rail) return;
            int left = Dpi.S(96), right = (int)av.X - Dpi.S(10);
            TextRenderer.DrawText(g, Session.UserName, Theme.FS(10), new Rectangle(left, (int)av.Y - Dpi.S(3), right - left, Dpi.S(22)), Theme.SidebarText, Gfx.RtlStart);
            TextRenderer.DrawText(g, Session.IsAdmin ? "مدير النظام" : "مستخدم", Theme.F(8.5f), new Rectangle(left, (int)av.Y + Dpi.S(18), right - left, Dpi.S(20)), Theme.SidebarMuted, Gfx.RtlStart);
        };
        var bLogout = new ModernButton { Kind = BtnKind.SideGhost, IconName = "log-out", Size = new Size(36, 36), Location = new Point(12, 16), TabStop = false };
        navTip.SetToolTip(bLogout, "تسجيل الخروج");
        bLogout.Click += (s, e) => { if (Ui.Confirm("تسجيل الخروج من البرنامج؟") && CloseAllTabs()) { LoggedOut = true; Close(); } };
        var bPwd = new ModernButton { Kind = BtnKind.SideGhost, IconName = "key-round", Size = new Size(36, 36), Location = new Point(52, 16), TabStop = false };
        navTip.SetToolTip(bPwd, "تغيير كلمة المرور");
        bPwd.Click += (s, e) => { using var d = new PasswordDialog(); d.ShowModal(); };
        userBox.Controls.Add(bLogout);
        userBox.Controls.Add(bPwd);
        userButtons = new Control[] { bLogout, bPwd };
        navTip.SetToolTip(userBox, Session.UserName);

        // ---------- الأقسام (تُفتح وتُطوى)، مجمّعة تحت عناوين صغيرة ----------
        nav.Add(new Panel { Height = 8, BackColor = Theme.Sidebar });
        var home = Pages.FirstOrDefault(p => p.Text == HomeKey);
        if (home != null)
        {
            homeHead = new NavSection { Text = HomeKey, IconName = home.Icon, Tint = HomeTint, Expandable = false };
            homeHead.Click += (s, e) => Navigate(home);
            navTip.SetToolTip(homeHead, HomeKey);
            nav.Add(homeHead);
        }
        string group = null;
        foreach (var sec in Sections)
        {
            var pages = Pages.Where(p => p.Group == sec.Name).ToList();
            if (pages.Count == 0) continue;
            if (sec.Group != group)
            {
                group = sec.Group;
                var label = new NavLabel { Text = group };
                navLabels.Add(label);
                nav.Add(label);
            }
            // قسم بشاشة واحدة يفتحها مباشرة (مثل «الأقساط» و«تقارير الأرباح»)
            var head = new NavSection { Text = sec.Name, IconName = sec.Icon, Tint = SecTint(sec), Expandable = pages.Count > 1 };
            navTip.SetToolTip(head, sec.Name);
            var items = new List<NavItem>();
            nav.Add(head);
            if (pages.Count == 1)
            {
                var only = pages[0];
                head.Click += (s, e) => Navigate(only);
                sections.Add((head, items));
                continue;
            }
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                var item = new NavItem { Text = p.Text, IconName = p.Icon, Fill = SecTint(sec), Visible = false, Last = i == pages.Count - 1 };
                item.Click += (s, e) => Navigate(p);
                items.Add(item);
                navItems.Add((item, p));
                nav.Add(item);
            }
            head.Click += (s, e) =>
            {
                if (rail) ShowFlyout(head, sec, pages);
                else Expand(head.Expanded ? null : sec.Name);
            };
            sections.Add((head, items));
        }
        nav.Add(new Panel { Height = 12, BackColor = Theme.Sidebar });
        side.Controls.Add(nav);
        side.Controls.Add(userBox);
        side.Controls.Add(logo);
        // خط فاصل رفيع بين القائمة والمحتوى
        side.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.SidebarBorder });

        // ---------- الشريط العلوي والتبويبات ----------
        top = new TopBar();
        top.Menu.Click += (s, e) => ToggleSidebar();
        top.Search.Click += (s, e) => ShowPalette();
        // الجرس: الفحوصات المفتوحة (قيد الفحص أو بانتظار قطعة)
        top.Bell.Click += (s, e) => { if (!Go(HistoryForm.PageTitle) && home != null) Navigate(home); };
        top.Backup.Click += (s, e) => BackupNow();
        top.Calc.Click += (s, e) => Shell("calc.exe");
        top.Help.Click += (s, e) => ShowHelp();
        top.Reload.Click += (s, e) => ReloadCurrent();

        var tabStrip = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = DocTabs.Strip, Padding = new Padding(12, 0, 12, 0) };
        tabStrip.Controls.Add(tabs);
        tabs.Selected += Activate;
        tabs.Closed += key => CloseTab(key);

        var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        main.Controls.Add(content);
        main.Controls.Add(tabStrip);
        main.Controls.Add(top);
        Controls.Add(main);
        Controls.Add(side);

        tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Maximized; Activate(); };

        timer.Tick += (s, e) => RefreshAlerts();
        Shown += (s, e) =>
        {
            if (Settings.Get("ui_sidebar_rail") == "1") SetRail(true, false);
            Navigate(Pages[0]);
            // ملفات بانك مُرّرت عند التشغيل («فتح باستخدام» أو السحب إلى أيقونة البرنامج)
            if (Program.StartupFiles.Count > 0 && Session.Can("analyze")) AnalyzeForm.OpenFiles(Program.StartupFiles);
            timer.Start();
            RefreshAlerts(force: true);
        };
        FormClosing += (s, e) =>
        {
            if (!LoggedOut && e.CloseReason == CloseReason.UserClosing && !CloseAllTabs()) { e.Cancel = true; return; }
            timer.Stop();
            if (Settings.Get("backup_on_exit") == "1") try { Backup.Run(); } catch { }
            tray.Visible = false;
            tray.Dispose();
        };
    }

    /// <summary>إخفاء أيقونة شريط المهام قبل الخروج المباشر (حتى لا تبقى أيقونة معلّقة)</summary>
    public void HideTray() { tray.Visible = false; tray.Dispose(); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int caption = 0x00FFFFFF, text = 0x002A1F1B;   // شريط عنوان أبيض ونص داكن (ويندوز 11)
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
        }
        catch { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.K: ShowPalette(); return true;
            case Keys.Control | Keys.W: if (activeKey != null) CloseTab(activeKey); return true;
            case Keys.Control | Keys.Tab: CycleTab(1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: CycleTab(-1); return true;
            case Keys.F1: ShowHelp(); return true;
            case Keys.F2: Go(AnalyzeForm.PageTitle); return true;
            case Keys.F5: ReloadCurrent(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    bool alertsBusy;
    long alertsVersion = -1;
    DateTime alertsAt;

    /// <summary>
    /// عدد التنبيهات على الجرس. الاستعلامات ثقيلة مع كثرة المواد، فتُحسب في الخلفية (كانت تجمّد الشاشة كل 30 ثانية)،
    /// وتُعاد فقط إذا تغيّرت البيانات أو مرّت بضع دقائق (التنبيهات المرتبطة بالتاريخ)
    /// </summary>
    public async void RefreshAlerts(bool force = false)
    {
        if (alertsBusy) return;
        long ver = Db.Version;
        if (!force && ver == alertsVersion && (DateTime.Now - alertsAt).TotalMinutes < 5) return;
        alertsBusy = true;
        try
        {
            long n = await Task.Run(Stats.AlertsCount);
            alertsVersion = ver;
            alertsAt = DateTime.Now;
            if (IsDisposed) return;
            top.Bell.Badge = (int)Math.Clamp(n, 0, int.MaxValue);
            top.Bell.Invalidate();
        }
        catch { }
        finally { alertsBusy = false; }
    }

    void ShowPalette()
    {
        using var p = new CommandPalette(Pages);
        if (p.ShowDialog(this) == DialogResult.OK && p.Selected != null) Navigate(p.Selected);
    }

    // ---------- القائمة الجانبية ----------
    /// <summary>تصغير القائمة إلى شريط أيقونات (مساحة عمل أكبر مثل شاشة البيع) أو إعادتها كاملة؛ يُحفظ الاختيار</summary>
    public void ToggleSidebar() => SetRail(!rail, true);

    void SetRail(bool on, bool save)
    {
        rail = on;
        SuspendLayout();
        nav.SuspendContent();
        side.Width = Dpi.S(on ? SideRail : SideWide);
        foreach (var l in navLabels) { l.Rail = on; l.Height = Dpi.S(on ? 14 : 34); }
        if (homeHead != null) homeHead.Rail = on;
        foreach (var (head, items) in sections)
        {
            head.Rail = on;
            foreach (var it in items) it.Visible = !on && head.Expanded;
        }
        foreach (var b in userButtons) b.Visible = !on;
        nav.ResumeContent();
        nav.ScrollToTop();
        ResumeLayout(true);
        // إعادة رسم كاملة فورية حتى لا تبقى آثار من الترتيب السابق
        Invalidate(true);
        Update();
        if (save) try { Settings.Set("ui_sidebar_rail", on ? "1" : "0"); } catch { }
    }

    /// <summary>في الوضع المصغّر: قائمة منبثقة بشاشات القسم بجانب أيقونته</summary>
    void ShowFlyout(NavSection head, Section sec, List<Page> pages)
    {
        var menu = new NavFlyout(sec.Name, SecTint(sec));
        foreach (var p in pages) menu.AddPage(p.Text, p.Icon, activeKey == p.Text, () => Navigate(p));
        menu.FormClosed += (s, e) => BeginInvoke(() => menu.Dispose());
        menu.ShowNear(head, this);
    }

    void Expand(string section)
    {
        nav.SuspendContent();
        foreach (var (head, items) in sections)
        {
            bool exp = head.Text == section;
            head.Expanded = exp;
            foreach (var it in items) it.Visible = exp && !rail;
        }
        nav.ResumeContent();
        var target = sections.FirstOrDefault(x => x.Head.Text == section);
        if (target.Head != null && !rail) nav.EnsureVisible(target.Items.LastOrDefault() ?? (Control)target.Head, target.Head);
    }

    void Highlight(Page page)
    {
        if (homeHead != null) homeHead.Active = page?.Text == HomeKey;
        foreach (var (btn, p) in navItems) btn.Active = ReferenceEquals(p, page);
        foreach (var (head, items) in sections) head.Active = page != null && head.Text == page.Group;
        if (page != null && page.Group != "" && sections.Any(x => x.Head.Text == page.Group && x.Items.Count > 0 && !x.Head.Expanded)) Expand(page.Group);
    }

    // ---------- التبويبات ----------
    public void Navigate(Page p)
    {
        if (open.ContainsKey(p.Text)) { Activate(p.Text); return; }
        // بعض عناصر الأدوات إجراءات مباشرة (الحاسبة، الدعم) لا تفتح تبويبًا
        var f = p.Make();
        if (f != null) Open(p.Text, f, p);
    }

    /// <summary>الشاشة المفتوحة في تبويب بهذا الاسم (null إن لم تكن مفتوحة)</summary>
    public Form Find(string key) => open.TryGetValue(key, out var e) ? e.Form : null;

    /// <summary>الانتقال إلى شاشة باسمها (إن كانت ضمن صلاحيات المستخدم)</summary>
    public bool Go(string pageText)
    {
        var p = Pages.FirstOrDefault(x => x.Text == pageText);
        if (p == null) return false;
        Navigate(p);
        return true;
    }

    /// <summary>فتح شاشة في تبويب جديد (أو استبدال تبويب بنفس العنوان)</summary>
    public void Open(string title, Form f) => Open(title, f, Pages.FirstOrDefault(x => x.Text == title));

    void Open(string key, Form f, Page page)
    {
        if (open.TryGetValue(key, out var existing))
        {
            if (existing.Form is BaseForm bf && !bf.ConfirmClose()) { f.Dispose(); Activate(key); return; }
            Detach(existing.Form);
            open.Remove(key);
        }
        // حد أقصى للتبويبات: يُغلق أقدم تبويب لا يحتوي عملًا غير محفوظ
        if (open.Count >= MaxTabs)
        {
            var old = tabs.Items.Select(t => t.Key).FirstOrDefault(k => k != HomeKey && k != activeKey && open[k].Form is not AnalyzeForm);
            if (old != null) { Detach(open[old].Form); open.Remove(old); tabs.Remove(old); }
        }
        Host(f);
        open[key] = (f, page);
        StampHome(key);
        tabs.Set(key, key, page?.Icon ?? "square-pen", key != HomeKey);
        // عنوان التبويب يتبع عنوان الشاشة (مثل «تعديل فاتورة» ← «فاتورة بيع» بعد الحفظ)
        if (page == null) f.TextChanged += (s, e) => { if (open.ContainsKey(key) && f.Text != "") tabs.Set(key, f.Text, "square-pen", true); };
        Activate(key, fresh: true);
    }

    /// <summary>تجهيز الشاشة لتعمل داخل تبويب: بلا إطار، بملء المساحة، ومكبَّرة حسب دقة العرض</summary>
    void Host(Form f)
    {
        f.TopLevel = false;
        f.FormBorderStyle = FormBorderStyle.None;
        f.BackColor = Theme.Bg;
        f.Visible = false;
        Dpi.ScaleTree(f);
        f.Dock = DockStyle.Fill;
        content.Controls.Add(f);
    }

    void Activate(string key) => Activate(key, false);

    long homeVersion = -1;
    DateTime homeBuilt;
    void StampHome(string key)
    {
        if (key != HomeKey) return;
        homeVersion = Db.Version;
        homeBuilt = DateTime.Now;
    }

    void Activate(string key, bool fresh)
    {
        if (!open.TryGetValue(key, out var entry)) return;
        // لوحة التحكم تُبنى من جديد عند الرجوع إليها لتعرض أحدث الأرقام — فقط إذا تغيّرت البيانات منذ بنائها
        // (إعادة بنائها مع كل نقرة على «الرئيسية» كانت تشغّل كل استعلامات التنبيهات والمؤشرات من جديد)
        if (!fresh && key == HomeKey && activeKey != HomeKey && entry.Page != null &&
            (Db.Version != homeVersion || (DateTime.Now - homeBuilt).TotalMinutes >= 5 || homeBuilt.Date != DateTime.Today))
        {
            var f = entry.Page.Make();
            Host(f);
            Detach(entry.Form);
            entry = (f, entry.Page);
            open[key] = entry;
            StampHome(key);
        }
        content.SuspendLayout();
        entry.Form.Show();
        entry.Form.BringToFront();
        foreach (var o in open.Values) if (o.Form != entry.Form && o.Form.Visible) o.Form.Hide();
        content.ResumeLayout();
        content.Invalidate(true);   // مسح أي بقايا رسم من الشاشة السابقة

        bool changed = activeKey != key;
        activeKey = key;
        tabs.Activate(key);
        top.SetTitle(entry.Page?.Text ?? entry.Form.Text, entry.Page?.Desc ?? "", entry.Page?.Icon ?? "square-pen");
        Highlight(entry.Page);
        if (changed && !fresh && entry.Form is BaseForm b) try { b.OnPageActivated(); } catch { }
        if (!fresh) entry.Form.SelectNextControl(entry.Form, true, true, true, true);
    }

    /// <summary>إعادة بناء الشاشة الحالية بأحدث البيانات (مع التأكيد إن كان فيها عمل غير محفوظ)</summary>
    void ReloadCurrent()
    {
        if (activeKey == null || !open.TryGetValue(activeKey, out var entry) || entry.Page == null) return;
        if (entry.Form is BaseForm bf && !bf.ConfirmClose()) return;
        var f = entry.Page.Make();
        Host(f);
        Detach(entry.Form);
        open[activeKey] = (f, entry.Page);
        StampHome(activeKey);
        Activate(activeKey, fresh: true);
        RefreshAlerts(force: true);
    }

    public bool CloseTab(string key)
    {
        if (key == HomeKey || !open.TryGetValue(key, out var entry)) return false;
        if (key != activeKey) Activate(key);
        if (entry.Form is BaseForm bf && !bf.ConfirmClose()) return false;
        var keys = tabs.Items.Select(t => t.Key).ToList();
        int idx = keys.IndexOf(key);
        open.Remove(key);
        tabs.Remove(key);
        Detach(entry.Form);
        if (activeKey == key)
        {
            activeKey = null;
            var rest = tabs.Items.Select(t => t.Key).ToList();
            if (rest.Count > 0) Activate(rest[Math.Clamp(idx - 1, 0, rest.Count - 1)]);
        }
        return true;
    }

    /// <summary>يغلق كل التبويبات (مع التأكيد على غير المحفوظ) — false إذا ألغى المستخدم</summary>
    bool CloseAllTabs()
    {
        foreach (var key in tabs.Items.Select(t => t.Key).Where(k => k != HomeKey).ToList())
            if (!CloseTab(key)) return false;
        return true;
    }

    void CycleTab(int dir)
    {
        var keys = tabs.Items.Select(t => t.Key).ToList();
        if (keys.Count < 2) return;
        int i = keys.IndexOf(activeKey);
        Activate(keys[((i + dir) % keys.Count + keys.Count) % keys.Count]);
    }

    void Detach(Form f)
    {
        content.Controls.Remove(f);
        BeginInvoke(() => f.Dispose());   // التخلص لاحقًا لأن الطلب قد يأتي من زر داخل الشاشة نفسها
    }

    // ---------- أدوات الشريط العلوي ----------
    void BackupNow()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var file = Backup.Run();
            Cursor = Cursors.Default;
            Toast.Show("تم حفظ نسخة احتياطية: " + Path.GetFileName(file));
        }
        catch (Exception ex) { Cursor = Cursors.Default; Dialogs.Error("تعذّر إنشاء النسخة الاحتياطية:\n" + ex.Message); }
    }

    static void Shell(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Dialogs.Warn("تعذّر الفتح: " + ex.Message); }
    }

    void ShowHelp() => Dialogs.Message(
        "اختصارات مفيدة:\n" +
        "•  Ctrl+K  البحث السريع عن أي شاشة\n" +
        "•  Ctrl+W  إغلاق التبويب الحالي،  Ctrl+Tab  التنقل بين التبويبات\n" +
        "•  F2  تحليل البانك — وفيها: Ctrl+O فتح ملفات، Ctrl+V لصق، F9 تحليل النص، Ctrl+S حفظ، Ctrl+P طباعة\n\n" +
        "من أين يأتي ملف البانك؟ على الآيفون: الإعدادات ← الخصوصية والأمان ← التحليلات والتحسينات ← بيانات التحليلات ← الملف الذي يبدأ بـ panic-full.\n" +
        "افتح أكثر من ملف لنفس الجهاز: تكرار نفس الحساس يرفع الثقة، واختلاف الأنواع يكشف سببًا عامًا.\n" +
        "ما تتأكد منه بنفسك أضفه في «خبرة المحل» فيظهر في التحليلات القادمة.\n\n" +
        $"قاعدة البيانات:\n{Db.DataDir}",
        "الدعم والمساعدة", Tone.Info);

    /// <summary>الشريط العلوي: زر القائمة وعنوان الشاشة، وأدوات سريعة (نسخ احتياطي، حاسبة، تنبيهات، مساعدة)</summary>
    sealed class TopBar : Panel
    {
        string title = "", desc = "", icon;
        public ModernButton Menu { get; }
        public ModernButton Search { get; }
        public ModernButton Backup { get; }
        public ModernButton Calc { get; }
        public ModernButton Bell { get; }
        public ModernButton Help { get; }
        public ModernButton Reload { get; }
        readonly ModernButton[] tools;
        readonly string searchText = "بحث سريع   Ctrl+K", helpText = "الدعم والمساعدة";

        public TopBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = 64;
            BackColor = Theme.Surface;
            var tip = new ToolTip();
            ModernButton Tool(string iconName, string hint, BtnKind kind = BtnKind.Secondary)
            {
                var b = new ModernButton { Kind = kind, IconName = iconName, Size = new Size(40, 38), TabStop = false, Radius = 10 };
                tip.SetToolTip(b, hint);
                Controls.Add(b);
                return b;
            }
            Menu = Tool("panel-right", "تصغير / توسيع القائمة الجانبية", BtnKind.Ghost);
            Help = new ModernButton { Kind = BtnKind.Dark, IconName = "headset", Text = helpText, Font = Theme.FS(9.5f), Height = 38, TabStop = false, Radius = 10 };
            Help.FitWidth(140);
            tip.SetToolTip(Help, helpText);
            Controls.Add(Help);
            Bell = Tool("bell", "التنبيهات", BtnKind.Amber);
            Reload = Tool("refresh-cw", "تحديث الشاشة الحالية (F5)");
            Calc = Tool("calculator", "الحاسبة");
            Backup = Tool("cloud-upload", "نسخة احتياطية الآن");
            Search = new ModernButton { Kind = BtnKind.Secondary, IconName = "search", Text = searchText, Font = Theme.F(9.5f), Height = 38, TabStop = false, Radius = 10 };
            Search.FitWidth(180);
            tip.SetToolTip(Search, "البحث عن أي شاشة (Ctrl+K)");
            Controls.Add(Search);
            tools = new[] { Help, Bell, Reload, Calc, Backup, Search };
        }

        public void SetTitle(string t, string d, string i) { title = t; desc = d; icon = i; Invalidate(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tools == null) return;   // يُستدعى من المُنشئ قبل إنشاء الأزرار
            Menu.Location = new Point(Width - Dpi.S(16) - Menu.Width, (Height - Menu.Height) / 2);
            // شاشة ضيقة: أزرار الدعم والبحث تصبح أيقونات فقط حتى يبقى للعنوان مكان
            bool narrow = Width < Dpi.S(1060);
            Help.Text = narrow ? "" : helpText;
            Search.Text = narrow ? "" : searchText;
            Help.FitWidth(narrow ? 40 : 140);
            Search.FitWidth(narrow ? 40 : 180);
            // الأدوات في الجهة اليسرى (نهاية السطر العربي)
            int x = Dpi.S(16);
            foreach (var b in tools)
            {
                b.Location = new Point(x, (Height - b.Height) / 2);
                x = b.Right + Dpi.S(b == Help || b == Backup ? 14 : 6);
            }
            Invalidate(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Gfx.Hq(g);
            int right = Menu.Left - Dpi.S(12);
            if (Icons.Has(icon))
            {
                int box = Dpi.S(38);
                var ir = new RectangleF(right - box, (Height - box) / 2f, box, box);
                Gfx.FillRound(g, ir, Dpi.S(10f), Theme.OrangeSoft);
                Icons.Draw(g, icon, ir, Theme.Orange, 19);
                right -= box + Dpi.S(12);
            }
            int left = Search.Right + Dpi.S(16);
            if (right - left < Dpi.S(60)) return;
            TextRenderer.DrawText(g, title, Theme.FS(13), new Rectangle(left, Dpi.S(9), right - left, Dpi.S(26)), Theme.Ink, Gfx.RtlStart);
            TextRenderer.DrawText(g, desc, Theme.F(9), new Rectangle(left, Dpi.S(35), right - left, Dpi.S(20)), Theme.Muted, Gfx.RtlStart);
            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

/// <summary>قائمة منبثقة لشاشات القسم عند تصغير القائمة الجانبية (نافذة مرسومة بالكامل، تُغلق عند النقر خارجها)</summary>
public class NavFlyout : Form
{
    readonly string title;
    readonly Color tint;
    readonly List<(string Text, string Icon, bool Active, Action Run)> items = new();
    int hover = -1;
    int HeadH => Dpi.S(44);
    int ItemH => Dpi.S(40);

    public NavFlyout(string title, Color tint)
    {
        this.title = title;
        this.tint = tint;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        RightToLeft = RightToLeft.Yes;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Hand;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Deactivate += (s, e) => Close();
    }

    public void AddPage(string text, string icon, bool active, Action run) => items.Add((text, icon, active, run));

    /// <summary>إظهار القائمة بجانب رأس القسم (يسار الشريط المصغّر)</summary>
    public void ShowNear(Control anchor, Form owner)
    {
        var font = Theme.FS(10);
        int w = Math.Max(Dpi.S(200), items.Select(i => TextRenderer.MeasureText(i.Text, font).Width).DefaultIfEmpty(0).Max() + Dpi.S(80));
        int h = HeadH + items.Count * ItemH + Dpi.S(10);
        var p = anchor.PointToScreen(Point.Empty);
        var area = Screen.FromControl(anchor).WorkingArea;
        int x = p.X - w - Dpi.S(6), y = Math.Min(p.Y, area.Bottom - h - Dpi.S(4));
        Bounds = new Rectangle(Math.Max(area.X, x), Math.Max(area.Y, y), w, h);
        Show(owner);
    }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }   // ظل

    int HitTest(Point pt) { int i = (pt.Y - HeadH) / ItemH; return pt.Y >= HeadH && i >= 0 && i < items.Count ? i : -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.Location); if (h != hover) { hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = HitTest(e.Location);
        if (i < 0) return;
        var run = items[i].Run;
        Close();
        run();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        Gfx.Hq(g);
        using (var pen = new Pen(Theme.BorderStrong)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(g, title, Theme.FS(10.5f), new Rectangle(Dpi.S(12), 0, Width - Dpi.S(28), HeadH), Gfx.Mix(tint, Theme.Ink, 0.4f), Gfx.RtlStart);
        using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, Dpi.S(10), HeadH - 1, Width - Dpi.S(10), HeadH - 1);
        for (int i = 0; i < items.Count; i++)
        {
            var (text, icon, active, _) = items[i];
            var r = new RectangleF(Dpi.S(6), HeadH + i * ItemH + Dpi.S(3), Width - Dpi.S(12), ItemH - Dpi.S(6));
            if (active) Gfx.FillRound(g, r, Dpi.S(8f), Gfx.Mix(tint, Color.White, 0.86f));
            else if (i == hover) Gfx.FillRound(g, r, Dpi.S(8f), Theme.SurfaceAlt);
            int ic = Dpi.S(17);
            var ir = new RectangleF(r.Right - Dpi.S(12) - ic, r.Y + (r.Height - ic) / 2f, ic, ic);
            Icons.Draw(g, icon, ir, active ? tint : Theme.Muted, 16);
            TextRenderer.DrawText(g, text, active ? Theme.FS(10) : Theme.F(10), new Rectangle((int)r.X + Dpi.S(8), (int)r.Y, (int)(ir.X - r.X) - Dpi.S(18), (int)r.Height),
                active ? Gfx.Mix(tint, Theme.Ink, 0.4f) : Theme.Ink, Gfx.RtlStart);
        }
    }
}

/// <summary>حاوية تمرير بعجلة الفأرة بدون شريط تمرير ظاهر (للقائمة الجانبية)؛ العناصر بعرضها الكامل</summary>
public class ScrollHost : Panel
{
    readonly FlowLayoutPanel inner = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Padding.Empty, Margin = Padding.Empty };

    public ScrollHost()
    {
        DoubleBuffered = true;
        inner.BackColor = Theme.Sidebar;
        Controls.Add(inner);
        inner.Location = Point.Empty;
        inner.SizeChanged += (s, e) => Scroll(0);
    }

    public void Add(Control c)
    {
        c.Width = Width;
        c.Margin = Padding.Empty;
        inner.Controls.Add(c);
        c.MouseWheel += (s, e) => Scroll(e.Delta);
    }

    public void SuspendContent() => inner.SuspendLayout();
    public void ScrollToTop() { inner.Top = 0; Scroll(0); }
    public void ResumeContent() => inner.ResumeLayout(true);

    /// <summary>تمرير يُظهر العنصر الأخير من القسم المفتوح مع بقاء رأسه ظاهرًا</summary>
    public void EnsureVisible(Control last, Control first)
    {
        int bottom = last.Bottom + inner.Top, topY = first.Top + inner.Top;
        if (bottom > Height) inner.Top -= bottom - Height;
        if (first.Top + inner.Top < 0 || topY < 0) inner.Top = -first.Top;
        Scroll(0);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        inner.Width = Width;
        foreach (Control c in inner.Controls) c.Width = Width;
        Scroll(0);
    }

    protected override void OnMouseWheel(MouseEventArgs e) { Scroll(e.Delta); base.OnMouseWheel(e); }

    new void Scroll(int delta)
    {
        int min = Math.Min(0, Height - inner.Height);
        inner.Top = Math.Max(min, Math.Min(0, inner.Top + delta / 3));
    }
}

/// <summary>البحث السريع (Ctrl+K): اكتب اسم الشاشة وانتقل إليها مباشرة</summary>
public class CommandPalette : BaseForm
{
    readonly TextBox q = new() { Width = 520, PlaceholderText = "اكتب ما تبحث عنه... مثل: تحليل، سجل، خبرة" };
    readonly ResultList list = new();
    readonly List<MainForm.Page> all;
    public MainForm.Page Selected { get; private set; }

    public CommandPalette(List<MainForm.Page> pages)
    {
        all = pages;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(600, 470);
        BackColor = Theme.Surface;
        Padding = new Padding(16);
        KeyPreview = true;

        var box = new InputBox(q, 566, "search") { Dock = DockStyle.Top, Height = 48 };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 30, Text = "↑ ↓ للتنقل   •   Enter للفتح   •   Esc للإغلاق", ForeColor = Theme.Subtle, Font = Theme.F(8.5f), TextAlign = ContentAlignment.MiddleCenter };
        list.Dock = DockStyle.Fill;
        list.Activated += Accept;
        Controls.Add(list);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Surface });
        Controls.Add(box);
        Controls.Add(hint);

        q.TextChanged += (s, e) => Filter();
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { list.Step(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { list.Step(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { list.Step(list.PageSize); e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp) { list.Step(-list.PageSize); e.Handled = true; }
        };
        Deactivate += (s, e) => { if (DialogResult == DialogResult.None) Close(); };
        Filter();
        Shown += (s, e) => q.Focus();
    }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Theme.BorderStrong);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    void Filter()
    {
        var t = q.Text.Trim();
        list.SetItems(all.Where(p => t == "" || p.Text.Contains(t) || p.Desc.Contains(t) || p.Group.Contains(t)).ToList());
    }

    void Accept()
    {
        if (list.SelectedItem is not MainForm.Page p) return;
        Selected = p;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>قائمة النتائج مرسومة بالكامل (بلا شريط تمرير النظام): العجلة والأسهم للتمرير، والنقر للفتح</summary>
    sealed class ResultList : Control
    {
        List<MainForm.Page> items = new();
        int selected = -1, top, hover = -1;
        public event Action Activated;
        int ItemH => Dpi.S(54);
        public int PageSize => Math.Max(1, Height / ItemH);
        public MainForm.Page SelectedItem => selected >= 0 && selected < items.Count ? items[selected] : null;

        public ResultList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
        }

        public void SetItems(List<MainForm.Page> list) { items = list; top = 0; selected = list.Count > 0 ? 0 : -1; Invalidate(); }

        public void Step(int d)
        {
            if (items.Count == 0) return;
            selected = Math.Clamp(selected + d, 0, items.Count - 1);
            if (selected < top) top = selected;
            if (selected >= top + PageSize) top = selected - PageSize + 1;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            top = Math.Clamp(top + (e.Delta > 0 ? -1 : 1), 0, Math.Max(0, items.Count - PageSize));
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e) { int h = top + e.Y / ItemH; if (h >= items.Count) h = -1; if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = top + e.Y / ItemH;
            if (i < 0 || i >= items.Count) return;
            selected = i;
            Invalidate();
            Activated?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Gfx.Hq(g);
            int h = ItemH;
            for (int i = top; i < items.Count && (i - top) * h < Height; i++)
            {
                var p = items[i];
                bool sel = i == selected;
                int sb = items.Count > PageSize ? Dpi.S(8) : 0;   // مكان مؤشر التمرير
                var r = new RectangleF(Dpi.S(2) + sb, (i - top) * h + Dpi.S(2), Width - Dpi.S(4) - sb, h - Dpi.S(4));
                if (sel) Gfx.FillRound(g, r, Dpi.S(10f), Theme.BrandSoft);
                else if (i == hover) Gfx.FillRound(g, r, Dpi.S(10f), Theme.SurfaceAlt);
                int box = Dpi.S(36);
                var ir = new RectangleF(r.Right - box - Dpi.S(8), r.Y + (r.Height - box) / 2f, box, box);
                Gfx.FillRound(g, ir, Dpi.S(9f), sel ? Theme.Surface : Theme.SurfaceAlt);
                Icons.Draw(g, p.Icon, ir, sel ? Theme.Brand : Theme.Muted, 18);
                int gw = 0;
                if (p.Group != "")
                {
                    var gs = TextRenderer.MeasureText(p.Group, Theme.F(8.5f));
                    var gr = new Rectangle((int)r.X + Dpi.S(10), (int)(r.Y + (r.Height - Dpi.S(22)) / 2), gs.Width + Dpi.S(14), Dpi.S(22));
                    Gfx.FillRound(g, gr, Dpi.S(11f), sel ? Theme.Surface : Theme.GraySoft);
                    TextRenderer.DrawText(g, p.Group, Theme.F(8.5f), gr, Theme.Muted, Gfx.Center);
                    gw = gr.Width + Dpi.S(10);
                }
                int tx = (int)r.X + Dpi.S(10) + gw, tw = (int)ir.X - Dpi.S(10) - tx;
                TextRenderer.DrawText(g, p.Text, Theme.FS(10.5f), new Rectangle(tx, (int)r.Y + Dpi.S(3), tw, Dpi.S(24)), Theme.Ink, Gfx.RtlStart);
                TextRenderer.DrawText(g, p.Desc, Theme.F(8.5f), new Rectangle(tx, (int)r.Y + Dpi.S(26), tw, Dpi.S(20)), Theme.Muted, Gfx.RtlStart);
            }
            // مؤشر تمرير رفيع على الحافة اليسرى
            if (items.Count > PageSize)
            {
                float track = Height - Dpi.S(8), thumb = Math.Max(Dpi.S(24f), track * PageSize / items.Count);
                float y = Dpi.S(4) + (track - thumb) * top / Math.Max(1, items.Count - PageSize);
                Gfx.FillRound(g, new RectangleF(Dpi.S(1), y, Dpi.S(4), thumb), Dpi.S(2f), Theme.BorderStrong);
            }
            if (items.Count == 0)
                TextRenderer.DrawText(g, "لا توجد شاشة بهذا الاسم", Theme.F(10), new Rectangle(0, Dpi.S(20), Width, Dpi.S(30)), Theme.Muted, Gfx.Center);
        }
    }
}

/// <summary>المستخدمون والصلاحيات المتقدمة</summary>
public class UsersForm : BaseForm
{
    readonly DataGridView grid = Ui.NewGrid();
    readonly TextBox tUser = new() { Width = 290 }, tPass = new() { Width = 290, UseSystemPasswordChar = true }, tName = new() { Width = 290 };
    readonly Toggle cAdmin = new() { Text = "مدير (كل الصلاحيات)", Width = 290 }, cActive = new() { Text = "حساب فعّال", Width = 290, Checked = true };
    readonly CheckedListBox perms = new() { Width = 286, Height = 330, CheckOnClick = true, BorderStyle = BorderStyle.None, Font = Theme.F(10), BackColor = Theme.Surface };
    readonly Label lblMode = new() { AutoSize = false, Width = 290, Height = 26, ForeColor = Theme.Brand, Font = Theme.FS(9.5f), TextAlign = ContentAlignment.MiddleLeft };
    long id;

    public UsersForm()
    {
        foreach (var p in Session.AllPerms) perms.Items.Add(p.Title);
        var editor = new CardPanel { Dock = DockStyle.Right, Width = 362, Title = "بيانات المستخدم", Subtitle = "اختر مستخدمًا من الجدول أو أنشئ جديدًا", IconName = "user-cog" };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        flow.Controls.Add(lblMode);
        flow.Controls.Add(Ui.Labeled("اسم الدخول", tUser));
        tPass.PlaceholderText = "اتركها فارغة لعدم التغيير";
        flow.Controls.Add(Ui.Labeled("كلمة المرور", tPass));
        flow.Controls.Add(Ui.Labeled("الاسم الكامل", tName));
        flow.Controls.Add(cAdmin);
        flow.Controls.Add(cActive);
        var permBox = new Panel { Width = 294, Height = 340, Padding = new Padding(4), BackColor = Theme.Surface, Margin = new Padding(6, 4, 6, 8) };
        permBox.Paint += (s, e) => { Gfx.Hq(e.Graphics); Gfx.DrawRound(e.Graphics, new RectangleF(0.5f, 0.5f, permBox.Width - 2, permBox.Height - 2), Dpi.S(8f), Theme.BorderStrong); };
        perms.Dock = DockStyle.Fill;
        permBox.Controls.Add(perms);
        flow.Controls.Add(new Label { Text = "الصلاحيات", AutoSize = false, Width = 290, Height = 26, Font = Theme.F(9), ForeColor = Theme.Text2, TextAlign = ContentAlignment.BottomLeft });
        flow.Controls.Add(permBox);
        editor.Controls.Add(flow);

        var bar = Theme.Bar();
        var bNew = Theme.Btn("مستخدم جديد", Theme.Gray, 130);
        var bSave = Theme.Btn("حفظ", Theme.Success, 110);
        bar.Controls.AddRange(new Control[] { bSave, bNew });

        Controls.Add(grid);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Bg });
        Controls.Add(editor);
        Controls.Add(bar);

        bNew.Click += (s, e) => New();
        bSave.Click += (s, e) => Save();
        grid.CellClick += (s, e) => LoadUser();
        cAdmin.CheckedChanged += (s, e) => perms.Enabled = !cAdmin.Checked;
        LoadGrid();
        New();
    }

    void LoadGrid() => grid.DataSource = Db.Query(@"SELECT id, username AS [اسم الدخول], full_name AS [الاسم],
        CASE is_admin WHEN 1 THEN 'مدير' ELSE 'مستخدم' END AS [النوع], CASE active WHEN 1 THEN 'فعّال' ELSE 'موقوف' END AS [الحالة] FROM users");

    void New()
    {
        id = 0;
        tUser.Clear(); tPass.Clear(); tName.Clear();
        tPass.PlaceholderText = "كلمة مرور المستخدم الجديد";
        cAdmin.Checked = false; cActive.Checked = true;
        for (int i = 0; i < perms.Items.Count; i++) perms.SetItemChecked(i, false);
        lblMode.Text = "مستخدم جديد";
        tUser.Focus();
    }

    void LoadUser()
    {
        if (grid.CurrentRow == null) return;
        id = Db.L(grid.CurrentRow.Cells["id"].Value);
        var found = Db.Query("SELECT * FROM users WHERE id=@p0", id);
        if (found.Rows.Count == 0) { LoadGrid(); New(); return; }
        var r = found.Rows[0];
        tUser.Text = Db.S(r["username"]);
        tName.Text = Db.S(r["full_name"]);
        tPass.Clear();
        tPass.PlaceholderText = "اتركها فارغة لعدم التغيير";
        cAdmin.Checked = Db.L(r["is_admin"]) == 1;
        cActive.Checked = Db.L(r["active"]) == 1;
        var set = Db.Query("SELECT perm FROM user_perms WHERE user_id=@p0", id).Rows.Cast<DataRow>().Select(x => Db.S(x["perm"])).ToHashSet();
        for (int i = 0; i < perms.Items.Count; i++) perms.SetItemChecked(i, set.Contains(Session.AllPerms[i].Key));
        lblMode.Text = "تعديل: " + tUser.Text;
    }

    void Save()
    {
        if (!Session.Guard("users")) return;
        string u = tUser.Text.Trim(), p = tPass.Text;
        if (u == "") { Ui.Warn("أدخل اسم الدخول."); return; }
        if (id == 0 && p == "") { Ui.Warn("أدخل كلمة المرور."); return; }
        if (p != "" && p.Length < 4) { Ui.Warn("كلمة المرور قصيرة جدًا (4 أحرف على الأقل)."); return; }
        if (id == Session.UserId && (!cActive.Checked || (Session.IsAdmin && !cAdmin.Checked))) { Ui.Warn("لا يمكنك إيقاف حسابك أو إزالة صلاحية المدير عن نفسك."); return; }
        if (Db.L(Db.Scalar("SELECT COUNT(*) FROM users WHERE username=@p0 COLLATE NOCASE AND id<>@p1", u, id)) > 0) { Ui.Warn("اسم الدخول مستخدم مسبقًا."); return; }
        try
        {
            using var tx = new Tx();
            long uid = id;
            if (uid == 0)
                uid = tx.Insert("INSERT INTO users(username,pass_hash,full_name,is_admin,active) VALUES(@p0,@p1,@p2,@p3,@p4)",
                    u, Session.HashPassword(p), tName.Text.Trim(), cAdmin.Checked ? 1 : 0, cActive.Checked ? 1 : 0);
            else
            {
                tx.Exec("UPDATE users SET username=@p0, full_name=@p1, is_admin=@p2, active=@p3 WHERE id=@p4",
                    u, tName.Text.Trim(), cAdmin.Checked ? 1 : 0, cActive.Checked ? 1 : 0, uid);
                if (p != "") tx.Exec("UPDATE users SET pass_hash=@p0 WHERE id=@p1", Session.HashPassword(p), uid);
            }
            tx.Exec("DELETE FROM user_perms WHERE user_id=@p0", uid);
            foreach (int i in perms.CheckedIndices)
                tx.Exec("INSERT INTO user_perms(user_id,perm) VALUES(@p0,@p1)", uid, Session.AllPerms[i].Key);
            tx.Commit();
            id = uid;
            tPass.Clear();
            LoadGrid();
            Toast.Show("تم حفظ المستخدم " + u);
            lblMode.Text = "تعديل: " + u;
        }
        catch (Exception ex) { Ui.Warn("تعذر الحفظ: " + ex.Message); }
    }
}

/// <summary>تغيير كلمة مرور المستخدم الحالي</summary>
public class PasswordDialog : DialogShell
{
    public PasswordDialog() : base("تغيير كلمة المرور", 440, 420, "key-round")
    {
        var old = new TextBox { Width = 380, UseSystemPasswordChar = true };
        var p1 = new TextBox { Width = 380, UseSystemPasswordChar = true };
        var p2 = new TextBox { Width = 380, UseSystemPasswordChar = true };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        flow.Controls.Add(Ui.Labeled("كلمة المرور الحالية", old));
        flow.Controls.Add(Ui.Labeled("كلمة المرور الجديدة", p1));
        flow.Controls.Add(Ui.Labeled("تأكيد كلمة المرور", p2));
        Body.Controls.Add(flow);
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        var ok = AddButton("حفظ", DialogResult.None);
        AcceptButton = ok;
        ok.Click += (s, e) =>
        {
            var hash = Db.S(Db.Scalar("SELECT pass_hash FROM users WHERE id=@p0", Session.UserId));
            if (!Session.Verify(old.Text, hash)) { Ui.Warn("كلمة المرور الحالية غير صحيحة."); return; }
            if (p1.Text.Length < 4) { Ui.Warn("كلمة المرور قصيرة جدًا (4 أحرف على الأقل)."); return; }
            if (p1.Text != p2.Text) { Ui.Warn("التأكيد غير مطابق."); return; }
            Db.Exec("UPDATE users SET pass_hash=@p0 WHERE id=@p1", Session.HashPassword(p1.Text), Session.UserId);
            Session.UsingDefaultPassword = false;
            Toast.Show("تم تغيير كلمة المرور");
            DialogResult = DialogResult.OK;
        };
        Shown += (s, e) => old.Focus();
    }
}
