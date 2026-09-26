using System.Drawing.Drawing2D;

namespace Kashif;

/// <summary>الرئيسية: بدء فحص جديد (فتح ملفات أو لصق)، مؤشرات الفحوصات، القطع الأكثر تسببًا بالبانك، وآخر الفحوصات</summary>
public class DashboardForm : BaseForm
{
    readonly MainForm main;

    public DashboardForm(MainForm main)
    {
        this.main = main;
        AutoScroll = true;
        AllowDrop = true;

        // ---------- الترحيب وبدء الفحص ----------
        var hero = new HeroStart { Dock = DockStyle.Top, Height = 170 };

        // ---------- المؤشرات ----------
        var kpis = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 140, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        var top = Stats.TopPart();
        var cards = new[]
        {
            Kpi("فحوصات اليوم", Stats.Today().ToString("#,0"), Theme.Orange, "scan-line", "تحليل محفوظ اليوم", HistoryForm.PageTitle),
            Kpi("أجهزة قيد الفحص", Stats.Open().ToString("#,0"), Theme.Warning, "wrench", "قيد الفحص أو بانتظار قطعة", HistoryForm.PageTitle),
            Kpi("كل الفحوصات", Stats.Total().ToString("#,0"), Theme.Info, "history", "في سجل الفحوصات", HistoryForm.PageTitle),
            Kpi("الأكثر تكرارًا", top == "" ? "—" : top, Theme.Purple, "trending-up", "القطعة الأرجح في آخر 30 يومًا", "الأعطال الأكثر تكرارًا"),
        };
        kpis.Controls.AddRange(cards);
        kpis.Resize += (s, e) =>
        {
            int w = (kpis.ClientSize.Width - Dpi.S(4) - cards.Length * Dpi.S(16)) / cards.Length;
            foreach (var c in cards) { c.Width = Math.Max(Dpi.S(150), w); c.Height = Dpi.S(122); }
        };

        // ---------- المخطط ----------
        var mid = new Panel { Dock = DockStyle.Top, Height = 300, Padding = new Padding(0, 6, 0, 8) };
        var chartCard = new CardPanel { Dock = DockStyle.Fill, Title = "القطع الأكثر تسببًا بالبانك", Subtitle = $"القطعة الأرجح في فحوصات آخر 90 يومًا — خبرة المحل: {Stats.Rules()} قاعدة", IconName = "chart-column" };
        var chart = new BarChart { Dock = DockStyle.Fill, HighlightLast = false };
        foreach (var p in Stats.PartsChart()) chart.Data.Add(p);
        chartCard.Controls.Add(chart);
        mid.Controls.Add(chartCard);

        // ---------- آخر الفحوصات ----------
        var recentCard = new CardPanel { Dock = DockStyle.Fill, Title = "آخر الفحوصات", Subtitle = "نقر مزدوج لفتح الفحص", IconName = "history" };
        var grid = Ui.NewGrid();
        if (Session.Can("history"))
            grid.DataSource = Db.Query(@"SELECT id, date AS [التاريخ], customer AS [الزبون], device AS [الجهاز], title AS [التشخيص], top_part AS [الأرجح], status AS [الحالة]
                FROM analyses ORDER BY id DESC LIMIT 50");
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) AnalyzeForm.OpenRecord(Db.L(grid.Rows[e.RowIndex].Cells["id"].Value)); };
        recentCard.Controls.Add(grid);
        var bottom = new Panel { Dock = DockStyle.Top, Height = 420, Padding = new Padding(0, 8, 0, 0) };
        bottom.Controls.Add(recentCard);

        Controls.Add(bottom);
        Controls.Add(mid);
        Controls.Add(kpis);
        Controls.Add(hero);
        Resize += (s, e) => bottom.Height = Math.Max(Dpi.S(320), ClientSize.Height - hero.Height - kpis.Height - mid.Height);

        // سحب ملفات البانك إلى الرئيسية يبدأ التحليل مباشرة
        DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files && Session.Can("analyze")) AnalyzeForm.OpenFiles(files); };
    }

    // لا تقفز الصفحة للأسفل عند تركيز الجدول
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;

    KpiCard Kpi(string title, string value, Color color, string icon, string hint, string page)
    {
        var k = new KpiCard { Title = title, Value = value, Accent = color, IconName = icon, Hint = hint };
        if (main.Pages.Any(p => p.Text == page)) { k.Cursor = Cursors.Hand; k.Click += (s, e) => main.Go(page); }
        return k;
    }

    /// <summary>شريط الترحيب بلون الهوية مع زري «فتح ملفات البانك» و«لصق من الحافظة»</summary>
    sealed class HeroStart : Panel
    {
        readonly ModernButton open = new() { Text = "فتح ملفات البانك", IconName = "folder-open", Kind = BtnKind.Amber, Height = 46, Width = 190 };
        readonly ModernButton paste = new() { Text = "لصق من الحافظة", IconName = "clipboard-list", Kind = BtnKind.Glass, Height = 46, Width = 180 };
        readonly System.Globalization.CultureInfo ar = new("ar-IQ");

        public HeroStart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Hero1;
            ar.DateTimeFormat.Calendar = new System.Globalization.GregorianCalendar();
            Controls.Add(open);
            Controls.Add(paste);
            open.Click += (s, e) => Start(files: true);
            paste.Click += (s, e) => Start(files: false);
        }

        static void Start(bool files)
        {
            if (!Session.Guard("analyze")) return;
            if (files)
            {
                using var ofd = new OpenFileDialog
                {
                    Multiselect = true, Title = "اختر ملفات البانك",
                    Filter = "سجلات البانك (*.ips;*.txt;*.json;*.log;*.panic)|*.ips;*.txt;*.json;*.log;*.panic|كل الملفات (*.*)|*.*",
                };
                if (ofd.ShowDialog() == DialogResult.OK) AnalyzeForm.OpenFiles(ofd.FileNames);
                return;
            }
            try
            {
                if (Clipboard.ContainsFileDropList()) AnalyzeForm.OpenFiles(Clipboard.GetFileDropList().Cast<string>().ToList());
                else if (Clipboard.ContainsText()) AnalyzeForm.OpenText(Clipboard.GetText());
                else Ui.Warn("الحافظة لا تحتوي نصًا. انسخ نص سجل البانك أولًا.");
            }
            catch (Exception ex) { Ui.Warn("تعذرت قراءة الحافظة: " + ex.Message); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (open == null) return;
            int y = Height - open.Height - Dpi.S(26);
            open.Location = new Point(Width - Dpi.S(32) - open.Width, y);
            paste.Location = new Point(open.Left - Dpi.S(10) - paste.Width, y);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            Gfx.Hq(g);
            var r = new RectangleF(0, 0, Width - 1, Height - Dpi.S(6));
            using (var path = Gfx.Round(r, Dpi.S(14f)))
            using (var b = new LinearGradientBrush(r, Theme.Hero1, Theme.Hero2, 20f))
                g.FillPath(b, path);
            using (var c = new SolidBrush(Color.FromArgb(22, 255, 255, 255))) g.FillEllipse(c, Dpi.S(-60), Dpi.S(-90), Dpi.S(260), Dpi.S(260));
            int right = Width - Dpi.S(32);
            TextRenderer.DrawText(g, "مرحبًا، " + Session.UserName, Theme.FS(17), new Rectangle(Dpi.S(32), Dpi.S(18), right - Dpi.S(32), Dpi.S(36)), Color.White, Gfx.RtlStart);
            TextRenderer.DrawText(g, DateTime.Now.ToString("dddd d MMMM yyyy", ar) + "  —  " + Settings.Get("shop_name") + "  —  اسحب ملفات panic-full إلى هنا أو افتحها", Theme.F(10),
                new Rectangle(Dpi.S(32), Dpi.S(56), right - Dpi.S(32), Dpi.S(24)), Theme.HeroAccent, Gfx.RtlStart);
        }
    }
}
