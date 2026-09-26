using System.Runtime.InteropServices;

namespace Kashif;

/// <summary>نافذة حوار حديثة بلا إطار ويندوز: رأس بعنوان وأيقونة، محتوى، وشريط أزرار</summary>
public class DialogShell : BaseForm
{
    public Panel Body { get; }
    public FlowLayoutPanel Buttons { get; }
    readonly Panel header;
    readonly string icon;
    readonly Color tone;
    Point dragStart;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public DialogShell(string title, int width = 460, int height = 300, string icon = null, Color? tone = null)
    {
        Text = title;
        this.icon = icon;
        this.tone = tone ?? Theme.Brand;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        BackColor = Theme.Surface;
        Size = new Size(width, height);
        Padding = new Padding(1);

        header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Theme.Surface };
        header.Paint += PaintHeader;
        header.MouseDown += (s, e) => dragStart = e.Location;
        header.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y); };
        var close = new ModernButton { Kind = BtnKind.Ghost, IconName = "x", Size = new Size(34, 34), Dock = DockStyle.Left, TabStop = false };
        close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var closeHost = new Panel { Dock = DockStyle.Left, Width = 50, Padding = new Padding(12, 14, 4, 14), BackColor = Theme.Surface };
        closeHost.Controls.Add(close);
        header.Controls.Add(closeHost);

        Body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 4, 22, 10), BackColor = Theme.Surface };
        Buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(16, 12, 16, 12), BackColor = Theme.SurfaceAlt,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        Buttons.Paint += (s, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, Buttons.Width, 0); };

        Controls.Add(Body);
        Controls.Add(Buttons);
        Controls.Add(header);
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }   // CS_DROPSHADOW
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }   // زوايا دائرية في ويندوز 11
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Theme.BorderStrong);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    void PaintHeader(object s, PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Hq(g);
        int x = header.Width - Dpi.S(22), box = Dpi.S(36);
        if (Icons.Has(icon))
        {
            var ir = new RectangleF(x - box, (header.Height - box) / 2f, box, box);
            using (var b = new SolidBrush(Gfx.Mix(tone, Color.White, 0.87f))) g.FillEllipse(b, ir);
            Icons.Draw(g, icon, ir, tone, 19);
            x -= box + Dpi.S(12);
        }
        TextRenderer.DrawText(g, Text, Theme.FS(12.5f), new Rectangle(Dpi.S(60), 0, x - Dpi.S(60), header.Height), Theme.Ink, Gfx.RtlStart);
    }

    /// <summary>إضافة زر إلى شريط الأزرار (الأول هو الأساسي)</summary>
    public ModernButton AddButton(string text, DialogResult result, BtnKind kind = BtnKind.Primary, string iconName = null)
    {
        var b = new ModernButton { Text = text, Kind = kind, IconName = iconName ?? Theme.AutoIcon(text), DialogResult = result, Margin = new Padding(6, 0, 0, 0) };
        b.FitWidth(110);
        Buttons.Controls.Add(b);
        if (result == DialogResult.OK || result == DialogResult.Yes) AcceptButton ??= b;
        if (result == DialogResult.Cancel || result == DialogResult.No) CancelButton ??= b;
        return b;
    }

    public DialogResult ShowModal()
    {
        var owner = Dialogs.Owner();
        if (owner == null) StartPosition = FormStartPosition.CenterScreen;
        return owner != null ? ShowDialog(owner) : ShowDialog();
    }
}

public enum Tone { Success, Info, Warning, Danger }

/// <summary>رسائل وتأكيدات بتصميم موحد بدلًا من صناديق رسائل ويندوز التقليدية</summary>
public static class Dialogs
{
    internal static Form Owner()
    {
        var f = Form.ActiveForm;
        if (f != null && f.Visible && f.TopLevel) return f;
        return Application.OpenForms.Cast<Form>().LastOrDefault(x => x.Visible && x.TopLevel && x is not ToastForm);
    }

    static (string Icon, Color Color) Look(Tone t) => t switch
    {
        Tone.Success => ("circle-check", Theme.Success),
        Tone.Warning => ("triangle-alert", Theme.Warning),
        Tone.Danger => ("circle-alert", Theme.Danger),
        _ => ("info", Theme.Info)
    };

    public static DialogResult Message(string text, string title, Tone tone, params (string Text, DialogResult Result, BtnKind Kind)[] buttons)
    {
        var (icon, color) = Look(tone);
        var font = Theme.F(10.5f);
        int width = 480;
        // القياس بالبكسل الفعلي ثم التحويل للمنطقي (النافذة تُكبَّر مع الشاشة عند فتحها)
        var size = TextRenderer.MeasureText(text ?? "", font, new Size(Dpi.S(width - 48), 4000), TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft);
        int height = Math.Min(620, 62 + 66 + Math.Max(40, Dpi.U(size.Height)) + 30);
        using var d = new DialogShell(title, width, height, icon, color);
        var lbl = new Label { Text = text, Dock = DockStyle.Fill, Font = font, ForeColor = Theme.Text2, TextAlign = ContentAlignment.TopLeft, BackColor = Theme.Surface };
        d.Body.Controls.Add(lbl);
        if (buttons.Length == 0) buttons = new[] { ("حسنًا", DialogResult.OK, BtnKind.Primary) };
        foreach (var b in buttons.Reverse()) d.AddButton(b.Text, b.Result, b.Kind, b.Kind == BtnKind.Danger ? "trash-2" : null);
        d.AcceptButton = d.Buttons.Controls.Cast<ModernButton>().Last();
        return d.ShowModal();
    }

    public static void Info(string text, string title = "تم بنجاح") => Message(text, title, Tone.Success);
    public static void Warn(string text, string title = "تنبيه") => Message(text, title, Tone.Warning);
    public static void Error(string text, string title = "حدث خطأ") => Message(text, title, Tone.Danger);

    public static bool Confirm(string text, string title = "تأكيد", string yes = "نعم، متابعة", bool danger = false) =>
        Message(text, title, danger ? Tone.Danger : Tone.Info,
            (yes, DialogResult.Yes, danger ? BtnKind.Danger : BtnKind.Primary), ("إلغاء", DialogResult.No, BtnKind.Secondary)) == DialogResult.Yes;
}

/// <summary>إشعار صغير يظهر أسفل النافذة ويختفي تلقائيًا (بدلًا من رسائل «تم الحفظ» المزعجة)</summary>
public static class Toast
{
    static readonly List<ToastForm> open = new();

    public static void Show(string text, Tone tone = Tone.Success)
    {
        var owner = Dialogs.Owner();
        if (owner == null) { Dialogs.Message(text, "كاشف", tone); return; }
        var t = new ToastForm(text, tone);
        var area = owner.RectangleToScreen(owner.ClientRectangle);
        int y = area.Bottom - t.Height - Dpi.S(28) - open.Sum(o => o.Height + Dpi.S(10));
        t.Location = new Point(area.X + (area.Width - t.Width) / 2, y);
        open.Add(t);
        t.FormClosed += (s, e) => open.Remove(t);
        t.Show(owner);
    }
}

internal sealed class ToastForm : Form
{
    readonly string text;
    readonly Tone tone;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    int ticks;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public ToastForm(string text, Tone tone)
    {
        this.text = text;
        this.tone = tone;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Ink;
        RightToLeft = RightToLeft.Yes;
        DoubleBuffered = true;
        var font = Theme.FS(10);
        int w = Math.Min(Dpi.S(560), TextRenderer.MeasureText(text, font).Width + Dpi.S(90));
        Size = new Size(Math.Max(Dpi.S(260), w), Dpi.S(52));
        timer.Tick += (s, e) =>
        {
            ticks++;
            if (ticks < 8) Opacity = ticks / 8.0;
            else if (ticks > 95) { Opacity -= 0.1; if (Opacity <= 0.05) { timer.Stop(); Close(); } }
        };
        Opacity = 0;
        Click += (s, e) => Close();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; cp.ClassStyle |= 0x20000; return cp; }  // NOACTIVATE | TOOLWINDOW | DROPSHADOW
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); timer.Start(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Hq(g);
        var (icon, col) = tone switch
        {
            Tone.Success => ("circle-check", ColorTranslator.FromHtml("#34D399")),
            Tone.Warning => ("triangle-alert", ColorTranslator.FromHtml("#FBBF24")),
            Tone.Danger => ("circle-alert", ColorTranslator.FromHtml("#F87171")),
            _ => ("info", ColorTranslator.FromHtml("#60A5FA"))
        };
        Icons.Draw(g, icon, new RectangleF(Width - Dpi.S(42), (Height - Dpi.S(22)) / 2f, Dpi.S(22), Dpi.S(22)), col, 20);
        TextRenderer.DrawText(g, text, Theme.FS(10), new Rectangle(Dpi.S(16), 0, Width - Dpi.S(66), Height), Color.White, Gfx.RtlStart);
    }

    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
