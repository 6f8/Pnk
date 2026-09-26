using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Kashif;

/// <summary>هوية البرنامج البصرية: الألوان والخطوط وأنماط الجداول والأشرطة</summary>
public static class Theme
{
    static Color C(string hex) => ColorTranslator.FromHtml(hex);

    // ---- الألوان الحالية (تُضبط من المظهر المختار عند تشغيل البرنامج، قبل بناء أي شاشة) ----
    public static Color Brand = C("#2B3A8F"), BrandDark = C("#1E2A6E"), BrandSoft = C("#EAEDF9"), BrandSoft2 = C("#CFD6F2");
    /// <summary>لون التمييز (التبويب النشط، أعمدة المخطط، أيقونة العنوان) ولونه الثاني للتدرجات</summary>
    public static Color Orange = C("#F0602A"), Amber = C("#F7B52C"), OrangeSoft = C("#FDE7DA");

    // ---- الشريط الجانبي ----
    public static Color Sidebar = C("#FAF8F4"), SidebarHover = C("#F0EBE1"), SidebarBorder = C("#E6E0D4"),
                        SidebarText = C("#1F2937"), SidebarMuted = C("#8B8171");
    /// <summary>شريط جانبي داكن (النصوص فاتحة والتمييز أقوى)</summary>
    public static bool DarkSidebar;
    /// <summary>أيقونات الأقسام بلون الهوية فقط بدل ألوان متعددة</summary>
    public static bool MonoSections;

    // ---- الأسطح والنصوص ----
    public static Color Bg = C("#EEEAE1"), Surface = Color.White, SurfaceAlt = C("#F8F6F1"), Border = C("#E4DED2"), BorderStrong = C("#D4CDBF");
    public static Color Ink = C("#1B1F2A"), Text2 = C("#374151"), Muted = C("#6B6557"), Subtle = C("#A39B8B");
    /// <summary>شريط تبويبات الشاشات المفتوحة، وشريط الترحيب في الرئيسية ولون التحية فيه</summary>
    public static Color Strip = C("#E4DED2"), StripHover = C("#D9D2C4"), Hero1 = C("#2B3A8F"), Hero2 = C("#1E2A6E"), HeroAccent = C("#F7B52C");

    // ---- حالات (ثابتة في كل المظاهر لأن معناها ثابت) ----
    public static readonly Color Success = C("#16A34A"), SuccessSoft = C("#E8F7EE");
    public static readonly Color Danger = C("#DC2626"), DangerSoft = C("#FDECEC");
    public static readonly Color Warning = C("#D97706"), WarningSoft = C("#FEF3E2");
    public static readonly Color Info = C("#2563EB"), InfoSoft = C("#EAF1FE");
    public static readonly Color Purple = C("#7C3AED"), PurpleSoft = C("#F2ECFE");
    public static readonly Color Gray = C("#6B7280");
    public static Color GraySoft = C("#EFEBE3");

    // أسماء قديمة ما زالت مستخدمة في الشاشات
    public static Color Accent => Brand;

    // ================= المظاهر =================
    /// <summary>درجات الرمادي للأسطح والحدود والنصوص الثانوية</summary>
    public record Neutrals(string Bg, string SurfaceAlt, string Border, string BorderStrong, string Muted, string Subtle, string GraySoft, string Strip);
    static readonly Neutrals Warm = new("#EEEAE1", "#F8F6F1", "#E4DED2", "#D4CDBF", "#6B6557", "#A39B8B", "#EFEBE3", "#E4DED2");
    static readonly Neutrals Cool = new("#EEF1F6", "#F7F8FB", "#E3E7EF", "#CDD3DF", "#5B6475", "#98A1B3", "#EDF0F5", "#E1E6EF");
    static readonly Neutrals Plain = new("#F0F0F1", "#F8F8F9", "#E5E5E7", "#D2D2D6", "#63636B", "#A1A1AA", "#F0F0F2", "#E4E4E7");
    static readonly Neutrals Mint = new("#EDF3F0", "#F6FAF8", "#DDE8E2", "#C7D6CE", "#5A6B63", "#97A89F", "#EAF1ED", "#DCE7E1");

    /// <summary>مظهر: لون الهوية (الأزرار)، لون التمييز ولونه الثاني، الرماديات، ولون الشريط الجانبي إن كان داكنًا</summary>
    public record Palette(string Key, string Name, string Brand, string Accent, string Accent2, Neutrals N,
                          string DarkSide = null, string Hero1 = null, string Hero2 = null, string HeroAccent = null, bool Mono = false);

    public static readonly Palette[] Palettes =
    {
        new("classic", "كاشف الكلاسيكي — كحلي وبرتقالي على كريمي", "#2B3A8F", "#F0602A", "#F7B52C", Warm),
        new("corporate", "أزرق مؤسسي — أزرق وسماوي على رمادي بارد", "#1D4ED8", "#0284C7", "#38BDF8", Cool, Hero1: "#1E40AF", Hero2: "#172554", HeroAccent: "#FDE68A"),
        new("midnight", "ليلي — شريط كحلي داكن، نيلي وكهرماني", "#4F46E5", "#F59E0B", "#FBBF24", Cool, DarkSide: "#0F172A", Hero1: "#312E81", Hero2: "#1E1B4B"),
        new("emerald", "زمردي — أخضر هادئ على خلفية نعناعية", "#047857", "#059669", "#34D399", Mint, Hero1: "#065F46", Hero2: "#022C22", HeroAccent: "#FDE68A"),
        new("violet", "بنفسجي — شريط بنفسجي داكن ولمسات وردية", "#6D28D9", "#DB2777", "#F472B6", Cool, DarkSide: "#1E1537", Hero1: "#5B21B6", Hero2: "#2E1065", HeroAccent: "#FBCFE8"),
        new("graphite", "جرافيت — فحمي داكن مع برتقالي", "#27272A", "#EA580C", "#FB923C", Plain, DarkSide: "#18181B", Hero1: "#27272A", Hero2: "#09090B", HeroAccent: "#FDBA74"),
        new("teal", "تركواز — فيروزي منعش على أبيض", "#0F766E", "#0891B2", "#22D3EE", Mint, Hero1: "#115E59", Hero2: "#042F2E", HeroAccent: "#A5F3FC"),
        new("burgundy", "عنابي وذهبي — فخم ودافئ", "#881337", "#B45309", "#EAB308", Warm, DarkSide: "#2A0A14", Hero1: "#881337", Hero2: "#4C0519", HeroAccent: "#FCD34D"),
        new("royal", "ملكي — أزرق عميق مع ذهبي", "#1E3A8A", "#CA8A04", "#FACC15", Cool, DarkSide: "#0B1E3F", Hero1: "#1E3A8A", Hero2: "#0B1E3F", HeroAccent: "#FDE047"),
        new("minimal", "بسيط — أبيض وأسود بلا ألوان", "#18181B", "#3F3F46", "#A1A1AA", Plain, Hero1: "#27272A", Hero2: "#18181B", HeroAccent: "#FAFAFA", Mono: true),
    };

    public static string Current { get; private set; } = "classic";

    /// <summary>تطبيق المظهر (يُستدعى مرة واحدة عند التشغيل قبل بناء أي شاشة)</summary>
    public static void Apply(string key)
    {
        var p = Palettes.FirstOrDefault(x => x.Key == key) ?? Palettes[0];
        Current = p.Key;
        var w = Color.White;
        Brand = C(p.Brand);
        BrandDark = Gfx.Mix(Brand, Color.Black, 0.22f);
        BrandSoft = Gfx.Mix(Brand, w, 0.91f);
        BrandSoft2 = Gfx.Mix(Brand, w, 0.78f);
        Orange = C(p.Accent);
        Amber = C(p.Accent2);
        OrangeSoft = Gfx.Mix(Orange, w, 0.87f);

        var n = p.N;
        Bg = C(n.Bg); SurfaceAlt = C(n.SurfaceAlt); Border = C(n.Border); BorderStrong = C(n.BorderStrong);
        Muted = C(n.Muted); Subtle = C(n.Subtle); GraySoft = C(n.GraySoft);
        Strip = C(n.Strip); StripHover = Gfx.Mix(Strip, Color.Black, 0.05f);
        Surface = w; Ink = C("#1B1F2A"); Text2 = C("#374151");

        DarkSidebar = p.DarkSide != null;
        if (DarkSidebar)
        {
            Sidebar = C(p.DarkSide);
            SidebarHover = Gfx.Mix(Sidebar, w, 0.08f);
            SidebarBorder = Gfx.Mix(Sidebar, w, 0.13f);
            SidebarText = C("#E5E7EB");
            SidebarMuted = Gfx.Mix(Sidebar, w, 0.55f);
        }
        else
        {
            Sidebar = Gfx.Mix(Bg, w, 0.72f);
            SidebarHover = Gfx.Mix(Bg, w, 0.15f);
            SidebarBorder = Border;
            SidebarText = C("#1F2937");
            SidebarMuted = Muted;
        }
        Hero1 = p.Hero1 != null ? C(p.Hero1) : Brand;
        Hero2 = p.Hero2 != null ? C(p.Hero2) : BrandDark;
        HeroAccent = p.HeroAccent != null ? C(p.HeroAccent) : Amber;
        MonoSections = p.Mono;
    }

    public static Font F(float size = 10f, FontStyle style = FontStyle.Regular) => FontKit.Get(size, style);
    /// <summary>وزن نصف عريض للعناوين والأزرار</summary>
    public static Font FS(float size = 10f) => FontKit.Get(size, FontStyle.Regular, true);

    /// <summary>زر حديث؛ اللون القديم يحدد نوعه، والأيقونة تُختار من نص الزر تلقائيًا</summary>
    public static ModernButton Btn(string text, Color? color = null, int width = 130, string icon = null)
    {
        var kind = color is Color c ? KindFor(c) : BtnKind.Primary;
        var b = new ModernButton { Text = text, Kind = kind, IconName = icon ?? AutoIcon(text), Margin = new Padding(4, 6, 4, 4) };
        b.FitWidth(width);
        return b;
    }

    static BtnKind KindFor(Color c)
    {
        if (c == Success || c == Brand) return c == Brand ? BtnKind.Soft : BtnKind.Primary;
        if (c == Danger) return BtnKind.Danger;
        if (c == Warning) return BtnKind.Warning;
        if (c == Gray || c == Purple) return BtnKind.Secondary;
        return BtnKind.Primary;
    }

    static readonly (string Key, string Icon)[] iconWords =
    {
        ("حذف السطر", "x"), ("حذف", "trash-2"), ("إرجاع قطعة", "package-plus"), ("صرف قطعة", "package-minus"),
        ("طباعة تجريبية", "printer"), ("طباع", "printer"), ("Excel", "file-spreadsheet"), ("واتساب", "message-circle"),
        ("تذكير", "send"), ("إشعار", "send"), ("تعديل", "pencil"), ("حفظ", "save"), ("جديد", "plus"), ("إضافة", "plus"),
        ("استلام", "inbox"), ("تأكيد التسليم", "check"), ("تسليم", "handshake"), ("تم التسليم", "check"), ("راجع", "undo-2"),
        ("تغيير الحالة", "repeat"), ("مناقلة", "arrow-left-right"), ("إتلاف", "ban"), ("توليد", "barcode"),
        ("العدد = الرصيد", "boxes"), ("تصفير", "rotate-ccw"), ("معاينة", "eye"), ("تحميل", "download"), ("اعتماد", "badge-check"),
        ("احتساب", "calculator"), ("الكل حاضر", "list-checks"), ("نسخ احتياطي", "cloud-upload"), ("استعادة", "rotate-ccw"),
        ("فتح مجلد", "folder-open"), ("اختبار", "send"), ("رابط تطبيق", "smartphone"), ("حصص", "hand-coins"),
        ("راتب", "banknote"), ("رواتب", "banknote"), ("تسديد", "wallet"), ("صرف", "banknote"), ("عرض", "eye"), ("تحديث", "refresh-cw"),
        ("بحث", "search"), ("موافق", "check"), ("إلغاء", "x"), ("دخول", "log-in"), ("فاتورة", "receipt-text"),
        ("نسخ", "copy"), ("ربط", "link"), ("استيراد", "download"), ("تحليل", "scan-line"), ("لصق", "clipboard-list"),
    };

    public static string AutoIcon(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var (k, i) in iconWords) if (text.Contains(k)) return i;
        return null;
    }

    /// <summary>كان عنوانًا كبيرًا مكررًا؛ العنوان الآن في رأس النافذة الرئيسية، فيبقى فاصل صغير فقط</summary>
    public static Control Title(string text) => new Panel { Dock = DockStyle.Top, Height = 2, Tag = text };

    /// <summary>شريط أدوات بشكل بطاقة بيضاء بزوايا دائرية</summary>
    public static FlowLayoutPanel Bar() => new ToolbarCard { Dock = DockStyle.Top };

    /// <summary>بطاقة مؤشر (رقم كبير مع أيقونة)</summary>
    public static Control Card(string title, string value, Color color, string icon = null, string hint = null) =>
        new KpiCard { Title = title, Value = value, Accent = color, IconName = icon, Hint = hint };

    // ================= الجداول =================
    public static void Grid(DataGridView g, bool readOnly = true)
    {
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
        g.ColumnHeadersDefaultCellStyle.Font = FS(9.5f);
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        // الجداول لا تُكبَّر تلقائيًا مع الشاشة: الارتفاعات بالبكسل الفعلي
        g.ColumnHeadersHeight = Dpi.S(44);
        g.RowTemplate.Height = Dpi.S(40);
        g.DefaultCellStyle.Font = F(10);
        g.DefaultCellStyle.ForeColor = Ink;
        g.DefaultCellStyle.BackColor = Surface;
        g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.DefaultCellStyle.SelectionBackColor = BrandSoft;
        g.DefaultCellStyle.SelectionForeColor = Ink;
        g.AlternatingRowsDefaultCellStyle.BackColor = Surface;
        g.GridColor = Gfx.Mix(Border, Surface, 0.35f);
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToResizeRows = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.ReadOnly = readOnly;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        g.Font = F(10);
        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(g, true);

        // قيمة غير صالحة في خلية (نص في عمود رقمي مثلًا): رسالة واضحة بدل نافذة خطأ ويندوز الطويلة
        g.DataError += (s, e) =>
        {
            e.ThrowException = false;
            // الخلية تبقى في وضع التحرير حتى تُصحَّح القيمة (أو Esc للتراجع)
            e.Cancel = true;
            if (e.Context.HasFlag(DataGridViewDataErrorContexts.Commit) || e.Context.HasFlag(DataGridViewDataErrorContexts.Parsing))
                Toast.Show("القيمة المدخلة غير صحيحة — أدخل رقمًا أو اضغط Esc للتراجع.", Tone.Warning);
        };

        g.DataBindingComplete += (s, e) =>
        {
            // نسخة من القائمة: تغيير العرض قد يعيد ربط جدول آخر أثناء المرور على الأعمدة
            foreach (var c in g.Columns.Cast<DataGridViewColumn>().ToList())
            {
                if (c.DataGridView == null) continue;
                if (c.Name == "id") c.Visible = false;
                // حد أدنى للعرض حسب العنوان: شريط تمرير أفقي بدل «...» في الشاشات الصغيرة
                int head = TextRenderer.MeasureText(c.HeaderText ?? "", FS(9.5f)).Width + Dpi.S(28);
                // عرض أطول قيمة في أول الصفوف (التواريخ مثلاً) حتى لا تُقص
                int content = 0;
                if (c.Visible)
                    for (int i = 0; i < Math.Min(g.Rows.Count, 25); i++)
                    {
                        var v = g.Rows[i].Cells[c.Index].FormattedValue as string;
                        if (!string.IsNullOrEmpty(v)) content = Math.Max(content, TextRenderer.MeasureText(v, F(10)).Width + Dpi.S(28));
                    }
                int min = Math.Max(Dpi.S(c.ValueType == typeof(double) || c.ValueType == typeof(long) ? 64 : 90), Math.Min(Math.Max(head, content), Dpi.S(220)));
                try { if (c.DataGridView != null && c.MinimumWidth != min) c.MinimumWidth = min; } catch { /* العمود يُعاد بناؤه أثناء الربط */ }
                if (c.ValueType == typeof(double) || c.ValueType == typeof(long))
                {
                    if (c.ValueType == typeof(double)) c.DefaultCellStyle.Format = "#,0.##";
                    c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            }
        };

        // تمييز الصف تحت المؤشر
        int hover = -1;
        g.CellMouseEnter += (s, e) => { if (e.RowIndex != hover) { int old = hover; hover = e.RowIndex; InvalidateRow(g, old); InvalidateRow(g, hover); } };
        g.MouseLeave += (s, e) => { int old = hover; hover = -1; InvalidateRow(g, old); };
        var hoverColor = Gfx.Mix(Bg, Surface, 0.55f);
        g.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            if (e.RowIndex == hover) e.CellStyle.BackColor = hoverColor;
            // اتجاه صحيح للقيم اللاتينية داخل جدول عربي: «2026-09-24 14:00» لا «14:00 2026-09-24»، و«-45,000» لا «45,000-»
            const string LRM = "\u200E";
            if (e.Value is double d && d < 0)
            {
                e.Value = LRM + d.ToString(string.IsNullOrEmpty(e.CellStyle.Format) ? "#,0.##" : e.CellStyle.Format);
                e.FormattingApplied = true;
            }
            else if (e.Value is string str && str.Length > 4 && char.IsDigit(str[0]) && (str.Contains(' ') || str.Contains(':')))
            {
                e.Value = LRM + str;
                e.FormattingApplied = true;
            }
            else if (e.Value is string s2 && Bidi(s2) is var b && !ReferenceEquals(b, s2))
            {
                e.Value = b;
                e.FormattingApplied = true;
            }
        };

        // شارات ملونة لأعمدة الحالة
        g.CellPainting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var col = g.Columns[e.ColumnIndex];
            if (col.Name is not ("الحالة" or "التنبيه" or "النوع" or "الجودة" or "الثقة" or "الدرجة")) return;
            var text = Convert.ToString(e.FormattedValue);
            if (string.IsNullOrEmpty(text)) return;
            var (fg, bg) = StatusColors(text);
            if (fg == Color.Empty) return;
            e.PaintBackground(e.CellBounds, true);
            var gr = e.Graphics;
            Gfx.Hq(gr);
            var font = FS(9);
            var sz = TextRenderer.MeasureText(gr, text, font, Size.Empty, TextFormatFlags.NoPadding);
            int w = Math.Min(e.CellBounds.Width - Dpi.S(12), sz.Width + Dpi.S(22)), h = Math.Min(e.CellBounds.Height - 4, Dpi.S(26));
            var r = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - w) / 2, e.CellBounds.Y + (e.CellBounds.Height - h) / 2, w, h);
            Gfx.FillRound(gr, r, h / 2f, bg);
            TextRenderer.DrawText(gr, text, font, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.RightToLeft);
            e.Handled = true;
        };

        // رسالة «لا توجد بيانات»
        g.Paint += (s, e) =>
        {
            if (g.Rows.Count > 0 || g.Columns.Count == 0) return;
            int top = g.ColumnHeadersVisible ? g.ColumnHeadersHeight : 0;
            var area = new Rectangle(0, top, g.Width, g.Height - top);
            if (area.Height < Dpi.S(90)) return;
            Gfx.Hq(e.Graphics);
            float cs = Dpi.S(56f);
            var circle = new RectangleF(area.X + area.Width / 2f - cs / 2, area.Y + area.Height / 2f - Dpi.S(50f), cs, cs);
            using (var gb = new SolidBrush(GraySoft)) e.Graphics.FillEllipse(gb, circle);
            Icons.Draw(e.Graphics, "inbox", circle, Subtle, 26);
            TextRenderer.DrawText(e.Graphics, "لا توجد بيانات لعرضها", FS(10.5f), new Rectangle(area.X, (int)circle.Bottom + Dpi.S(10), area.Width, Dpi.S(28)), Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.RightToLeft);
        };
    }

    /// <summary>
    /// نص إنكليزي داخل واجهة عربية: «Apple iPhone SE (2020)» كانت تظهر «(2020) Apple iPhone SE»، و«A2111, A2221» تبدأ بفاصلة.
    /// علامة الاتجاه (LRM) حول النص اللاتيني تُبقيه بترتيبه الصحيح. النص الذي يبدأ بالعربي لا يتغير إلا الأقواس في آخره.
    /// </summary>
    public static string Bidi(string s)
    {
        if (string.IsNullOrEmpty(s) || s[0] == '\u200E') return s;
        const string LRM = "\u200E";
        char first = s.FirstOrDefault(char.IsLetter);
        if (first != default && first < 0x0250) return LRM + s + LRM;              // يبدأ بحرف لاتيني
        char last = s.TrimEnd().LastOrDefault();
        if ((last == ')' || last == '.') && s.Any(c => c < 0x0250 && char.IsLetter(c))) return s + LRM;
        return s;
    }

    static void InvalidateRow(DataGridView g, int i)
    {
        if (i >= 0 && i < g.Rows.Count) g.InvalidateRow(i);
    }

    /// <summary>ألوان الشارة حسب نص الحالة</summary>
    public static (Color Fg, Color Bg) StatusColors(string s)
    {
        if (s is "جاهز" or "مسدد" or "فعّال" or "فعّالة" or "على الملاك" or "حاضر" or "تم التسليم" or "بيع" or "قبض" or "جهاز جاهز للتسليم") return (C("#15803D"), SuccessSoft);
        if (s is "متأخر" or "لا يصلح" or "ملغي" or "موقوف" or "متوقفة" or "منفك" or "غائب" or "منتهية الصلاحية" or "راجع" or "إتلاف" or "صرف" or "مصروف" or "تجاوز سقف الذمة" or "تأخر التسديد") return (C("#B91C1C"), DangerSoft);
        if (s is "بانتظار قطعة" or "قيد الفحص" or "قيد التصليح" or "قاربت على الانتهاء" or "قسط مستحق" or "قيد التوصيل" or "إجازة" or "مخزون منخفض" or "إرجاع بيع" or "إرجاع شراء" or "وصل حد الأمان") return (C("#B45309"), WarningSoft);
        if (s is "مستلم" or "قائم" or "شراء" or "مدير" or "تحويل" or "صيرفة" or "راتب" or "سلفة" or "دفعة فاتورة") return (C("#1D4ED8"), InfoSoft);
        if (s is "صيانة" or "عربون صيانة" or "توزيع أرباح" or "تجاوز الحد الأعلى" or "مادة راكدة") return (C("#6D28D9"), PurpleSoft);
        if (s is "هدف البيع") return (C("#1D4ED8"), InfoSoft);
        if (s is "مكافأة" or "تسديد قسط") return (C("#15803D"), SuccessSoft);
        if (s is "خصم") return (C("#B91C1C"), DangerSoft);
        if (s is "مستخدم" or "عطلة") return (Text2, GraySoft);
        // ترتيب الأسباب ودرجة الثقة في التحليل
        if (s is "الأرجح") return (C("#B91C1C"), DangerSoft);
        if (s is "مرجّح") return (C("#B45309"), WarningSoft);
        if (s is "احتمال بعيد") return (Text2, GraySoft);
        if (s is "عالية") return (C("#15803D"), SuccessSoft);
        if (s is "متوسطة" or "شائع") return (C("#B45309"), WarningSoft);
        if (s is "منخفضة") return (Text2, GraySoft);
        if (s is "محتمل") return (C("#1D4ED8"), InfoSoft);
        // درجات التوافق وجودة القطع
        if (s is "مؤكد" or "أصلي" or "متوفر" or "في البرنامج") return (C("#15803D"), SuccessSoft);
        if (s is "يحتاج تعديل" or "كوبي" or "منخفض") return (C("#B45309"), WarningSoft);
        if (s is "غير مجرب" or "درجة أولى") return (C("#1D4ED8"), InfoSoft);
        if (s is "غير متوافق" or "نافد") return (C("#B91C1C"), DangerSoft);
        if (s is "مستعمل") return (C("#6D28D9"), PurpleSoft);
        return (Color.Empty, Color.Empty);
    }
}

/// <summary>أدوات الرسم المشتركة</summary>
public static class Gfx
{
    public static void Hq(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Max(0, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
        if (rad < 0.5f) { p.AddRectangle(r); return p; }
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
    {
        using var p = Round(r, rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static void DrawRound(Graphics g, RectangleF r, float rad, Color c, float w = 1f)
    {
        using var p = Round(r, rad);
        using var pen = new Pen(c, w);
        g.DrawPath(pen, p);
    }

    /// <summary>ظل ناعم أسفل البطاقة</summary>
    public static void Shadow(Graphics g, RectangleF r, float rad)
    {
        float k = Dpi.F;
        for (int i = 1; i <= 3; i++)
            FillRound(g, new RectangleF(r.X - (i - 1) * k, r.Y + i * k, r.Width + (2 * i - 2) * k, r.Height + (i - 1) * k), rad + i * k, Color.FromArgb(7 - i, 15, 23, 42));
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Color Alpha(Color c, int a) => Color.FromArgb(a, c);

    /// <summary>أول لون خلفية معتم لدى الآباء (للزوايا الدائرية)</summary>
    public static Color OpaqueBack(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return Theme.Surface;
    }

    public const TextFormatFlags RtlStart = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.VerticalCenter |
                                           TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
    public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.VerticalCenter |
                                         TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
}
