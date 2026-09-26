using System.Data;

namespace Kashif;

/// <summary>
/// تحليل البانك — الشاشة الرئيسية للبرنامج (F2):
/// افتح ملفات panic-full أو الصق النص (حتى المنسوخ من صورة)، فيظهر التشخيص والأسباب مرتبة وخطوات الفحص
/// والأدلة من السجل نفسه. عدة سجلات لنفس الجهاز تُحلَّل معًا (التكرار يرفع الثقة، والاختلاف يكشف السبب العام).
/// </summary>
public class AnalyzeForm : BaseForm
{
    public const string PageTitle = "تحليل البانك";
    const long MaxFileBytes = 20 * 1024 * 1024;

    readonly DataGridView logsGrid = Ui.NewGrid(), causes = Ui.NewGrid(), steps = Ui.NewGrid(), evidence = Ui.NewGrid(), previous = Ui.NewGrid();
    readonly TextBox raw = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None, RightToLeft = RightToLeft.No, BackColor = Theme.SurfaceAlt, ForeColor = Theme.Ink, MaxLength = 0,
    };
    readonly TextBox report = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        BackColor = Theme.Surface, ForeColor = Theme.Ink,
    };
    readonly TextBox customer = new() { Width = 300, PlaceholderText = "اسم الزبون" };
    readonly TextBox phone = new() { Width = 200, PlaceholderText = "07xx xxx xxxx" };
    readonly TextBox notes = new() { Width = 520, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "ما وجدته عند الفحص، القطعة التي بُدّلت، النتيجة..." };
    readonly ComboBox status = Ui.Combo(200);
    readonly Toggle tLiquid = new() { Text = "تعرض لسوائل", Width = 150 }, tBattery = new() { Text = "بطارية مستبدلة", Width = 160 },
        tFlex = new() { Text = "فلاتة شحن مستبدلة", Width = 180 }, tScreen = new() { Text = "شاشة مستبدلة", Width = 150 }, tDrop = new() { Text = "سقوط أو ضربة", Width = 150 };
    readonly Toggle tCombine = new() { Text = "تجميع سجلات الجهاز", Width = 190 };
    readonly VerdictPanel verdict = new() { Dock = DockStyle.Top, Height = 196 };
    readonly CardPanel logsCard, rawCard;
    readonly ModernTabs tabs = new() { Dock = DockStyle.Fill };
    readonly ModernButton bSave, bCopy, bPrint, bRemove;
    readonly Label lblPrev = new() { Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Muted, Font = Theme.F(9.5f), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface };

    readonly List<PanicLog> logs = new();
    readonly List<Diagnosis> diags = new();
    Diagnosis shown;
    List<PanicLog> shownLogs = new();
    long recordId;
    bool dirty, loading;

    // ---------- فتح الشاشة من أماكن أخرى ----------
    public static void OpenRecord(long id)
    {
        var rec = PanicStore.Load(id);
        if (rec == null) { Ui.Warn("السجل غير موجود (ربما حُذف)."); return; }
        Ui.OpenPage($"فحص رقم {id}", new AnalyzeForm(rec));
    }

    /// <summary>ملفات من خارج الشاشة (الرئيسية، السحب إلى الأيقونة، «فتح باستخدام») — تُضاف إلى شاشة التحليل المفتوحة إن وُجدت</summary>
    public static void OpenFiles(IEnumerable<string> files) => Target()?.AddFiles(files.ToList());

    public static void OpenText(string text) => Target()?.AddText(text, "نص ملصوق");

    /// <summary>شاشة التحليل المفتوحة (بلا استبدال عملها غير المحفوظ)، أو شاشة جديدة</summary>
    static AnalyzeForm Target()
    {
        if (MainForm.Instance?.Find(PageTitle) is AnalyzeForm existing && !existing.IsDisposed)
        {
            MainForm.Instance.Go(PageTitle);
            return existing;
        }
        var f = new AnalyzeForm();
        Ui.OpenPage(PageTitle, f);
        return f.IsDisposed ? null : f;
    }

    public AnalyzeForm() : this(null) { }

    AnalyzeForm(PanicStore.Record rec)
    {
        Text = rec == null ? PageTitle : $"فحص رقم {rec.Id}";
        KeyPreview = true;
        AllowDrop = true;
        tCombine.Checked = Settings.On("analyze_combine");
        status.Items.AddRange(PanicStore.Statuses);
        status.SelectedIndex = 0;
        try { raw.Font = new Font("Consolas", 9.5f); } catch { raw.Font = Theme.F(9); }
        report.Font = Theme.F(10);

        // ---------- شريط الأدوات ----------
        var bar = Theme.Bar();
        var bOpen = Theme.Btn("فتح ملفات", Theme.Brand, 130, "folder-open");
        var bPaste = Theme.Btn("لصق", Theme.Brand, 90, "clipboard-list");
        var bAnalyze = Theme.Btn("تحليل النص", Theme.Success, 130, "scan-line");
        var bClear = Theme.Btn("مسح الكل", Theme.Gray, 110, "x");
        bSave = Theme.Btn("حفظ في السجل", Theme.Success, 140, "save");
        bCopy = Theme.Btn("نسخ التقرير", Theme.Gray, 125, "copy");
        bPrint = Theme.Btn("طباعة", Theme.Gray, 100, "printer");
        tCombine.Margin = new Padding(12, 8, 6, 2);
        bar.Controls.AddRange(new Control[] { bOpen, bPaste, bAnalyze, bClear, tCombine, bSave, bCopy, bPrint });
        foreach (var b in bar.Controls.OfType<ModernButton>()) { b.Height = 40; b.Margin = new Padding(4, 3, 4, 3); }

        // ---------- الجهة الأولى: السجلات ونصها وما حدث للجهاز ----------
        logsCard = new CardPanel { Dock = DockStyle.Top, Height = 250, Title = "السجلات", IconName = "file-text", Subtitle = "افتح ملفات panic-full أو الصق النص — أو اسحب الملفات إلى هنا" };
        bRemove = new ModernButton { Text = "إزالة المحدد", IconName = "trash-2", Kind = BtnKind.Secondary, Height = 34, Dock = DockStyle.Bottom };
        logsCard.Controls.Add(logsGrid);
        logsCard.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 6, BackColor = Theme.Surface });
        logsCard.Controls.Add(bRemove);

        rawCard = new CardPanel { Dock = DockStyle.Fill, Title = "نص السجل", IconName = "scroll-text", Subtitle = "يمكنك تعديل النص ثم «تحليل النص»" };
        var rawHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Padding = new Padding(6) };
        rawHost.Controls.Add(raw);
        rawCard.Controls.Add(rawHost);

        var flagsCard = new CardPanel { Dock = DockStyle.Bottom, Height = 150, Title = "ما حدث للجهاز", IconName = "wrench", Subtitle = "يغيّر ترتيب الأسباب" };
        var flagsFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, WrapContents = true };
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) { t.Margin = new Padding(4, 2, 10, 2); flagsFlow.Controls.Add(t); }
        flagsCard.Controls.Add(flagsFlow);

        var input = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(14, 0, 0, 0) };
        input.Controls.Add(rawCard);
        input.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Bg });
        input.Controls.Add(logsCard);
        input.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 12, BackColor = Theme.Bg });
        input.Controls.Add(flagsCard);

        // ---------- الجهة الثانية: النتيجة ----------
        FitColumns(logsGrid, ("#", 8), ("المصدر", 30), ("الجهاز", 26), ("النوع", 20), ("الأرجح", 30));
        FitColumns(causes, ("#", 6), ("السبب / القطعة", 34), ("الحالة", 14), ("لماذا", 46));
        FitColumns(steps, ("#", 6), ("الخطوة", 94));
        FitColumns(evidence, ("الدليل", 20), ("القيمة", 34), ("المعنى", 46));
        FitColumns(previous, ("التاريخ", 22), ("التشخيص", 36), ("الأرجح", 26), ("الحالة", 16));
        foreach (var g in new[] { causes, steps, evidence })
        {
            g.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        }

        tabs.Add("الأسباب مرتبة", Wrap(causes), "list-ordered", "الأسباب");
        tabs.Add("خطوات الفحص", Wrap(steps), "list-checks", "الخطوات");
        tabs.Add("الأدلة من السجل", Wrap(evidence), "search", "الأدلة");
        tabs.Add("التقرير", Wrap(report), "file-text");
        tabs.Add("الزبون والحفظ", CustomerPage(), "user", "الزبون");
        var prevPage = Wrap(previous);
        prevPage.Controls.Add(lblPrev);
        tabs.Add("فحوصات سابقة للجهاز", prevPage, "history", "السابقة");

        var result = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        result.Controls.Add(tabs);
        result.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Bg });
        result.Controls.Add(verdict);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        body.Controls.Add(input, 0, 0);
        body.Controls.Add(result, 1, 0);

        Controls.Add(body);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        Controls.Add(bar);

        // ---------- الأحداث ----------
        bOpen.Click += (s, e) => PickFiles();
        bPaste.Click += (s, e) => PasteClipboard();
        bAnalyze.Click += (s, e) => AnalyzeRawText();
        bClear.Click += (s, e) => ClearAll(ask: true);
        bSave.Click += (s, e) => SaveRecord(silent: false);
        bCopy.Click += (s, e) => CopyReport();
        bPrint.Click += (s, e) => PrintReport();
        bRemove.Click += (s, e) => RemoveSelected();
        tCombine.CheckedChanged += (s, e) => ShowResult();
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) t.CheckedChanged += (s, e) => { if (!loading) { dirty = true; Reanalyze(); } };
        foreach (var c in new Control[] { customer, phone, notes }) c.TextChanged += (s, e) => { if (!loading) dirty = true; };
        status.SelectedIndexChanged += (s, e) => { if (!loading) dirty = true; };
        logsGrid.SelectionChanged += (s, e) => { if (!loading) ShowResult(); };
        previous.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenRecord(Db.L(previous.Rows[e.RowIndex].Cells["id"].Value)); };
        DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (s, e) => Drop(e.Data);
        raw.AllowDrop = true;
        raw.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        raw.DragDrop += (s, e) => Drop(e.Data);

        // السجل المحفوظ يُحمَّل عند ظهور الشاشة (بعد ربط الجداول بالنافذة)
        if (rec != null) Load += (s, e) => LoadRecord(rec);
        ShowResult();
        UpdateButtons();
    }

    static Panel Wrap(Control c)
    {
        var p = new Panel { BackColor = Theme.Surface, Padding = new Padding(8) };
        c.Dock = DockStyle.Fill;
        p.Controls.Add(c);
        return p;
    }

    Control CustomerPage()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, AutoScroll = true, Padding = new Padding(4, 8, 4, 4) };
        flow.Controls.Add(Ui.Labeled("الزبون", customer));
        flow.Controls.Add(Ui.Labeled("الهاتف", phone));
        flow.Controls.Add(Ui.Labeled("حالة الجهاز", status));
        flow.SetFlowBreak(flow.Controls[^1], true);
        flow.Controls.Add(Ui.Labeled("ملاحظات الفحص", notes));
        flow.SetFlowBreak(flow.Controls[^1], true);
        var b = Theme.Btn("حفظ في السجل", Theme.Success, 150, "save");
        b.Margin = new Padding(6, 12, 6, 4);
        b.Click += (s, e) => SaveRecord(silent: false);
        flow.Controls.Add(b);
        flow.Controls.Add(new Label
        {
            Text = "يُحفظ نص السجلات الأصلي كما هو: عند فتح الفحص لاحقًا يُعاد تحليله بأحدث قاعدة معرفة وخبرة المحل.",
            AutoSize = false, Width = 520, Height = 44, ForeColor = Theme.Muted, Font = Theme.F(9), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 10, 6, 0),
        });
        return flow;
    }

    /// <summary>عرض نسبي للأعمدة يُطبَّق بعد كل ربط (مثل شاشة البحث في توافق)</summary>
    static void FitColumns(DataGridView g, params (string Name, float Weight)[] cols)
    {
        g.ScrollBars = ScrollBars.Vertical;
        g.ShowCellToolTips = true;
        g.DataBindingComplete += (s, e) =>
        {
            foreach (var (name, w) in cols)
                if (g.Columns.Contains(name))
                {
                    var c = g.Columns[name];
                    c.MinimumWidth = Dpi.S(name == "#" ? 36 : 60);
                    c.FillWeight = w;
                }
        };
    }

    // ============================================================== الإدخال
    void PickFiles()
    {
        if (!Session.Guard("analyze")) return;
        using var ofd = new OpenFileDialog
        {
            Multiselect = true, Title = "اختر ملفات البانك",
            Filter = "سجلات البانك (*.ips;*.txt;*.json;*.log;*.panic)|*.ips;*.txt;*.json;*.log;*.panic|كل الملفات (*.*)|*.*",
        };
        if (ofd.ShowDialog() == DialogResult.OK) AddFiles(ofd.FileNames);
    }

    public void AddFiles(IEnumerable<string> files)
    {
        var skipped = new List<string>();
        int before = logs.Count;
        foreach (var f in files)
        {
            try
            {
                var info = new FileInfo(f);
                if (!info.Exists) continue;
                if (info.Length > MaxFileBytes) { skipped.Add($"{info.Name}: حجمه أكبر من 20 ميغابايت"); continue; }
                var found = PanicParser.ParseMany(File.ReadAllText(f, System.Text.Encoding.UTF8), info.Name);
                if (found.Count == 0) skipped.Add($"{info.Name}: لا يحتوي سجل بانك");
                logs.AddRange(found);
            }
            catch (Exception ex) { skipped.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        AfterAdd(before);
        if (skipped.Count > 0) Dialogs.Message("لم تُقرأ بعض الملفات:\n" + string.Join("\n", skipped.Take(12)), "فتح الملفات", Tone.Warning);
    }

    public void AddText(string text, string source)
    {
        var found = PanicParser.ParseMany(text ?? "", source);
        if (found.Count == 0)
        {
            Ui.Warn("لم يُعثر على سجل بانك في النص.\nانسخ ملف panic-full كاملًا من الآيفون: الإعدادات ← الخصوصية والأمان ← التحليلات والتحسينات ← بيانات التحليلات.");
            return;
        }
        int before = logs.Count;
        logs.AddRange(found);
        AfterAdd(before);
    }

    void AfterAdd(int before)
    {
        if (logs.Count == before) return;
        dirty = true;
        Reanalyze(select: logs.Count - 1);
        if (logs.Count - before > 1) Toast.Show($"أُضيف {logs.Count - before} سجلات");
        if (Settings.On("analyze_autosave") && Session.Can("history")) SaveRecord(silent: true);
    }

    void PasteClipboard()
    {
        if (!Session.Guard("analyze")) return;
        try
        {
            if (Clipboard.ContainsFileDropList()) { AddFiles(Clipboard.GetFileDropList().Cast<string>()); return; }
            if (!Clipboard.ContainsText()) { Ui.Warn("الحافظة لا تحتوي نصًا. انسخ نص السجل أولًا."); return; }
            AddText(Clipboard.GetText(), "نص ملصوق");
        }
        catch (Exception ex) { Ui.Warn("تعذرت قراءة الحافظة: " + ex.Message); }
    }

    void Drop(IDataObject data)
    {
        if (!Session.Can("analyze")) return;
        if (data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
        else if (data.GetData(DataFormats.UnicodeText) is string text) AddText(text, "نص مسحوب");
    }

    /// <summary>تحليل ما في مربع النص: يستبدل السجل المحدد (بعد تعديله)، أو يضيف سجلًا جديدًا إن لم يكن هناك تحديد</summary>
    void AnalyzeRawText()
    {
        if (!Session.Guard("analyze")) return;
        var text = raw.Text;
        if (string.IsNullOrWhiteSpace(text)) { Ui.Warn("الصق نص السجل في مربع «نص السجل» أولًا، أو استخدم «لصق» أو «فتح ملفات»."); return; }
        int i = SelectedIndex();
        if (i < 0 || logs[i].Raw == PanicParser.Normalize(text)) { if (i < 0) AddText(text, "نص ملصوق"); else Reanalyze(i); return; }
        var found = PanicParser.ParseMany(text, logs[i].Source);
        if (found.Count == 0) { Ui.Warn("لم يُعثر على سجل بانك في النص بعد التعديل."); return; }
        logs.RemoveAt(i);
        logs.InsertRange(i, found);
        dirty = true;
        Reanalyze(i);
    }

    void RemoveSelected()
    {
        int i = SelectedIndex();
        if (i < 0) return;
        logs.RemoveAt(i);
        dirty = true;
        Reanalyze(Math.Min(i, logs.Count - 1));
    }

    void ClearAll(bool ask)
    {
        if (ask && logs.Count > 0 && !Ui.Confirm("مسح كل السجلات والبدء بفحص جديد؟")) return;
        loading = true;
        logs.Clear();
        recordId = 0;
        customer.Clear(); phone.Clear(); notes.Clear();
        status.SelectedIndex = 0;
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) t.Checked = false;
        raw.Clear();
        loading = false;
        dirty = false;
        Text = PageTitle;
        Reanalyze();
    }

    void LoadRecord(PanicStore.Record rec)
    {
        loading = true;
        recordId = rec.Id;
        customer.Text = rec.Customer; phone.Text = rec.Phone; notes.Text = rec.Notes;
        status.SelectedIndex = Math.Max(0, Array.IndexOf(PanicStore.Statuses, rec.Status));
        tLiquid.Checked = rec.Flags.Liquid; tBattery.Checked = rec.Flags.BatteryReplaced; tFlex.Checked = rec.Flags.ChargingFlexReplaced;
        tScreen.Checked = rec.Flags.ScreenReplaced; tDrop.Checked = rec.Flags.Dropped;
        foreach (var (source, text) in rec.Logs)
        {
            var one = PanicParser.Parse(text, source);
            if (!one.IsEmpty) logs.Add(one);
        }
        loading = false;
        Reanalyze(0);
        dirty = false;
    }

    // ============================================================== التحليل والعرض
    CaseFlags Flags() => new()
    {
        Liquid = tLiquid.Checked, BatteryReplaced = tBattery.Checked, ChargingFlexReplaced = tFlex.Checked, ScreenReplaced = tScreen.Checked, Dropped = tDrop.Checked,
    };

    int SelectedIndex() =>
        logsGrid.CurrentRow != null && logsGrid.Columns.Contains("id") && Db.L(logsGrid.CurrentRow.Cells["id"].Value) is var i && i >= 0 && i < logs.Count ? (int)i : -1;

    /// <summary>تحليل كل السجلات من جديد (بعد إضافة سجل أو تغيير معلومات الحالة أو خبرة المحل)</summary>
    void Reanalyze(int select = -2)
    {
        if (select == -2) select = SelectedIndex();
        var rules = PanicStore.Rules();
        var flags = Flags();
        diags.Clear();
        foreach (var l in logs) diags.Add(PanicAnalyzer.Analyze(l, flags, rules));

        var dt = new DataTable();
        dt.Columns.Add("id", typeof(long));
        foreach (var c in new[] { "#", "المصدر", "الجهاز", "النوع", "الأرجح" }) dt.Columns.Add(c);
        for (int i = 0; i < logs.Count; i++)
        {
            var d = diags[i];
            dt.Rows.Add((long)i, (i + 1).ToString(), logs[i].Source + (d.Time != "" ? "  —  " + d.Time : ""), d.Device == "" ? "غير معروف" : d.Device, d.Kind, d.TopPart == "" ? "—" : d.TopPart);
        }
        loading = true;
        logsGrid.DataSource = dt;
        if (logs.Count > 0 && logsGrid.Rows.Count == logs.Count)
        {
            int row = Math.Clamp(select < 0 ? 0 : select, 0, logs.Count - 1);
            logsGrid.CurrentCell = logsGrid.Rows[row].Cells["#"];
        }
        loading = false;
        int devices = logs.Select(l => l.DeviceKey).Where(k => k != "").Distinct().Count();
        logsCard.Subtitle = logs.Count == 0 ? "افتح ملفات panic-full أو الصق النص — أو اسحب الملفات إلى هنا"
            : $"{logs.Count} سجل" + (devices > 1 ? $" من {devices} أجهزة مختلفة" : "") + " — اختر سجلًا لعرض تحليله";
        ShowResult();
    }

    /// <summary>النتيجة المعروضة: تحليل السجل المحدد، أو التحليل المجمّع لكل سجلات نفس الجهاز</summary>
    void ShowResult()
    {
        int i = SelectedIndex();
        if (i < 0)
        {
            shown = null;
            shownLogs = new();
            raw.Text = logs.Count == 0 ? raw.Text : "";
            verdict.Show(null, null);
            causes.DataSource = null; steps.DataSource = null; evidence.DataSource = null; previous.DataSource = null;
            report.Text = "";
            lblPrev.Text = "";
            UpdateButtons();
            return;
        }
        raw.Text = logs[i].Raw.Replace("\n", "\r\n");
        var key = logs[i].DeviceKey;
        var group = Enumerable.Range(0, logs.Count).Where(k => k == i || (key != "" && logs[k].DeviceKey == key)).ToList();
        if (tCombine.Checked && group.Count > 1)
        {
            shown = PanicAnalyzer.Combine(group.Select(k => diags[k]).ToList());
            shownLogs = group.Select(k => logs[k]).ToList();
        }
        else
        {
            shown = diags[i];
            shownLogs = new List<PanicLog> { logs[i] };
        }
        var extra = new List<string>();
        int devices = logs.Select(l => l.DeviceKey).Where(k => k != "").Distinct().Count();
        if (devices > 1) extra.Add($"في القائمة سجلات من {devices} أجهزة مختلفة — يُعرض تحليل جهاز السجل المحدد فقط.");
        if (!tCombine.Checked && group.Count > 1) extra.Add($"لهذا الجهاز {group.Count} سجلات — فعّل «تجميع سجلات الجهاز» لتحليلها معًا.");
        verdict.Show(shown, extra);

        var ct = new DataTable();
        foreach (var c in new[] { "#", "السبب / القطعة", "الحالة", "لماذا" }) ct.Columns.Add(c);
        for (int k = 0; k < shown.Candidates.Count; k++) ct.Rows.Add((k + 1).ToString(), shown.Candidates[k].Part, shown.Candidates[k].Label, shown.Candidates[k].Why);
        causes.DataSource = ct;

        var st = new DataTable();
        st.Columns.Add("#"); st.Columns.Add("الخطوة");
        for (int k = 0; k < shown.Steps.Count; k++) st.Rows.Add((k + 1).ToString(), shown.Steps[k]);
        steps.DataSource = st;

        var et = new DataTable();
        foreach (var c in new[] { "الدليل", "القيمة", "المعنى" }) et.Columns.Add(c);
        foreach (var e in shown.Evidence) et.Rows.Add(e.What, e.Value, e.Meaning);
        evidence.DataSource = et;

        report.Text = PanicAnalyzer.Report(shown).Replace("\n", "\r\n");
        var prev = Session.Can("history") ? PanicStore.Previous(key, recordId) : new DataTable();
        previous.DataSource = prev;
        lblPrev.Text = key == "" ? "لا يوجد مفتاح جهاز في السجل لمطابقة الفحوصات السابقة."
            : prev.Rows.Count == 0 ? "لم يُفحص هذا الجهاز من قبل في المحل." : $"هذا الجهاز فُحص {prev.Rows.Count} مرة من قبل — نقر مزدوج لفتح الفحص.";
        UpdateButtons();
    }

    void UpdateButtons()
    {
        bSave.Enabled = shown != null && Session.Can("history");
        bCopy.Enabled = shown != null;
        bPrint.Enabled = shown != null && Session.Can("print");
        bRemove.Enabled = logs.Count > 0;
        bSave.Text = recordId > 0 ? "تحديث السجل" : "حفظ في السجل";
        bSave.Invalidate();
    }

    // ============================================================== الحفظ والتقرير
    void SaveRecord(bool silent)
    {
        if (shown == null) { if (!silent) Ui.Warn("لا يوجد تحليل لحفظه."); return; }
        if (!Session.Guard("history")) return;
        try
        {
            bool isNew = recordId == 0;
            // السجل الواحد يخص جهازًا واحدًا: تُحفظ سجلات الجهاز المعروض فقط
            recordId = PanicStore.Save(recordId, shown, shownLogs, Flags(), customer.Text, phone.Text, notes.Text, status.Text);
            if (isNew) Db.Audit("حفظ فحص", $"رقم {recordId}: {shown.Device} — {shown.TopPart}");
            dirty = false;
            Text = $"فحص رقم {recordId}";
            UpdateButtons();
            if (!silent) Toast.Show(isNew ? $"حُفظ الفحص برقم {recordId}" : "تم تحديث السجل");
        }
        catch (Exception ex) { if (!silent) Ui.Warn("تعذر الحفظ: " + ex.Message); }
    }

    void CopyReport()
    {
        if (shown == null) return;
        try { Clipboard.SetText(PanicAnalyzer.Report(shown)); Toast.Show("نُسخ التقرير — الصقه في رسالة للزبون أو في ملاحظاتك"); }
        catch (Exception ex) { Ui.Warn("تعذر النسخ: " + ex.Message); }
    }

    void PrintReport()
    {
        if (shown == null || !Session.Guard("print")) return;
        var d = shown;
        var doc = PrintDoc.Header("تقرير فحص الجهاز");
        doc.ForceA4 = true;
        doc.Pair("الزبون", customer.Text.Trim() == "" ? "—" : customer.Text.Trim(), "الهاتف", phone.Text.Trim() == "" ? "—" : phone.Text.Trim());
        doc.Pair("الجهاز", d.Device == "" ? "غير معروف" : d.Device, "iOS", d.Ios == "" ? "—" : d.Ios + (d.Build != "" ? $" ({d.Build})" : ""));
        doc.Pair("وقت البانك", d.Time == "" ? "—" : d.Time, "رقم الفحص", recordId > 0 ? recordId.ToString() : "غير محفوظ");
        doc.Line();
        doc.Text(d.Title, 12, true);
        doc.Text(d.Summary, 10.5f);
        doc.Text("درجة الثقة: " + d.Confidence + (d.LogCount > 1 ? $" — مبنية على {d.LogCount} سجلات" : ""), 9.5f);
        doc.Space(6);
        doc.Table(new[] { "#", "السبب / القطعة", "الدرجة", "لماذا" }, new[] { 6f, 32, 14, 48 },
            d.Candidates.Select((c, k) => new[] { (k + 1).ToString(), c.Part, c.Label, c.Why }).ToList());
        if (d.Steps.Count > 0)
        {
            doc.Space(6);
            doc.Table(new[] { "#", "خطوات الفحص" }, new[] { 6f, 94 }, d.Steps.Select((s, k) => new[] { (k + 1).ToString(), s }).ToList());
        }
        if (notes.Text.Trim() != "") { doc.Space(6); doc.Text("ملاحظات الفحص: " + notes.Text.Trim(), 10); }
        doc.Footer();
        doc.Print();
    }

    public override bool ConfirmClose() =>
        !dirty || logs.Count == 0 || !Session.Can("history") || Ui.Confirm("التحليل لم يُحفظ في سجل الفحوصات. إغلاق بدون حفظ؟");

    public override void OnPageActivated()
    {
        // خبرة المحل قد تغيّرت من شاشة أخرى: يُعاد التحليل بالقواعد الجديدة
        if (logs.Count > 0) Reanalyze();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.O: PickFiles(); return true;
            case Keys.Control | Keys.S: SaveRecord(silent: false); return true;
            case Keys.Control | Keys.P: PrintReport(); return true;
            case Keys.Control | Keys.V when !raw.Focused && ActiveControl is not TextBoxBase: PasteClipboard(); return true;
            case Keys.F9: AnalyzeRawText(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

/// <summary>بطاقة الخلاصة: عنوان التشخيص والخلاصة ودرجة الثقة والجهاز والتنبيهات — أو إرشاد البدء إن لم يوجد سجل</summary>
public class VerdictPanel : Control
{
    Diagnosis d;
    List<string> extra = new();

    public VerdictPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void Show(Diagnosis diagnosis, List<string> extraWarnings)
    {
        d = diagnosis;
        extra = extraWarnings ?? new();
        Invalidate();
    }

    static Color ConfColor(string c) => c switch { "عالية" => Theme.Success, "متوسطة" => Theme.Warning, _ => Theme.Gray };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        int S(int v) => Dpi.S(v);
        var r = new RectangleF(S(2), 0.5f, Width - S(4), Height - S(5));
        Gfx.Shadow(g, r, S(14));
        Gfx.FillRound(g, r, S(14), Theme.Surface);
        Gfx.DrawRound(g, r, S(14), Theme.Border);

        var accent = d == null ? Theme.Brand : ConfColor(d.Confidence);
        // شريط لوني في بداية البطاقة (الجهة اليمنى) حسب درجة الثقة
        Gfx.FillRound(g, new RectangleF(r.Right - S(8), r.Y + S(14), S(5), r.Height - S(28)), S(3), accent);

        int right = (int)r.Right - S(28), left = (int)r.X + S(22);
        int box = S(46);
        var ir = new RectangleF(right - box, S(20), box, box);
        Gfx.FillRound(g, ir, S(14), Gfx.Mix(accent, Color.White, 0.86f));
        Icons.Draw(g, d == null ? "scan-line" : d.Confidence == "عالية" ? "badge-check" : "circle-alert", ir, accent, 24);
        int tx = (int)ir.X - S(14);
        const TextFormatFlags wrap = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        if (d == null)
        {
            TextRenderer.DrawText(g, "الصق سجل البانك أو افتح ملف panic-full", Theme.FS(14), new Rectangle(left, S(18), tx - left, S(30)), Theme.Ink, Gfx.RtlStart);
            TextRenderer.DrawText(g,
                "على الآيفون: الإعدادات ← الخصوصية والأمان ← التحليلات والتحسينات ← بيانات التحليلات ← الملف الذي يبدأ بـ panic-full. " +
                "افتحه ثم شاركه أو انسخ نصه كاملًا. يقبل البرنامج أكثر من ملف معًا، والنص المنسوخ من صورة أيضًا.",
                Theme.F(10), new Rectangle(left, S(54), tx - left, S(66)), Theme.Text2, wrap);
            TextRenderer.DrawText(g, "اختصارات: Ctrl+O فتح ملفات  •  Ctrl+V لصق  •  F9 تحليل النص  •  Ctrl+S حفظ  •  Ctrl+P طباعة",
                Theme.F(9), new Rectangle(left, Height - S(44), right - left, S(24)), Theme.Muted, Gfx.RtlStart);
            return;
        }

        var device = string.Join("  •  ", new[]
        {
            d.Device == "" ? "جهاز غير معروف" : d.Device, d.Soc, d.Ios != "" ? "iOS " + d.Ios + (d.Build != "" ? $" ({d.Build})" : "") : "",
            d.LogCount > 1 ? $"{d.LogCount} سجلات" : d.Time,
        }.Where(x => !string.IsNullOrEmpty(x)));
        TextRenderer.DrawText(g, Theme.Bidi(device), Theme.F(9.5f), new Rectangle(left, S(16), tx - left, S(22)), Theme.Muted, Gfx.RtlStart);
        TextRenderer.DrawText(g, Theme.Bidi(d.Title), Theme.FS(14), new Rectangle(left, S(38), tx - left, S(30)), Theme.Ink, Gfx.RtlStart);
        TextRenderer.DrawText(g, d.Summary, Theme.F(10.5f), new Rectangle(left, S(72), right - left, S(46)), Theme.Text2, wrap);

        // شارات: الثقة، النوع، الأرجح
        int x = right;
        void Chip(string text, Color fg, Color bg)
        {
            var f = Theme.FS(9);
            int w = TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding).Width + S(22);
            if (x - w < left) return;
            var cr = new Rectangle(x - w, S(122), w, S(26));
            Gfx.FillRound(g, cr, cr.Height / 2f, bg);
            TextRenderer.DrawText(g, text, f, cr, fg, Gfx.Center);
            x -= w + S(8);
        }
        Chip("الثقة: " + d.Confidence, ConfColor(d.Confidence), Gfx.Mix(ConfColor(d.Confidence), Color.White, 0.86f));
        Chip(d.Kind, Theme.Brand, Theme.BrandSoft);
        if (d.TopPart != "") Chip("الأرجح: " + d.TopPart, Theme.Danger, Theme.DangerSoft);

        var warns = extra.Concat(d.Warnings).ToList();
        if (warns.Count > 0)
            TextRenderer.DrawText(g, "⚠ " + warns[0] + (warns.Count > 1 ? $"  (+{warns.Count - 1} تنبيه آخر)" : ""), Theme.F(9),
                new Rectangle(left, Height - S(40), right - left, S(24)), Theme.Warning, Gfx.RtlStart);
    }
}
