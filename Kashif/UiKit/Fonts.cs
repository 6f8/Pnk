using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Kashif;

/// <summary>
/// خط النصوص المضمّن داخل البرنامج (IBM Plex Sans Arabic) — لا يحتاج تثبيتًا على الجهاز.
/// يُسجَّل مرتين: لـ GDI+ (الرسم المخصص) عبر PrivateFontCollection، ولـ GDI (مربعات النص والقوائم والجداول)
/// كخط خاص بالبرنامج فقط. التحميل من ملف أولًا (الأكثر ثباتًا على ويندوز)، ومن الذاكرة احتياطًا.
/// إن تعذر كل ذلك يُستخدم Segoe UI، ويُكتب السبب في fonts.log داخل مجلد البيانات.
/// </summary>
public static class FontKit
{
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int AddFontResourceEx(string name, uint fl, IntPtr res);
    [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);
    const uint FR_PRIVATE = 0x10;

    static readonly PrivateFontCollection pfc = new();
    static readonly Dictionary<(float, FontStyle, bool), Font> cache = new();
    static FontFamily text, semi;
    static bool loaded;
    static readonly List<string> log = new();

    public const string Fallback = "Segoe UI";
    /// <summary>هل الخط المضمّن محمَّل؟ (للتشخيص)</summary>
    public static bool Embedded => text != null;

    public static void Init()
    {
        if (loaded) return;
        loaded = true;
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)).ToList();
        string dir = null;
        try
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kashif", "fonts");
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) { log.Add("fonts dir: " + ex.Message); dir = null; }

        foreach (var name in names)
        {
            byte[] data;
            try
            {
                using var s = asm.GetManifestResourceStream(name);
                data = new byte[s.Length];
                s.ReadExactly(data);
            }
            catch (Exception ex) { log.Add($"{name}: read {ex.Message}"); continue; }

            if (dir != null && LoadFromFile(dir, name, data)) continue;
            LoadFromMemory(name, data);
        }
        text = Find("IBM Plex Sans Arabic");
        semi = Find("IBM Plex Sans Arabic SemiBold");
        if (text == null)
        {
            log.Add("families: " + string.Join(", ", pfc.Families.Select(f => f.Name)));
            WriteLog();
        }
    }

    static bool LoadFromFile(string dir, string resName, byte[] data)
    {
        try
        {
            var file = Path.Combine(dir, resName.Split('.').Reverse().Skip(1).First() + ".ttf");
            var fi = new FileInfo(file);
            if (!fi.Exists || fi.Length != data.Length) File.WriteAllBytes(file, data);
            pfc.AddFontFile(file);                                   // GDI+
            if (AddFontResourceEx(file, FR_PRIVATE, IntPtr.Zero) == 0)   // GDI (خاص بهذا البرنامج)
                log.Add($"{resName}: AddFontResourceEx failed ({Marshal.GetLastWin32Error()})");
            return true;
        }
        catch (Exception ex) { log.Add($"{resName}: file {ex.Message}"); return false; }
    }

    static void LoadFromMemory(string resName, byte[] data)
    {
        try
        {
            // الذاكرة تبقى محجوزة طوال عمر البرنامج (يتطلبها GDI+ وGDI)
            var ptr = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);
            pfc.AddMemoryFont(ptr, data.Length);
            uint count = 0;
            AddFontMemResourceEx(ptr, (uint)data.Length, IntPtr.Zero, ref count);
        }
        catch (Exception ex) { log.Add($"{resName}: memory {ex.Message}"); }
    }

    /// <summary>البحث عن العائلة بالاسم الإنجليزي (ويندوز العربي قد يعيد اسمًا مترجمًا)</summary>
    static FontFamily Find(string family)
    {
        foreach (var f in pfc.Families)
        {
            try
            {
                if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.GetName(1033), family, StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch { }
        }
        return null;
    }

    static void WriteLog()
    {
        try
        {
            Directory.CreateDirectory(Db.DataDir);
            File.AppendAllText(Path.Combine(Db.DataDir, "fonts.log"),
                $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {string.Join(" | ", log)}\r\n");
        }
        catch { }
    }

    /// <summary>خط النصوص بالنقاط (يكبر تلقائيًا مع دقة الشاشة). semibold = وزن متوسط للعناوين والأزرار</summary>
    public static Font Get(float size, FontStyle style = FontStyle.Regular, bool semibold = false)
    {
        size = (float)Math.Round(size, 2);
        var key = (size, style, semibold);
        if (cache.TryGetValue(key, out var f)) return f;
        try
        {
            if (semibold && semi != null) f = new Font(semi, size, style & ~FontStyle.Bold);
            else if (text != null) f = new Font(text, size, style);
            else f = new Font(semibold ? "Segoe UI Semibold" : Fallback, size, style);
        }
        catch { f = new Font(Fallback, size, style); }
        cache[key] = f;
        return f;
    }

    /// <summary>خط بارتفاع محدد بالبكسل الفعلي (للشعارات المرسومة داخل مساحة معروفة)</summary>
    public static Font GetPx(float px, FontStyle style = FontStyle.Regular, bool semibold = false) =>
        Get(px * 72f / Dpi.Value, style, semibold);

    /// <summary>خط جديد غير مخزّن (للطباعة: يُتخلص منه بعد الاستخدام)</summary>
    public static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        try { return text != null ? new Font(text, size, style) : new Font(Fallback, size, style); }
        catch { return new Font(Fallback, size, style); }
    }
}

/// <summary>
/// أيقونات Lucide مرسومة كمسارات متجهة (لا تعتمد على أي خط): واضحة بأي دقة وعلى كل الأجهزة.
/// </summary>
public static partial class Icons
{
    static readonly Dictionary<string, GraphicsPath> paths = new();

    public static bool Has(string name) => name != null && data.ContainsKey(name);

    static GraphicsPath PathOf(string name)
    {
        if (name == null) return null;
        if (paths.TryGetValue(name, out var p)) return p;
        if (!data.TryGetValue(name, out var d)) return null;
        p = Parse(d);
        paths[name] = p;
        return p;
    }

    /// <summary>تحويل نص المسار (M/L/Q/C/Z) إلى GraphicsPath؛ المنحنيات التربيعية تُحوَّل إلى تكعيبية</summary>
    static GraphicsPath Parse(string d)
    {
        var path = new GraphicsPath(FillMode.Winding);
        PointF cur = PointF.Empty, start = PointF.Empty;
        int i = 0;
        float Num()
        {
            while (i < d.Length && d[i] == ' ') i++;
            int s = i;
            if (i < d.Length && d[i] == '-') i++;
            while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.')) i++;
            return float.Parse(d.AsSpan(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        PointF Pt() { float x = Num(); float y = Num(); return new PointF(x, y); }
        bool open = false;
        while (i < d.Length)
        {
            char c = d[i++];
            switch (c)
            {
                case 'M':
                    if (open) path.CloseFigure();
                    path.StartFigure();
                    cur = start = Pt(); open = true;
                    break;
                case 'L':
                    { var p = Pt(); path.AddLine(cur, p); cur = p; break; }
                case 'Q':
                    {
                        var q = Pt(); var p = Pt();
                        var c1 = new PointF(cur.X + 2f / 3 * (q.X - cur.X), cur.Y + 2f / 3 * (q.Y - cur.Y));
                        var c2 = new PointF(p.X + 2f / 3 * (q.X - p.X), p.Y + 2f / 3 * (q.Y - p.Y));
                        path.AddBezier(cur, c1, c2, p); cur = p; break;
                    }
                case 'C':
                    { var a = Pt(); var b = Pt(); var p = Pt(); path.AddBezier(cur, a, b, p); cur = p; break; }
                case 'Z':
                    if (open) { path.CloseFigure(); open = false; }
                    cur = start;
                    break;
            }
        }
        if (open) path.CloseFigure();
        return path;
    }

    /// <summary>
    /// رسم أيقونة في وسط المستطيل (بالبكسل الفعلي) بلون محدد.
    /// px = حجم الأيقونة بالبكسل المنطقي (عند دقة 100%) ويُكبَّر تلقائيًا مع دقة الشاشة؛ 0 = حسب المستطيل.
    /// </summary>
    public static void Draw(Graphics g, string name, RectangleF r, Color color, float px = 0) =>
        DrawDevice(g, name, r, color, px > 0 ? Dpi.S(px) : Math.Min(r.Width, r.Height) * 0.9f);

    /// <summary>مثل Draw لكن الحجم بالبكسل الفعلي مباشرة</summary>
    public static void DrawDevice(Graphics g, string name, RectangleF r, Color color, float size)
    {
        var p = PathOf(name);
        if (p == null || size <= 0) return;
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float k = size / 1000f;
            g.TranslateTransform(r.X + r.Width / 2f - 500 * k, r.Y + r.Height / 2f - 500 * k);
            g.ScaleTransform(k, k);
            using var b = new SolidBrush(color);
            g.FillPath(b, p);
        }
        finally { g.Restore(state); }
    }

    /// <summary>صورة صغيرة للأيقونة (للقوائم والأزرار القياسية) — الحجم بالبكسل الفعلي</summary>
    public static Bitmap Image(string name, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        DrawDevice(g, name, new RectangleF(0, 0, size, size), color, size * 0.86f);
        return bmp;
    }
}

/// <summary>
/// دقة الشاشة: البرنامج «واعٍ للدقة» فيُرسم بوضوح كامل على الشاشات بتكبير 125% و150% و200%.
/// كل الأبعاد في الكود مكتوبة بالبكسل المنطقي (عند 100%) وتُضرب بمعامل الدقة.
/// </summary>
public static class Dpi
{
    static float value;

    /// <summary>دقة الشاشة الفعلية (96 = 100%)</summary>
    public static float Value
    {
        get
        {
            if (value <= 0)
            {
                try { using var g = Graphics.FromHwnd(IntPtr.Zero); value = g.DpiX; }
                catch { value = 96; }
                if (value < 96) value = 96;
            }
            return value;
        }
    }

    /// <summary>معامل التكبير (1 = 100%، 1.5 = 150%)</summary>
    public static float F => Value / 96f;

    public static int S(int v) => (int)Math.Round(v * F);
    public static float S(float v) => v * F;
    public static Size S(int w, int h) => new(S(w), S(h));
    public static Size S(Size s) => new(S(s.Width), S(s.Height));
    public static Point P(int x, int y) => new(S(x), S(y));
    public static Padding Pad(int all) => new(S(all));
    public static Padding Pad(int l, int t, int r, int b) => new(S(l), S(t), S(r), S(b));
    /// <summary>
    /// تكبير شاشة كاملة (المواقع والأحجام والهوامش) مرة واحدة بعد بنائها. الخطوط بالنقاط فتكبر وحدها.
    /// الشاشات الفرعية داخلها تُعلَّم حتى لا تُكبَّر مرتين.
    /// </summary>
    public static void ScaleTree(Control root)
    {
        if (root is BaseForm { DpiScaled: true }) return;
        if (F > 1.01f)
        {
            root.SuspendLayout();
            root.Scale(new SizeF(F, F));
            root.ResumeLayout(true);
        }
        Mark(root);
    }

    static void Mark(Control c)
    {
        if (c is BaseForm b) b.DpiScaled = true;
        foreach (Control k in c.Controls) Mark(k);
    }

    /// <summary>هل كُبِّرت الشاشة التي تحتوي هذه الأداة؟</summary>
    public static bool IsScaled(Control c)
    {
        for (var p = c; p != null; p = p.Parent) if (p is BaseForm b) return b.DpiScaled;
        return false;
    }

    /// <summary>أداة تُنشأ بعد فتح الشاشة (مثل بطاقات المجاميع بعد التحديث): تُكبَّر بنفسها لتطابق ما حولها</summary>
    public static T Fit<T>(Control parent, T child) where T : Control
    {
        if (F > 1.01f && IsScaled(parent)) child.Scale(new SizeF(F, F));
        return child;
    }

    /// <summary>تحويل من بكسل فعلي إلى منطقي</summary>
    public static int U(int v) => (int)Math.Round(v / F);
}
