using System.Data;

namespace Kashif;

public enum FType { Text, Memo, Number, Bool, Lookup, Choice, Date, DateTime }

public class Field
{
    public string Name, Caption, Lookup;
    public FType Type = FType.Text;
    public string[] Choices;
    public bool Free;          // قائمة اختيار تقبل الكتابة الحرة
    public object Default;
    public int Width = 400;
}

public class EntityDef
{
    public string Table, Title, ListSql, Perm, Icon = "file-text";
    public string SearchWhere = "[الاسم] LIKE @p0";
    public List<Field> Fields = new();
    /// <summary>قيم ثابتة تُحفظ مع كل سجل جديد (مثل نوع الصندوق)</summary>
    public Dictionary<string, object> Fixed = new();
    /// <summary>بعد الحفظ (مثل: إلغاء «الافتراضي» عن بقية المخازن)</summary>
    public Action<long> AfterSave;
    /// <summary>رسالة التأكيد قبل الحذف (مثل: «الجهاز مرتبط بـ 12 قطعة وستُحذف روابطه») — null للرسالة العامة</summary>
    public Func<long, string> DeleteWarning;
    /// <summary>تحقق قبل الحفظ: يعيد رسالة الخطأ أو null (يُمرَّر له قارئ قيمة الحقل باسمه)</summary>
    public Func<Func<string, object>, string> Check;
    /// <summary>أزرار في كل سطر من الجدول</summary>
    public List<(string Caption, Action<long> Run)> RowActions = new();
}

/// <summary>
/// شاشة عامة للإضافة والتعديل والحذف والبحث — تُبنى من تعريف الكيان.
/// الحقول في جهة (العنوان بجانب الحقل)، والجدول مع البحث في الجهة الأخرى، وأزرار جديد / حفظ / حذف.
/// </summary>
public class CrudForm : BaseForm
{
    const int CaptionW = 140, InputW = 300;
    readonly EntityDef def;
    readonly DataGridView grid = Ui.NewGrid();
    readonly TextBox search = new() { Width = 300, PlaceholderText = "بحث..." };
    readonly Dictionary<string, Control> inputs = new();
    readonly CardPanel card;
    readonly ModernButton bDel;
    long currentId;

    public CrudForm(EntityDef d)
    {
        def = d;
        Text = d.Title;

        // ---------- الحقول ----------
        card = new CardPanel { Dock = DockStyle.Fill, Title = d.Title, Subtitle = "سجل جديد", IconName = d.Icon };
        // الحقول في عمود متجاوب داخل مساحة قابلة للتمرير (تتمدد مع البطاقة ولا تخرج عن حدودها)
        var editor = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface };
        var fields = new FormStack { Dock = DockStyle.Top, Padding = new Padding(0, 4, 4, 8) };
        foreach (var f in def.Fields) fields.Controls.Add(MakeInput(f));
        editor.Controls.Add(fields);

        // أزرار جديد / حفظ / حذف من اليمين، بعرض متساوٍ
        var actions = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Surface };
        var bNew = new ModernButton { Text = "جديد", IconName = "plus", Height = 44 };
        var bSave = new ModernButton { Text = "حفظ", IconName = "save", Height = 44 };
        bDel = new ModernButton { Text = "حذف", IconName = "trash-2", Kind = BtnKind.Coral, Height = 44 };
        var order = new[] { bNew, bSave, bDel };
        actions.Controls.AddRange(order);
        actions.Resize += (s, e) =>
        {
            int w = Math.Min(Dpi.S(150), (actions.ClientSize.Width - Dpi.S(16)) / 3), x = actions.ClientSize.Width;
            foreach (var b in order) { x -= w; b.SetBounds(x, Dpi.S(12), w - Dpi.S(8), Dpi.S(44)); }
        };
        card.Controls.Add(editor);
        card.Controls.Add(actions);

        // ---------- الجدول والبحث ----------
        // الجدول بعرض ما يتبقى بعد بطاقة الحقول (البطاقة بعرض ثابت في الجهة اليمنى)
        var listCard = new CardPanel { Dock = DockStyle.Right, Title = "السجلات", IconName = "list" };
        // سطر البحث: مربع البحث يتمدد، وأزرار الطباعة وExcel تنزل لسطر ثانٍ إذا ضاقت البطاقة
        var sb = new InputBox(search, 320, "search") { Height = 42 };
        var toolHost = new FlowLayoutPanel();
        Ui.GridTools(toolHost, grid, () => def.Title);
        var rowItems = new List<Control> { sb };
        foreach (var mb in toolHost.Controls.OfType<ModernButton>().ToList()) { mb.Height = 42; mb.Margin = new Padding(0); rowItems.Add(mb); }
        var searchCaption = new Label { Text = "البحث", AutoSize = false, Width = 60, Height = 42, Font = Theme.FS(10.5f), ForeColor = Theme.Brand, TextAlign = ContentAlignment.MiddleLeft };
        var top = new FormRow(searchCaption, rowItems) { Dock = DockStyle.Top, Height = 46 };
        listCard.Controls.Add(grid);
        listCard.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Surface });
        listCard.Controls.Add(top);

        Controls.Add(card);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14 });
        Controls.Add(listCard);
        // بطاقة الحقول نحو 40% من العرض (بين حدين)، والجدول الباقي
        Resize += (s, e) => listCard.Width = Math.Max(Dpi.S(300), ClientSize.Width - Math.Clamp(ClientSize.Width * 2 / 5, Dpi.S(340), Dpi.S(540)) - Dpi.S(14));

        Ui.OnTextIdle(search, LoadList);
        bNew.Click += (s, e) => NewRecord();
        bSave.Click += (s, e) => Save();
        bDel.Click += (s, e) => Delete();
        grid.CellClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Tag is int ai)
            {
                long rid = Db.L(grid.Rows[e.RowIndex].Cells["id"].Value);
                def.RowActions[ai].Run(rid);
                LoadList();
                return;
            }
            LoadSelected();
        };
        grid.SelectionChanged += (s, e) => { if (grid.Focused) LoadSelected(); };
        grid.DataBindingComplete += (s, e) => AddActionColumn();

        LoadList();
        NewRecord();
    }

    /// <summary>عند الرجوع للتبويب: تحديث الجدول والقوائم المنسدلة (ماركة أو صنف أُضيف من شاشة أخرى)</summary>
    public override void OnPageActivated()
    {
        foreach (var f in def.Fields.Where(f => f.Type == FType.Lookup))
        {
            var cb = (ComboBox)inputs[f.Name];
            long keep = Ui.GetId(cb);
            Ui.FillCombo(cb, f.Lookup, true);
            Ui.SelectId(cb, keep);
        }
        LoadList();
    }

    void AddActionColumn()
    {
        for (int i = 0; i < def.RowActions.Count; i++)
        {
            var (caption, _) = def.RowActions[i];
            if (grid.Columns.Contains("__action" + i)) continue;
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "__action" + i, Tag = i, HeaderText = caption, Text = caption, UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat, FillWeight = 60, MinimumWidth = Dpi.S(96),
                DefaultCellStyle = { BackColor = Theme.BrandSoft, ForeColor = Theme.BrandDark, SelectionBackColor = Theme.BrandSoft2, SelectionForeColor = Theme.BrandDark }
            });
        }
    }

    static Label Caption(string text) => new()
    {
        Text = text, AutoSize = false, Width = CaptionW, Height = 40, Font = Theme.FS(10), ForeColor = Theme.Ink,
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 4, 0)
    };

    Control MakeInput(Field f)
    {
        Control c;
        switch (f.Type)
        {
            case FType.Memo: c = new TextBox { Multiline = true, Height = 76, ScrollBars = ScrollBars.Vertical }; break;
            case FType.Number: c = Ui.Num(200, 2); break;
            case FType.Bool: c = new Toggle { Text = f.Caption, Height = 38 }; break;
            case FType.Lookup:
                {
                    var cb = Ui.Combo();
                    Ui.FillCombo(cb, f.Lookup, true);
                    c = cb; break;
                }
            case FType.Choice:
                {
                    var cb = Ui.Combo();
                    if (f.Free) cb.DropDownStyle = ComboBoxStyle.DropDown;
                    cb.Items.AddRange(f.Choices);
                    c = cb; break;
                }
            case FType.Date: c = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true }; break;
            case FType.DateTime: c = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm" }; break;
            default: c = new TextBox(); break;
        }
        c.Width = f.Type == FType.Bool ? CaptionW + InputW : InputW;
        inputs[f.Name] = c;
        if (f.Type == FType.Bool) { c.Margin = new Padding(8, 6, 6, 4); return c; }
        var field = Ui.Wrap(c);
        field.Margin = new Padding(0);
        return new FormRow(Caption(f.Caption), new[] { field }) { Margin = new Padding(0, 3, 0, 3) };
    }

    object GetValue(Field f)
    {
        var c = inputs[f.Name];
        switch (f.Type)
        {
            case FType.Number: return (double)((NumericUpDown)c).Value;
            case FType.Bool: return ((CheckBox)c).Checked ? 1 : 0;
            case FType.Lookup: return Db.N(Ui.GetId((ComboBox)c));
            case FType.Choice: return ((ComboBox)c).Text;
            case FType.Date:
                {
                    var d = (DateTimePicker)c;
                    return d.Checked ? (object)d.Value.ToString(Ui.DFmt) : DBNull.Value;
                }
            case FType.DateTime: return ((DateTimePicker)c).Value.ToString(Ui.TFmt);
            default: return c.Text.Trim();
        }
    }

    void SetValue(Field f, object v)
    {
        var c = inputs[f.Name];
        bool isNull = v == null || v is DBNull;
        switch (f.Type)
        {
            case FType.Number: Ui.SetNum((NumericUpDown)c, isNull ? 0 : Db.D(v)); break;
            case FType.Bool: ((CheckBox)c).Checked = !isNull && Db.L(v) == 1; break;
            case FType.Lookup: Ui.SelectId((ComboBox)c, isNull ? 0 : Db.L(v)); break;
            case FType.Choice:
                {
                    var cb = (ComboBox)c;
                    var s = Db.S(v);
                    if (cb.DropDownStyle == ComboBoxStyle.DropDown) cb.Text = s == "" && cb.Items.Count > 0 ? cb.Items[0].ToString() : s;
                    else
                    {
                        int i = cb.Items.IndexOf(s);
                        cb.SelectedIndex = i >= 0 ? i : (cb.Items.Count > 0 ? 0 : -1);
                    }
                    break;
                }
            case FType.Date:
                {
                    var d = (DateTimePicker)c;
                    if (!isNull && DateTime.TryParse(Db.S(v), out var dt)) { d.Value = dt; d.Checked = true; }
                    else { d.Value = DateTime.Today; d.Checked = false; }
                    break;
                }
            case FType.DateTime:
                {
                    var d = (DateTimePicker)c;
                    d.Value = !isNull && DateTime.TryParse(Db.S(v), out var dt) ? dt : DateTime.Now.AddMinutes(5);
                    break;
                }
            default: c.Text = Db.S(v); break;
        }
    }

    void LoadList()
    {
        var q = search.Text.Trim();
        long keep = currentId;
        grid.DataSource = q == ""
            ? Db.Query($"SELECT * FROM ({def.ListSql}) ORDER BY id DESC")
            : Db.Query($"SELECT * FROM ({def.ListSql}) WHERE {def.SearchWhere} ORDER BY id DESC", "%" + q + "%");
        card.Subtitle = currentId > 0 ? $"تعديل السجل رقم {currentId}" : "سجل جديد";
        if (keep > 0)
            foreach (DataGridViewRow r in grid.Rows)
                if (Db.L(r.Cells["id"].Value) == keep) { r.Selected = true; break; }
    }

    void NewRecord()
    {
        currentId = 0;
        foreach (var f in def.Fields) SetValue(f, f.Default);
        card.Subtitle = "سجل جديد";
        bDel.Enabled = false;
        inputs[def.Fields[0].Name].Focus();
    }

    void LoadSelected()
    {
        if (grid.CurrentRow == null) return;
        long id = Db.L(grid.CurrentRow.Cells["id"].Value);
        var dt = Db.Query($"SELECT * FROM {def.Table} WHERE id=@p0", id);
        if (dt.Rows.Count == 0) return;
        currentId = id;
        foreach (var f in def.Fields) SetValue(f, dt.Rows[0][f.Name]);
        card.Subtitle = $"تعديل السجل رقم {id}";
        bDel.Enabled = true;
    }

    void Save()
    {
        if (!Session.Guard(def.Perm)) return;
        var first = def.Fields[0];
        var required = def.Fields.FirstOrDefault(f => f.Type == FType.Text && f.Caption.StartsWith("*")) ?? first;
        if (GetValue(required) is string s0 && s0 == "") { Ui.Warn($"يرجى إدخال: {required.Caption.TrimStart('*', ' ')}"); inputs[required.Name].Focus(); return; }

        if (def.Check?.Invoke(name => GetValue(def.Fields.First(f => f.Name == name))) is string err) { Ui.Warn(err); return; }

        var cols = def.Fields.Select(f => f.Name).ToList();
        var vals = def.Fields.Select(GetValue).ToList();
        if (currentId == 0)
        {
            foreach (var (k, v) in def.Fixed) { cols.Add(k); vals.Add(v); }
            var sql = $"INSERT INTO {def.Table}({string.Join(",", cols)}) VALUES({string.Join(",", cols.Select((c, i) => "@p" + i))})";
            currentId = Db.Insert(sql, vals.ToArray());
        }
        else
        {
            var sql = $"UPDATE {def.Table} SET {string.Join(",", cols.Select((c, i) => $"{c}=@p{i}"))} WHERE id=@p{cols.Count}";
            vals.Add(currentId);
            Db.Exec(sql, vals.ToArray());
        }
        def.AfterSave?.Invoke(currentId);
        bDel.Enabled = true;
        LoadList();
        Toast.Show("تم الحفظ بنجاح");
    }

    void Delete()
    {
        if (currentId == 0 || !Session.Guard(def.Perm) || !Session.Guard("delete")) return;
        if (!Ui.Confirm(def.DeleteWarning?.Invoke(currentId) ?? "هل تريد حذف السجل المحدد؟")) return;
        try
        {
            var name = Db.S(Db.Scalar($"SELECT name FROM {def.Table} WHERE id=@p0", currentId));
            Db.Exec($"DELETE FROM {def.Table} WHERE id=@p0", currentId);
            Db.Audit("حذف من " + def.Title, name);
            def.AfterSave?.Invoke(0);
            currentId = 0;
            LoadList();
            NewRecord();
            Toast.Show("تم الحذف");
        }
        catch { Ui.Warn("لا يمكن حذف هذا السجل لأنه مستخدم في سجلات أخرى."); }
    }
}

/// <summary>تعريفات الشاشات العامة</summary>
public static class Defs
{
    static Field F(string name, string caption, FType type = FType.Text, object def = null) =>
        new() { Name = name, Caption = caption, Type = type, Default = def };
    static Field C(string name, string caption, bool free, params string[] choices) =>
        new() { Name = name, Caption = caption, Type = FType.Choice, Choices = choices, Free = free };

    /// <summary>أسماء القطع المعروفة (يمكن كتابة غيرها)</summary>
    static readonly string[] KnownParts =
    {
        Parts.ChargingFlex, Parts.Battery, Parts.BatteryConn, Parts.PowerFlex, Parts.FrontFlex, Parts.Screen, Parts.Camera,
        Parts.Biometric, Parts.TouchId, Parts.ChargeIc, Parts.AudioIc, Parts.AudioParts, Parts.Wifi, Parts.Baseband, Parts.Nand,
        Parts.Pmu, Parts.SmcLine, Parts.Board, Parts.Liquid, Parts.Ios,
    };

    /// <summary>خبرة المحل: ما تأكدت منه بنفسك يظهر في التحليلات القادمة (بدرجة «مؤكد» يصبح الأرجح)</summary>
    public static EntityDef KbRules() => new()
    {
        Table = "kb_rules", Title = "خبرة المحل", Perm = "kb", Icon = "lightbulb",
        ListSql = @"SELECT id, name AS [الوصف], pattern AS [النص في السجل], IFNULL(device,'') AS [الجهاز], part AS [القطعة / السبب], level AS [الدرجة],
            CASE active WHEN 1 THEN 'فعّالة' ELSE 'متوقفة' END AS [الحالة] FROM kb_rules",
        SearchWhere = "[الوصف] LIKE @p0 OR [النص في السجل] LIKE @p0 OR [القطعة / السبب] LIKE @p0 OR [الجهاز] LIKE @p0",
        Fields =
        {
            F("name", "* الوصف"),
            F("pattern", "* النص في السجل أو رمز الحساس"),
            F("device", "الجهاز (اختياري)"),
            C("part", "* القطعة أو السبب", true, KnownParts),
            C("level", "الدرجة", false, CustomRule.Levels).With("شائع"),
            F("note", "ملاحظة", FType.Memo),
            F("active", "القاعدة فعّالة", FType.Bool, 1L),
        },
        Check = get =>
        {
            if (Db.S(get("pattern")).Trim().Length < 3) return "اكتب نصًا من السجل (3 أحرف على الأقل) أو رمز الحساس، مثل Prs0 أو SMC PANIC.";
            if (Db.S(get("part")).Trim() == "") return "اختر القطعة أو السبب.";
            return null;
        },
        AfterSave = id => { if (id > 0) Db.Audit("خبرة المحل", Db.S(Db.Scalar("SELECT name || ': ' || pattern || ' ← ' || part FROM kb_rules WHERE id=@p0", id))); },
    };

    static Field With(this Field f, object def) { f.Default = def; return f; }
}
