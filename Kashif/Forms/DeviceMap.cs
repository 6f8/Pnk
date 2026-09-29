namespace Kashif;

// الواجهة 6 «أين العطل داخل الجهاز؟»: أزرار الأسباب بجانب مخطط آيفون تُلوَّن فيه أماكن القطع المشتبه بها.
// الألوان والمقاسات مأخوذة من التصميم المعتمد (بكسل التصميم × دقة العرض).

/// <summary>ألوان الواجهة 6</summary>
public static class Palette6
{
    static Color C(string hex) => ColorTranslator.FromHtml(hex);
    public static readonly Color Ground = C("#F5F2EC"), Card = Color.White, Ink = C("#1B1A17"), Muted = C("#5F5B53"),
        Line = C("#E4DED3"), Screen = C("#FBF9F5"), ScreenLine = C("#D8D1C4"),
        Petrol = C("#0E5A55"), PetrolText = C("#0B4A46"), PetrolSoft = C("#E3EFEC"),
        Mid = C("#3F7F7A"), MidSoft = C("#CFE2DF"), Light = C("#8FB3AF"), LightText = C("#3F5F5C"),
        Warn = C("#8A3F0C"), WarnLine = C("#D8C3B0"), Unlikely = C("#F1EEE8"), UnlikelyLine = C("#C9C2B5");

    /// <summary>سبب مستبعد: لا يُرقَّم ويُرسم متقطعًا</summary>
    public static bool IsUnlikely(Candidate c) => c.Label == "احتمال بعيد";

    /// <summary>سطر السبب الثاني: الدرجة ثم أول جملة من السبب (أو تحذير صريح للسبب المستبعد)</summary>
    public static string SubLine(Candidate c)
    {
        if (IsUnlikely(c))
            return (c.Why ?? "").Contains("لا دليل") ? "لا دليل في السجل — لا تبدّلها" : "احتمال بعيد — افحص ما فوقه أولًا";
        var why = (c.Why ?? "").Split(new[] { " — ", "؛", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        if (why.Length > 70) why = why[..70].TrimEnd() + "…";
        return why == "" ? c.Label : $"{c.Label} · {why}";
    }
}

/// <summary>زر سبب في الواجهة 6: «1 · القطعة» وتحته الدرجة وسبب مختصر. الأول بإطار بترولي، المستبعد بإطار متقطع.</summary>
public class CauseButton : StackItem
{
    readonly int rank;
    readonly Candidate c;
    bool hover;

    public CauseButton(int rank, Candidate c)
    {
        this.rank = rank;
        this.c = c;
        Cursor = Cursors.Hand;
    }

    public string Part => c.Part;

    static int S(int v) => Dpi.S(v);
    bool First => rank == 1 && !Palette6.IsUnlikely(c);
    string Title => Palette6.IsUnlikely(c) ? c.Part : $"{rank} · {c.Part}";
    Font TitleFont => First ? Theme.F(13.5f, FontStyle.Bold) : Theme.FS(12.5f);
    static Font SubFont => Theme.F(10.5f);
    int Inner(int width) => width - 2 * S(20);

    public override int Measure(int width) =>
        S(First ? 18 : 16) * 2 + TextHeight(Title, TitleFont, Inner(width)) + S(6) + TextHeight(Palette6.SubLine(c), SubFont, Inner(width));

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        bool unlikely = Palette6.IsUnlikely(c);
        float bw = First ? S(2) : Math.Max(1, S(1));
        var r = new RectangleF(bw / 2f, bw / 2f, Width - bw - 1, Height - bw - 1);
        Gfx.FillRound(g, r, S(16), hover ? Gfx.Mix(Palette6.Card, Palette6.Ground, 0.6f) : Palette6.Card);
        using (var p = new Pen(First ? Palette6.Petrol : unlikely ? Palette6.WarnLine : Palette6.Line, bw))
        using (var path = Gfx.Round(r, S(16)))
        {
            if (unlikely) p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
            g.DrawPath(p, path);
        }
        int pad = S(First ? 18 : 16), w = Inner(Width);
        int th = TextHeight(Title, TitleFont, w);
        TextRenderer.DrawText(g, Title, TitleFont, new Rectangle(S(20), pad, w, th), Palette6.Ink, Wrap);
        var sub = Palette6.SubLine(c);
        TextRenderer.DrawText(g, sub, SubFont, new Rectangle(S(20), pad + th + S(6), w, TextHeight(sub, SubFont, w)),
            First ? Palette6.PetrolText : unlikely ? Palette6.Warn : Palette6.Muted, Wrap);
    }
}

/// <summary>
/// مخطط آيفون من الداخل (بإحداثيات تصميم 360×700): لا تُرسم إلا أماكن القطع التي بين الأسباب،
/// ملوّنة حسب ترتيبها ومكتوب عليها اسمها. المستبعد رمادي متقطع مع «لا دليل». النقر على مكان يفتح دليله وخطوات فحصه.
/// </summary>
public class DeviceMap : Control
{
    enum Shape { Rect, Circle, Notch }

    /// <summary>مكان على المخطط: مستطيله في إحداثيات التصميم، ومكان اسمه (أو في وسطه إن اتسع)، والقطع التي يمثلها</summary>
    sealed record Zone(string Name, RectangleF Box, Shape Shape, PointF? LabelAt, float FontPx, string[] Parts);

    static Zone Z(string name, float x, float y, float w, float h, Shape shape, PointF? label, float font, params string[] parts) =>
        new(name, new RectangleF(x, y, w, h), shape, label, font, parts);

    // مرتبة من الأكبر إلى الأصغر: الصغير يُرسم فوق الكبير ويُلتقط أولًا بالنقر
    static readonly Zone[] Zones =
    {
        Z("البطارية", 70, 170, 168, 330, Shape.Rect, null, 16, Parts.Battery),
        Z("البوردة", 250, 120, 60, 420, Shape.Rect, new PointF(280, 560), 14,
            Parts.Board, Parts.SmcLine, Parts.Pmu, Parts.SocRam, Parts.Nand, Parts.Wifi, Parts.Baseband, Parts.AudioIc, Parts.ChargeIc, Parts.Sensors),
        Z("فلاتة الشحن", 90, 600, 180, 46, Shape.Rect, null, 15, Parts.ChargingFlex),
        Z("الكاميرا", 56, 118, 84, 40, Shape.Rect, null, 13, Parts.Camera),
        Z("موصل البطارية", 144, 136, 94, 26, Shape.Rect, null, 12, Parts.BatteryConn),
        Z("السماعة", 46, 600, 36, 46, Shape.Rect, new PointF(64, 662), 12, Parts.AudioParts),
        Z("زر التشغيل", 334, 170, 12, 90, Shape.Rect, null, 12, Parts.PowerFlex),
        Z("البصمة", 160, 540, 40, 40, Shape.Circle, null, 12, Parts.TouchId),
        Z("Face ID", 58, 40, 52, 30, Shape.Rect, new PointF(84, 92), 12, Parts.Biometric),
        Z("حساس القرب والإضاءة", 120, 33, 154, 44, Shape.Notch, new PointF(180, 110), 16, Parts.FrontFlex),
    };

    readonly ToolTip tip = new() { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 15000 };
    List<Candidate> candidates = new();
    readonly List<(Zone Zone, RectangleF Rect, Candidate Cause)> drawn = new();
    Zone hover;
    string selected;

    /// <summary>نقر على مكان في المخطط — يحمل اسم القطعة الأعلى ترتيبًا فيه</summary>
    public event Action<string> PartClicked;

    public DeviceMap()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Palette6.Ground;
    }

    public void Set(Diagnosis d)
    {
        candidates = d?.Candidates.ToList() ?? new();
        hover = null;
        if (selected != null && RankOf(selected) == 0) selected = null;
        tip.SetToolTip(this, null);
        Invalidate();
    }

    /// <summary>تمييز مكان القطعة (عند النقر على سببها في القائمة)</summary>
    public void Select(string part)
    {
        selected = part;
        Invalidate();
    }

    int RankOf(string part) => candidates.FindIndex(c => c.Part == part) + 1;

    (int Rank, Candidate Cause) Best(Zone z) =>
        z.Parts.Select(p => RankOf(p)).Where(r => r > 0).OrderBy(r => r).Select(r => (r, candidates[r - 1])).FirstOrDefault();

    /// <summary>ألوان المكان حسب ترتيبه: الأول بترولي غامق، الثاني والثالث متوسط، البقية فاتح، والمستبعد رمادي متقطع</summary>
    static (Color Fill, Color Stroke, Color Text, bool Dashed) Style(int rank, Candidate c)
    {
        if (Palette6.IsUnlikely(c)) return (Palette6.Unlikely, Palette6.UnlikelyLine, Palette6.Warn, true);
        if (rank == 1) return (Palette6.PetrolSoft, Palette6.Petrol, Palette6.PetrolText, false);
        if (rank <= 3) return (Palette6.MidSoft, Palette6.Mid, Palette6.PetrolText, false);
        return (Palette6.PetrolSoft, Palette6.Light, Palette6.LightText, false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.Hq(g);
        drawn.Clear();

        // مقياس التصميم (360×700) داخل المساحة المتاحة، في المنتصف
        float k = Math.Min((Width - Dpi.S(8)) / 360f, (Height - Dpi.S(8)) / 700f);
        if (k <= 0.1f) return;
        float ox = (Width - 360 * k) / 2f, oy = (Height - 700 * k) / 2f;
        RectangleF R(RectangleF b) => new(ox + b.X * k, oy + b.Y * k, b.Width * k, b.Height * k);
        RectangleF Rc(float x, float y, float w, float h) => R(new RectangleF(x, y, w, h));
        Font Px(float px, bool bold) => FontKit.GetPx(Math.Max(9f, px * k), bold ? FontStyle.Bold : FontStyle.Regular);
        float Line(float w) => Math.Max(1.5f, w * k);

        // الهيكل والشاشة
        Gfx.FillRound(g, Rc(20, 10, 320, 680), 52 * k, Color.White);
        Gfx.DrawRound(g, Rc(20, 10, 320, 680), 52 * k, Palette6.Ink, Line(3));
        var inner = Rc(38, 28, 284, 644);
        Gfx.FillRound(g, inner, 38 * k, Palette6.Screen);
        int screenRank = RankOf(Parts.Screen);
        if (screenRank > 0)
        {
            var (_, stroke, text, dashed) = Style(screenRank, candidates[screenRank - 1]);
            using (var p = new Pen(stroke, Line(3)))
            using (var path = Gfx.Round(inner, 38 * k))
            {
                if (dashed) p.DashPattern = new[] { 4f, 3f };
                g.DrawPath(p, path);
            }
            DrawLabel(g, "الشاشة", Px(14, true), new PointF(ox + 180 * k, oy + 580 * k), text);
        }
        else Gfx.DrawRound(g, inner, 38 * k, Palette6.ScreenLine, Line(1.5f));

        foreach (var z in Zones)
        {
            var (rank, cause) = Best(z);
            if (cause == null) continue;
            var r = R(z.Box);
            drawn.Add((z, r, cause));
            var (fill, stroke, text, dashed) = Style(rank, cause);
            bool top = rank == 1 && !dashed;
            if (z == hover) fill = Gfx.Mix(fill, stroke, 0.2f);

            if (selected != null && z.Parts.Contains(selected))
            {
                var ring = RectangleF.Inflate(r, 6 * k, 6 * k);
                if (z.Shape == Shape.Circle) using (var p = new Pen(Palette6.Ink, Line(2))) g.DrawEllipse(p, ring);
                else Gfx.DrawRound(g, ring, 16 * k, Palette6.Ink, Line(2));
            }

            if (z.Shape == Shape.Notch)
            {
                // كما في التصميم: حبة الكاميرا الأمامية ممتلئة، ودائرة متقطعة لمكان الحساس بجانبها
                var pill = Rc(120, 40, 120, 30);
                Gfx.FillRound(g, pill, 15 * k, top ? Palette6.Petrol : fill);
                if (!top) Gfx.DrawRound(g, pill, 15 * k, stroke, Line(2));
                using var p = new Pen(top ? Palette6.Petrol : stroke, Line(3)) { DashPattern = new[] { 2f, 1.7f } };
                g.DrawEllipse(p, Rc(228, 33, 44, 44));
            }
            else
            {
                float rad = z.Shape == Shape.Circle ? Math.Min(r.Width, r.Height) / 2f : (z.Box.Width > 100 ? 18 : 10) * k;
                using var path = z.Shape == Shape.Circle ? Ellipse(r) : Gfx.Round(r, rad);
                using var b = new SolidBrush(fill);
                using var p = new Pen(stroke, Line(top ? 3 : 2));
                if (dashed) p.DashPattern = new[] { 4f, 3f };
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }

            // الاسم: في مكانه المحدد، أو في وسط المكان إن اتسع له (الأماكن الضيقة تكتفي بالتلميح)
            string name = z.Name + (dashed ? ((cause.Why ?? "").Contains("لا دليل") ? " — لا دليل" : " — مستبعد") : "");
            var font = Px(z.FontPx, top);
            if (z.LabelAt is PointF at) DrawLabel(g, name, font, new PointF(ox + at.X * k, oy + at.Y * k), text);
            else if (TextRenderer.MeasureText(name, font).Width <= r.Width - 6 * k)
                DrawLabel(g, name, font, new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f), text);
        }
    }

    static System.Drawing.Drawing2D.GraphicsPath Ellipse(RectangleF r)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddEllipse(r);
        return p;
    }

    static void DrawLabel(Graphics g, string text, Font font, PointF center, Color color)
    {
        var size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
        var box = new Rectangle((int)(center.X - size.Width / 2f) - 2, (int)(center.Y - size.Height / 2f), size.Width + 4, size.Height);
        TextRenderer.DrawText(g, text, font, box, color, Gfx.Center);
    }

    (Zone Zone, RectangleF Rect, Candidate Cause) HitTest(Point p)
    {
        for (int i = drawn.Count - 1; i >= 0; i--)
            if (RectangleF.Inflate(drawn[i].Rect, Dpi.S(4), Dpi.S(4)).Contains(p)) return drawn[i];
        return default;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit.Zone == hover) return;
        hover = hit.Zone;
        Cursor = hit.Cause != null ? Cursors.Hand : Cursors.Default;
        tip.SetToolTip(this, hit.Zone == null ? null : Describe(hit.Zone));
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover == null) return;
        hover = null;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var hit = HitTest(e.Location);
        if (hit.Cause == null) return;
        Select(hit.Cause.Part);
        PartClicked?.Invoke(hit.Cause.Part);
    }

    string Describe(Zone z) => string.Join("\n", z.Parts.Select(p => RankOf(p)).Where(r => r > 0).OrderBy(r => r)
        .Select(r => $"{r} · {candidates[r - 1].Part} — {candidates[r - 1].Label}"));

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
