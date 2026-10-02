using Kashif.Device;

namespace Kashif;

/// <summary>
/// السجل المباشر من الآيفون الموصول: أسطر الأخطاء المهمة (حساسات، SMC، I2C، AOP) بلونها ومعناها لحظة ظهورها،
/// ومعها كل الأسطر عند الطلب. لا يغيّر ترتيب الأسباب — يريك ما يشتكي منه الجهاز قبل أن يصل إلى البانك.
/// </summary>
public class LiveLogForm : BaseForm
{
    static readonly Color Bg = ColorTranslator.FromHtml("#121513"), Ink = ColorTranslator.FromHtml("#F2EFE8"), Muted = ColorTranslator.FromHtml("#B7B2A8"),
        Teal = ColorTranslator.FromHtml("#7FD1C6"), Amber = ColorTranslator.FromHtml("#E8A36B"), Panel2 = ColorTranslator.FromHtml("#1B1F1C");

    readonly LiveSession session = new();
    readonly ListBox flagged = new() { Dock = DockStyle.Fill, BackColor = Panel2, ForeColor = Ink, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10.5f), RightToLeft = RightToLeft.No, HorizontalScrollbar = true, IntegralHeight = false };
    readonly ListBox all = new() { Dock = DockStyle.Fill, BackColor = Panel2, ForeColor = Muted, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f), RightToLeft = RightToLeft.No, HorizontalScrollbar = true, IntegralHeight = false, Visible = false };
    readonly Label status = new() { Dock = DockStyle.Top, Height = 64, AutoSize = false, Font = Theme.F(12), ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label counts = new() { Dock = DockStyle.Top, Height = 40, AutoSize = false, Font = Theme.FS(12), ForeColor = Teal, TextAlign = ContentAlignment.MiddleLeft };
    readonly Toggle tAll = new() { Text = "كل الأسطر", Width = 140 };
    readonly Dictionary<string, int> byCategory = new();
    int lines;

    public LiveLogForm()
    {
        Text = "السجل المباشر — كاشف";
        Size = new Size(1180, 720);
        MinimumSize = new Size(800, 500);
        BackColor = Bg;
        ForeColor = Ink;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        var title = new Label { Dock = DockStyle.Top, Height = 64, AutoSize = false, Text = "السجل المباشر من الآيفون", Font = Theme.F(20, FontStyle.Bold), ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, BackColor = Bg, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
        var bClose = new ModernButton { Text = "إيقاف وإغلاق", IconName = "x", Kind = BtnKind.Secondary, Width = 170, Height = 48 };
        var bCopy = new ModernButton { Text = "نسخ الأخطاء", IconName = "copy", Kind = BtnKind.Secondary, Width = 150, Height = 48 };
        tAll.Margin = new Padding(14, 10, 0, 0);
        bar.Controls.AddRange(new Control[] { bClose, bCopy, tAll });
        var lists = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        lists.Controls.Add(flagged);
        lists.Controls.Add(all);
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(36, 24, 36, 16) };
        page.Controls.Add(lists);
        page.Controls.Add(counts);
        page.Controls.Add(status);
        page.Controls.Add(title);
        page.Controls.Add(bar);
        Controls.Add(page);

        status.Text = "يتصل بالآيفون… الأسطر المهمة تظهر هنا لحظة كتابتها. جرّب أثناء ذلك: افصل فلاتة ثم شغّل الجهاز وراقب هل تختفي الأخطاء.";
        counts.Text = "لا أخطاء بعد.";
        flagged.DrawMode = DrawMode.OwnerDrawFixed;
        flagged.ItemHeight = 26;
        flagged.DrawItem += DrawFlag;

        session.Line += l => Ui(() =>
        {
            lines++;
            if (all.Items.Count > 1000) all.Items.RemoveAt(0);
            all.Items.Add(l);
            if (all.Visible) all.TopIndex = Math.Max(0, all.Items.Count - 1);
        });
        session.Flagged += f => Ui(() =>
        {
            byCategory[f.Category] = byCategory.GetValueOrDefault(f.Category) + 1;
            flagged.Items.Insert(0, f);
            if (flagged.Items.Count > 500) flagged.Items.RemoveAt(flagged.Items.Count - 1);
            counts.Text = string.Join("   ·   ", byCategory.Select(kv => $"{kv.Key}: {kv.Value}"));
            if (f.Sensor != null && PanicKnowledge.FindSensor(f.Sensor.Split(',')[0]) is { } sensor)
                status.Text = $"حساس مفقود في السجل المباشر: {f.Sensor} — {sensor.What}. {sensor.Note}";
        });
        session.Ended += err => Ui(() =>
        {
            status.Text = err != null ? err : $"انقطع السجل (أعاد الجهاز التشغيل أو فُصل الكيبل). قُرئ {lines} سطرًا.";
            status.ForeColor = err != null ? Amber : Muted;
        });

        tAll.CheckedChanged += (s, e) => { all.Visible = tAll.Checked; flagged.Visible = !tAll.Checked; };
        bClose.Click += (s, e) => Close();
        bCopy.Click += (s, e) =>
        {
            var text = string.Join(Environment.NewLine, session.Flags.Select(f => $"{f.At:HH:mm:ss}  [{f.Category}]  {f.Line}"));
            try { if (text != "") Clipboard.SetText(text); Toast.Show("نُسخت الأخطاء"); } catch { }
        };
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += (s, e) => { session.Start(); status.Text = "يقرأ السجل المباشر… الأسطر المهمة تظهر هنا لحظة كتابتها. أثناء ذلك افصل فلاتة وشغّل الجهاز وراقب هل تختفي الأخطاء."; };
        FormClosing += (s, e) => session.Stop();
    }

    void Ui(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    void DrawFlag(object sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || flagged.Items[e.Index] is not LiveFlag f) return;
        var color = f.Category is "حساس" or "SMC" or "I2C" ? Amber : Teal;
        using var b = new SolidBrush(Panel2);
        e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, $"{f.At:HH:mm:ss}  [{f.Category}]  {f.Line}", flagged.Font, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) session.Dispose();
        base.Dispose(disposing);
    }
}
