using System.Data;

namespace Kashif;

/// <summary>نموذج أساسي: عربي من اليمين لليسار، خط وألوان موحدة</summary>
public class BaseForm : Form
{
    public BaseForm()
    {
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = Theme.F();
        BackColor = Theme.Bg;
        ForeColor = Theme.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
    }

    /// <summary>تم تكبير أبعاد الشاشة حسب دقة العرض (مرة واحدة فقط)</summary>
    internal bool DpiScaled { get; set; }

    protected override void OnLoad(EventArgs e)
    {
        // النوافذ المستقلة تُكبَّر هنا؛ الشاشات داخل التبويبات تُكبَّر عند فتحها في النافذة الرئيسية
        if (TopLevel && !DpiScaled)
        {
            Dpi.ScaleTree(this);
            FitToScreen();
        }
        base.OnLoad(e);
    }

    /// <summary>لا تتجاوز النافذة مساحة الشاشة (شاشات صغيرة أو تكبير 150%) وتبقى في الوسط</summary>
    void FitToScreen()
    {
        var area = Screen.FromPoint(Owner != null ? Owner.Location : Cursor.Position).WorkingArea;
        if (MinimumSize.Width > area.Width || MinimumSize.Height > area.Height)
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        if (WindowState == FormWindowState.Normal)
        {
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            if (StartPosition == FormStartPosition.CenterParent && Owner != null)
                Location = new Point(Owner.Left + (Owner.Width - Width) / 2, Owner.Top + (Owner.Height - Height) / 2);
            else if (StartPosition is FormStartPosition.CenterScreen or FormStartPosition.CenterParent)
                Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
            Location = new Point(Math.Max(area.X, Math.Min(Left, area.Right - Width)), Math.Max(area.Y, Math.Min(Top, area.Bottom - Height)));
        }
    }

    /// <summary>يُستدعى كلما عادت الشاشة لتكون التبويب النشط (لتحديث القوائم مثلًا)</summary>
    public virtual void OnPageActivated() { }
    /// <summary>قبل إغلاق التبويب: false لإلغاء الإغلاق (مثل فاتورة لم تُحفظ)</summary>
    public virtual bool ConfirmClose() => true;
}

/// <summary>عنصر في القوائم المنسدلة</summary>
public class Opt
{
    public long Id;
    public string Name;
    public object Tag;
    public override string ToString() => Name;
}

public static class Ui
{
    public const string DtFmt = "yyyy-MM-dd HH:mm:ss", TFmt = "yyyy-MM-dd HH:mm", DFmt = "yyyy-MM-dd";
    public static string Now => DateTime.Now.ToString(DtFmt);
    public static string Today => DateTime.Now.ToString(DFmt);


    public static string M(double v) => (!double.IsFinite(v) || Math.Abs(v) < 0.005 ? 0 : v).ToString("#,0.##");

    /// <summary>أول n حرف من النص (بدون استثناء إذا كان النص أقصر)</summary>
    public static string Cut(string s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length > n ? s[..n] : s;

    /// <summary>ضبط قيمة حقل رقمي بأمان: القيمة خارج حدود الحقل كانت توقف البرنامج برسالة خطأ</summary>
    public static void SetNum(NumericUpDown n, double v)
    {
        if (!double.IsFinite(v)) v = 0;
        decimal d = v >= (double)n.Maximum ? n.Maximum : v <= (double)n.Minimum ? n.Minimum : (decimal)v;
        if (n.Value != d) n.Value = d;
    }

    /// <summary>تأخير البحث حتى يتوقف المستخدم عن الكتابة لحظة (بدل إعادة التحميل مع كل حرف)</summary>
    public static void OnTextIdle(TextBox box, Action action, int ms = 300)
    {
        var timer = new System.Windows.Forms.Timer { Interval = ms };
        timer.Tick += (s, e) => { timer.Stop(); if (!box.IsDisposed) action(); };
        box.TextChanged += (s, e) => { timer.Stop(); timer.Start(); };
        box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && timer.Enabled) { timer.Stop(); action(); } };
        box.Disposed += (s, e) => timer.Dispose();
    }

    public static ComboBox Combo(int width = 200) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Font = Theme.F(10) };

    public static NumericUpDown Num(int width = 140, int decimals = 0) => new()
    {
        Width = width, Maximum = 1_000_000_000_000m, Minimum = -1_000_000_000_000m,
        DecimalPlaces = decimals, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10)
    };

    public static void FillCombo(ComboBox cb, string sql, bool none = false, string noneText = "— بدون —", params object[] p)
    {
        cb.Items.Clear();
        if (none) cb.Items.Add(new Opt { Id = 0, Name = noneText });
        foreach (DataRow r in Db.Query(sql, p).Rows)
            cb.Items.Add(new Opt { Id = Db.L(r[0]), Name = Db.S(r[1]), Tag = r });
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    public static long GetId(ComboBox cb) => cb.SelectedItem is Opt o ? o.Id : 0;
    public static DataRow GetRow(ComboBox cb) => cb.SelectedItem is Opt o ? o.Tag as DataRow : null;

    public static void SelectId(ComboBox cb, long id)
    {
        for (int i = 0; i < cb.Items.Count; i++)
            if (cb.Items[i] is Opt o && o.Id == id) { cb.SelectedIndex = i; return; }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    /// <summary>قائمة منسدلة قابلة للبحث بالكتابة</summary>
    public static void MakeSearchable(ComboBox cb)
    {
        cb.DropDownStyle = ComboBoxStyle.DropDown;
        cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        cb.AutoCompleteSource = AutoCompleteSource.ListItems;
        cb.Leave += (s, e) =>
        {
            if (cb.SelectedIndex >= 0) return;
            for (int i = 0; i < cb.Items.Count; i++)
                if (cb.Items[i].ToString() == cb.Text) { cb.SelectedIndex = i; return; }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        };
    }

    /// <summary>يلف حقول الإدخال بإطار حديث (النصوص، الأرقام، القوائم، التواريخ)</summary>
    public static Control Wrap(Control c, string icon = null) =>
        c is TextBox or NumericUpDown or ComboBox or DateTimePicker ? new InputBox(c, c.Width, icon) : c;

    /// <summary>حقل بعنوان صغير فوقه</summary>
    public static Control Labeled(string caption, Control c)
    {
        var field = Wrap(c, c is TextBox { PlaceholderText.Length: > 0 } t && t.PlaceholderText.StartsWith("بحث") ? "search" : null);
        var p = new Panel { Width = field.Width + 4, Height = field.Height + 23, Margin = new Padding(6, 1, 6, 3) };
        var l = new Label
        {
            Text = caption, Dock = DockStyle.Top, Height = 23, ForeColor = Theme.Text2, Font = Theme.F(9), AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 0, 2, 0)
        };
        field.Dock = DockStyle.Bottom;
        p.Controls.Add(field);
        p.Controls.Add(l);
        return p;
    }

    /// <summary>مربع بحث بأيقونة عدسة</summary>
    public static Control SearchBox(TextBox t, int width = 0)
    {
        if (width > 0) t.Width = width;
        return new InputBox(t, t.Width, "search") { Margin = new Padding(6, 24, 6, 3) };
    }

    public static DataGridView NewGrid(bool readOnly = true)
    {
        var g = new DataGridView { Dock = DockStyle.Fill };
        Theme.Grid(g, readOnly);
        return g;
    }

    public static bool Confirm(string msg) =>
        Dialogs.Confirm(msg, "تأكيد", msg.Contains("حذف") ? "نعم، احذف" : "نعم، متابعة", danger: msg.Contains("حذف") || msg.Contains("استبدال") || msg.Contains("فك"));

    /// <summary>الرسائل القصيرة تظهر كإشعار عابر، والطويلة كنافذة</summary>
    public static void Info(string msg)
    {
        if (msg.Length <= 90 && !msg.Contains('\n')) Toast.Show(msg);
        else Dialogs.Info(msg);
    }

    public static void Warn(string msg) => Dialogs.Warn(msg);

    public static bool AskNumber(string title, string caption, double def, out double value, string hint = null)
    {
        using var f = new DialogShell(title, 420, hint == null ? 250 : 290, "calculator");
        var n = Num(372, 2);
        SetNum(n, def);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(0, 6, 0, 0) };
        flow.Controls.Add(Labeled(caption, n));
        if (hint != null)
            flow.Controls.Add(new Label { Text = hint, AutoSize = false, Width = 372, Height = 34, ForeColor = Theme.Muted, Font = Theme.F(9), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 6, 0) });
        f.Body.Controls.Add(flow);
        f.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        f.AddButton("موافق", DialogResult.OK);
        f.Shown += (s, e) => { n.Focus(); n.Select(0, n.Text.Length); };
        var r = f.ShowModal();
        value = (double)n.Value;
        return r == DialogResult.OK;
    }

    public static Opt Pick(string title, string caption, string sql, params object[] p)
    {
        using var f = new DialogShell(title, 420, 250, "list");
        var cb = Combo(372);
        FillCombo(cb, sql, false, "", p);
        MakeSearchable(cb);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(0, 6, 0, 0) };
        flow.Controls.Add(Labeled(caption, cb));
        f.Body.Controls.Add(flow);
        f.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        f.AddButton("موافق", DialogResult.OK);
        return f.ShowModal() == DialogResult.OK ? cb.SelectedItem as Opt : null;
    }

    /// <summary>إضافة زري «طباعة» و«Excel» لأي جدول</summary>
    public static void GridTools(Control bar, DataGridView grid, Func<string> title, Func<string> subtitle = null)
    {
        if (!Session.Can("print")) return;
        var bPrint = Theme.Btn("طباعة", Theme.Gray, 90);
        var bXls = Theme.Btn("Excel", Theme.Gray, 80);
        bPrint.Click += (s, e) => PrintDoc.PrintGrid(grid, title(), subtitle?.Invoke());
        bXls.Click += (s, e) => Excel.ExportGrid(grid, title());
        bar.Controls.Add(bPrint);
        bar.Controls.Add(bXls);
    }

    /// <summary>فتح شاشة في تبويب داخل النافذة الرئيسية (أو نافذة مستقلة إن لم تكن مفتوحة)</summary>
    public static void OpenPage(string title, Form f)
    {
        if (MainForm.Instance != null) MainForm.Instance.Open(title, f);
        else f.Show();
    }
}
