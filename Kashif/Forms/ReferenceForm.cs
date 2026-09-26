using System.Data;

namespace Kashif;

/// <summary>
/// دليل رموز البانك: كل ما يعرفه البرنامج — رموز الحساسات، مفاتيح SMC، خدمات النظام، أنواع البانك، وخبرة المحل.
/// اكتب رمزًا أو كلمة من السجل (مثل Prs0 أو TG0B أو thermalmonitord أو SMC) لترى معناه والقطع المرتبطة به.
/// </summary>
public class ReferenceForm : BaseForm
{
    public const string PageTitle = "دليل رموز البانك";

    sealed record Entry(string Type, string Code, string What, Func<string, List<PanicKnowledge.Choice>> Choices, string Note, string[] Steps, string Source = "", string Level = "");

    readonly TextBox search = new() { Width = 420, PlaceholderText = "رمز أو كلمة من السجل — مثل Prs0 أو TG0B أو thermalmonitord أو SMC" };
    readonly ComboBox cbModel = Ui.Combo(260);
    readonly DataGridView list = Ui.NewGrid(), parts = Ui.NewGrid();
    readonly CardPanel listCard, detailCard;
    readonly Label note = new() { Dock = DockStyle.Top, AutoSize = false, Height = 64, ForeColor = Theme.Text2, Font = Theme.F(10), BackColor = Theme.Surface, TextAlign = ContentAlignment.TopLeft };
    readonly Label stepsText = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 110, ForeColor = Theme.Text2, Font = Theme.F(9.5f), BackColor = Theme.Surface, TextAlign = ContentAlignment.TopLeft };
    List<Entry> all = new(), shown = new();

    public ReferenceForm()
    {
        Text = PageTitle;
        // الموديل يحدد أماكن الحساسات: معلومة الموديل إن وُثّقت، وإلا جيله، وإلا الأماكن العامة
        cbModel.Items.Add(new Opt { Name = "عام (بلا موديل محدد)", Tag = "" });
        foreach (var (product, model) in AppleDevices.All.GroupBy(x => x.Model.Name).Select(g => g.First()).OrderBy(x => x.Model.Family == AppleDevices.Family.IPad).ThenBy(x => x.Product, StringComparer.Ordinal))
            cbModel.Items.Add(new Opt { Name = model.Name, Tag = product });
        cbModel.SelectedIndex = 0;
        Ui.MakeSearchable(cbModel);

        var bar = Theme.Bar();
        bar.Controls.Add(new InputBox(search, 440, "search") { Height = 42, Margin = new Padding(6, 2, 6, 2) });
        bar.Controls.Add(new InputBox(cbModel, 270) { Height = 42, Margin = new Padding(6, 2, 6, 2) });

        listCard = new CardPanel { Dock = DockStyle.Fill, Title = "الرموز", IconName = "book-open", Margin = new Padding(14, 0, 0, 0) };
        listCard.Controls.Add(list);

        detailCard = new CardPanel { Dock = DockStyle.Fill, Title = "اختر رمزًا من القائمة", IconName = "info" };
        detailCard.Controls.Add(parts);
        detailCard.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Theme.Surface });
        detailCard.Controls.Add(note);
        detailCard.Controls.Add(stepsText);

        foreach (var g in new[] { list, parts })
        {
            g.ScrollBars = ScrollBars.Vertical;
            g.ShowCellToolTips = true;
        }
        parts.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        parts.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        body.Controls.Add(listCard, 0, 0);
        body.Controls.Add(detailCard, 1, 0);
        Controls.Add(body);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4 });
        Controls.Add(bar);

        Ui.OnTextIdle(search, Filter, 250);
        cbModel.SelectedIndexChanged += (s, e) => ShowSelected();
        list.SelectionChanged += (s, e) => ShowSelected();
        Build();
        Filter();
        Shown += (s, e) => search.Focus();
    }

    public override void OnPageActivated() { Build(); Filter(); }

    string Product => cbModel.SelectedItem is Opt { Tag: string p } ? p : "";

    void Build()
    {
        var e = new List<Entry>();
        foreach (var s in PanicKnowledge.Sensors)
            e.Add(new("حساس", s.Code, s.What, p => s.LocateFor(p, AppleDevices.FamilyOf(p)).Choices.ToList(), s.Note,
                new[] { "افحص القطعة الأرجح وموصلها.", "شغّل الجهاز والقطعة مفصولة: يظهر نفس الرمز.", "ركّب قطعة سليمة: إن اختفى البانك فهي السبب.", "إن بقي: خط الحساس على البوردة." }, s.Source, s.Level));
        foreach (var s in PanicKnowledge.Services)
            e.Add(new("خدمة", s.Name, s.What, p => s.Choices.ToList(), $"يظهر في سطر «no successful checkins from {s.Name}». درجة الثقة: {s.Confidence}.", s.Steps, s.Source, s.Level));
        foreach (var s in PanicKnowledge.Signatures)
            e.Add(new("نوع بانك", s.Kind, s.Title, p => s.Choices.ToList(), s.Explain, s.Steps, s.Source, s.Level));
        foreach (var k in PanicKnowledge.Current.SmcKeys.Values)
            e.Add(new("مفتاح SMC", k.Key, k.Meaning, p => k.Battery
                    ? new List<PanicKnowledge.Choice> { new(Parts.Battery, 75, "مفتاح بطارية"), new(Parts.BatteryConn, 65, "الموصل أو الفلاتة"), new(Parts.SmcLine, 42, "خط الاتصال على البوردة") }
                    : new List<PanicKnowledge.Choice>(),
                "يظهر مشفّرًا في رسائل Mailbox داخل بانك SMC: أول 4 بايت من القيمة ‎0x…‎ حروف مقروءة. البرنامج يفكّها تلقائيًا.", Array.Empty<string>(), k.Source, "مؤكد"));
        e.Add(new("مفتاح SMC", "B…", "أي مفتاح يبدأ بحرف B وليس في القائمة", p => new List<PanicKnowledge.Choice> { new(Parts.Battery, 58, "دليل ضعيف: الحرف B للبطارية في تسمية Apple، لكن المفتاح غير موثّق بالاسم") },
            "حسب تسمية Apple: B بطارية، T حرارة، V جهد، I تيار، P طاقة. المفاتيح غير الموثّقة بالاسم تُعامل كدليل ضعيف.", Array.Empty<string>(), "تسمية Apple لمفاتيح SMC", "محتمل"));
        if (Session.Can("kb") || Session.Can("analyze"))
            foreach (var r in PanicStore.Rules())
                e.Add(new("خبرة المحل", r.Pattern, r.Name, p => new List<PanicKnowledge.Choice> { new(r.Part, r.Score, (r.Device != "" ? $"({r.Device}) " : "") + r.Note) },
                    $"من «خبرة المحل»" + (r.IsRegex ? " (تعبير منتظم)" : "") + (r.Priority != 0 ? $" — أولوية {r.Priority}" : "") + (r.Device != "" ? $" — للجهاز: {r.Device}" : ""),
                    Array.Empty<string>(), "خبرة المحل", r.Level));
        all = e;
    }

    void Filter()
    {
        var q = search.Text.Trim();
        shown = q == "" ? all : all.Where(x => x.Code.Contains(q, StringComparison.OrdinalIgnoreCase) || x.What.Contains(q, StringComparison.OrdinalIgnoreCase)
                                             || x.Type.Contains(q) || x.Note.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(long));
        dt.Columns.Add("النوع"); dt.Columns.Add("الرمز"); dt.Columns.Add("المعنى");
        for (int i = 0; i < shown.Count; i++) dt.Rows.Add((long)i, shown[i].Type, shown[i].Code, shown[i].What);
        list.DataSource = dt;
        listCard.Subtitle = shown.Count == 0 ? "لا يوجد رمز مطابق — أضف ما تعرفه في «خبرة المحل»" : $"{shown.Count} رمز";
        if (list.Rows.Count > 0) list.CurrentCell = list.Rows[0].Cells["الرمز"];
        ShowSelected();
    }

    void ShowSelected()
    {
        var i = list.CurrentRow != null && list.Columns.Contains("id") ? (int)Db.L(list.CurrentRow.Cells["id"].Value) : -1;
        if (i < 0 || i >= shown.Count)
        {
            detailCard.Title = "اختر رمزًا من القائمة"; detailCard.Subtitle = "";
            note.Text = ""; stepsText.Text = ""; parts.DataSource = null;
            return;
        }
        var x = shown[i];
        detailCard.Title = Theme.Bidi($"{x.Code} — {x.What}");
        detailCard.Subtitle = string.Join("  —  ", new[]
        {
            x.Type, x.Level != "" ? "الدرجة: " + x.Level : "", x.Source != "" ? "المصدر: " + x.Source : "",
            x.Type == "حساس" ? "الموديل: " + cbModel.Text : "",
        }.Where(t => t != ""));
        note.Text = x.Note;
        stepsText.Text = x.Steps.Length == 0 ? "" : "خطوات الفحص:\n" + string.Join("\n", x.Steps.Select((s, k) => $"{k + 1}. {s}"));
        var dt = new DataTable();
        foreach (var c in new[] { "القطعة / السبب", "الحالة", "لماذا" }) dt.Columns.Add(c);
        var choices = x.Choices(Product).OrderByDescending(c => c.Score).ToList();
        for (int k = 0; k < choices.Count; k++)
            dt.Rows.Add(choices[k].Part, PanicAnalyzer.LabelOf(choices[k].Score, k == 0 && (choices.Count == 1 || choices[0].Score - choices[1].Score >= 10)), choices[k].Why);
        parts.DataSource = dt;
    }
}
