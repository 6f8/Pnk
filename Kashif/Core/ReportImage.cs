using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Kashif;

/// <summary>
/// تقرير الزبون صورةً (الواجهة 10) جاهزة للإرسال على واتساب: بطاقة بيضاء على أرضية رملية، لغة بسيطة بلا رموز تقنية.
/// الخط يُمرَّر من الخارج (خط البرنامج المضمّن، أو خط النظام في الفحوصات).
/// </summary>
public static class ReportImage
{
    public sealed record Content(string Shop, string Date, string Device, string Problem, string Cause, string Done, string Note);

    static readonly Color Ground = ColorTranslator.FromHtml("#E9E4DA"), Card = Color.White, Ink = ColorTranslator.FromHtml("#1B1A17"),
        Muted = ColorTranslator.FromHtml("#5F5B53"), Line = ColorTranslator.FromHtml("#E4DED3"), Petrol = ColorTranslator.FromHtml("#0E5A55"),
        Soft = ColorTranslator.FromHtml("#E3EFEC");

    const TextFormatFlags Rtl = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

    /// <param name="font">(الحجم بالبكسل على صورة 96 نقطة/إنش، عريض؟) ← خط. الخطوط لا يتخلص منها الرسم (قد تكون مخزّنة عند المستدعي)</param>
    public static Bitmap Render(Content c, Func<float, bool, Font> font, int width = 1080)
    {
        int pad = 72, cardPad = 64, inner = width - 2 * pad - 2 * cardPad;
        var rows = new List<(string Label, string Text, bool Strong)>
        {
            ("الجهاز", c.Device, true),
            ("المشكلة", c.Problem, false),
            ("السبب", c.Cause, false),
        };
        if (!string.IsNullOrWhiteSpace(c.Done)) rows.Add(("ما تم", c.Done, true));

        var title = font(46, true);
        var small = font(26, false);
        var label = font(26, false);
        var body = font(34, false);
        var strong = font(36, true);
        var note = font(25, false);
        int H(string t, Font f) => string.IsNullOrEmpty(t) ? 0 : TextRenderer.MeasureText(t, f, new Size(inner, int.MaxValue), Rtl).Height;

        int y = cardPad + H(c.Shop, title) + 12 + H(c.Date, small) + 40 + 2 + 40;
        foreach (var (lb, tx, st) in rows) y += H(lb, label) + 10 + H(tx, st ? strong : body) + 40;
        y += H(c.Note, note) + 28 + 40 + cardPad;
        int height = y + 2 * pad;

        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Ground);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var cardRect = new Rectangle(pad, pad, width - 2 * pad, height - 2 * pad);
        using (var path = Rounded(cardRect, 28)) using (var b = new SolidBrush(Card)) g.FillPath(b, path);

        int x = pad + cardPad;
        y = pad + cardPad;
        void Draw(string t, Font f, Color col)
        {
            if (string.IsNullOrEmpty(t)) return;
            int h = H(t, f);
            TextRenderer.DrawText(g, t, f, new Rectangle(x, y, inner, h), col, Card, Rtl);
            y += h;
        }
        Draw(c.Shop, title, Ink);
        y += 12;
        Draw(c.Date, small, Muted);
        y += 40;
        using (var p = new Pen(Line, 2)) g.DrawLine(p, x, y, x + inner, y);
        y += 42;
        foreach (var (lb, tx, st) in rows)
        {
            Draw(lb, label, Muted);
            y += 10;
            Draw(tx, st ? strong : body, Ink);
            y += 40;
        }
        // الملاحظة في شريط بترولي فاتح
        int nh = H(c.Note, note);
        var band = new Rectangle(x - 24, y - 14, inner + 48, nh + 28);
        using (var path = Rounded(band, 16)) using (var b = new SolidBrush(Soft)) g.FillPath(b, path);
        TextRenderer.DrawText(g, c.Note, note, new Rectangle(x, y, inner, nh), Petrol, Soft, Rtl);
        return bmp;
    }

    static GraphicsPath Rounded(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>محتوى التقرير من التشخيص: نفس كلمات تقرير الزبون النصي</summary>
    public static Content From(Diagnosis d, string shop, string fixedPart, DateTime date)
    {
        var cause = d.TopPart == "" ? "يُحدَّد بالفحص العملي." : d.TopPart + (d.Candidates.Count > 1 && !d.Summary.StartsWith("مؤكد بالفحص", StringComparison.Ordinal) ? $" (وقد يكون: {d.Candidates[1].Part})" : "");
        if (d.Summary.StartsWith("مؤكد بالفحص", StringComparison.Ordinal)) cause += " — تأكدنا منه بالفحص العملي.";
        return new Content(
            string.IsNullOrWhiteSpace(shop) ? "تقرير فحص الجهاز" : shop.Trim(),
            date.ToString("yyyy/MM/dd"),
            d.Device == "" ? "غير معروف" : d.Device,
            PanicAnalyzer.CustomerProblem(d),
            cause,
            string.IsNullOrWhiteSpace(fixedPart) ? "" : "تبديل " + fixedPart.Trim() + "، وفحص الجهاز بعد التبديل.",
            "التشخيص مبني على سجل الأعطال الذي يحفظه الجهاز، ويُؤكَّد بالفحص العملي قبل تبديل أي قطعة.");
    }
}
