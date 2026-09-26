using System.Data;

namespace Kashif;

/// <summary>سجل الفحوصات: كل تحليل محفوظ — بحث بالزبون أو الهاتف أو الجهاز أو القطعة، فلتر بالحالة والتاريخ، فتح وحذف</summary>
public class HistoryForm : BaseForm
{
    public const string PageTitle = "سجل الفحوصات";

    readonly DataGridView grid = Ui.NewGrid();
    readonly TextBox search = new() { Width = 300, PlaceholderText = "بحث: الزبون، الهاتف، الجهاز، القطعة، التشخيص" };
    readonly ComboBox cbStatus = Ui.Combo(170);
    readonly DateTimePicker from = new() { Format = DateTimePickerFormat.Short, Width = 150 }, to = new() { Format = DateTimePickerFormat.Short, Width = 150 };
    readonly CardPanel card;

    /// <summary>فتح السجل مفلترًا (من التقارير: القطعة أو النوع أو الجهاز)</summary>
    public static void OpenFiltered(string q) => Ui.OpenPage(PageTitle, new HistoryForm(q));

    public HistoryForm() : this(null) { }

    HistoryForm(string query)
    {
        Text = PageTitle;
        cbStatus.Items.Add("كل الحالات");
        cbStatus.Items.AddRange(PanicStore.Statuses);
        cbStatus.SelectedIndex = 0;
        from.Value = query == null ? DateTime.Today.AddDays(-180) : DateTime.Today.AddYears(-10);
        to.Value = DateTime.Today;
        if (query != null) search.Text = query;

        var bar = Theme.Bar();
        bar.Controls.Add(Ui.SearchBox(search));
        bar.Controls.Add(Ui.Labeled("الحالة", cbStatus));
        bar.Controls.Add(Ui.Labeled("من", from));
        bar.Controls.Add(Ui.Labeled("إلى", to));
        var bOpen = Theme.Btn("فتح الفحص", Theme.Brand, 120, "eye");
        var bNew = Theme.Btn("فحص جديد", Theme.Success, 120, "plus");
        var bDel = Theme.Btn("حذف", Theme.Danger, 90, "trash-2");
        bar.Controls.AddRange(new Control[] { bOpen, bNew, bDel });
        Ui.GridTools(bar, grid, () => PageTitle, () => $"من {from.Value:yyyy-MM-dd} إلى {to.Value:yyyy-MM-dd}");
        foreach (var b in bar.Controls.OfType<ModernButton>()) b.Margin = new Padding(4, 24, 4, 3);

        card = new CardPanel { Dock = DockStyle.Fill, Title = PageTitle, IconName = "history" };
        card.Controls.Add(grid);
        Controls.Add(card);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4 });
        Controls.Add(bar);

        Ui.OnTextIdle(search, Reload, 300);
        cbStatus.SelectedIndexChanged += (s, e) => Reload();
        from.ValueChanged += (s, e) => Reload();
        to.ValueChanged += (s, e) => Reload();
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OpenSelected(); } };
        bOpen.Click += (s, e) => OpenSelected();
        bNew.Click += (s, e) => MainForm.Instance?.Go(AnalyzeForm.PageTitle);
        bDel.Click += (s, e) => DeleteSelected();
        Reload();
    }

    public override void OnPageActivated() => Reload();

    long SelectedId() => grid.CurrentRow == null ? 0 : Db.L(grid.CurrentRow.Cells["id"].Value);

    void Reload()
    {
        var q = search.Text.Trim();
        var st = cbStatus.SelectedIndex > 0 ? cbStatus.Text : "";
        long keep = SelectedId();
        grid.DataSource = Db.Query(@"SELECT id, date AS [التاريخ], customer AS [الزبون], phone AS [الهاتف], device AS [الجهاز],
                title AS [التشخيص], top_part AS [الأرجح], confidence AS [الثقة], status AS [الحالة], logs AS [السجلات]
            FROM analyses
            WHERE (@p0='' OR customer LIKE @p1 OR phone LIKE @p1 OR device LIKE @p1 OR product LIKE @p1 OR title LIKE @p1
                   OR top_part LIKE @p1 OR kind LIKE @p1 OR notes LIKE @p1 OR device_key=@p0)
              AND (@p2='' OR status=@p2) AND date BETWEEN @p3 AND @p4
            ORDER BY id DESC LIMIT 2000",
            q, "%" + q + "%", st, from.Value.ToString(Ui.DFmt), to.Value.ToString(Ui.DFmt) + " 23:59:59");
        card.Subtitle = $"{grid.Rows.Count} فحص" + (grid.Rows.Count >= 2000 ? " (أول 2000 — ضيّق البحث)" : "") + "  —  نقر مزدوج أو Enter لفتح الفحص";
        if (keep > 0)
            foreach (DataGridViewRow r in grid.Rows)
                if (Db.L(r.Cells["id"].Value) == keep) { grid.CurrentCell = r.Cells[1]; break; }
    }

    void OpenSelected()
    {
        long id = SelectedId();
        if (id > 0) AnalyzeForm.OpenRecord(id);
    }

    void DeleteSelected()
    {
        long id = SelectedId();
        if (id == 0 || !Session.Guard("history") || !Session.Guard("delete")) return;
        if (!Ui.Confirm($"حذف الفحص رقم {id} نهائيًا؟ (يُحذف معه نص السجلات المحفوظ)")) return;
        var title = Db.S(Db.Scalar("SELECT IFNULL(device,'') || ' — ' || IFNULL(title,'') FROM analyses WHERE id=@p0", id));
        Db.Exec("DELETE FROM analyses WHERE id=@p0", id);
        Db.Audit("حذف فحص", $"رقم {id}: {title}");
        Reload();
        Toast.Show("تم الحذف");
    }
}
