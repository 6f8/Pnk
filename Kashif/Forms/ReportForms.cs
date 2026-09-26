using System.Data;

namespace Kashif;

/// <summary>
/// التقارير: الأعطال الأكثر تكرارًا، حسب نوع البانك، حسب الجهاز، سجل العمليات
/// — فترة زمنية، بحث داخل النتائج، طباعة وExcel.
/// </summary>
public class ReportsForm : BaseForm
{
    readonly string kind;
    readonly DataGridView grid = Ui.NewGrid();
    readonly DateTimePicker from = new() { Format = DateTimePickerFormat.Short, Width = 150 }, to = new() { Format = DateTimePickerFormat.Short, Width = 150 };
    readonly TextBox filter = new() { Width = 230, PlaceholderText = "بحث في النتائج" };
    readonly CardPanel card;

    public ReportsForm(string kind)
    {
        this.kind = kind;
        Text = kind;
        from.Value = DateTime.Today.AddDays(kind == "سجل العمليات" ? -30 : -90);
        to.Value = DateTime.Today;

        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("من", from));
        bar.Controls.Add(Ui.Labeled("إلى", to));
        bar.Controls.Add(Ui.SearchBox(filter));
        var bShow = Theme.Btn("عرض", Theme.Success, 100, "refresh-cw");
        bar.Controls.Add(bShow);
        Ui.GridTools(bar, grid, () => kind, Period);
        foreach (var b in bar.Controls.OfType<ModernButton>()) b.Margin = new Padding(4, 24, 4, 3);

        card = new CardPanel { Dock = DockStyle.Fill, Title = kind, IconName = "chart-column" };
        card.Controls.Add(grid);
        Controls.Add(card);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4 });
        Controls.Add(bar);

        bShow.Click += (s, e) => Reload();
        Ui.OnTextIdle(filter, ApplyFilter, 200);
        grid.CellDoubleClick += (s, e) => Drill(e.RowIndex);
        Reload();
    }

    public override void OnPageActivated() => Reload();

    string Period() => $"من {from.Value:yyyy-MM-dd} إلى {to.Value:yyyy-MM-dd}";

    void Reload()
    {
        grid.DataSource = Reports.Run(kind, from.Value, to.Value);
        card.Subtitle = Reports.Subtitle(kind) + (kind == "سجل العمليات" ? "" : "  —  نقر مزدوج لعرض فحوصاته");
        ApplyFilter();
    }

    /// <summary>البحث داخل النتائج: كل الأعمدة النصية</summary>
    void ApplyFilter()
    {
        if (grid.DataSource is not DataTable dt) return;
        var q = filter.Text.Trim().Replace("'", "''").Replace("[", "[[]").Replace("*", "[*]").Replace("%", "[%]");
        var textCols = dt.Columns.Cast<DataColumn>().Where(c => c.DataType == typeof(string)).Select(c => $"[{c.ColumnName}] LIKE '%{q}%'").ToList();
        dt.DefaultView.RowFilter = q == "" || textCols.Count == 0 ? "" : string.Join(" OR ", textCols);
    }

    /// <summary>نقر مزدوج: إضافة قاعدة مقترحة إلى خبرة المحل، أو فتح سجل الفحوصات مفلترًا بالقطعة أو النوع أو الجهاز</summary>
    void Drill(int row)
    {
        if (row < 0 || kind == "سجل العمليات" || grid.Columns.Count < 2) return;
        if (kind == "قواعد مقترحة") { AddSuggested(row); return; }
        var value = Db.S(grid.Rows[row].Cells[1].Value);
        if (value != "" && value != "الكل") HistoryForm.OpenFiltered(value);
    }

    void AddSuggested(int row)
    {
        if (!Session.Guard("kb")) return;
        string Cell(string c) => grid.Columns.Contains(c) ? Db.S(grid.Rows[row].Cells[c].Value) : "";
        string pattern = Cell("النمط"), device = Cell("الجهاز"), part = Cell("القطعة المُصلِحة"), times = Cell("مرات");
        if (pattern == "" || part == "") return;
        if (!Ui.Confirm($"إضافة قاعدة إلى خبرة المحل؟\n\n«{pattern}»" + (device != "" ? $" على {device}" : "") + $" ← {part}\n(تكرر {times} مرات في فحوصات المحل)")) return;
        Db.Insert("INSERT INTO kb_rules(name, pattern, device, part, level, note, active, is_regex, priority) VALUES(@p0,@p1,@p2,@p3,'شائع',@p4,1,0,0)",
            "مقترحة من النتائج", pattern, device, part, $"تكررت {times} مرات مع نفس القطعة المُصلِحة");
        Db.Audit("خبرة المحل", $"قاعدة مقترحة: {pattern} ← {part}");
        Reload();
        Toast.Show("أُضيفت القاعدة — عدّل درجتها من «خبرة المحل» إن أردت");
    }
}
