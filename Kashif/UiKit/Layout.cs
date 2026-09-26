using static Kashif.Dpi;

namespace Kashif;

// تخطيط متجاوب للنماذج: الحقول تتمدد لتملأ المساحة المتاحة وتلتف أو تنزل تحت عناوينها في الشاشات الضيقة،
// فلا يخرج أي حقل عن حدود البطاقة مهما كان حجم الشاشة أو نسبة التكبير.
// ملاحظة: هذه اللوحات غير معكوسة، فالترتيب يبدأ من اليمين يدويًا (بداية السطر العربي).

/// <summary>عمود من العناصر فوق بعضها؛ كل عنصر يأخذ عرض العمود (عدا الأزرار) ويُحسب ارتفاع العمود تلقائيًا</summary>
public class FormStack : Panel
{
    bool busy;
    /// <summary>أقصى عرض للمحتوى بالبكسل المنطقي (حتى لا تتمدد الحقول بلا حد في الشاشات العريضة)</summary>
    public int MaxContent { get; set; } = 820;

    public FormStack()
    {
        BackColor = Theme.Surface;
        DoubleBuffered = true;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (busy) return;
        busy = true;
        try { Height = Arrange(ClientSize.Width); }
        finally { busy = false; }
    }

    /// <summary>ترتيب العناصر بعرض محدد؛ يعيد الارتفاع المطلوب</summary>
    public int Arrange(int width)
    {
        int content = Math.Min(width, S(MaxContent));
        bool narrow = content < S(520);
        int y = Padding.Top;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            var m = c.Margin;
            // الإزاحة من البداية (اليمين) لمحاذاة العنصر تحت الحقول؛ تُلغى في الشاشات الضيقة لأن العناوين تنزل فوق حقولها
            int start = narrow && m.Left > S(60) ? S(8) : m.Left;
            int avail = Math.Max(S(40), content - Padding.Right - Padding.Left - start - m.Right);
            int right = width - Padding.Right - start;
            y += m.Top;
            int w = c is ModernButton mb0 ? Math.Min(mb0.NaturalWidth, avail) : avail;
            // العناصر المُزاحة تحت الحقول لا تتجاوز عرض الحقول المعتاد
            if (start > S(60) && c is not (FormRow or ModernButton)) w = Math.Min(w, S(560));
            int h = c switch
            {
                FormRow r => r.Arrange(w),
                FormStack s => s.Arrange(w),
                FormColumns col => col.Arrange(w),
                Label { AutoSize: false } l when !string.IsNullOrEmpty(l.Text) => Math.Max(l.MinimumSize.Height > 0 ? l.MinimumSize.Height : l.Height, WrapHeight(l, w)),
                _ => c.Height
            };
            if (c is ModernButton mb) mb.SetLayoutBounds(right - w, y, w, h);
            else c.SetBounds(right - w, y, w, h);
            y += h + m.Bottom;
        }
        return y + Padding.Bottom;
    }

    /// <summary>ارتفاع نص التسمية إذا التف على أكثر من سطر</summary>
    static int WrapHeight(Label l, int w)
    {
        if (l.MinimumSize.Height == 0) l.MinimumSize = new Size(0, l.Height);   // الارتفاع الأصلي حدًا أدنى
        var sz = TextRenderer.MeasureText(l.Text, l.Font, new Size(Math.Max(10, w - l.Padding.Horizontal - S(4)), 10000),
            TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft | TextFormatFlags.TextBoxControl);
        return sz.Height + l.Padding.Vertical + S(6);
    }
}

/// <summary>
/// سطر نموذج: عنوان بعرض ثابت في البداية (يمين) ثم الحقول. أول حقل إدخال يتمدد ليملأ المتبقي.
/// إذا ضاقت المساحة ينزل العنوان فوق الحقول، وإذا ضاقت أكثر تلتف الحقول على أسطر.
/// </summary>
public class FormRow : Panel
{
    readonly Label caption;
    readonly int captionNatural;
    readonly List<(Control C, int Natural)> items = new();
    Control stretch;
    int stretchNatural;
    bool busy;
    public int Gap { get; set; } = 8;

    public FormRow(Label caption, IEnumerable<Control> fields)
    {
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        this.caption = caption;
        if (caption != null)
        {
            captionNatural = caption.Width;
            caption.AutoEllipsis = true;
            Controls.Add(caption);
        }
        foreach (var f in fields)
        {
            items.Add((f, f.Width));
            Controls.Add(f);
            // الحقل المتمدد: أول حقل إدخال في سطر له عنوان (الأسطر بلا عنوان تبقى بأحجامها)
            if (caption != null && stretch == null && f is InputBox) { stretch = f; stretchNatural = f.Width; }
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (busy) return;
        busy = true;
        try { Height = Arrange(ClientSize.Width); }
        finally { busy = false; }
    }

    public int Arrange(int width)
    {
        int gap = S(Gap);
        var visItems = items.Where(i => i.C.Visible).ToList();
        var vis = visItems.Select(i => i.C).ToList();
        // العرض الطبيعي (المنطقي عند البناء) يُكبَّر مع الشاشة؛ الأزرار تحسب عرضها بنفسها
        int Natural((Control C, int Natural) i) => i.C is ModernButton b ? b.NaturalWidth : S(i.Natural);
        foreach (var i in visItems)
        {
            if (i.C == stretch) continue;
            int nw = Natural(i);
            if (i.C is ModernButton b) { if (b.Width != nw) b.SetLayoutWidth(nw); }
            else if (i.C.Width != nw) i.C.Width = nw;
        }
        int capW = caption != null ? S(captionNatural) : 0;
        int fixedW = vis.Where(c => c != stretch).Sum(c => c.Width + c.Margin.Horizontal) + gap * Math.Max(0, vis.Count - 1);
        // الحد الأدنى للحقل المتمدد: 60% من عرضه الطبيعي (بالبكسل الفعلي)
        int natural = S(stretchNatural);
        int minStretch = stretch != null ? Math.Min(natural, Math.Max(S(120), natural * 6 / 10)) : 0;
        int maxStretch = stretch != null ? Math.Max(natural, S(460)) : 0;
        bool oneLine = capW + (capW > 0 ? gap : 0) + fixedW + minStretch <= width;

        int y = 0, lineH;
        int right = width;
        if (!oneLine && caption != null)
        {
            // العنوان فوق الحقول
            int ch = Math.Min(caption.Height, S(28));
            caption.SetBounds(0, 0, width, ch);
            caption.TextAlign = ContentAlignment.BottomLeft;
            y = ch + S(2);
        }
        else if (caption != null)
        {
            caption.SetBounds(width - capW, 0, capW, 1);   // الارتفاع يُضبط بعد معرفة ارتفاع السطر
            right = width - capW - gap;
        }

        int avail = right;
        if (stretch != null && stretch.Visible)
        {
            int sw = Math.Max(Math.Min(minStretch, avail - fixedW), Math.Min(maxStretch, avail - fixedW));
            if (sw < S(60)) sw = Math.Min(avail, natural);   // لا مكان للسطر الواحد: الحقل بعرض السطر ويلتف ما بعده
            stretch.Width = Math.Max(S(60), sw - stretch.Margin.Horizontal);
        }

        // ترتيب من اليمين مع الالتفاف عند الحاجة
        int x = right, lineTop = y;
        lineH = 0;
        var line = new List<Control>();
        void Flush()
        {
            foreach (var c in line) c.Top = lineTop + c.Margin.Top + (lineH - c.Height - c.Margin.Vertical) / 2;
            lineTop += lineH + S(4);
            line.Clear();
            lineH = 0;
        }
        foreach (var c in vis)
        {
            int w = Math.Min(c.Width, Math.Max(S(40), right - c.Margin.Horizontal));
            if (w != c.Width)
            {
                if (c is ModernButton b) b.SetLayoutWidth(w);
                else c.Width = w;
            }
            int need = c.Width + c.Margin.Horizontal;
            if (line.Count > 0 && x - need < 0) { Flush(); x = right; }
            c.Left = x - c.Margin.Right - c.Width;
            x -= need + gap;
            lineH = Math.Max(lineH, c.Height + c.Margin.Vertical);
            line.Add(c);
        }
        int firstLineH = lineH;
        if (line.Count > 0) Flush();
        int total = Math.Max(lineTop - S(4), y);

        if (caption != null && oneLine)
        {
            caption.TextAlign = ContentAlignment.MiddleLeft;
            caption.Height = Math.Max(firstLineH == 0 ? caption.Height : firstLineH, S(24));
            caption.Top = 0;
            total = Math.Max(total, caption.Height);
        }
        return Math.Max(total, S(10));
    }
}

/// <summary>أعمدة متجاورة في الشاشات العريضة، وتنزل تحت بعضها في الشاشات الضيقة</summary>
public class FormColumns : Panel
{
    bool busy;
    /// <summary>أقل عرض للعمود الواحد بالبكسل المنطقي</summary>
    public int MinColumn { get; set; } = 380;
    public int Gap { get; set; } = 24;

    public FormColumns()
    {
        BackColor = Theme.Surface;
        DoubleBuffered = true;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (busy) return;
        busy = true;
        try { Height = Arrange(ClientSize.Width); }
        finally { busy = false; }
    }

    public int Arrange(int width)
    {
        var cols = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        if (cols.Count == 0) return 0;
        int gap = S(Gap);
        bool side = width >= S(MinColumn) * cols.Count + gap * (cols.Count - 1);
        int y = 0, h = 0;
        int colW = side ? (width - gap * (cols.Count - 1)) / cols.Count : width;
        int x = width;
        foreach (var c in cols)
        {
            int ch = c switch { FormStack s => s.Arrange(colW), FormRow r => r.Arrange(colW), _ => c.Height };
            if (side)
            {
                c.SetBounds(x - colW, 0, colW, ch);
                x -= colW + gap;
                h = Math.Max(h, ch);
            }
            else
            {
                c.SetBounds(0, y, colW, ch);
                y += ch + S(8);
                h = y;
            }
        }
        return h;
    }
}
