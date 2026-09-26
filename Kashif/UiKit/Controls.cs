using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using static Kashif.Dpi;

namespace Kashif;

// ملاحظة الدقة: أحجام الأدوات تُكتب بالبكسل المنطقي (عند 100%) وتُكبَّر مرة واحدة مع الشاشة (Dpi.ScaleTree)،
// أما الأرقام داخل دوال الرسم فتُمرَّر عبر S() لأنها تُحسب عند كل رسم بالبكسل الفعلي.

public enum BtnKind { Primary, Secondary, Soft, Danger, Warning, Ghost, Dark, Success, Glass, Accent, Coral, Amber, SideGhost }

/// <summary>زر حديث: زوايا دائرية، أيقونة، حالات مرور وضغط وتركيز</summary>
public class ModernButton : Button
{
    BtnKind kind = BtnKind.Primary;
    bool hover, down;
    int fitMin;
    int natural;          // العرض المطلوب للزر (بالبكسل الفعلي) كما حدده الكود، لا كما صغّرته لوحات الترتيب
    bool layoutSizing;    // التغيير الحالي في العرض من لوحة ترتيب مؤقتًا (لا يغيّر العرض المطلوب)

    [DefaultValue(BtnKind.Primary)]
    public BtnKind Kind { get => kind; set { kind = value; Invalidate(); } }
    [DefaultValue(null)] public string IconName { get; set; }
    [DefaultValue(8)] public int Radius { get; set; } = 8;
    /// <summary>عدد يظهر كشارة صغيرة فوق الزر (0 = مخفي)</summary>
    [DefaultValue(0)] public int Badge { get; set; }

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = Theme.FS(10);
        Height = 38;
    }

    /// <summary>يوسّع الزر ليتسع للنص والأيقونة (min بالبكسل المنطقي)</summary>
    public void FitWidth(int min)
    {
        fitMin = Math.Max(1, min);
        Width = FitCalc();
    }

    int FitCalc()
    {
        int text = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        int icon = Icons.Has(IconName) ? S(18) + (text > 0 ? S(8) : 0) : 0;
        return Math.Max(S(fitMin), text + icon + (text > 0 ? S(32) : S(20)));
    }

    /// <summary>العرض المحسوب من قياس النص هو بالبكسل الفعلي أصلًا: لا يُضرب مرة ثانية</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        // العرض المطلوب يُكبَّر بنفسه (قد يكون الزر مصغّرًا مؤقتًا لحظة التكبير)
        int before = natural;
        base.ScaleControl(factor, specified);
        if (before > 0 && (specified & BoundsSpecified.Width) != 0) natural = (int)Math.Round(before * factor.Width);
        if (fitMin > 0) Width = FitCalc();
    }

    /// <summary>
    /// العرض الذي يطلبه الزر لنفسه. لوحات الترتيب المتجاوبة تصغّر الزر مؤقتًا عند ضيق المساحة
    /// (مثل لحظة بناء الشاشة قبل أن تأخذ حجمها الحقيقي) ثم تعيده إلى هذا العرض، فلا يبقى النص مقصوصًا «كشف الح...».
    /// </summary>
    public int NaturalWidth => fitMin > 0 ? FitCalc() : natural > 0 ? natural : Width;

    /// <summary>عرض مؤقت من لوحة الترتيب (لا يغيّر NaturalWidth)</summary>
    internal void SetLayoutBounds(int x, int y, int w, int h)
    {
        layoutSizing = true;
        try { SetBounds(x, y, w, h); }
        finally { layoutSizing = false; }
    }

    internal void SetLayoutWidth(int w) => SetLayoutBounds(Left, Top, w, Height);

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        base.SetBoundsCore(x, y, width, height, specified);
        if (!layoutSizing && (specified & BoundsSpecified.Width) != 0) natural = width;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    (Color Bg, Color Fg, Color Border) Colors()
    {
        float t = down ? 1f : hover ? 0.55f : 0f;
        return kind switch
        {
            BtnKind.Primary => (Gfx.Mix(Theme.Brand, Theme.BrandDark, t), Color.White, Color.Empty),
            BtnKind.Secondary => (Gfx.Mix(Theme.Surface, Theme.GraySoft, t), Theme.Ink, Theme.BorderStrong),
            BtnKind.Soft => (Gfx.Mix(Theme.BrandSoft, Theme.BrandSoft2, t), Theme.BrandDark, Color.Empty),
            BtnKind.Danger => (Gfx.Mix(Theme.DangerSoft, ColorTranslator.FromHtml("#F9D0D0"), t), ColorTranslator.FromHtml("#C0262D"), Color.Empty),
            BtnKind.Warning => (Gfx.Mix(Theme.WarningSoft, ColorTranslator.FromHtml("#FBE0B5"), t), ColorTranslator.FromHtml("#B45309"), Color.Empty),
            BtnKind.Ghost => (Gfx.Alpha(Theme.GraySoft, (int)(255 * t)), Theme.Text2, Color.Empty),
            BtnKind.Dark => (Gfx.Mix(ColorTranslator.FromHtml("#1F2937"), ColorTranslator.FromHtml("#374151"), t), Color.White, Color.Empty),
            BtnKind.Success => (Gfx.Mix(Theme.Success, ColorTranslator.FromHtml("#15803D"), t), Color.White, Color.Empty),
            BtnKind.Accent => (Gfx.Mix(Theme.Orange, Gfx.Mix(Theme.Orange, Color.Black, 0.15f), t), Color.White, Color.Empty),
            // زر شفاف داخل القائمة الجانبية (فاتحة أو داكنة)
            BtnKind.SideGhost => (Gfx.Alpha(Theme.SidebarHover, (int)(255 * t)), Theme.SidebarText, Color.Empty),
            // زر حذف بلون مرجاني صريح (مثل أزرار الحذف في الشاشات المألوفة)
            BtnKind.Coral => (Gfx.Mix(ColorTranslator.FromHtml("#F25F5C"), ColorTranslator.FromHtml("#DC4543"), t), Color.White, Color.Empty),
            BtnKind.Amber => (Gfx.Mix(ColorTranslator.FromHtml("#F6BE2C"), ColorTranslator.FromHtml("#E5A812"), t), Color.White, Color.Empty),
            // زر شفاف فوق خلفية ملونة (مثل لوحة الدخول)
            BtnKind.Glass => (Color.FromArgb(40 + (int)(40 * t), 255, 255, 255), Color.White, Color.Empty),
            _ => (Theme.Brand, Color.White, Color.Empty)
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // الأزرار في WinForms «معتمة» فلا تُرسم خلفيتها تلقائيًا؛ نرسمها بلون الحاوية
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var (bg, fg, bd) = Colors();
        if (!Enabled)
        {
            bg = bg.A == 0 ? bg : Gfx.Mix(bg, Theme.GraySoft, 0.6f);
            fg = Theme.Subtle;
        }
        float rad = S((float)Radius);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (bg.A > 0) Gfx.FillRound(g, r, rad, bg);
        if (!bd.IsEmpty) Gfx.DrawRound(g, r, rad, bd);
        if (Focused && ShowFocusCues)
            Gfx.DrawRound(g, RectangleF.Inflate(r, -S(2.5f), -S(2.5f)), rad - S(2f), kind == BtnKind.Primary ? Gfx.Alpha(Color.White, 170) : Gfx.Alpha(Theme.Brand, 150), S(1.5f));

        bool rtl = RightToLeft == RightToLeft.Yes;
        bool hasIcon = Icons.Has(IconName);
        int iconPx = S(18), gap = string.IsNullOrEmpty(Text) || !hasIcon ? 0 : S(8);
        int tw = string.IsNullOrEmpty(Text) ? 0 : Math.Min(Width - S(16) - (hasIcon ? iconPx + gap : 0),
            TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
        int total = tw + (hasIcon ? iconPx + gap : 0);
        int x0 = (Width - total) / 2;
        Rectangle textRect, iconRect;
        if (rtl) { textRect = new Rectangle(x0, 0, tw, Height); iconRect = new Rectangle(x0 + tw + gap, (Height - iconPx) / 2, iconPx, iconPx); }
        else { iconRect = new Rectangle(x0, (Height - iconPx) / 2, iconPx, iconPx); textRect = new Rectangle(x0 + iconPx + gap, 0, tw, Height); }
        if (hasIcon) Icons.Draw(g, IconName, iconRect, fg, 17);
        if (tw > 0) TextRenderer.DrawText(g, Text, Font, textRect, fg, Gfx.Center);

        if (Badge > 0)
        {
            var txt = Badge > 99 ? "99+" : Badge.ToString();
            int bh = S(18), bw = Math.Max(bh, TextRenderer.MeasureText(txt, Theme.FS(8)).Width + S(6));
            var br = new Rectangle(rtl ? S(2) : Width - bw - S(2), S(2), bw, bh);
            Gfx.FillRound(g, br, bh / 2f, Theme.Danger);
            TextRenderer.DrawText(g, txt, Theme.FS(8), br, Color.White, Gfx.Center);
        }
    }
}

/// <summary>غلاف حديث لحقول الإدخال: إطار دائري بلون هادئ يتحول للون الهوية عند التركيز</summary>
public class InputBox : Panel
{
    public Control Inner { get; }
    public string LeadingIcon { get; set; }
    Control trailing;
    /// <summary>زر صغير داخل الحقل في نهايته (مثل إظهار كلمة المرور)</summary>
    public Control Trailing
    {
        get => trailing;
        set { if (trailing != null) Controls.Remove(trailing); trailing = value; if (value != null) { Controls.Add(value); value.BringToFront(); } Arrange(); }
    }
    readonly Panel clip;
    readonly Control arrow;
    bool focused, hover;
    public const int StdHeight = 40;

    public InputBox(Control c, int width, string icon = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Inner = c;
        LeadingIcon = icon;
        Width = Math.Max(60, width);
        Margin = new Padding(0);
        bool multi = c is TextBox { Multiline: true };
        Height = multi ? Math.Max(c.Height + 16, 64) : StdHeight;

        c.Font = c.Font == null || c.Font == Control.DefaultFont ? Theme.F(10) : c.Font;
        switch (c)
        {
            case TextBox t:
                t.BorderStyle = BorderStyle.None; t.BackColor = Theme.Surface;
                if (t.Multiline) t.ScrollBars = ScrollBars.None;   // التمرير بالعجلة والأسهم يكفي، وشريط التمرير القديم يشوّه الشكل
                break;
            case DateTimePicker d when d.Format == DateTimePickerFormat.Short:
                d.Format = DateTimePickerFormat.Custom; d.CustomFormat = "yyyy-MM-dd";   // صيغة تاريخ موحدة في كل البرنامج
                break;
            case NumericUpDown n:
                n.BorderStyle = BorderStyle.None; n.BackColor = Theme.Surface;
                if (n.Controls.Count > 0) n.Controls[0].Visible = false;   // إخفاء أسهم الزيادة (العجلة والأسهم تعمل)
                break;
            case ComboBox cb: cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Theme.Surface; break;
        }
        clip = new Panel { BackColor = Theme.Surface };
        clip.Controls.Add(c);
        Controls.Add(clip);
        if (c is ComboBox combo)
        {
            arrow = new ComboFace(combo);
            clip.Controls.Add(arrow);
            arrow.BringToFront();
            combo.HandleCreated += (s, e) => Arrange();
            combo.DropDownStyleChanged += (s, e) => { Arrange(); arrow.Invalidate(); };
        }
        c.Dock = DockStyle.None;
        c.Enter += (s, e) => { focused = true; Invalidate(); };
        c.Leave += (s, e) => { focused = false; Invalidate(); };
        c.MouseEnter += (s, e) => { hover = true; Invalidate(); };
        c.MouseLeave += (s, e) => { hover = false; Invalidate(); };
        c.EnabledChanged += (s, e) => SyncBack();
        c.SizeChanged += (s, e) => { if (!arranging) Arrange(); };
        Click += (s, e) => c.Focus();
        clip.Click += (s, e) => c.Focus();
    }

    void SyncBack()
    {
        var bg = Inner.Enabled ? Theme.Surface : Theme.SurfaceAlt;
        clip.BackColor = bg;
        if (Inner is not DateTimePicker) Inner.BackColor = bg;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Gfx.OpaqueBack(this));
    protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
    protected override void OnRightToLeftChanged(EventArgs e) { base.OnRightToLeftChanged(e); Arrange(); }

    /// <summary>الأداة الداخلية يرتبها Arrange بالبكسل الفعلي بعد التكبير</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        Arrange();
    }

    bool arranging;
    void Arrange()
    {
        if (arranging || clip == null) return;
        arranging = true;
        try
        {
            bool rtl = RightToLeft == RightToLeft.Yes;
            int iconW = Icons.Has(LeadingIcon) ? S(28) : 0;
            int trailW = trailing != null ? trailing.Width + S(4) : 0;
            int padS = S(11) + iconW, padE = S(11) + trailW;    // بداية (يمين) ونهاية (يسار)
            if (trailing != null) trailing.Location = new Point(rtl ? S(6) : Width - S(6) - trailing.Width, (Height - trailing.Height) / 2);
            int x = rtl ? padE : padS, w = Math.Max(10, Width - padS - padE);
            switch (Inner)
            {
                case TextBox { Multiline: true } t:
                    clip.Bounds = new Rectangle(x, S(8), w, Height - S(16));
                    t.Bounds = new Rectangle(0, 0, clip.Width, clip.Height);
                    break;
                case TextBox t:
                    {
                        int h = t.PreferredHeight;
                        clip.Bounds = new Rectangle(x, (Height - h) / 2, w, h);
                        t.Bounds = new Rectangle(0, 0, clip.Width, h);
                        break;
                    }
                case NumericUpDown n:
                    {
                        int h = n.PreferredHeight;
                        int spin = n.Controls.Count > 0 ? n.Controls[0].Width : 0;
                        clip.Bounds = new Rectangle(x, (Height - h) / 2, w, h);
                        // مساحة الأسهم المخفية تُدفع خارج المنطقة الظاهرة
                        bool spinLeft = rtl ^ (n.UpDownAlign == LeftRightAlignment.Left);
                        n.Bounds = new Rectangle(spinLeft ? -spin : 0, 0, clip.Width + spin, h);
                        break;
                    }
                case ComboBox cb:
                    {
                        int h = cb.Height;
                        // نقص 3 بكسل من كل جهة لإخفاء إطار القائمة الأصلي
                        clip.Bounds = new Rectangle(x - 3, (Height - (h - 6)) / 2, w + 6, h - 6);
                        cb.Bounds = new Rectangle(-3, -3, clip.Width + 6, h);
                        if (cb.DropDownStyle == ComboBoxStyle.DropDownList)
                        {
                            // القائمة غير القابلة للكتابة: واجهة مرسومة بالكامل فوقها (النص والسهم)
                            // فتبدو واضحة وموحدة على كل الأجهزة ولا يظهر سهم النظام أو يُقص النص
                            arrow.Bounds = new Rectangle(0, 0, clip.Width, clip.Height);
                            break;
                        }
                        // القائمة القابلة للبحث: سهم موحد فوق سهم القائمة الأصلي (يمين أو يسار حسب الاتجاه)
                        int aw = SystemInformation.VerticalScrollBarWidth + S(4);
                        int ax = rtl ? 0 : clip.Width - aw;
                        if (cb.IsHandleCreated && ComboButton(cb, out var btn))
                        {
                            aw = btn.Width + S(6);
                            ax = btn.X + cb.Left - 3 < clip.Width / 2 ? 0 : clip.Width - aw;
                        }
                        arrow.Bounds = new Rectangle(ax, 0, aw, clip.Height);
                        break;
                    }
                case DateTimePicker d:
                    {
                        int h = d.Height;
                        clip.Bounds = new Rectangle(x - 4, (Height - (h - 4)) / 2, w + 8, h - 4);
                        d.Bounds = new Rectangle(-2, -2, clip.Width + 4, h);
                        break;
                    }
                default:
                    clip.Bounds = new Rectangle(x, S(4), w, Height - S(8));
                    Inner.Bounds = new Rectangle(0, 0, clip.Width, clip.Height);
                    break;
            }
        }
        finally { arranging = false; }
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)]
    struct COMBOBOXINFO { public int cbSize; public RECT rcItem, rcButton; public int stateButton; public IntPtr hwndCombo, hwndItem, hwndList; }
    [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO info);

    static bool ComboButton(ComboBox cb, out Rectangle r)
    {
        r = Rectangle.Empty;
        try
        {
            var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };
            if (!GetComboBoxInfo(cb.Handle, ref info) || info.rcButton.R <= info.rcButton.L) return false;
            r = Rectangle.FromLTRB(info.rcButton.L, info.rcButton.T, info.rcButton.R, info.rcButton.B);
            return true;
        }
        catch { return false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Hq(g);
        var r = new RectangleF(S(1.5f), S(1.5f), Width - S(3.5f), Height - S(3.5f));
        if (focused) Gfx.DrawRound(g, RectangleF.Inflate(r, S(1f), S(1f)), S(10f), Theme.BrandSoft2, S(3f));
        Gfx.FillRound(g, r, S(8f), Inner.Enabled ? Theme.Surface : Theme.SurfaceAlt);
        Gfx.DrawRound(g, r, S(8f), focused ? Theme.Brand : hover ? Gfx.Mix(Theme.BorderStrong, Theme.Brand, 0.3f) : Theme.BorderStrong, focused ? S(1.4f) : 1f);
        if (Icons.Has(LeadingIcon))
        {
            bool rtl = RightToLeft == RightToLeft.Yes;
            var ir = new RectangleF(rtl ? Width - S(36) : S(10), (Height - S(18)) / 2f, S(18), S(18));
            Icons.Draw(g, LeadingIcon, ir, focused ? Theme.Brand : Theme.Subtle, 17);
        }
    }

    /// <summary>
    /// واجهة القائمة المنسدلة: في القوائم غير القابلة للكتابة ترسم النص المختار والسهم فوق القائمة الأصلية كلها،
    /// وفي القوائم القابلة للبحث ترسم السهم فقط. النقر يفتح القائمة، ولوحة المفاتيح تعمل على القائمة الأصلية.
    /// </summary>
    sealed class ComboFace : Control
    {
        readonly ComboBox cb;
        bool hover;
        public ComboFace(ComboBox c)
        {
            cb = c;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            c.EnabledChanged += (s, e) => { BackColor = c.Enabled ? Theme.Surface : Theme.SurfaceAlt; Invalidate(); };
            c.SelectedIndexChanged += (s, e) => Invalidate();
            c.TextChanged += (s, e) => Invalidate();
            c.GotFocus += (s, e) => Invalidate();
            c.LostFocus += (s, e) => Invalidate();
            c.FontChanged += (s, e) => Invalidate();
        }
        bool Full => cb.DropDownStyle == ComboBoxStyle.DropDownList;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!cb.Enabled || e.Button != MouseButtons.Left) return;
            cb.Focus();
            cb.DroppedDown = !cb.DroppedDown;
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // التمرير بالعجلة يغيّر الاختيار فقط إذا كانت القائمة مركَّزًا عليها (حتى لا تتغير القيم أثناء تمرير الصفحة)
            if (!Full || !cb.Focused || cb.Items.Count == 0) { base.OnMouseWheel(e); return; }
            int i = Math.Clamp(cb.SelectedIndex + (e.Delta > 0 ? -1 : 1), 0, cb.Items.Count - 1);
            if (i != cb.SelectedIndex) cb.SelectedIndex = i;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            bool rtl = cb.RightToLeft == RightToLeft.Yes;
            int aw = S(26);
            var ar = rtl ? new RectangleF(0, 0, aw, Height) : new RectangleF(Width - aw, 0, aw, Height);
            if (!Full)
            {
                Icons.Draw(g, "chevron-down", new RectangleF(0, 0, Width, Height), Theme.Muted, 16);
                return;
            }
            Icons.Draw(g, "chevron-down", ar, hover || cb.Focused ? Theme.Brand : Theme.Muted, 16);
            var text = cb.SelectedItem != null ? cb.GetItemText(cb.SelectedItem) : cb.Text;
            var tr = rtl ? new Rectangle(aw, 0, Width - aw - S(3), Height) : new Rectangle(S(3), 0, Width - aw - S(3), Height);
            var fg = !cb.Enabled ? Theme.Subtle : Theme.Ink;
            TextRenderer.DrawText(g, text, cb.Font, tr, fg,
                (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>مفتاح تشغيل/إيقاف حديث بدل مربع الاختيار</summary>
public class Toggle : CheckBox
{
    bool hover;
    public Toggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 34;
        Cursor = Cursors.Hand;
        Font = Theme.F(10);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        bool rtl = RightToLeft == RightToLeft.Yes;
        float tw = S(40f), th = S(22f);
        var track = new RectangleF(rtl ? Width - tw - S(2) : S(2), (Height - th) / 2f, tw, th);
        var on = Checked;
        var col = !Enabled ? Theme.BorderStrong : on ? (hover ? Theme.BrandDark : Theme.Brand) : (hover ? Gfx.Mix(Theme.BorderStrong, Theme.Brand, 0.3f) : Theme.BorderStrong);
        Gfx.FillRound(g, track, th / 2f, col);
        float pad = S(3f), k = th - 2 * pad;
        float kx = on ^ rtl ? track.Right - k - pad : track.X + pad;
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, track.Y + pad, k, k);
        if (Focused && ShowFocusCues) Gfx.DrawRound(g, RectangleF.Inflate(track, S(2f), S(2f)), th / 2f + S(2f), Theme.BrandSoft2, S(2f));
        int gap = S(10);
        var tr = rtl ? new Rectangle(0, 0, (int)track.X - gap, Height) : new Rectangle((int)track.Right + gap, 0, Width - (int)track.Right - gap, Height);
        TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Ink : Theme.Subtle, rtl ? Gfx.RtlStart : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>بطاقة بيضاء بزوايا دائرية وظل خفيف، مع عنوان وأيقونة اختيارية</summary>
public class CardPanel : Panel
{
    string title, subtitle;
    Padding autoPad = new(-1);
    public string IconName { get; set; }
    public Color IconColor { get; set; } = Theme.Brand;
    public int Radius { get; set; } = 12;
    /// <summary>ارتفاع رأس البطاقة بالبكسل الفعلي</summary>
    public int HeaderHeight => string.IsNullOrEmpty(title) ? 0 : S(string.IsNullOrEmpty(subtitle) ? 52 : 64);

    public string Title { get => title; set { title = value; UpdatePadding(); Invalidate(); } }
    public string Subtitle { get => subtitle; set { subtitle = value; UpdatePadding(); Invalidate(); } }

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(16);
    }

    void UpdatePadding() => Padding = autoPad = new Padding(S(16), S(12) + HeaderHeight, S(16), S(16));

    /// <summary>الهامش المحسوب من العنوان هو بالبكسل الفعلي أصلًا: يُعاد حسابه بدل مضاعفته</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        bool auto = Padding == autoPad;
        base.ScaleControl(factor, specified);
        if (auto) UpdatePadding();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        float rad = S((float)Radius);
        var r = new RectangleF(S(1.5f), 0.5f, Width - S(4f), Height - S(4f));
        Gfx.Shadow(g, r, rad);
        Gfx.FillRound(g, r, rad, Theme.Surface);
        Gfx.DrawRound(g, r, rad, Theme.Border);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (string.IsNullOrEmpty(title)) return;
        var g = e.Graphics;
        Gfx.Hq(g);
        bool rtl = RightToLeft == RightToLeft.Yes;
        int x = S(18), iconBox = 0;
        if (Icons.Has(IconName))
        {
            iconBox = S(36);
            var ir = new RectangleF(rtl ? Width - x - iconBox - S(2) : x, S(14), iconBox, iconBox);
            Gfx.FillRound(g, ir, S(10f), Gfx.Mix(IconColor, Color.White, 0.88f));
            Icons.Draw(g, IconName, ir, IconColor, 18);
            iconBox += S(12);
        }
        var tr = rtl ? new Rectangle(S(18), S(12), Width - S(38) - iconBox, S(26)) : new Rectangle(x + iconBox, S(12), Width - S(38) - iconBox, S(26));
        if (string.IsNullOrEmpty(subtitle)) tr.Y = S(18);
        TextRenderer.DrawText(g, title, Theme.FS(11.5f), tr, Theme.Ink, rtl ? Gfx.RtlStart : TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sr = tr; sr.Y += S(24); sr.Height = S(22);
            TextRenderer.DrawText(g, subtitle, Theme.F(9), sr, Theme.Muted, rtl ? Gfx.RtlStart : TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>شريط أدوات على شكل بطاقة (يُستخدم عبر Theme.Bar)؛ الأزرار تنتقل لسطر جديد عند ضيق المساحة</summary>
public class ToolbarCard : FlowLayoutPanel
{
    public const int Gap = 12;
    public ToolbarCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
        BackColor = Theme.Surface;
        Padding = new Padding(10, 8, 10, 8 + Gap);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        // الفراغ أسفل البطاقة = الفرق بين الهامشين السفلي والعلوي (يكبر مع الشاشة)
        int gap = Math.Max(0, Padding.Bottom - Padding.Top);
        var r = new RectangleF(S(1.5f), 0.5f, Width - S(4f), Height - gap - S(2f));
        Gfx.Shadow(g, r, S(12f));
        Gfx.FillRound(g, r, S(12f), Theme.Surface);
        Gfx.DrawRound(g, r, S(12f), Theme.Border);
    }
}

/// <summary>بطاقة مؤشر رقمي: أيقونة ملونة، عنوان، قيمة كبيرة، وملاحظة</summary>
public class KpiCard : Control
{
    public string Title { get; set; }
    public string Value { get; set; }
    public string Hint { get; set; }
    public string IconName { get; set; }
    public Color Accent { get; set; } = Theme.Brand;
    bool hover;

    public KpiCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(236, 118);
        Margin = new Padding(8);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(S(1.5f), 0.5f, Width - S(4f), Height - S(4f));
        Gfx.Shadow(g, r, S(14f));
        Gfx.FillRound(g, r, S(14f), Theme.Surface);
        Gfx.DrawRound(g, r, S(14f), hover && Cursor == Cursors.Hand ? Theme.BrandSoft2 : Theme.Border);

        bool rtl = RightToLeft == RightToLeft.Yes;
        int m = S(18), box = S(40);
        var ir = new RectangleF(rtl ? m : Width - m - box, m, box, box);
        Gfx.FillRound(g, ir, S(12f), Gfx.Mix(Accent, Color.White, 0.88f));
        Icons.Draw(g, IconName, ir, Accent, 20);

        var flags = rtl ? Gfx.RtlStart : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        int tx = rtl ? m + box + S(4) : m, tw = Width - 2 * m - box - S(4);
        TextRenderer.DrawText(g, Title, Theme.F(9.5f), new Rectangle(tx, S(20), tw, S(22)), Theme.Muted, flags);
        var vf = Theme.FS(Value != null && Value.Length > 11 ? 15 : 18);
        TextRenderer.DrawText(g, Value, vf, new Rectangle(m, S(46), Width - 2 * m, S(36)), Theme.Ink, flags);
        if (!string.IsNullOrEmpty(Hint))
            TextRenderer.DrawText(g, Hint, Theme.F(8.5f), new Rectangle(m, Height - S(32), Width - 2 * m, S(20)), Accent, flags);
    }
}

/// <summary>تبويبات حديثة: أفقية (خط سفلي، سطر واحد دائمًا) أو عمودية (قائمة جانبية)</summary>
public class ModernTabs : Panel
{
    readonly TabStrip strip;
    readonly Panel body = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly List<(string Title, string Icon, Control Page)> pages = new();
    readonly List<string> shortTitles = new();
    int selected = -1;
    public event EventHandler SelectedIndexChanged;
    public bool Vertical { get; }

    public ModernTabs(bool vertical = false)
    {
        Vertical = vertical;
        BackColor = Theme.Bg;
        strip = new TabStrip(this) { Dock = vertical ? DockStyle.Right : DockStyle.Top };
        if (vertical) strip.Width = 214;
        Controls.Add(body);
        if (vertical) Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Bg });
        Controls.Add(strip);
    }

    public int Count => pages.Count;
    internal IReadOnlyList<(string Title, string Icon, Control Page)> Pages => pages;

    /// <summary>داخل بطاقة بيضاء تأخذ التبويبات لون البطاقة بدل لون الخلفية الرمادي</summary>
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (Parent == null) return;
        var back = Gfx.OpaqueBack(this);
        if (back == Theme.Surface) { BackColor = strip.BackColor = body.BackColor = back; }
    }

    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value < 0 || value >= pages.Count || value == selected) return;
            selected = value;
            for (int i = 0; i < pages.Count; i++) pages[i].Page.Visible = i == value;
            strip.Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>إضافة صفحة جاهزة من نوع TabPage (تُنقل عناصرها إلى الصفحة الجديدة)</summary>
    public void Add(TabPage tp, string icon = null)
    {
        var panel = new Panel { BackColor = tp.BackColor == Color.Transparent ? Theme.Bg : tp.BackColor, Font = tp.Font };
        var ctrls = tp.Controls.Cast<Control>().ToArray();
        tp.Controls.Clear();
        panel.Controls.AddRange(ctrls);
        Add(tp.Text, panel, icon);
    }

    /// <summary>shortTitle: عنوان مختصر يظهر عند ضيق المساحة (مثل «الأساسية» بدل «المعلومات الأساسية»)</summary>
    public void Add(string title, Control page, string icon = null, string shortTitle = null)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        body.Controls.Add(page);
        pages.Add((title, icon, page));
        shortTitles.Add(shortTitle ?? title);
        strip.Relayout();
        if (selected < 0) SelectedIndex = 0;
    }

    sealed class TabStrip : Control
    {
        readonly ModernTabs owner;
        readonly List<Rectangle> rects = new();
        bool compact;   // مساحة ضيقة: بلا أيقونات
        int hover = -1;
        readonly ToolTip tip = new();

        public TabStrip(ModernTabs o)
        {
            owner = o;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = 56;
            Cursor = Cursors.Hand;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        public void Relayout()
        {
            rects.Clear();
            var font = Theme.FS(10);
            if (owner.Vertical)
            {
                int y = S(12);
                foreach (var p in owner.pages) { rects.Add(new Rectangle(S(8), y, Width - S(16), S(42))); y += S(46); }
            }
            else
            {
                // سطر واحد دائمًا: عند الضيق تُحذف الأيقونات ثم تُضغط العناوين بالتساوي (مع «...» وتلميح بالاسم الكامل)
                int avail = Width - S(8), gap = S(4);
                int[] Widths(bool icons, int pad) => owner.pages.Select((p, i) =>
                    TextRenderer.MeasureText(icons ? p.Title : owner.shortTitles[i], font).Width + pad + (icons && Icons.Has(p.Icon) ? S(24) : 0)).ToArray();
                var w = Widths(true, S(28));
                compact = false;
                if (w.Sum() + gap * w.Length > avail) { w = Widths(false, S(16)); compact = true; }
                if (w.Sum() + gap * w.Length > avail && w.Length > 0)
                {
                    double k = (double)Math.Max(1, avail - gap * w.Length) / w.Sum();
                    w = w.Select(x => Math.Max(S(56), (int)(x * k))).ToArray();
                }
                int xr = Width - S(4);
                for (int i = 0; i < w.Length; i++)
                {
                    rects.Add(new Rectangle(xr - w[i], S(4), w[i], S(44)));
                    xr -= w[i] + gap;
                }
                int h = S(56);
                if (Height != h) Height = h;
            }
            Invalidate();
        }

        int HitTest(Point p) { for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h == hover) return;
            hover = h;
            tip.SetToolTip(this, h >= 0 && h < owner.pages.Count ? owner.pages[h].Title : null);
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { int h = HitTest(e.Location); if (h >= 0) owner.SelectedIndex = h; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Gfx.Hq(g);
            if (owner.Vertical)
            {
                var card = new RectangleF(S(1.5f), 0.5f, Width - S(4f), Math.Min(Height - S(4f), rects.Count * S(46) + S(24)));
                Gfx.Shadow(g, card, S(12f));
                Gfx.FillRound(g, card, S(12f), Theme.Surface);
                Gfx.DrawRound(g, card, S(12f), Theme.Border);
            }
            else
            {
                using var pen = new Pen(Theme.Border);
                g.DrawLine(pen, 0, Height - S(13), Width, Height - S(13));
            }
            for (int i = 0; i < rects.Count && i < owner.pages.Count; i++)
            {
                var r = rects[i];
                var (title, icon, _) = owner.pages[i];
                bool sel = i == owner.selected, hov = i == hover;
                Color fg = sel ? Theme.BrandDark : hov ? Theme.Ink : Theme.Muted;
                if (owner.Vertical)
                {
                    if (sel) Gfx.FillRound(g, r, S(8f), Theme.BrandSoft);
                    else if (hov) Gfx.FillRound(g, r, S(8f), Theme.SurfaceAlt);
                    if (sel) Gfx.FillRound(g, new RectangleF(r.Right - S(4), r.Y + S(10), S(4), r.Height - S(20)), S(2f), Theme.Brand);
                }
                else
                {
                    if (hov && !sel) Gfx.FillRound(g, new RectangleF(r.X, r.Y + S(4), r.Width, r.Height - S(10)), S(8f), Theme.GraySoft);
                    if (sel) Gfx.FillRound(g, new RectangleF(r.X + S(10), r.Bottom - S(5), r.Width - S(20), S(3)), S(1.5f), Theme.Brand);
                }
                bool showIcon = Icons.Has(icon) && (owner.Vertical || !compact);
                int iconW = showIcon ? S(24) : 0;
                int lift = owner.Vertical ? 0 : S(2);
                if (showIcon) Icons.Draw(g, icon, new RectangleF(r.Right - S(14) - S(18), r.Y + (r.Height - S(18)) / 2f - lift, S(18), S(18)), sel ? Theme.Brand : fg, 17);
                var tr = owner.Vertical || showIcon
                    ? new Rectangle(r.X + S(8), r.Y - lift, r.Width - S(22) - iconW, r.Height)
                    : new Rectangle(r.X + S(6), r.Y - lift, r.Width - S(12), r.Height);
                var shown = compact && !owner.Vertical ? owner.shortTitles[i] : title;
                TextRenderer.DrawText(g, shown, sel ? Theme.FS(10) : Theme.F(10), tr, fg, owner.Vertical ? Gfx.RtlStart : Gfx.Center);
            }
        }
    }
}

/// <summary>عنوان مجموعة صغير في القائمة الجانبية (العمليات، الحسابات والتقارير، الإدارة)</summary>
public class NavLabel : Control
{
    bool rail;
    /// <summary>في الوضع المصغّر يصبح خطًا فاصلًا قصيرًا</summary>
    public bool Rail { get => rail; set { rail = value; Invalidate(); } }

    public NavLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 34;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Sidebar);
        if (rail)
        {
            using var pen = new Pen(Theme.SidebarBorder);
            g.DrawLine(pen, S(20), Height / 2, Width - S(20), Height / 2);
            return;
        }
        TextRenderer.DrawText(g, Text, Theme.FS(8.5f), new Rectangle(S(16), S(8), Width - S(38), Height - S(8)), Theme.SidebarMuted, Gfx.RtlStart);
    }
}

/// <summary>
/// رأس قسم في القائمة الجانبية: أيقونة ملونة في مربع ناعم، عنوان، وسهم للفتح والطي.
/// في الوضع المصغّر (Rail) تظهر الأيقونة وحدها في الوسط.
/// </summary>
public class NavSection : Control
{
    bool hover, expanded, active, rail;
    public string IconName { get; set; }
    public Color Tint { get; set; } = Theme.Orange;
    public bool Expandable { get; set; } = true;
    public bool Expanded { get => expanded; set { expanded = value; Invalidate(); } }
    /// <summary>الشاشة الحالية ضمن هذا القسم</summary>
    public bool Active { get => active; set { active = value; Invalidate(); } }
    public bool Rail { get => rail; set { rail = value; Invalidate(); } }

    public NavSection()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 44;
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Sidebar);
        Gfx.Hq(g);
        // «مختار» ظاهريًا: قسم بلا عناصر وشاشته مفتوحة، أو قسم مطوي يحتوي الشاشة الحالية
        bool selected = active && (!Expandable || !expanded || rail);
        var pill = new RectangleF(S(10), S(3), Width - S(20), Height - S(6));
        bool dark = Theme.DarkSidebar;
        if (selected) Gfx.FillRound(g, pill, S(10f), Gfx.Mix(Tint, Theme.Sidebar, dark ? 0.72f : 0.87f));
        else if (hover) Gfx.FillRound(g, pill, S(10f), Theme.SidebarHover);

        int box = S(30);
        var ib = rail
            ? new RectangleF((Width - box) / 2f, (Height - box) / 2f, box, box)
            : new RectangleF(Width - S(20) - box, (Height - box) / 2f, box, box);
        Gfx.FillRound(g, ib, S(8f), selected ? Tint : Gfx.Mix(Tint, Theme.Sidebar, dark ? (expanded ? 0.62f : 0.74f) : (expanded ? 0.80f : 0.88f)));
        Icons.Draw(g, IconName, ib, selected ? Color.White : dark ? Gfx.Mix(Tint, Color.White, 0.35f) : Tint, 17);
        if (rail) return;

        int textRight = (int)ib.X - S(10);
        int left = S(20) + (Expandable ? S(20) : 0);
        TextRenderer.DrawText(g, Text, Theme.FS(10.5f), new Rectangle(left, 0, textRight - left, Height),
            selected ? (dark ? Color.White : Gfx.Mix(Tint, Theme.Ink, 0.45f)) : Theme.SidebarText, Gfx.RtlStart);
        if (Expandable)
            Icons.Draw(g, expanded ? "chevron-down" : "chevron-left", new RectangleF(S(18), (Height - S(16)) / 2f, S(16), S(16)),
                expanded ? Theme.SidebarText : Theme.SidebarMuted, 15);
    }
}

/// <summary>شاشة داخل قسم مفتوح: سطر خفيف بأيقونة صغيرة وخط إرشاد يربطه بالقسم، وتمييز ناعم للشاشة الحالية</summary>
public class NavItem : Control
{
    bool hover, active;
    public string IconName { get; set; }
    /// <summary>لون القسم (للتمييز)</summary>
    public Color Fill { get; set; } = Theme.Orange;
    public bool Active { get => active; set { active = value; Invalidate(); } }
    /// <summary>آخر عنصر في القسم (ينتهي عنده خط الإرشاد)</summary>
    public bool Last { get; set; }

    public NavItem()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 36;
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Sidebar);
        Gfx.Hq(g);
        // خط الإرشاد تحت منتصف أيقونة القسم
        float guide = Width - S(20) - S(15);
        using (var pen = new Pen(Theme.SidebarBorder, Math.Max(1f, S(1.2f))))
            g.DrawLine(pen, guide, 0, guide, Last ? Height / 2f : Height);

        var pill = new RectangleF(S(10), S(2), guide - S(8) - S(10), Height - S(4));
        bool dark = Theme.DarkSidebar;
        var fill = dark ? Gfx.Mix(Fill, Color.White, 0.3f) : Fill;
        if (active) Gfx.FillRound(g, pill, S(8f), Gfx.Mix(Fill, Theme.Sidebar, dark ? 0.7f : 0.86f));
        else if (hover) Gfx.FillRound(g, pill, S(8f), Theme.SidebarHover);
        if (active) Gfx.FillRound(g, new RectangleF(guide - S(1.5f), S(8), S(3), Height - S(16)), S(1.5f), fill);

        int ic = S(17);
        var ir = new RectangleF(pill.Right - S(10) - ic, (Height - ic) / 2f, ic, ic);
        var fg = dark ? (active || hover ? Color.White : Theme.SidebarText)
                      : active ? Gfx.Mix(Fill, Theme.Ink, 0.45f) : hover ? Theme.Ink : Theme.Text2;
        Icons.Draw(g, IconName, ir, active ? fill : Theme.SidebarMuted, 16);
        TextRenderer.DrawText(g, Text, active ? Theme.FS(10) : Theme.F(10), new Rectangle((int)pill.X + S(6), 0, (int)(ir.X - pill.X) - S(14), Height), fg, Gfx.RtlStart);
    }
}

/// <summary>تبويبات الشاشات المفتوحة أعلى المحتوى (مع زر إغلاق)</summary>
public class DocTabs : Control
{
    public record Tab(string Key, string Title, string Icon, bool Closable);
    readonly List<Tab> tabs = new();
    readonly List<(Rectangle Box, Rectangle Close)> rects = new();
    int hover = -1;
    bool hoverClose;
    readonly ToolTip tip = new();
    public string ActiveKey { get; private set; }
    public event Action<string> Selected, Closed;
    /// <summary>لون شريط التبويبات (التبويب النشط بلون خلفية الصفحة فيتصل بها)</summary>
    public static Color Strip => Theme.Strip;

    public DocTabs()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 44;
        BackColor = Strip;
    }

    public void Set(string key, string title, string icon, bool closable)
    {
        int i = tabs.FindIndex(t => t.Key == key);
        var t = new Tab(key, title, icon, closable);
        if (i >= 0) tabs[i] = t; else tabs.Add(t);
        Relayout();
    }
    public void Remove(string key) { tabs.RemoveAll(t => t.Key == key); Relayout(); }
    public void Activate(string key) { ActiveKey = key; Invalidate(); }
    public IReadOnlyList<Tab> Items => tabs;

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    void Relayout()
    {
        rects.Clear();
        if (tabs.Count == 0) { Invalidate(); return; }
        var font = Theme.FS(10);
        var widths = tabs.Select(t => Math.Min(S(240), TextRenderer.MeasureText(t.Title, font).Width + S(30) + (t.Icon != null ? S(24) : 0) + (t.Closable ? S(26) : 0))).ToList();
        int avail = Width - S(12), gap = S(4), total = widths.Sum() + gap * tabs.Count;
        if (total > avail) { double k = (double)Math.Max(1, avail - gap * tabs.Count) / widths.Sum(); widths = widths.Select(w => Math.Max(S(84), (int)(w * k))).ToList(); }
        int x = Width - S(4), top = S(7);
        for (int i = 0; i < tabs.Count; i++)
        {
            var box = new Rectangle(x - widths[i], top, widths[i], Height - top);
            int cs = S(20);
            var close = tabs[i].Closable ? new Rectangle(box.X + S(8), box.Y + (box.Height - cs) / 2, cs, cs) : Rectangle.Empty;
            rects.Add((box, close));
            x -= widths[i] + gap;
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = rects.FindIndex(r => r.Box.Contains(e.Location));
        bool hc = h >= 0 && rects[h].Close.Contains(e.Location);
        if (h != hover) tip.SetToolTip(this, h >= 0 && h < tabs.Count ? tabs[h].Title : null);
        if (h != hover || hc != hoverClose) { hover = h; hoverClose = hc; Invalidate(); }
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; hoverClose = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        int h = rects.FindIndex(r => r.Box.Contains(e.Location));
        if (h < 0) return;
        var t = tabs[h];
        if ((rects[h].Close.Contains(e.Location) || e.Button == MouseButtons.Middle) && t.Closable) Closed?.Invoke(t.Key);
        else if (e.Button == MouseButtons.Left) Selected?.Invoke(t.Key);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Strip);
        Gfx.Hq(g);
        int act = tabs.FindIndex(t => t.Key == ActiveKey);
        for (int i = 0; i < rects.Count && i < tabs.Count; i++)
        {
            var (box, close) = rects[i];
            var t = tabs[i];
            bool isAct = i == act;
            if (isAct || i == hover)
            {
                using var path = TopRounded(box, S(9));
                using var b = new SolidBrush(isAct ? Theme.Bg : Theme.StripHover);
                g.FillPath(b, path);
                if (isAct) Gfx.FillRound(g, new RectangleF(box.X + S(10), box.Y, box.Width - S(20), S(3)), S(1.5f), Theme.Orange);
            }
            // فاصل رفيع بين التبويبات غير النشطة
            else if (i + 1 != act && i != rects.Count - 1)
                using (var pen = new Pen(Theme.BorderStrong)) g.DrawLine(pen, box.X - S(2), box.Y + S(10), box.X - S(2), box.Bottom - S(8));

            int right = box.Right - S(14);
            if (Icons.Has(t.Icon))
            {
                Icons.Draw(g, t.Icon, new RectangleF(right - S(18), box.Y + (box.Height - S(18)) / 2f, S(18), S(18)), isAct ? Theme.Orange : Theme.Muted, 16);
                right -= S(26);
            }
            int left = t.Closable ? close.Right + S(4) : box.X + S(10);
            TextRenderer.DrawText(g, t.Title, isAct ? Theme.FS(10) : Theme.F(10), new Rectangle(left, box.Y, right - left, box.Height), isAct ? Theme.Ink : Theme.Text2, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            if (t.Closable)
            {
                bool hc = i == hover && hoverClose;
                if (hc) using (var b = new SolidBrush(Theme.DangerSoft)) g.FillEllipse(b, close);
                Icons.Draw(g, "x", close, hc ? Theme.Danger : Theme.Muted, 14);
            }
        }
    }

    static GraphicsPath TopRounded(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Y + rad, r.Right, r.Bottom);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }
}

/// <summary>مؤشر صغير: أيقونة ملونة، عدد، وعنوان — يُنقر للانتقال</summary>
public class StatChip : Control
{
    public long Count { get; set; }
    public string IconName { get; set; }
    public Color Accent { get; set; } = Theme.Brand;
    bool hover;

    public StatChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var active = Count > 0;
        Gfx.FillRound(g, r, S(12f), hover ? Gfx.Mix(Accent, Color.White, 0.9f) : Theme.SurfaceAlt);
        Gfx.DrawRound(g, r, S(12f), hover ? Gfx.Mix(Accent, Color.White, 0.6f) : Theme.Border);
        int box = S(38);
        var ir = new RectangleF(Width - S(12) - box, (Height - box) / 2f, box, box);
        Gfx.FillRound(g, ir, S(10f), active ? Gfx.Mix(Accent, Color.White, 0.85f) : Theme.GraySoft);
        Icons.Draw(g, IconName, ir, active ? Accent : Theme.Subtle, 18);
        int tw = Width - S(70);
        TextRenderer.DrawText(g, Count.ToString("#,0"), Theme.FS(15), new Rectangle(S(10), S(6), tw, S(28)), active ? Theme.Ink : Theme.Subtle, Gfx.RtlStart);
        TextRenderer.DrawText(g, Text, Theme.F(8.5f), new Rectangle(S(10), S(33), tw, S(22)), Theme.Muted, Gfx.RtlStart);
    }
}

/// <summary>مخطط أعمدة بسيط وأنيق (مبيعات الأيام الأخيرة)</summary>
public class BarChart : Control
{
    public List<(string Label, double Value)> Data { get; set; } = new();
    /// <summary>سلسلة زمنية: آخر عمود (اليوم / الشهر الحالي) بلون مميز</summary>
    public bool HighlightLast { get; set; } = true;
    int hover = -1;
    readonly List<RectangleF> bars = new();

    public BarChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1;
        for (int i = 0; i < bars.Count; i++) if (e.X >= bars[i].X - S(4) && e.X <= bars[i].Right + S(4)) h = i;
        if (h != hover) { hover = h; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.Hq(g);
        bars.Clear();
        if (Data.Count == 0) return;
        double max = Math.Max(1, Data.Max(d => d.Value));
        // تقريب الحد الأعلى لرقم جميل
        double mag = Math.Pow(10, Math.Floor(Math.Log10(max)));
        double top = Math.Ceiling(max / mag) * mag;
        int left = S(8), right = S(64), topPad = S(26), bottom = S(30);
        var plot = new RectangleF(left, topPad, Width - left - right, Height - topPad - bottom);
        if (plot.Width < 10 || plot.Height < 10) return;
        using var grid = new Pen(Theme.Border) { DashStyle = DashStyle.Dash };
        var lf = Theme.F(8.5f);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.Bottom - plot.Height * i / 4f;
            g.DrawLine(grid, plot.X, y, plot.Right, y);
            TextRenderer.DrawText(g, Short(top * i / 4), lf, new Rectangle((int)plot.Right + S(6), (int)y - S(10), right - S(8), S(20)), Theme.Subtle,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        int n = Data.Count;
        float slot = plot.Width / n, bw = Math.Min(S(34f), slot * 0.56f);
        for (int i = 0; i < n; i++)
        {
            // من اليمين إلى اليسار: أقدم يوم على اليمين
            float cx = plot.Right - slot * i - slot / 2f;
            float h = (float)(plot.Height * Data[i].Value / top);
            var br = new RectangleF(cx - bw / 2, plot.Bottom - Math.Max(h, 2), bw, Math.Max(h, 2));
            bars.Add(br);
            var col = i == hover ? Gfx.Mix(Theme.Orange, Color.Black, 0.15f)
                : !HighlightLast ? Gfx.Mix(Theme.Orange, Theme.Amber, 0.35f)
                : i == n - 1 ? Theme.Orange : Gfx.Mix(Theme.Amber, Color.White, 0.25f);
            using (var path = TopRound(br, Math.Min(S(6f), bw / 2)))
            using (var b = new SolidBrush(col)) g.FillPath(b, path);
            float per = slot / F;
            int every = per < 30 ? 3 : per < 46 ? 2 : 1;
            if (i % every == (n - 1) % every || i == hover)
                TextRenderer.DrawText(g, Data[i].Label, lf, new Rectangle((int)(cx - slot / 2) - S(10), (int)plot.Bottom + S(6), (int)slot + S(20), S(20)),
                    i == hover ? Theme.Ink : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        if (hover >= 0 && hover < bars.Count)
        {
            var b = bars[hover];
            var txt = Ui.M(Data[hover].Value);
            var sz = TextRenderer.MeasureText(txt, Theme.FS(9));
            var tip = new RectangleF(b.X + b.Width / 2 - sz.Width / 2f - S(10), Math.Max(0, b.Y - S(32)), sz.Width + S(20), S(26));
            tip.X = Math.Max(0, Math.Min(Width - tip.Width, tip.X));
            Gfx.FillRound(g, tip, S(7f), Theme.Ink);
            TextRenderer.DrawText(g, txt, Theme.FS(9), Rectangle.Round(tip), Color.White, Gfx.Center);
        }
    }

    static GraphicsPath TopRound(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        if (r.Height < d) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Y + rad, r.Right, r.Bottom);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }

    static string Short(double v) => v >= 1_000_000 ? (v / 1_000_000).ToString("0.#") + " م" : v >= 1000 ? (v / 1000).ToString("0.#") + " ألف" : v.ToString("0");
}

/// <summary>صورة رمزية دائرية بالأحرف الأولى</summary>
public static class Avatar
{
    public static void Draw(Graphics g, RectangleF r, string name, Color bg)
    {
        Gfx.Hq(g);
        using (var b = new SolidBrush(bg)) g.FillEllipse(b, r);
        var initials = Initials(name);
        TextRenderer.DrawText(g, initials, Theme.FS(r.Height > S(36f) ? 12 : 10), Rectangle.Round(r), Color.White, Gfx.Center);
    }

    public static string Initials(string name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "؟";
        return parts.Length == 1 ? parts[0][..1] : parts[0][..1] + " " + parts[1][..1];
    }
}

/// <summary>رقم بارز بعنوان صغير فوقه (يُرسم ذاتياً لضمان المحاذاة من اليمين)</summary>
public class StatLabel : Control
{
    string caption = "", value = "";
    Color valueColor = Theme.Ink;
    public string Caption { get => caption; set { caption = value; Invalidate(); } }
    public string Value { get => value; set { this.value = value; Invalidate(); } }
    public Color ValueColor { get => valueColor; set { valueColor = value; Invalidate(); } }
    public float ValueSize { get; set; } = 15f;

    public StatLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(150, 64);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        TextRenderer.DrawText(g, caption, Theme.F(9), new Rectangle(S(2), S(2), Width - S(4), S(22)), Theme.Muted, Gfx.RtlStart);
        TextRenderer.DrawText(g, value, Theme.FS(ValueSize), new Rectangle(S(2), S(24), Width - S(4), Height - S(26)), valueColor, Gfx.RtlStart);
    }
}
