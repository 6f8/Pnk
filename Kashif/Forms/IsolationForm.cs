using Kashif.Device;

namespace Kashif;

/// <summary>نتيجة اختبار عزل واحد</summary>
/// <param name="LiveErrors">أسطر خطأ في السجل المباشر خلال الاختبار (-1 = لم يُراقب السجل)</param>
public sealed record IsolationResult(string Part, bool Stopped, string Duration, int LiveErrors = -1);

/// <summary>
/// وضع الطاولة (الواجهة 5): اختبار العزل بشاشة داكنة وعدّاد كبير.
/// اختر القطعة التي فصلتها ← «ابدأ» ← إن كان الآيفون موصولًا بالكيبل يلاحظ كاشف إعادة التشغيل وحده (ينقطع الاتصال)،
/// وإلا تحكم أنت بزر «أعاد التشغيل» أو «بقي يعمل».
/// </summary>
public class IsolationForm : BaseForm
{
    static readonly Color Bg = ColorTranslator.FromHtml("#121513"), Panel2 = ColorTranslator.FromHtml("#1B1F1C"), Line = ColorTranslator.FromHtml("#3A403C"),
        Ink = ColorTranslator.FromHtml("#F2EFE8"), Muted = ColorTranslator.FromHtml("#B7B2A8"), Teal = ColorTranslator.FromHtml("#7FD1C6"),
        Amber = ColorTranslator.FromHtml("#E8A36B"), Track = ColorTranslator.FromHtml("#252B27");

    readonly RebootWatch watch = new(() => new Usbmux().ListDevices().Select(d => d.Udid).ToList());
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly ComboBox part = new() { Width = 520, Font = Theme.F(13), DropDownStyle = ComboBoxStyle.DropDown };
    readonly Label title = new() { AutoSize = false, Dock = DockStyle.Top, Height = 120, Font = Theme.F(24, FontStyle.Bold), ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 70, Font = Theme.F(13), ForeColor = Muted, TextAlign = ContentAlignment.TopLeft };
    readonly Ring ring = new() { Dock = DockStyle.Fill };
    readonly ModernButton bStart, bStayed, bRebooted, bClose;
    readonly TimeSpan limit;
    bool polling;
    // السجل المباشر أثناء الاختبار: هل ما زالت أخطاء الحساسات تظهر بعد فصل القطعة؟
    LiveSession live;
    DateTime started;
    string liveNote = "";

    public List<IsolationResult> Results { get; } = new();

    public IsolationForm(Diagnosis d)
    {
        Text = "اختبار العزل — كاشف";
        Size = new Size(1180, 720);
        MinimumSize = new Size(900, 600);
        BackColor = Bg;
        ForeColor = Ink;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        limit = RebootWatch.LimitFor(d?.UptimeSeconds);

        foreach (var c in d?.Candidates.Where(c => c.Part != Parts.Ios).Take(8) ?? Enumerable.Empty<Candidate>()) part.Items.Add(c.Part);
        if (part.Items.Count > 0) part.SelectedIndex = 0;

        bStart = new ModernButton { Text = "ابدأ الاختبار", IconName = "clock", Width = 220, Height = 64, Font = Theme.FS(13) };
        bStayed = new ModernButton { Text = "بقي يعمل", IconName = "check", Width = 200, Height = 64, Font = Theme.FS(13), Visible = false };
        bRebooted = new ModernButton { Text = "أعاد التشغيل", IconName = "rotate-ccw", Kind = BtnKind.Secondary, Width = 200, Height = 64, Font = Theme.FS(13), Visible = false };
        bClose = new ModernButton { Text = "إغلاق", IconName = "x", Kind = BtnKind.Secondary, Width = 130, Height = 64, Font = Theme.FS(12) };

        var left = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(0, 10, 0, 0) };
        var pick = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, BackColor = Bg, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        pick.Controls.Add(new Label { Text = "ما الذي فصلته من الجهاز؟", AutoSize = false, Width = 520, Height = 36, Font = Theme.FS(12), ForeColor = Teal, TextAlign = ContentAlignment.MiddleLeft });
        pick.Controls.Add(part);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90, BackColor = Bg, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
        foreach (var b in new[] { bStart, bStayed, bRebooted, bClose }) { b.Margin = new Padding(0, 0, 14, 0); buttons.Controls.Add(b); }
        left.Controls.Add(status);
        left.Controls.Add(title);
        left.Controls.Add(pick);
        left.Controls.Add(buttons);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Bg, Padding = new Padding(48, 36, 48, 28) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(ring, 1, 0);
        Controls.Add(grid);

        ring.Limit = limit;
        ring.Marker = d?.UptimeSeconds is double u && u > 0 ? TimeSpan.FromSeconds(u) : null;
        Idle();

        bStart.Click += (s, e) => Start();
        bStayed.Click += (s, e) => Done(watch.Mark(false, DateTime.Now));
        bRebooted.Click += (s, e) => Done(watch.Mark(true, DateTime.Now));
        bClose.Click += (s, e) => Close();
        timer.Tick += (s, e) => Tick();
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        FormClosing += (s, e) => { watch.Stop(DateTime.Now); timer.Stop(); live?.Dispose(); };
    }

    void Idle()
    {
        title.Text = "اختبار العزل";
        var mark = ring.Marker is TimeSpan m ? $"كان الجهاز يعيد التشغيل بعد {RebootWatch.Clock(m)} من الإقلاع. " : "";
        status.Text = $"افصل فلاتة القطعة وشغّل الجهاز، ثم اضغط «ابدأ». {mark}المهلة {RebootWatch.Clock(limit)}: إن تجاوزها بلا إعادة تشغيل فالقطعة المفصولة هي السبب.";
        ring.Elapsed = TimeSpan.Zero;
        ring.State = RebootWatch.State.Idle;
        ring.Invalidate();
    }

    void Start()
    {
        if (string.IsNullOrWhiteSpace(part.Text)) { Dialogs.Warn("اكتب أو اختر القطعة التي فصلتها."); part.Focus(); return; }
        watch.Start(DateTime.Now, limit);
        title.Text = $"{part.Text.Trim()} مفصولة.\nانتظر حتى ينتهي العدّاد.";
        status.Text = watch.Watching
            ? "يراقب كاشف الآيفون بالكيبل: إن أعاد التشغيل سيلاحظ ذلك وحده. لا تفصل الكيبل أثناء الاختبار."
            : "الآيفون غير موصول بالكيبل (أو لا توجد خدمة Apple): راقبه بنفسك واضغط الزر المناسب.";
        bStart.Visible = false;
        bStayed.Visible = bRebooted.Visible = true;
        part.Enabled = false;
        ring.State = RebootWatch.State.Running;
        started = DateTime.Now;
        liveNote = "";
        live?.Dispose();
        live = null;
        if (watch.Watching)
        {
            live = new LiveSession();
            live.Flagged += f => liveNote = $"{f.Category}: {Short(f.Line)}";
            live.Start();
        }
        timer.Start();
    }

    async void Tick()
    {
        if (polling) return;
        polling = true;
        RebootWatch.State st;
        try { st = await Task.Run(() => watch.Tick(DateTime.Now)); }
        finally { polling = false; }
        if (IsDisposed) return;
        ring.Elapsed = watch.Elapsed(DateTime.Now);
        ring.Invalidate();
        if (live != null && st == RebootWatch.State.Running)
        {
            int n = live.CountSince(started);
            status.Text = "يراقب كاشف الآيفون بالكيبل: إن أعاد التشغيل سيلاحظ ذلك وحده. لا تفصل الكيبل أثناء الاختبار.\n" +
                (n == 0 ? "السجل المباشر: لا أخطاء حساسات منذ البدء." : $"السجل المباشر: {n} سطر خطأ منذ البدء — آخرها {liveNote}");
        }
        if (st != RebootWatch.State.Running) Done(st);
    }

    void Done(RebootWatch.State st)
    {
        if (st is not (RebootWatch.State.Rebooted or RebootWatch.State.Passed)) return;
        timer.Stop();
        bool stopped = st == RebootWatch.State.Passed;
        var dur = RebootWatch.Clock(watch.Result);
        int liveErrors = live != null ? live.CountSince(started) : -1;
        live?.Stop();
        Results.Add(new IsolationResult(part.Text.Trim(), stopped, dur, liveErrors));
        ring.State = st;
        ring.Elapsed = watch.Result;
        ring.Invalidate();
        title.Text = stopped ? $"بقي يعمل {dur} بلا إعادة تشغيل." : $"أعاد التشغيل بعد {dur}.";
        status.Text = stopped
            ? $"«{part.Text.Trim()}» هي السبب على الأرجح. ركّب قطعة سليمة وأعد الاختبار للتأكيد قبل الشراء."
            : $"«{part.Text.Trim()}» ليست السبب. أرجعها وافصل القطعة التالية ثم ابدأ اختبارًا جديدًا.";
        if (watch.Watching && st == RebootWatch.State.Rebooted) status.Text += "\n(لوحظ انقطاع الآيفون عن الكيبل — إن فصلته أنت فأعد الاختبار.)";
        if (liveErrors >= 0) status.Text += liveErrors == 0 ? "\nالسجل المباشر: لا أخطاء حساسات طوال الاختبار." : $"\nالسجل المباشر: {liveErrors} سطر خطأ خلال الاختبار.";
        bStayed.Visible = bRebooted.Visible = false;
        bStart.Text = "اختبار جديد";
        bStart.Visible = true;
        part.Enabled = true;
        if (!stopped && part.SelectedIndex >= 0 && part.SelectedIndex + 1 < part.Items.Count) part.SelectedIndex++;
    }

    static string Short(string line) => line.Length > 90 ? line[..90] + "…" : line;

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>العدّاد الدائري: يمتلئ حتى المهلة، وعلامة كهرمانية عند مدة الانهيار في السجل</summary>
    sealed class Ring : Control
    {
        public TimeSpan Limit, Elapsed;
        public TimeSpan? Marker;
        public RebootWatch.State State;

        public Ring()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Bg);
            Gfx.Hq(g);
            float size = Math.Min(Width, Height) - Dpi.S(30), w = Dpi.S(18);
            if (size < Dpi.S(80)) return;
            var r = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            using (var p = new Pen(Track, w)) g.DrawEllipse(p, r);
            float frac = Limit.TotalSeconds > 0 ? (float)Math.Clamp(Elapsed.TotalSeconds / Limit.TotalSeconds, 0, 1) : 0;
            var color = State == RebootWatch.State.Rebooted ? Amber : Teal;
            if (frac > 0) using (var p = new Pen(color, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round }) g.DrawArc(p, r, -90, 360 * frac);
            if (Marker is TimeSpan m && Limit.TotalSeconds > 0)
            {
                double a = (-90 + 360 * Math.Clamp(m.TotalSeconds / Limit.TotalSeconds, 0, 1)) * Math.PI / 180;
                float cx = r.X + r.Width / 2 + (float)Math.Cos(a) * r.Width / 2, cy = r.Y + r.Height / 2 + (float)Math.Sin(a) * r.Height / 2;
                using var b = new SolidBrush(Amber);
                g.FillEllipse(b, cx - w / 2, cy - w / 2, w, w);
            }
            var clock = RebootWatch.Clock(Elapsed);
            TextRenderer.DrawText(g, clock, FontKit.GetPx(size * 0.2f, FontStyle.Bold), Rectangle.Round(new RectangleF(r.X, r.Y + r.Height * 0.3f, r.Width, r.Height * 0.25f)), Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var sub = State switch
            {
                RebootWatch.State.Rebooted => "أعاد التشغيل",
                RebootWatch.State.Passed => "بقي يعمل",
                _ => "من " + RebootWatch.Clock(Limit) + (Marker != null ? " · العلامة = مدة الانهيار" : ""),
            };
            TextRenderer.DrawText(g, sub, Theme.F(12), Rectangle.Round(new RectangleF(r.X, r.Y + r.Height * 0.58f, r.Width, r.Height * 0.12f)), Muted, Gfx.Center);
        }
    }
}
