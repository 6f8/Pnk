namespace Kashif;

// عناصر العرض في شاشة التحليل: بطاقات الأسباب، قائمة خطوات الفحص، ورأس الخلاصة العريض.
// هذه البطاقات تُنشأ بعد تكبير الشاشة حسب دقة العرض، فأبعادها كلها بالبكسل الفعلي (Dpi.S) ولا تُكبَّر مرة ثانية.

/// <summary>بطاقة داخل <see cref="CardStack"/>: تحدد ارتفاعها بنفسها حسب العرض المتاح</summary>
public abstract class StackItem : Control
{
    protected StackItem()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    public abstract int Measure(int width);

    protected const TextFormatFlags Wrap = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

    protected static int TextHeight(string text, Font f, int width) =>
        string.IsNullOrEmpty(text) || width <= 0 ? 0 : TextRenderer.MeasureText(text, f, new Size(width, int.MaxValue), Wrap).Height;
}

/// <summary>قائمة عمودية قابلة للتمرير من البطاقات، كل بطاقة بعرض القائمة كاملًا</summary>
public class CardStack : FlowLayoutPanel
{
    readonly Label empty = new() { AutoSize = false, ForeColor = Theme.Muted, Font = Theme.F(10.5f), TextAlign = ContentAlignment.MiddleCenter };

    public CardStack()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
    }

    public string EmptyText { get; set; } = "لا يوجد شيء لعرضه";

    public void SetItems(IEnumerable<StackItem> items)
    {
        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); if (c != empty) c.Dispose(); }
        var list = items.ToList();
        if (list.Count == 0) { empty.Text = EmptyText; Controls.Add(empty); }
        else foreach (var i in list) { i.Margin = new Padding(0, 0, 0, Dpi.S(10)); Controls.Add(i); }
        Fit();
        ResumeLayout(true);
        AutoScrollPosition = Point.Empty;
    }

    protected override void OnClientSizeChanged(EventArgs e) { base.OnClientSizeChanged(e); Fit(); }

    void Fit()
    {
        int w = Math.Max(Dpi.S(120), ClientSize.Width - Padding.Horizontal - Dpi.S(2));
        foreach (Control c in Controls)
        {
            if (c is StackItem s) { s.Width = w; s.Height = s.Measure(w); }
            else if (c == empty) { empty.Width = w; empty.Height = Math.Max(Dpi.S(80), ClientSize.Height - Dpi.S(20)); }
        }
    }
}

/// <summary>سبب محتمل: رقم الترتيب، اسم القطعة، شارة الدرجة، شريط الدرجة، ولماذا</summary>
public class CauseCard : StackItem
{
    readonly int rank;
    readonly Candidate c;

    public CauseCard(int rank, Candidate c) { this.rank = rank; this.c = c; }

    static int S(int v) => Dpi.S(v);
    int InnerWidth(int width) => width - S(28) - S(40) - S(14);

    public override int Measure(int width) => S(16) + S(28) + S(10) + S(8) + S(12) + TextHeight(c.Why, Theme.F(10), InnerWidth(width)) + S(16);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var (fg, bg) = Theme.StatusColors(c.Label);
        if (fg == Color.Empty) (fg, bg) = (Theme.Text2, Theme.GraySoft);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Gfx.FillRound(g, r, S(12), rank == 1 ? Gfx.Mix(bg, Color.White, 0.55f) : Theme.SurfaceAlt);
        Gfx.DrawRound(g, r, S(12), rank == 1 ? Gfx.Mix(fg, Color.White, 0.55f) : Theme.Border);

        int right = Width - S(14), top = S(16);
        // رقم الترتيب
        var circle = new RectangleF(right - S(36), top - S(2), S(34), S(34));
        using (var b = new SolidBrush(rank == 1 ? fg : Theme.GraySoft)) g.FillEllipse(b, circle);
        TextRenderer.DrawText(g, rank.ToString(), Theme.FS(11), Rectangle.Round(circle), rank == 1 ? Color.White : Theme.Text2, Gfx.Center);
        int textRight = (int)circle.X - S(14), left = S(14);

        // شارة الدرجة في نهاية السطر
        var chipFont = Theme.FS(9);
        int chipW = TextRenderer.MeasureText(g, c.Label, chipFont, Size.Empty, TextFormatFlags.NoPadding).Width + S(24);
        var chip = new Rectangle(left, top, chipW, S(26));
        Gfx.FillRound(g, chip, chip.Height / 2f, bg);
        TextRenderer.DrawText(g, c.Label, chipFont, chip, fg, Gfx.Center);

        // اسم القطعة
        TextRenderer.DrawText(g, Theme.Bidi(c.Part), Theme.FS(12), new Rectangle(chip.Right + S(12), top - S(2), textRight - chip.Right - S(12), S(30)), Theme.Ink, Gfx.RtlStart);

        // شريط الدرجة (من اليمين)
        int barY = top + S(28) + S(10), barW = textRight - left;
        var track = new RectangleF(left, barY, barW, S(8));
        Gfx.FillRound(g, track, S(4), Theme.GraySoft);
        float fill = barW * Math.Clamp(c.Score, 1, 99) / 100f;
        Gfx.FillRound(g, new RectangleF(textRight - fill, barY, fill, S(8)), S(4), fg);

        // لماذا
        TextRenderer.DrawText(g, c.Why, Theme.F(10), new Rectangle(left, barY + S(8) + S(12), InnerWidth(Width), Height - barY - S(20)), Theme.Text2, Wrap);
    }
}

/// <summary>خطوة فحص قابلة للتعليم: النقر يعلّمها «تم» (للمتابعة أثناء العمل على الجهاز)</summary>
public class StepCard : StackItem
{
    readonly int number;
    readonly string text;
    bool done, hover;
    public event EventHandler DoneChanged;

    public StepCard(int number, string text, bool done)
    {
        this.number = number;
        this.text = text;
        this.done = done;
        Cursor = Cursors.Hand;
    }

    public bool Done => done;
    public string StepText => text;

    static int S(int v) => Dpi.S(v);
    int TextWidth(int width) => width - S(28) - S(34) - S(14);

    public override int Measure(int width) => Math.Max(S(58), S(14) + TextHeight(text, Theme.F(10.5f), TextWidth(width)) + S(14) + S(2));

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { done = !done; Invalidate(); DoneChanged?.Invoke(this, EventArgs.Empty); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Gfx.FillRound(g, r, S(12), done ? Theme.SuccessSoft : hover ? Gfx.Mix(Theme.SurfaceAlt, Theme.Border, 0.3f) : Theme.SurfaceAlt);
        Gfx.DrawRound(g, r, S(12), done ? Gfx.Mix(Theme.Success, Color.White, 0.6f) : Theme.Border);
        int right = Width - S(14);
        var box = new RectangleF(right - S(30), (Height - S(30)) / 2f, S(30), S(30));
        if (done)
        {
            using (var b = new SolidBrush(Theme.Success)) g.FillEllipse(b, box);
            Icons.Draw(g, "check", box, Color.White, 16);
        }
        else
        {
            using (var p = new Pen(Theme.BorderStrong, S(2))) g.DrawEllipse(p, box);
            TextRenderer.DrawText(g, number.ToString(), Theme.FS(10), Rectangle.Round(box), Theme.Text2, Gfx.Center);
        }
        TextRenderer.DrawText(g, text, Theme.F(10.5f), new Rectangle(S(14), S(14), TextWidth(Width), Height - S(28)), done ? Theme.Muted : Theme.Ink, Wrap | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// رأس الخلاصة بعرض الشاشة: الجهاز، التشخيص، الخلاصة، الشارات والتنبيه — وعلى الجهة الأخرى أعلى 3 أسباب بأشرطة.
/// بلا سجل: إرشاد البدء ومن أين يأتي ملف البانك.
/// </summary>
public class VerdictHero : Control
{
    Diagnosis d;
    List<string> extra = new();

    /// <summary>عرض أعلى 3 أسباب في جهة الرأس (يُطفأ عندما تكون الأسباب ظاهرة بجانبه)</summary>
    public bool ShowTopCauses { get; set; } = true;

    public VerdictHero()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void Show(Diagnosis diagnosis, List<string> extraWarnings)
    {
        d = diagnosis;
        extra = extraWarnings ?? new();
        Invalidate();
    }

    public static Color ConfColor(string c) => c switch { "عالية" => Theme.Success, "متوسطة" => Theme.Warning, _ => Theme.Gray };

    static int S(int v) => Dpi.S(v);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(S(2), 0.5f, Width - S(4), Height - S(5));
        Gfx.Shadow(g, r, S(16));
        Gfx.FillRound(g, r, S(16), Theme.Surface);
        Gfx.DrawRound(g, r, S(16), Theme.Border);

        var accent = d == null ? Theme.Brand : ConfColor(d.Confidence);
        Gfx.FillRound(g, new RectangleF(r.Right - S(8), r.Y + S(16), S(5), r.Height - S(32)), S(3), accent);

        // الجهة الأخرى: أعلى 3 أسباب — فقط إذا كانت الشاشة عريضة بما يكفي
        bool side = ShowTopCauses && d != null && d.Candidates.Count > 0 && Width >= S(980);
        int sideW = side ? Math.Min(S(420), Width * 36 / 100) : 0;
        int left = (int)r.X + S(24) + (side ? sideW + S(24) : 0);
        int right = (int)r.Right - S(30);
        int box = S(52);
        var ir = new RectangleF(right - box, S(22), box, box);
        Gfx.FillRound(g, ir, S(16), Gfx.Mix(accent, Color.White, 0.86f));
        Icons.Draw(g, d == null ? "scan-line" : d.Confidence == "عالية" ? "badge-check" : "circle-alert", ir, accent, 26);
        int tx = (int)ir.X - S(16);
        const TextFormatFlags wrap = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        if (d == null)
        {
            TextRenderer.DrawText(g, "افتح ملفات panic-full أو الصق نص البانك", Theme.FS(16), new Rectangle(left, S(22), tx - left, S(34)), Theme.Ink, Gfx.RtlStart);
            TextRenderer.DrawText(g,
                "على الآيفون: الإعدادات ← الخصوصية والأمان ← التحليلات والتحسينات ← بيانات التحليلات ← الملف الذي يبدأ بـ panic-full. " +
                "افتحه ثم شاركه أو انسخ نصه كاملًا. يقبل البرنامج عدة ملفات معًا، والنص المنسوخ من صورة، والسحب والإفلات.",
                Theme.F(10.5f), new Rectangle(left, S(64), tx - left, S(72)), Theme.Text2, wrap);
            TextRenderer.DrawText(g, "Ctrl+O فتح ملفات  •  Ctrl+V لصق  •  F9 تحليل النص  •  Ctrl+S حفظ  •  Ctrl+P طباعة",
                Theme.F(9.5f), new Rectangle(left, Height - S(46), right - left, S(24)), Theme.Muted, Gfx.RtlStart);
            return;
        }

        var device = string.Join("  •  ", new[]
        {
            d.Device == "" ? "جهاز غير معروف" : d.Device, d.Soc, d.Ios != "" ? "iOS " + d.Ios + (d.Build != "" ? $" ({d.Build})" : "") : "",
            d.LogCount > 1 ? $"{d.LogCount} سجلات" : d.Time,
        }.Where(x => !string.IsNullOrEmpty(x)));
        TextRenderer.DrawText(g, Theme.Bidi(device), Theme.F(10), new Rectangle(left, S(18), tx - left, S(22)), Theme.Muted, Gfx.RtlStart);
        TextRenderer.DrawText(g, Theme.Bidi(d.Title), Theme.FS(16), new Rectangle(left, S(42), tx - left, S(34)), Theme.Ink, Gfx.RtlStart);
        TextRenderer.DrawText(g, d.Summary, Theme.F(11), new Rectangle(left, S(84), right - left, S(50)), Theme.Text2, wrap);

        // الشارات
        int x = right, chipY = S(138);
        void Chip(string text, Color fg, Color bg)
        {
            var f = Theme.FS(9.5f);
            int w = TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding).Width + S(24);
            if (x - w < left) return;
            var cr = new Rectangle(x - w, chipY, w, S(28));
            Gfx.FillRound(g, cr, cr.Height / 2f, bg);
            TextRenderer.DrawText(g, text, f, cr, fg, Gfx.Center);
            x -= w + S(8);
        }
        Chip("الثقة: " + d.Confidence, ConfColor(d.Confidence), Gfx.Mix(ConfColor(d.Confidence), Color.White, 0.86f));
        Chip(d.Kind, Theme.Brand, Theme.BrandSoft);
        if (!side && d.TopPart != "") Chip("الأرجح: " + d.TopPart, Theme.Danger, Theme.DangerSoft);
        if (d.AnswersApplied > 0) Chip($"أجوبة الفحص: {d.AnswersApplied}", Theme.Success, Theme.SuccessSoft);
        if (d.ModelSpecific) Chip("معلومة خاصة بالموديل", Theme.Success, Theme.SuccessSoft);

        var warns = extra.Concat(d.Warnings).ToList();
        if (warns.Count > 0)
            TextRenderer.DrawText(g, "⚠ " + warns[0] + (warns.Count > 1 ? $"  (+{warns.Count - 1} تنبيه آخر)" : ""), Theme.F(9.5f),
                new Rectangle(left, ShowTopCauses ? Height - S(40) : chipY + S(36), right - left, S(24)), Theme.Warning, Gfx.RtlStart);

        if (!side) return;
        // أعلى 3 أسباب بأشرطة
        int sx = (int)r.X + S(24), sy = S(22);
        var panel = new RectangleF(sx, sy, sideW, Height - S(50));
        Gfx.FillRound(g, panel, S(12), Theme.SurfaceAlt);
        TextRenderer.DrawText(g, "الأسباب الأرجح", Theme.FS(10), new Rectangle(sx + S(14), sy + S(10), sideW - S(28), S(22)), Theme.Muted, Gfx.RtlStart);
        int rowY = sy + S(40), rowH = Math.Max(S(34), ((int)panel.Height - S(48)) / 3);
        foreach (var c in d.Candidates.Take(3))
        {
            var (fg, _) = Theme.StatusColors(c.Label);
            if (fg == Color.Empty) fg = Theme.Gray;
            int inner = sideW - S(28);
            TextRenderer.DrawText(g, Theme.Bidi(c.Part), Theme.FS(10), new Rectangle(sx + S(14), rowY, inner - S(90), S(22)), Theme.Ink, Gfx.RtlStart);
            TextRenderer.DrawText(g, c.Label, Theme.F(9), new Rectangle(sx + S(14), rowY, S(86), S(22)), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft);
            var track = new RectangleF(sx + S(14), rowY + S(24), inner, S(7));
            Gfx.FillRound(g, track, S(3), Theme.GraySoft);
            float fill = inner * Math.Clamp(c.Score, 1, 99) / 100f;
            Gfx.FillRound(g, new RectangleF(track.Right - fill, track.Y, fill, track.Height), S(3), fg);
            rowY += rowH;
        }
    }
}

/// <summary>دليل من السجل كبطاقة: ماذا وُجد، قيمته، ومعناه — النقر يفتح نص السجل مظللًا عند مكانه</summary>
public class EvidenceCard : StackItem
{
    readonly Evidence e;
    bool hover;
    public event EventHandler Open;

    public EvidenceCard(Evidence e)
    {
        this.e = e;
        if (!string.IsNullOrEmpty(e.Needle)) Cursor = Cursors.Hand;
    }

    static int S(int v) => Dpi.S(v);
    int Inner(int width) => width - S(28);

    public override int Measure(int width) =>
        S(12) + S(22) + S(4) + TextHeight(Theme.Bidi(e.Value), Theme.F(10), Inner(width)) + (e.Meaning == "" ? 0 : S(4) + TextHeight(e.Meaning, Theme.F(9), Inner(width))) + S(12);

    protected override void OnMouseEnter(EventArgs ev) { hover = true; Invalidate(); base.OnMouseEnter(ev); }
    protected override void OnMouseLeave(EventArgs ev) { hover = false; Invalidate(); base.OnMouseLeave(ev); }
    protected override void OnClick(EventArgs ev) { if (!string.IsNullOrEmpty(e.Needle)) Open?.Invoke(this, EventArgs.Empty); base.OnClick(ev); }

    protected override void OnPaint(PaintEventArgs pe)
    {
        var g = pe.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var back = e.IsExam ? Theme.SuccessSoft : e.IsInfo ? Theme.Surface : Theme.SurfaceAlt;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Gfx.FillRound(g, r, S(10), hover && Cursor == Cursors.Hand ? Gfx.Mix(back, Theme.BrandSoft, 0.6f) : back);
        Gfx.DrawRound(g, r, S(10), e.IsExam ? Gfx.Mix(Theme.Success, Color.White, 0.6f) : Theme.Border);
        int w = Inner(Width), y = S(12);
        var titleColor = e.IsExam ? Theme.Success : e.IsInfo ? Theme.Muted : Theme.Brand;
        TextRenderer.DrawText(g, e.What + (Cursor == Cursors.Hand ? "  ↗" : ""), Theme.FS(9.5f), new Rectangle(S(14), y, w, S(22)), titleColor, Gfx.RtlStart);
        y += S(22) + S(4);
        var value = Theme.Bidi(e.Value);
        int vh = TextHeight(value, Theme.F(10), w);
        TextRenderer.DrawText(g, value, Theme.F(10), new Rectangle(S(14), y, w, vh), Theme.Ink, Wrap);
        y += vh + S(4);
        if (e.Meaning != "") TextRenderer.DrawText(g, e.Meaning, Theme.F(9), new Rectangle(S(14), y, w, Height - y - S(8)), Theme.Muted, Wrap);
    }
}

/// <summary>
/// الفحص التفاعلي: سؤال عن اختبار عملي على الجهاز وأزرار أجوبته. كل جواب يعيد ترتيب الأسباب فورًا.
/// مرسوم بالكامل (الأزرار مساحات قابلة للنقر) فيبقى حادًا في كل دقة عرض.
/// </summary>
public class InterviewView : Control
{
    PanicKnowledge.Question q;
    int remaining, answeredCount;
    bool confirmed;
    List<string> answeredLines = new();
    readonly List<(Rectangle Rect, int Action)> hits = new();
    int hover = int.MinValue;

    /// <summary>رقم الجواب (0...)، أو -1 تخطي السؤال، -2 تراجع عن آخر جواب، -3 إعادة الفحص من البداية</summary>
    public event Action<int> Clicked;

    public const int Skip = -1, Undo = -2, Reset = -3;

    public InterviewView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public PanicKnowledge.Question Question => q;

    public void Set(PanicKnowledge.Question question, int remainingCount, IEnumerable<string> answered, bool isConfirmed)
    {
        q = question;
        remaining = remainingCount;
        answeredLines = answered.ToList();
        answeredCount = answeredLines.Count;
        confirmed = isConfirmed;
        Invalidate();
    }

    static int S(int v) => Dpi.S(v);
    const TextFormatFlags Wrap = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

    /// <summary>حساب المواضع (للرسم والنقر وللارتفاع المطلوب) بعرض محدد</summary>
    int Arrange(int width, Graphics g, bool draw)
    {
        hits.Clear();
        int pad = S(18), inner = width - 2 * pad, right = width - pad, y = pad;
        void Text(string t, Font f, Color c, int h) { if (draw) TextRenderer.DrawText(g, t, f, new Rectangle(pad, y, inner, h), c, Wrap); }
        int H(string t, Font f) => string.IsNullOrEmpty(t) ? 0 : TextRenderer.MeasureText(t, f, new Size(inner, int.MaxValue), Wrap).Height;

        // الرأس: العنوان والتقدم، وروابط التراجع والإعادة في نهاية السطر
        if (draw)
        {
            var box = new RectangleF(right - S(26), y, S(26), S(26));
            Gfx.FillRound(g, box, S(8), Theme.Brand);
            Icons.Draw(g, "list-checks", box, Color.White, 14);
            var title = "الفحص التفاعلي" + (q != null ? $"  —  {(answeredCount + 1)} من {answeredCount + remaining}" : "");
            TextRenderer.DrawText(g, title, Theme.FS(11), new Rectangle(pad, y, inner - S(34), S(26)), Theme.Ink, Gfx.RtlStart);
        }
        int lx = pad;
        void Link(string text, int action)
        {
            var f = Theme.FS(9.5f);
            int w = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding).Width + S(16);
            var rr = new Rectangle(lx, y, w, S(26));
            hits.Add((rr, action));
            if (draw)
            {
                if (hover == action) Gfx.FillRound(g, rr, S(8), Theme.BrandSoft);
                TextRenderer.DrawText(g, text, f, rr, Theme.Brand, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft);
            }
            lx += w + S(6);
        }
        if (answeredCount > 0) { Link("إعادة الفحص", Reset); Link("تراجع", Undo); }
        y += S(26) + S(10);

        if (q == null)
        {
            var msg = confirmed ? "اكتمل الفحص: التشخيص مؤكد بالفحص العملي." : answeredCount > 0 ? "لا توجد أسئلة أخرى للأسباب الحالية." : "لا توجد أسئلة لهذه الأسباب — اتبع خطوات الفحص.";
            int mh = H(msg, Theme.FS(11));
            Text(msg, Theme.FS(11), confirmed ? Theme.Success : Theme.Text2, mh);
            y += mh + S(8);
        }
        else
        {
            int qh = H(q.Text, Theme.FS(12));
            Text(q.Text, Theme.FS(12), Theme.Ink, qh);
            y += qh + S(6);
            if (q.Hint != "")
            {
                int hh = H(q.Hint, Theme.F(9.5f));
                Text(q.Hint, Theme.F(9.5f), Theme.Muted, hh);
                y += hh + S(6);
            }
            y += S(6);
            // أزرار الأجوبة: من بداية السطر (اليمين) وتلتف
            int x = right, rowH = S(38);
            var pills = q.Answers.Select((a, i) => (a.Label, i)).Append(("تخطي السؤال", Skip)).ToList();
            foreach (var (label, action) in pills)
            {
                var f = Theme.FS(10);
                int w = Math.Min(inner, TextRenderer.MeasureText(label, f, Size.Empty, TextFormatFlags.NoPadding).Width + S(36));
                if (x - w < pad) { x = right; y += rowH + S(8); }
                var rr = new Rectangle(x - w, y, w, rowH);
                hits.Add((rr, action));
                if (draw)
                {
                    bool isSkip = action == Skip, h = hover == action;
                    var fill = isSkip ? (h ? Theme.GraySoft : Theme.Surface) : (h ? Theme.BrandDark : Theme.Brand);
                    Gfx.FillRound(g, rr, rowH / 2f, fill);
                    if (isSkip) Gfx.DrawRound(g, rr, rowH / 2f, Theme.BorderStrong);
                    TextRenderer.DrawText(g, label, f, rr, isSkip ? Theme.Text2 : Color.White, Gfx.Center);
                }
                x -= w + S(8);
            }
            y += rowH + S(12);
        }

        // آخر الأجوبة
        foreach (var line in answeredLines.TakeLast(3))
        {
            if (draw) TextRenderer.DrawText(g, "✓ " + line, Theme.F(9), new Rectangle(pad, y, inner, S(20)), Theme.Success, Gfx.RtlStart);
            y += S(22);
        }
        if (answeredLines.Count > 3 && draw)
            TextRenderer.DrawText(g, $"و{answeredLines.Count - 3} أجوبة أخرى في «الأدلة»", Theme.F(9), new Rectangle(pad, y, inner, S(20)), Theme.Muted, Gfx.RtlStart);
        if (answeredLines.Count > 3) y += S(22);
        return y + pad - S(6);
    }

    /// <summary>الارتفاع المطلوب بعرض محدد</summary>
    public int Measure(int width) => Arrange(width, null, false);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(S(2), 0.5f, Width - S(4), Height - S(3));
        Gfx.FillRound(g, r, S(14), Gfx.Mix(Theme.BrandSoft, Color.White, 0.35f));
        Gfx.DrawRound(g, r, S(14), Theme.BrandSoft2);
        Arrange(Width, g, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = hits.FirstOrDefault(x => x.Rect.Contains(e.Location)) is { Rect.Width: > 0 } hit ? hit.Action : int.MinValue;
        Cursor = h == int.MinValue ? Cursors.Default : Cursors.Hand;
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = int.MinValue; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var hit = hits.FirstOrDefault(x => x.Rect.Contains(e.Location));
        if (hit.Rect.Width > 0) Clicked?.Invoke(hit.Action);
        base.OnMouseClick(e);
    }
}
