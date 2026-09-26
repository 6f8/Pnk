using System.Data;
using System.Drawing.Printing;

namespace Kashif;

/// <summary>صور المحل في المطبوعات: الشعار، وترويسة القوائم (20 × 4.5 سم) — تُحفظ بجانب قاعدة البيانات</summary>
public static class Branding
{
    public static string LogoPath => Path.Combine(Db.DataDir, "logo.png");
    public static string HeaderPath => Path.Combine(Db.DataDir, "header.png");

    public static Image Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var img = System.Drawing.Image.FromStream(fs);
            return new Bitmap(img);   // نسخة في الذاكرة حتى لا يبقى الملف مقفلًا
        }
        catch { return null; }
    }

    public static void Set(string target, string source)
    {
        using var img = Load(source) ?? throw new InvalidOperationException("الملف المختار ليس صورة.");
        img.Save(target, System.Drawing.Imaging.ImageFormat.Png);
    }

    public static void Clear(string target) { try { if (File.Exists(target)) File.Delete(target); } catch { } }
}

/// <summary>
/// مستند طباعة بسيط (عناوين، أزواج، جداول، خطوط، باركود) يُطبع على A4 أو ورق حراري 80 ملم،
/// مع تقسيم تلقائي للصفحات وتكرار رأس الجدول.
/// </summary>
public class PrintDoc
{
    abstract class El { }
    class TextEl : El { public string Text; public float Size; public bool Bold; public StringAlignment Align; }
    class PairEl : El { public string K1, V1, K2, V2; }
    class TableEl : El { public string[] Head; public float[] W; public List<string[]> Rows; }
    class LineEl : El { }
    class SpaceEl : El { public float H; }
    class BarcodeEl : El { public string Code; }
    class ImageEl : El { public Image Img; public float MaxH; }

    readonly List<El> els = new();
    public bool Landscape;
    public bool? ForceA4;

    public PrintDoc Text(string t, float size = 10, bool bold = false, StringAlignment align = StringAlignment.Near)
    { els.Add(new TextEl { Text = t ?? "", Size = size, Bold = bold, Align = align }); return this; }
    public PrintDoc Pair(string k1, string v1, string k2 = null, string v2 = null) { els.Add(new PairEl { K1 = k1, V1 = v1, K2 = k2, V2 = v2 }); return this; }
    public PrintDoc Table(string[] head, float[] weights, List<string[]> rows) { els.Add(new TableEl { Head = head, W = weights, Rows = rows }); return this; }
    public PrintDoc Line() { els.Add(new LineEl()); return this; }
    public PrintDoc Space(float h = 8) { els.Add(new SpaceEl { H = h }); return this; }
    public PrintDoc Barcode(string code) { els.Add(new BarcodeEl { Code = code }); return this; }
    /// <summary>صورة بعرض الصفحة (أو أصغر) مع الحفاظ على النسبة — الارتفاع الأقصى بوحدة 1/100 بوصة</summary>
    public PrintDoc Image(Image img, float maxH) { if (img != null) els.Add(new ImageEl { Img = img, MaxH = maxH }); return this; }

    /// <summary>رأس المطبوعات: صورة الترويسة إن وُجدت، وإلا الشعار واسم المحل وعنوانه وهاتفه</summary>
    public static PrintDoc Header(string title)
    {
        var d = new PrintDoc();
        var banner = Branding.Load(Branding.HeaderPath);
        if (banner != null) d.Image(banner, 190);
        else
        {
            d.Image(Branding.Load(Branding.LogoPath), 80);
            d.Text(Settings.Get("shop_name"), 16, true, StringAlignment.Center);
            if (Settings.Get("shop_activity") != "") d.Text(Settings.Get("shop_activity"), 9.5f, false, StringAlignment.Center);
        }
        var sub = string.Join("  —  ", new[] { Settings.Get("shop_city"), Settings.Get("shop_address"), Settings.Get("shop_phone") }.Where(x => x != ""));
        if (sub != "" && banner == null) d.Text(sub, 9, false, StringAlignment.Center);
        d.Line();
        d.Text(title, 13, true, StringAlignment.Center);
        d.Space(4);
        return d;
    }

    public PrintDoc Footer()
    {
        Line();
        var f = Settings.Get("invoice_footer");
        if (f != "") Text(f, 9, false, StringAlignment.Center);
        Text("طُبع بتاريخ " + DateTime.Now.ToString("yyyy/MM/dd HH:mm") + " — برنامج كاشف", 7, false, StringAlignment.Center);
        return this;
    }

    // ---------------- التصيير ----------------
    record Item(Func<Graphics, float, float> Height, Action<Graphics, RectangleF> Draw, TableEl Table, bool IsHead);

    bool Thermal => ForceA4 != true && Settings.Get("print_mode") == "80mm";
    float Scale => Thermal ? 0.82f : 1f;
    // نفس خط البرنامج في المطبوعات (IBM Plex Sans Arabic المضمّن)
    Font Fnt(float size, bool bold) => FontKit.Create(size * Scale, bold ? FontStyle.Bold : FontStyle.Regular);

    static StringFormat Sf(StringAlignment a, bool wrap = true)
    {
        var sf = new StringFormat(StringFormatFlags.DirectionRightToLeft) { Alignment = a, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        if (!wrap) sf.FormatFlags |= StringFormatFlags.NoWrap;
        return sf;
    }

    List<Item> Build()
    {
        var items = new List<Item>();
        foreach (var el in els)
        {
            switch (el)
            {
                case TextEl t:
                    items.Add(new Item((g, w) => { using var f = Fnt(t.Size, t.Bold); return g.MeasureString(t.Text, f, (int)w, Sf(t.Align)).Height + 3; },
                        (g, r) => { using var f = Fnt(t.Size, t.Bold); g.DrawString(t.Text, f, Brushes.Black, r, Sf(t.Align)); }, null, false));
                    break;
                case PairEl p:
                    items.Add(new Item((g, w) => { using var f = Fnt(10, false); return f.GetHeight(g) + 5; },
                        (g, r) =>
                        {
                            using var f = Fnt(10, false);
                            using var fb = Fnt(10, true);
                            var half = r.Width / 2;
                            if (p.K2 == null) half = r.Width;
                            DrawKV(g, new RectangleF(r.Right - half, r.Y, half, r.Height), p.K1, p.V1, f, fb);
                            if (p.K2 != null) DrawKV(g, new RectangleF(r.X, r.Y, half, r.Height), p.K2, p.V2, f, fb);
                        }, null, false));
                    break;
                case LineEl:
                    items.Add(new Item((g, w) => 8, (g, r) => { using var pen = new Pen(Color.Black, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }; g.DrawLine(pen, r.X, r.Y + 4, r.Right, r.Y + 4); }, null, false));
                    break;
                case SpaceEl s:
                    items.Add(new Item((g, w) => s.H, (g, r) => { }, null, false));
                    break;
                case ImageEl im:
                    items.Add(new Item((g, w) => Math.Min(im.MaxH, w * im.Img.Height / Math.Max(1f, im.Img.Width)) + 6, (g, r) =>
                    {
                        float h = r.Height - 6, w = Math.Min(r.Width, h * im.Img.Width / Math.Max(1f, im.Img.Height));
                        g.DrawImage(im.Img, new RectangleF(r.X + (r.Width - w) / 2, r.Y + 2, w, h));
                    }, null, false));
                    break;
                case BarcodeEl b:
                    items.Add(new Item((g, w) => 58, (g, r) => Code128.Draw(g, b.Code, new RectangleF(r.X, r.Y + 4, r.Width, 36), true), null, false));
                    break;
                case TableEl t:
                    items.Add(new Item((g, w) => RowH(g, w, t, t.Head, true), (g, r) => DrawRow(g, r, t, t.Head, true), t, true));
                    foreach (var row in t.Rows)
                    {
                        var rr = row;
                        items.Add(new Item((g, w) => RowH(g, w, t, rr, false), (g, r) => DrawRow(g, r, t, rr, false), t, false));
                    }
                    break;
            }
        }
        return items;
    }

    static void DrawKV(Graphics g, RectangleF r, string k, string v, Font f, Font fb)
    {
        var text = $"{k}: ";
        var kw = g.MeasureString(text, fb).Width;
        g.DrawString(text, fb, Brushes.Black, new RectangleF(r.Right - kw - 2, r.Y, kw + 2, r.Height), Sf(StringAlignment.Near, false));
        g.DrawString(v ?? "", f, Brushes.Black, new RectangleF(r.X, r.Y, Math.Max(10, r.Width - kw - 2), r.Height), Sf(StringAlignment.Near, false));
    }

    float[] ColW(TableEl t, float w) { var s = t.W.Sum(); return t.W.Select(x => w * x / s).ToArray(); }

    float RowH(Graphics g, float w, TableEl t, string[] cells, bool head)
    {
        using var f = Fnt(head ? 9.5f : 9.5f, head);
        var cw = ColW(t, w);
        float h = f.GetHeight(g) + 6;
        for (int i = 0; i < cells.Length && i < cw.Length; i++)
            h = Math.Max(h, g.MeasureString(cells[i] ?? "", f, (int)Math.Max(10, cw[i] - 4), Sf(StringAlignment.Near)).Height + 6);
        return h;
    }

    void DrawRow(Graphics g, RectangleF r, TableEl t, string[] cells, bool head)
    {
        using var f = Fnt(9.5f, head);
        var cw = ColW(t, r.Width);
        if (head) g.FillRectangle(Brushes.Gainsboro, r);
        float x = r.Right;
        for (int i = 0; i < cw.Length; i++)
        {
            x -= cw[i];
            var cell = new RectangleF(x + 2, r.Y, cw[i] - 4, r.Height);
            if (i < cells.Length) g.DrawString(cells[i] ?? "", f, Brushes.Black, cell, Sf(i == 1 || head ? StringAlignment.Near : StringAlignment.Center));
        }
        using var pen = new Pen(Color.Silver);
        g.DrawLine(pen, r.X, r.Bottom, r.Right, r.Bottom);
    }

    // ---------------- الطباعة ----------------
    public void Print(bool? preview = null)
    {
        var items = Build();
        int index = 0;
        var pd = new PrintDocument { DocumentName = "Kashif" };
        var printer = Settings.Get("printer_name");
        if (printer != "") pd.PrinterSettings.PrinterName = printer;
        if (!pd.PrinterSettings.IsValid) pd.PrinterSettings = new PrinterSettings();

        if (Thermal)
        {
            // ورق 80 ملم: نحسب طول الورقة حسب المحتوى
            using var bmp = new Bitmap(10, 10);
            using var mg = Graphics.FromImage(bmp);
            // الطابعة تقيس بوحدة 1/100 بوصة؛ القياس بالبكسل (96 نقطة) كان يُقصّر الإيصالات الطويلة
            mg.PageUnit = GraphicsUnit.Inch;
            mg.PageScale = 0.01f;
            float width = 315 - 24, total = 30 + items.Sum(i => i.Height(mg, width));
            pd.DefaultPageSettings.PaperSize = new PaperSize("Roll80", 315, (int)Math.Max(300, total));
            pd.DefaultPageSettings.Margins = new Margins(12, 12, 10, 10);
        }
        else
        {
            pd.DefaultPageSettings.Landscape = Landscape;
            pd.DefaultPageSettings.Margins = new Margins(45, 45, 40, 45);
        }

        int page = 0;
        pd.BeginPrint += (s, e) => { index = 0; page = 0; };
        pd.PrintPage += (s, e) =>
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var mb = e.MarginBounds;
            float y = mb.Top, bottom = mb.Bottom - (Thermal ? 0 : 18);
            page++;
            bool first = true;
            while (index < items.Count)
            {
                var it = items[index];
                // تكرار رأس الجدول في الصفحة الجديدة
                if (first && page > 1 && it.Table != null && !it.IsHead)
                {
                    var hh = RowH(g, mb.Width, it.Table, it.Table.Head, true);
                    DrawRow(g, new RectangleF(mb.Left, y, mb.Width, hh), it.Table, it.Table.Head, true);
                    y += hh;
                }
                float h = it.Height(g, mb.Width);
                if (!Thermal && y + h > bottom && !first) break;
                it.Draw(g, new RectangleF(mb.Left, y, mb.Width, h));
                y += h;
                index++;
                first = false;
            }
            if (!Thermal)
            {
                using var f = FontKit.Create(8);
                g.DrawString($"صفحة {page}", f, Brushes.Gray, new RectangleF(mb.Left, mb.Bottom, mb.Width, 20), Sf(StringAlignment.Center));
            }
            e.HasMorePages = index < items.Count;
        };

        bool pv = preview ?? Settings.Get("print_preview", "1") == "1";
        try
        {
            if (pv)
            {
                using var dlg = new PrintPreviewDialog { Document = pd, Width = 1000, Height = 800, StartPosition = FormStartPosition.CenterScreen, RightToLeft = RightToLeft.Yes, RightToLeftLayout = true };
                dlg.ShowDialog();
            }
            else pd.Print();
        }
        catch (Exception ex) { Ui.Warn("تعذرت الطباعة: " + ex.Message); }
    }

    /// <summary>طباعة أي جدول بيانات (تقارير، قوائم) على A4</summary>
    public static void PrintGrid(DataGridView grid, string title, string subtitle = null)
    {
        var cols = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c.Name != "id").OrderBy(c => c.DisplayIndex).ToList();
        if (cols.Count == 0 || grid.Rows.Count == 0) { Ui.Warn("لا توجد بيانات للطباعة."); return; }
        var doc = Header(title);
        doc.ForceA4 = true;
        doc.Landscape = cols.Count > 7;
        if (!string.IsNullOrEmpty(subtitle)) doc.Text(subtitle, 9, false, StringAlignment.Center);
        var rows = new List<string[]>();
        foreach (DataGridViewRow r in grid.Rows)
            if (r.Visible) rows.Add(cols.Select(c => Fmt(r.Cells[c.Index].Value)).ToArray());
        doc.Table(cols.Select(c => c.HeaderText).ToArray(), cols.Select(c => c.FillWeight).ToArray(), rows);
        doc.Footer();
        doc.Print(true);
    }

    static string Fmt(object v) => v switch
    {
        null or DBNull => "",
        double d => d.ToString("#,0.##"),
        float f => f.ToString("#,0.##"),
        decimal m => m.ToString("#,0.##"),
        _ => Convert.ToString(v)
    };
}

/// <summary>توليد ورسم باركود Code 128 (B و C)</summary>
public static class Code128
{
    static readonly string[] P = ("212222 222122 222221 121223 121322 131222 122213 122312 132212 221213 221312 231212 112232 122132 122231 113222 " +
        "123122 123221 223211 221132 221231 213212 223112 312131 311222 321122 321221 312212 322112 322211 212123 212321 232121 111323 131123 " +
        "131321 112313 132113 132311 211313 231113 231311 112133 112331 132131 113123 113321 133121 313121 211331 231131 213113 213311 213131 " +
        "311123 311321 331121 312113 312311 332111 314111 221411 431111 111224 111422 121124 121421 141122 141221 112214 112412 122114 122411 " +
        "142112 142211 241211 221114 413111 241112 134111 111242 121142 121241 114212 124112 124211 411212 421112 421211 212141 214121 412121 " +
        "111143 111341 131141 114113 114311 411113 411311 113141 114131 311141 411131 211412 211214 211232 2331112").Split(' ');

    /// <summary>قيم الرموز مع رمز البداية والتحقق والنهاية</summary>
    public static List<int> Encode(string text)
    {
        text = new string((text ?? "").Where(c => c >= 32 && c <= 126).ToArray());
        var v = new List<int>();
        bool digits = text.Length >= 4 && text.All(char.IsDigit);
        if (digits)
        {
            int i = 0;
            if (text.Length % 2 == 1) { v.Add(104); v.Add(text[0] - 32); v.Add(99); i = 1; }
            else v.Add(105);
            for (; i < text.Length; i += 2) v.Add(int.Parse(text.Substring(i, 2)));
        }
        else
        {
            v.Add(104);
            foreach (var c in text) v.Add(c - 32);
        }
        int sum = v[0];
        for (int k = 1; k < v.Count; k++) sum += k * v[k];
        v.Add(sum % 103);
        v.Add(106);
        return v;
    }

    public static string Modules(string text) => string.Concat(Encode(text).Select(x => P[x]));

    public static void Draw(Graphics g, string text, RectangleF r, bool caption)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var mods = Modules(text);
        int total = mods.Sum(c => c - '0') + 20;  // + منطقة هادئة
        float capH = caption ? Math.Min(14, r.Height * 0.28f) : 0;
        float m = r.Width / total;
        float barH = r.Height - capH;
        float x = r.X + (r.Width - m * (total - 20)) / 2;
        var old = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        bool bar = true;
        foreach (var c in mods)
        {
            float w = (c - '0') * m;
            if (bar) g.FillRectangle(Brushes.Black, x, r.Y, w, barH);
            x += w;
            bar = !bar;
        }
        g.SmoothingMode = old;
        if (caption)
        {
            using var f = new Font("Consolas", Math.Max(5, capH * 0.62f));
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, f, Brushes.Black, new RectangleF(r.X, r.Y + barH, r.Width, capH), sf);
        }
    }

    public static Bitmap Image(string text, int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        Draw(g, text, new RectangleF(4, 4, w - 8, h - 8), true);
        return bmp;
    }
}
