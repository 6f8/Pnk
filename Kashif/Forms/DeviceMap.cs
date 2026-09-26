namespace Kashif;

/// <summary>
/// خريطة الجهاز: مخطط آيفون من الداخل تُلوَّن فيه أماكن القطع حسب ترتيب الأسباب (الأغمق = الأرجح)،
/// وعلى كل مكان رقم السبب في القائمة. البطارية بلا دليل تُرسم متقطعة ورمادية حتى لا تُبدَّل بلا سبب.
/// الأسباب التي ليس لها مكان على المخطط (برمجي، سوائل، آخر قطعة، ملحق) تُكتب تحت الجهاز.
/// </summary>
public class DeviceMap : Control
{
    /// <summary>مكان على المخطط بإحداثيات نسبية (0–1) داخل هيكل الجهاز، والقطع التي يمثلها</summary>
    sealed record Zone(string Name, RectangleF Box, bool Round, params string[] Parts);

    static readonly Zone[] Zones =
    {
        new("الكاميرا الخلفية", new(0.10f, 0.05f, 0.30f, 0.15f), false, Parts.Camera),
        new("البوردة", new(0.46f, 0.05f, 0.44f, 0.20f), false,
            Parts.Board, Parts.SmcLine, Parts.Pmu, Parts.SocRam, Parts.Nand, Parts.Wifi, Parts.Baseband, Parts.AudioIc, Parts.ChargeIc, Parts.Sensors),
        new("فلاتة الكاميرا الأمامية والحساسات", new(0.30f, 0.012f, 0.40f, 0.03f), false, Parts.FrontFlex),
        new("Face ID", new(0.14f, 0.012f, 0.13f, 0.03f), false, Parts.Biometric),
        new("زر التشغيل", new(0.93f, 0.16f, 0.05f, 0.16f), false, Parts.PowerFlex),
        new("موصل البطارية", new(0.56f, 0.27f, 0.22f, 0.035f), false, Parts.BatteryConn),
        new("البطارية", new(0.14f, 0.32f, 0.72f, 0.44f), false, Parts.Battery),
        new("زر البصمة", new(0.42f, 0.78f, 0.16f, 0.07f), true, Parts.TouchId),
        new("السماعة والميكروفون", new(0.10f, 0.865f, 0.22f, 0.05f), false, Parts.AudioParts),
        new("فلاتة الشحن", new(0.36f, 0.865f, 0.54f, 0.05f), false, Parts.ChargingFlex),
    };

    readonly ToolTip tip = new() { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 15000 };
    List<Candidate> candidates = new();
    readonly List<(Zone Zone, RectangleF Rect, int Rank, Candidate Cause)> drawn = new();
    Zone hover;
    string selected;

    /// <summary>نقر على مكان في المخطط — يحمل اسم القطعة الأعلى ترتيبًا فيه</summary>
    public event Action<string> PartClicked;

    public DeviceMap()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
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

    static int S(int v) => Dpi.S(v);

    int RankOf(string part) => candidates.FindIndex(c => c.Part == part) + 1;

    /// <summary>أفضل سبب في المكان (أقل رقم ترتيب)، أو لا شيء إن لم تكن أي قطعة منه ضمن الأسباب</summary>
    (int Rank, Candidate Cause) Best(Zone z) =>
        z.Parts.Select(p => (Rank: RankOf(p), Part: p)).Where(x => x.Rank > 0).OrderBy(x => x.Rank)
            .Select(x => (x.Rank, candidates[x.Rank - 1])).FirstOrDefault();

    static bool Unlikely(Candidate c) => c.Label == "احتمال بعيد";

    static (Color Fg, Color Bg) Colors(Candidate c)
    {
        var (fg, bg) = Theme.StatusColors(c.Label);
        return fg == Color.Empty || Unlikely(c) ? (Theme.Muted, Theme.GraySoft) : (fg, bg);
    }

    List<(int Rank, Candidate Cause)> Unmapped() => candidates
        .Select((c, i) => (Rank: i + 1, Cause: c))
        .Where(x => x.Cause.Part != Parts.Screen && !Zones.Any(z => z.Parts.Contains(x.Cause.Part)))
        .Take(4).ToList();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        drawn.Clear();

        var extra = Unmapped();
        int legendH = extra.Count * S(28) + (candidates.Count > 0 ? S(26) : 0);
        var area = new RectangleF(S(8), S(6), Width - S(16), Height - S(12) - legendH);
        // هيكل الجهاز بنسبة آيفون (العرض ≈ 0.48 من الطول)
        float h = Math.Min(area.Height, area.Width / 0.48f), w = h * 0.48f;
        if (w < S(60)) return;
        var body = new RectangleF(area.X + (area.Width - w) / 2f, area.Y + (area.Height - h) / 2f, w, h);
        Gfx.FillRound(g, body, w * 0.16f, Theme.Surface);
        Gfx.DrawRound(g, body, w * 0.16f, Theme.Ink, S(2));
        var inner = RectangleF.Inflate(body, -w * 0.05f, -w * 0.05f);

        // الشاشة: إطار داخلي يتلوّن إن كانت الشاشة ضمن الأسباب
        int screenRank = RankOf(Parts.Screen);
        if (screenRank > 0)
        {
            var (fg, _) = Colors(candidates[screenRank - 1]);
            using var p = new Pen(fg, S(3)) { DashStyle = Unlikely(candidates[screenRank - 1]) ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid };
            using var path = Gfx.Round(inner, w * 0.12f);
            g.DrawPath(p, path);
        }
        else Gfx.DrawRound(g, inner, w * 0.12f, Theme.Border);

        foreach (var z in Zones)
        {
            var (rank, cause) = Best(z);
            // زر البصمة لا يُرسم إلا إن كان ضمن الأسباب (غير موجود في معظم الموديلات)
            if (cause == null && z.Parts.Contains(Parts.TouchId)) continue;
            var r = new RectangleF(inner.X + z.Box.X * inner.Width, inner.Y + z.Box.Y * inner.Height, z.Box.Width * inner.Width, z.Box.Height * inner.Height);
            drawn.Add((z, r, rank, cause));
            float rad = z.Round ? Math.Min(r.Width, r.Height) / 2f : S(6);
            bool hot = z == hover;
            if (cause == null)
            {
                Gfx.FillRound(g, r, rad, hot ? Theme.GraySoft : Theme.SurfaceAlt);
                Gfx.DrawRound(g, r, rad, Theme.BorderStrong);
                continue;
            }
            var (fg, bg) = Colors(cause);
            if (selected != null && z.Parts.Contains(selected))
                Gfx.DrawRound(g, RectangleF.Inflate(r, S(4), S(4)), rad + S(4), Theme.Ink, S(2));
            Gfx.FillRound(g, r, rad, hot ? Gfx.Mix(bg, fg, 0.18f) : bg);
            using (var p = new Pen(fg, rank == 1 ? S(3) : S(2)))
            using (var path = Gfx.Round(r, rad))
            {
                if (Unlikely(cause)) p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawPath(p, path);
            }
            DrawRank(g, new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f), rank, fg);
            // البطارية بلا دليل: تنبيه صريح داخلها
            if (z.Parts.Contains(Parts.Battery) && Unlikely(cause) && r.Height > S(70))
                TextRenderer.DrawText(g, "لا دليل — لا تبدّلها", Theme.FS(9), Rectangle.Round(new RectangleF(r.X, r.Y + r.Height / 2f + S(18), r.Width, S(24))), Theme.Muted, Gfx.Center);
        }

        // أسباب بلا مكان على المخطط
        if (candidates.Count == 0) return;
        float y = body.Bottom + S(8);
        TextRenderer.DrawText(g, "الرقم = ترتيب السبب في القائمة", Theme.F(9), Rectangle.Round(new RectangleF(S(4), y, Width - S(8), S(22))), Theme.Muted, Gfx.Center);
        y += S(26);
        foreach (var (rank, cause) in extra)
        {
            var (fg, _) = Colors(cause);
            float right = Width - S(8);
            DrawRank(g, new PointF(right - S(12), y + S(12)), rank, fg);
            TextRenderer.DrawText(g, Theme.Bidi(cause.Part), Theme.F(9.5f), Rectangle.Round(new RectangleF(S(4), y, right - S(32) - S(4), S(24))), Theme.Text2, Gfx.RtlStart);
            y += S(28);
        }
    }

    static void DrawRank(Graphics g, PointF c, int rank, Color fg)
    {
        float d = S(24);
        var circle = new RectangleF(c.X - d / 2f, c.Y - d / 2f, d, d);
        using (var b = new SolidBrush(rank == 1 ? fg : Color.White)) g.FillEllipse(b, circle);
        using (var p = new Pen(fg, S(2))) g.DrawEllipse(p, circle);
        TextRenderer.DrawText(g, rank.ToString(), Theme.FS(9), Rectangle.Round(circle), rank == 1 ? Color.White : fg, Gfx.Center);
    }

    (Zone Zone, RectangleF Rect, int Rank, Candidate Cause) HitTest(Point p)
    {
        // الأماكن الصغيرة فوق الكبيرة: يُبحث من آخر ما رُسم
        for (int i = drawn.Count - 1; i >= 0; i--)
            if (drawn[i].Rect.Contains(p)) return drawn[i];
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

    string Describe(Zone z)
    {
        var lines = z.Parts.Select(p => (Rank: RankOf(p), Part: p)).Where(x => x.Rank > 0).OrderBy(x => x.Rank)
            .Select(x => $"{x.Rank} · {x.Part} — {candidates[x.Rank - 1].Label}").ToList();
        return lines.Count == 0 ? $"{z.Name}: لا دليل عليه في السجل" : string.Join("\n", lines);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
