using System.Data;

namespace Kashif;

/// <summary>
/// تحليل البانك — الشاشة الرئيسية للبرنامج (F2).
/// التخطيط: شريط أدوات، رأس خلاصة بعرض الشاشة (مع أعلى 3 أسباب)، شريط «ما حدث للجهاز»،
/// ثم مساحة العمل: قائمة السجلات (تُطوى) وتبويبات واسعة للأسباب والخطوات والأدلة مع نص السجل والخط الزمني والزبون والتقرير.
/// </summary>
public class AnalyzeForm : BaseForm
{
    public const string PageTitle = "تحليل البانك";
    const long MaxFileBytes = 20 * 1024 * 1024;

    // ---------- الإدخال ----------
    readonly DataGridView logsGrid = Ui.NewGrid(), evidence = Ui.NewGrid(), timeline = Ui.NewGrid(), previous = Ui.NewGrid();
    readonly RichTextBox raw = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, WordWrap = false, DetectUrls = false, HideSelection = false,
        RightToLeft = RightToLeft.No, BackColor = Theme.SurfaceAlt, ForeColor = Theme.Ink, ScrollBars = RichTextBoxScrollBars.Both,
    };
    readonly Toggle tLiquid = new() { Text = "تعرض لسوائل", Width = 150 }, tBattery = new() { Text = "بطارية مستبدلة", Width = 160 },
        tFlex = new() { Text = "فلاتة شحن مستبدلة", Width = 180 }, tScreen = new() { Text = "شاشة مستبدلة", Width = 150 }, tDrop = new() { Text = "سقوط أو ضربة", Width = 150 };
    readonly Toggle tCombine = new() { Text = "تجميع سجلات الجهاز", Width = 190 };

    // ---------- الزبون والنتيجة ----------
    readonly TextBox customer = new() { Width = 320, PlaceholderText = "اسم الزبون" };
    readonly TextBox phone = new() { Width = 220, PlaceholderText = "07xx xxx xxxx" };
    readonly ComboBox status = Ui.Combo(220);
    readonly ComboBox fixedPart = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDown, Font = Theme.F(10) };
    readonly TextBox notes = new() { Width = 780, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "ما وجدته عند الفحص، ما جرّبته، النتيجة..." };

    // ---------- العرض ----------
    readonly VerdictHero verdict = new() { Dock = DockStyle.Top, Height = 214 };
    readonly CardStack causes = new() { Dock = DockStyle.Fill, Padding = new Padding(4), EmptyText = "لا توجد أسباب — افتح سجل بانك" };
    readonly CardStack steps = new() { Dock = DockStyle.Fill, Padding = new Padding(4), EmptyText = "لا توجد خطوات" };
    readonly TextBox report = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Ink };
    readonly Toggle tCustomerReport = new() { Text = "تقرير الزبون (مبسّط)", Width = 220 };
    readonly Label lblPrev = new() { Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Muted, Font = Theme.F(10), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface };
    readonly Label lblTimeline = new() { Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Muted, Font = Theme.F(10), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface };
    readonly Label lblSteps = new() { Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Muted, Font = Theme.F(9.5f), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface, Text = "انقر على الخطوة عند إنجازها" };
    readonly CardPanel logsCard;
    readonly ModernTabs tabs = new() { Dock = DockStyle.Fill };
    readonly ModernButton bSave, bCopy, bPrintTech, bPrintCustomer, bRemove, bLogs, bOpen, bPaste, bAnalyze, bClear;
    ModernButton bCompare;

    readonly List<PanicLog> logs = new();
    readonly List<Diagnosis> diags = new();
    readonly HashSet<string> doneSteps = new();
    List<Evidence> shownEvidence = new();
    Diagnosis shown;
    List<PanicLog> shownLogs = new();
    long recordId;
    bool dirty, loading, busy;

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
        fixedPart.Items.AddRange(Parts.All.OrderBy(p => p).Cast<object>().ToArray());
        fixedPart.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        fixedPart.AutoCompleteSource = AutoCompleteSource.ListItems;
        try { raw.Font = new Font("Consolas", 10f); } catch { raw.Font = Theme.F(9.5f); }
        report.Font = Theme.F(10.5f);

        // ---------- شريط الأدوات ----------
        var bar = Theme.Bar();
        bOpen = Theme.Btn("فتح ملفات", Theme.Brand, 130, "folder-open");
        bPaste = Theme.Btn("لصق", Theme.Brand, 90, "clipboard-list");
        bAnalyze = Theme.Btn("تحليل النص", Theme.Success, 130, "scan-line");
        bClear = Theme.Btn("فحص جديد", Theme.Gray, 120, "plus");
        bSave = Theme.Btn("حفظ في السجل", Theme.Success, 140, "save");
        bPrintTech = Theme.Btn("طباعة للفني", Theme.Gray, 125, "printer");
        bPrintCustomer = Theme.Btn("طباعة للزبون", Theme.Gray, 130, "printer");
        bCopy = Theme.Btn("نسخ التقرير", Theme.Gray, 125, "copy");
        bLogs = Theme.Btn("السجلات", Theme.Gray, 110, "panel-right");
        tCombine.Margin = new Padding(12, 8, 6, 2);
        bar.Controls.AddRange(new Control[] { bOpen, bPaste, bAnalyze, bClear, tCombine, bSave, bPrintTech, bPrintCustomer, bCopy, bLogs });
        foreach (var b in bar.Controls.OfType<ModernButton>()) { b.Height = 40; b.Margin = new Padding(4, 3, 4, 3); }

        // ---------- ما حدث للجهاز (سطر واحد بعرض الشاشة) ----------
        var flags = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Bg, WrapContents = false, Padding = new Padding(4, 6, 4, 0) };
        flags.Controls.Add(new Label { Text = "ما حدث للجهاز:", AutoSize = false, Width = 130, Height = 34, Font = Theme.FS(10), ForeColor = Theme.Text2, TextAlign = ContentAlignment.MiddleLeft });
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) { t.Margin = new Padding(6, 0, 14, 0); flags.Controls.Add(t); }

        // ---------- قائمة السجلات (جانبية، تُطوى) ----------
        logsCard = new CardPanel { Dock = DockStyle.Right, Width = 340, Title = "السجلات", IconName = "file-text", Subtitle = "اسحب الملفات إلى هنا" };
        bRemove = new ModernButton { Text = "إزالة المحدد", IconName = "trash-2", Kind = BtnKind.Secondary, Height = 36, Dock = DockStyle.Bottom };
        logsCard.Controls.Add(logsGrid);
        logsCard.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 8, BackColor = Theme.Surface });
        logsCard.Controls.Add(bRemove);
        logsGrid.ColumnHeadersVisible = false;
        logsGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        logsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        logsGrid.ScrollBars = ScrollBars.Vertical;
        logsGrid.DataBindingComplete += (s, e) =>
        {
            if (logsGrid.Columns.Contains("#")) { logsGrid.Columns["#"].FillWeight = 14; logsGrid.Columns["#"].MinimumWidth = Dpi.S(34); logsGrid.Columns["#"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; }
            if (logsGrid.Columns.Contains("السجل")) { logsGrid.Columns["السجل"].FillWeight = 86; logsGrid.Columns["السجل"].MinimumWidth = Dpi.S(120); }
        };

        // ---------- التبويبات ----------
        FitColumns(evidence, ("الدليل", 18), ("القيمة", 36), ("المعنى", 46));
        FitColumns(timeline, ("الوقت", 22), ("النوع", 14), ("التشخيص", 34), ("الأرجح", 22), ("المصدر", 8));
        FitColumns(previous, ("التاريخ", 16), ("التشخيص", 30), ("الأرجح", 20), ("القطعة المُصلِحة", 20), ("الحالة", 14));
        foreach (var g in new[] { evidence, timeline }) { g.DefaultCellStyle.WrapMode = DataGridViewTriState.True; g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells; }

        tabs.Add("الأسباب مرتبة", Pad(causes), "list-ordered", "الأسباب");
        var stepsPage = Pad(steps);
        stepsPage.Controls.Add(lblSteps);
        tabs.Add("خطوات الفحص", stepsPage, "list-checks", "الخطوات");
        tabs.Add("الأدلة ونص السجل", EvidencePage(), "search", "الأدلة");
        var tlPage = Pad(timeline);
        tlPage.Controls.Add(lblTimeline);
        tabs.Add("الخط الزمني", tlPage, "history", "الزمن");
        tabs.Add("الزبون والنتيجة", CustomerPage(), "user", "الزبون");
        tabs.Add("فحوصات سابقة للجهاز", PreviousPage(), "repeat", "السابقة");
        tabs.Add("التقرير", ReportPage(), "file-text");

        var work = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        work.Controls.Add(tabs);
        work.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Bg });
        work.Controls.Add(logsCard);

        Controls.Add(work);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6 });
        Controls.Add(flags);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        Controls.Add(verdict);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
        Controls.Add(bar);

        // ---------- الأحداث ----------
        bOpen.Click += (s, e) => PickFiles();
        bPaste.Click += (s, e) => PasteClipboard();
        bAnalyze.Click += (s, e) => AnalyzeRawText();
        bClear.Click += (s, e) => ClearAll(ask: true);
        bSave.Click += (s, e) => SaveRecord(silent: false);
        bCopy.Click += (s, e) => CopyReport();
        bPrintTech.Click += (s, e) => PrintReport(forCustomer: false);
        bPrintCustomer.Click += (s, e) => PrintReport(forCustomer: true);
        bRemove.Click += (s, e) => RemoveSelected();
        bLogs.Click += (s, e) => { logsCard.Visible = !logsCard.Visible; bLogs.Text = logsCard.Visible ? "السجلات" : "إظهار السجلات"; bLogs.Invalidate(); };
        tCombine.CheckedChanged += (s, e) => ShowResult();
        tCustomerReport.CheckedChanged += (s, e) => FillReport();
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) t.CheckedChanged += (s, e) => { if (!loading) { dirty = true; Reanalyze(); } };
        foreach (var c in new Control[] { customer, phone, notes, fixedPart }) c.TextChanged += (s, e) => { if (!loading) dirty = true; };
        status.SelectedIndexChanged += (s, e) => { if (!loading) dirty = true; };
        logsGrid.SelectionChanged += (s, e) => { if (!loading) ShowResult(); };
        evidence.SelectionChanged += (s, e) => HighlightEvidence();
        previous.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenRecord(Db.L(previous.Rows[e.RowIndex].Cells["id"].Value)); };
        DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (s, e) => Drop(e.Data);
        raw.AllowDrop = true;
        raw.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        raw.DragDrop += (s, e) => Drop(e.Data);

        // السجل المحفوظ يُحمَّل عند ظهور الشاشة (بعد ربط الجداول بالنافذة)
        if (rec != null) Load += (s, e) => LoadRecord(rec);
        ShowResult();
    }

    static Panel Pad(Control c)
    {
        var p = new Panel { BackColor = Theme.Surface, Padding = new Padding(10) };
        c.Dock = DockStyle.Fill;
        p.Controls.Add(c);
        return p;
    }

    Control EvidencePage()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Border, SplitterWidth = 6 };
        split.Panel1.BackColor = Theme.Surface;
        split.Panel2.BackColor = Theme.Surface;
        split.Panel1.Padding = new Padding(8);
        split.Panel2.Padding = new Padding(8);
        split.Panel1.Controls.Add(evidence);
        var hint = new Label { Dock = DockStyle.Top, Height = 28, Text = "نص السجل — اختر دليلًا من الجدول ليُظلَّل مكانه هنا. يمكنك تعديل النص ثم «تحليل النص» (F9).", ForeColor = Theme.Muted, Font = Theme.F(9.5f), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface };
        var rawHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Padding = new Padding(6) };
        rawHost.Controls.Add(raw);
        split.Panel2.Controls.Add(rawHost);
        split.Panel2.Controls.Add(hint);
        split.HandleCreated += (s, e) => { try { split.SplitterDistance = Math.Max(Dpi.S(120), split.Height * 45 / 100); } catch { } };
        return split;
    }

    Control CustomerPage()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, AutoScroll = true, Padding = new Padding(6, 10, 6, 6) };
        flow.Controls.Add(Ui.Labeled("الزبون", customer));
        flow.Controls.Add(Ui.Labeled("الهاتف", phone));
        flow.Controls.Add(Ui.Labeled("حالة الجهاز", status));
        flow.SetFlowBreak(flow.Controls[^1], true);
        flow.Controls.Add(Ui.Labeled("القطعة التي أصلحت الجهاز فعلًا (بعد الإصلاح)", fixedPart));
        flow.Controls.Add(new Label
        {
            Text = "تسجيلها يحسب دقة البرنامج في محلك (التقارير ← دقة التشخيص) ويقترح قواعد جديدة لخبرة المحل.",
            AutoSize = false, Width = 360, Height = 52, ForeColor = Theme.Muted, Font = Theme.F(9), TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(6, 4, 6, 0),
        });
        flow.SetFlowBreak(flow.Controls[^1], true);
        flow.Controls.Add(Ui.Labeled("ملاحظات الفحص", notes));
        flow.SetFlowBreak(flow.Controls[^1], true);
        var b = Theme.Btn("حفظ في السجل", Theme.Success, 160, "save");
        b.Margin = new Padding(6, 14, 6, 4);
        b.Click += (s, e) => SaveRecord(silent: false);
        flow.Controls.Add(b);
        flow.Controls.Add(new Label
        {
            Text = "يُحفظ نص السجلات الأصلي كما هو: عند فتح الفحص لاحقًا يُعاد تحليله بأحدث قاعدة معرفة وخبرة المحل.",
            AutoSize = false, Width = 560, Height = 44, ForeColor = Theme.Muted, Font = Theme.F(9), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 14, 6, 0),
        });
        return flow;
    }

    Control PreviousPage()
    {
        var page = Pad(previous);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Surface, WrapContents = false };
        bCompare = Theme.Btn("مقارنة مع الفحص المحدد", Theme.Brand, 210, "arrow-left-right");
        bCompare.Height = 38;
        bCompare.Click += (s, e) => CompareSelected();
        top.Controls.Add(bCompare);
        page.Controls.Add(lblPrev);
        page.Controls.Add(top);
        return page;
    }

    Control ReportPage()
    {
        var page = Pad(report);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Surface, WrapContents = false };
        tCustomerReport.Margin = new Padding(6, 6, 6, 2);
        top.Controls.Add(tCustomerReport);
        page.Controls.Add(top);
        return page;
    }

    /// <summary>عرض نسبي للأعمدة يُطبَّق بعد كل ربط</summary>
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
                    c.MinimumWidth = Dpi.S(60);
                    c.FillWeight = w;
                }
        };
    }

    // ============================================================== الإدخال
    void PickFiles()
    {
        if (busy || !Session.Guard("analyze")) return;
        using var ofd = new OpenFileDialog
        {
            Multiselect = true, Title = "اختر ملفات البانك",
            Filter = "سجلات البانك (*.ips;*.txt;*.json;*.log;*.panic)|*.ips;*.txt;*.json;*.log;*.panic|كل الملفات (*.*)|*.*",
        };
        if (ofd.ShowDialog() == DialogResult.OK) AddFiles(ofd.FileNames);
    }

    /// <summary>قراءة الملفات وتحليلها في الخلفية (مع التقدم) حتى لا تتجمد الشاشة مع عشرات الملفات</summary>
    public async void AddFiles(IEnumerable<string> files)
    {
        if (busy) return;
        var list = files.ToList();
        if (list.Count == 0) return;
        busy = true;
        UpdateButtons();
        var progress = new Progress<int>(n => logsCard.Subtitle = $"جارٍ قراءة الملفات: {n} من {list.Count}…");
        (List<PanicLog> Found, List<string> Skipped) result;
        try
        {
            result = await Task.Run(() =>
            {
                var found = new List<PanicLog>();
                var skipped = new List<string>();
                IProgress<int> p = progress;
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    try
                    {
                        var info = new FileInfo(f);
                        if (!info.Exists) continue;
                        if (info.Length > MaxFileBytes) { skipped.Add($"{info.Name}: حجمه أكبر من 20 ميغابايت"); continue; }
                        var text = File.ReadAllText(f, System.Text.Encoding.UTF8);
                        var logs = PanicParser.ParseMany(text, info.Name);
                        if (logs.Count == 0) skipped.Add($"{info.Name}: " + (PanicParser.ExplainNonPanic(text, info.Name) ?? "لا يحتوي سجل بانك"));
                        found.AddRange(logs);
                    }
                    catch (Exception ex) { skipped.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
                    p.Report(i + 1);
                }
                return (found, skipped);
            });
        }
        finally { busy = false; }
        if (IsDisposed) return;
        Merge(result.Found);
        if (result.Skipped.Count > 0) Dialogs.Message("لم تُقرأ بعض الملفات:\n" + string.Join("\n", result.Skipped.Take(12)), "فتح الملفات", Tone.Warning);
    }

    public void AddText(string text, string source)
    {
        var found = PanicParser.ParseMany(text ?? "", source);
        if (found.Count == 0)
        {
            Ui.Warn(PanicParser.ExplainNonPanic(text) ??
                "لم يُعثر على سجل بانك في النص.\nانسخ ملف panic-full كاملًا من الآيفون: الإعدادات ← الخصوصية والأمان ← التحليلات والتحسينات ← بيانات التحليلات.");
            return;
        }
        Merge(found);
    }

    /// <summary>إضافة سجلات جديدة مع تجاهل المكرر (نفس رقم الحادثة أو نفس النص)</summary>
    void Merge(List<PanicLog> found)
    {
        var known = logs.Select(l => l.Identity).ToHashSet();
        int before = logs.Count, dup = 0;
        foreach (var l in found)
        {
            if (!known.Add(l.Identity)) { dup++; continue; }
            logs.Add(l);
        }
        if (dup > 0) Toast.Show($"تُجوهل {dup} سجل مكرر (أُضيف من قبل)", Tone.Info);
        if (logs.Count == before) { Reanalyze(); return; }
        dirty = true;
        Reanalyze(select: logs.Count - 1);
        if (logs.Count - before > 1) Toast.Show($"أُضيف {logs.Count - before} سجلات");
        if (Settings.On("analyze_autosave") && Session.Can("history")) SaveRecord(silent: true);
    }

    void PasteClipboard()
    {
        if (busy || !Session.Guard("analyze")) return;
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
        if (busy || !Session.Can("analyze")) return;
        if (data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
        else if (data.GetData(DataFormats.UnicodeText) is string text) AddText(text, "نص مسحوب");
    }

    /// <summary>تحليل ما في مربع نص السجل: يستبدل السجل المحدد (بعد تعديله)، أو يضيف سجلًا جديدًا إن لم يكن هناك تحديد</summary>
    void AnalyzeRawText()
    {
        if (busy || !Session.Guard("analyze")) return;
        var text = raw.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            tabs.SelectedIndex = 2;
            Ui.Warn("الصق نص السجل في «نص السجل» (تبويب الأدلة) أولًا، أو استخدم «لصق» أو «فتح ملفات».");
            return;
        }
        int i = SelectedIndex();
        if (i < 0) { AddText(text, "نص ملصوق"); return; }
        if (logs[i].Raw == PanicParser.Normalize(text)) { Reanalyze(i); return; }
        var found = PanicParser.ParseMany(text, logs[i].Source);
        if (found.Count == 0) { Ui.Warn(PanicParser.ExplainNonPanic(text) ?? "لم يُعثر على سجل بانك في النص بعد التعديل."); return; }
        logs.RemoveAt(i);
        logs.InsertRange(i, found);
        dirty = true;
        Reanalyze(i);
    }

    void RemoveSelected()
    {
        int i = SelectedIndex();
        if (i < 0 || busy) return;
        logs.RemoveAt(i);
        dirty = true;
        Reanalyze(Math.Min(i, logs.Count - 1));
    }

    void ClearAll(bool ask)
    {
        if (busy) return;
        if (ask && logs.Count > 0 && dirty && Session.Can("history") && !Ui.Confirm("التحليل الحالي لم يُحفظ. بدء فحص جديد ومسح السجلات؟")) return;
        loading = true;
        logs.Clear();
        recordId = 0;
        customer.Clear(); phone.Clear(); notes.Clear(); fixedPart.Text = "";
        status.SelectedIndex = 0;
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) t.Checked = false;
        raw.Clear();
        doneSteps.Clear();
        loading = false;
        dirty = false;
        Text = PageTitle;
        Reanalyze();
    }

    void LoadRecord(PanicStore.Record rec)
    {
        loading = true;
        recordId = rec.Id;
        customer.Text = rec.Customer; phone.Text = rec.Phone; notes.Text = rec.Notes; fixedPart.Text = rec.FixedPart;
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

        var groups = PanicAnalyzer.GroupDevices(logs);
        var dt = new DataTable();
        dt.Columns.Add("id", typeof(long));
        dt.Columns.Add("#"); dt.Columns.Add("السجل");
        for (int i = 0; i < logs.Count; i++)
        {
            var d = diags[i];
            var line1 = logs[i].Source + (d.Time != "" ? "  —  " + d.Time : "");
            var line2 = (d.Device == "" ? "جهاز غير معروف" : d.Device) + "  •  " + d.Kind;
            var line3 = d.TopPart == "" ? "" : "الأرجح: " + d.TopPart;
            dt.Rows.Add((long)i, (i + 1) + (groups.Distinct().Count() > 1 ? "\n" + "جهاز " + (groups[i] + 1) : ""), string.Join("\n", new[] { line1, line2, line3 }.Where(x => x != "")));
        }
        loading = true;
        logsGrid.DataSource = dt;
        if (logs.Count > 0 && logsGrid.Rows.Count == logs.Count)
        {
            int row = Math.Clamp(select < 0 ? 0 : select, 0, logs.Count - 1);
            logsGrid.CurrentCell = logsGrid.Rows[row].Cells["السجل"];
        }
        loading = false;
        int devices = groups.Distinct().Count();
        logsCard.Subtitle = logs.Count == 0 ? "افتح ملفات أو الصق النص — أو اسحب الملفات إلى هنا"
            : $"{logs.Count} سجل" + (devices > 1 ? $" من {devices} أجهزة" : "");
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
            if (logs.Count > 0) raw.Clear();
            verdict.Show(null, null);
            causes.SetItems(Array.Empty<StackItem>());
            steps.SetItems(Array.Empty<StackItem>());
            evidence.DataSource = null; timeline.DataSource = null; previous.DataSource = null;
            shownEvidence = new();
            lblPrev.Text = ""; lblTimeline.Text = "";
            report.Text = "";
            UpdateButtons();
            return;
        }
        raw.Text = logs[i].Raw;
        var groups = PanicAnalyzer.GroupDevices(logs);
        var group = Enumerable.Range(0, logs.Count).Where(k => groups[k] == groups[i]).ToList();
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
        int devices = groups.Distinct().Count();
        if (devices > 1) extra.Add($"في القائمة سجلات من {devices} أجهزة مختلفة — يُعرض تحليل جهاز السجل المحدد فقط.");
        if (!tCombine.Checked && group.Count > 1) extra.Add($"لهذا الجهاز {group.Count} سجلات — فعّل «تجميع سجلات الجهاز» لتحليلها معًا.");
        try
        {
            long same = Session.Can("history") ? PanicStore.SameBuildDevices(shown) : 0;
            if (same >= 2) extra.Add($"نفس النمط ظهر على {same} أجهزة أخرى بنفس iOS {shown.Build} — قد يكون السبب برمجيًا في هذا الإصدار.");
        }
        catch { }
        verdict.Show(shown, extra);

        causes.SetItems(shown.Candidates.Select((c, k) => (StackItem)new CauseCard(k + 1, c)));
        steps.SetItems(shown.Steps.Select((s, k) =>
        {
            var card = new StepCard(k + 1, s, doneSteps.Contains(s));
            card.DoneChanged += (o, e) => { if (card.Done) doneSteps.Add(card.StepText); else doneSteps.Remove(card.StepText); };
            return (StackItem)card;
        }));

        shownEvidence = shown.Evidence.ToList();
        var et = new DataTable();
        et.Columns.Add("id", typeof(long));
        foreach (var c in new[] { "الدليل", "القيمة", "المعنى" }) et.Columns.Add(c);
        for (int k = 0; k < shownEvidence.Count; k++) et.Rows.Add((long)k, shownEvidence[k].What, shownEvidence[k].Value, shownEvidence[k].Meaning);
        evidence.DataSource = et;

        var tt = new DataTable();
        foreach (var c in new[] { "الوقت", "النوع", "التشخيص", "الأرجح", "المصدر" }) tt.Columns.Add(c);
        var items = shown.Timeline.Count > 0 ? shown.Timeline
            : new List<TimelineItem> { new(shown.Log?.Time, shown.Time, shown.Kind, shown.Title, shown.TopPart, shown.Signature, shown.Log?.Source ?? "") };
        foreach (var t in items) tt.Rows.Add(t.TimeText, t.Kind, t.Title, t.TopPart, t.Source);
        timeline.DataSource = tt;
        var gap = shown.Evidence.FirstOrDefault(e => e.What == "المدة بين البانكات");
        lblTimeline.Text = items.Count < 2 ? "سجل واحد — افتح سجلات أخرى لنفس الجهاز لترى تكرار البانك مع الوقت."
            : gap != null ? $"{items.Count} سجلات — المدة بين البانكات: {gap.Value} — {gap.Meaning}" : $"{items.Count} سجلات مرتبة من الأقدم";

        FillReport();
        var key = logs[i].DeviceKey;
        var prev = Session.Can("history") ? PanicStore.Previous(key, recordId) : new DataTable();
        previous.DataSource = prev;
        lblPrev.Text = key == "" ? "لا يوجد مفتاح جهاز في السجل لمطابقة الفحوصات السابقة."
            : prev.Rows.Count == 0 ? "لم يُفحص هذا الجهاز من قبل في المحل." : $"هذا الجهاز فُحص {prev.Rows.Count} مرة من قبل — نقر مزدوج لفتح الفحص، أو قارن معه.";
        UpdateButtons();
    }

    void FillReport()
    {
        report.Text = shown == null ? "" : (tCustomerReport.Checked ? PanicAnalyzer.CustomerReport(shown, Settings.Get("shop_name")) : PanicAnalyzer.Report(shown)).Replace("\n", "\r\n");
    }

    /// <summary>تظليل مكان الدليل المحدد داخل نص السجل (يُبحث أيضًا بصيغة JSON حيث «/» مكتوبة «\/»)</summary>
    void HighlightEvidence()
    {
        if (evidence.CurrentRow == null || !evidence.Columns.Contains("id")) return;
        int k = (int)Db.L(evidence.CurrentRow.Cells["id"].Value);
        if (k < 0 || k >= shownEvidence.Count) return;
        var needle = shownEvidence[k].Needle;
        if (string.IsNullOrEmpty(needle) || raw.TextLength == 0) return;
        var text = raw.Text;
        int at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase), len = needle.Length;
        if (at < 0) { var alt = needle.Replace("/", "\\/"); at = text.IndexOf(alt, StringComparison.OrdinalIgnoreCase); len = alt.Length; }
        raw.SuspendLayout();
        raw.SelectAll();
        raw.SelectionBackColor = raw.BackColor;
        if (at >= 0)
        {
            raw.Select(at, len);
            raw.SelectionBackColor = Theme.Amber;
            raw.ScrollToCaret();
        }
        raw.Select(Math.Max(0, at), 0);
        raw.ResumeLayout();
    }

    void UpdateButtons()
    {
        foreach (var b in new[] { bOpen, bPaste, bAnalyze, bClear }) b.Enabled = !busy;
        bSave.Enabled = !busy && shown != null && Session.Can("history");
        bCopy.Enabled = shown != null;
        bPrintTech.Enabled = bPrintCustomer.Enabled = shown != null && Session.Can("print");
        bRemove.Enabled = !busy && logs.Count > 0;
        bCompare.Enabled = shown != null && previous.Rows.Count > 0;
        bSave.Text = recordId > 0 ? "تحديث السجل" : "حفظ في السجل";
        bSave.Invalidate();
    }

    // ============================================================== الحفظ والتقرير والمقارنة
    void SaveRecord(bool silent)
    {
        if (shown == null) { if (!silent) Ui.Warn("لا يوجد تحليل لحفظه."); return; }
        if (!Session.Guard("history")) return;
        try
        {
            bool isNew = recordId == 0;
            // السجل الواحد يخص جهازًا واحدًا: تُحفظ سجلات الجهاز المعروض فقط
            recordId = PanicStore.Save(recordId, shown, shownLogs, Flags(), customer.Text, phone.Text, notes.Text, status.Text, fixedPart.Text);
            if (isNew) Db.Audit("حفظ فحص", $"رقم {recordId}: {shown.Device} — {shown.TopPart}");
            if (fixedPart.Text.Trim() != "") Db.Audit("نتيجة فحص", $"رقم {recordId}: الأرجح {shown.TopPart} — أُصلح بـ {fixedPart.Text.Trim()}");
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
        try
        {
            Clipboard.SetText(tCustomerReport.Checked ? PanicAnalyzer.CustomerReport(shown, Settings.Get("shop_name")) : PanicAnalyzer.Report(shown));
            Toast.Show(tCustomerReport.Checked ? "نُسخ تقرير الزبون — الصقه في رسالة" : "نُسخ تقرير الفني");
        }
        catch (Exception ex) { Ui.Warn("تعذر النسخ: " + ex.Message); }
    }

    /// <summary>طباعة تقرير الفني (كل الأسباب والخطوات) أو تقرير الزبون (مبسّط بلا رموز)</summary>
    void PrintReport(bool forCustomer)
    {
        if (shown == null || !Session.Guard("print")) return;
        var d = shown;
        var doc = PrintDoc.Header(forCustomer ? "تقرير فحص الجهاز" : "تقرير فحص الجهاز — للفني");
        doc.ForceA4 = true;
        doc.Pair("الزبون", customer.Text.Trim() == "" ? "—" : customer.Text.Trim(), "الهاتف", phone.Text.Trim() == "" ? "—" : phone.Text.Trim());
        doc.Pair("الجهاز", d.Device == "" ? "غير معروف" : d.Device, "رقم الفحص", recordId > 0 ? recordId.ToString() : "غير محفوظ");
        doc.Line();
        if (forCustomer)
        {
            doc.Text("المشكلة: " + PanicAnalyzer.CustomerProblem(d), 11);
            if (d.TopPart != "") doc.Text("السبب المرجّح: " + d.TopPart + (d.Candidates.Count > 1 ? " (وقد يكون: " + d.Candidates[1].Part + ")" : ""), 11, true);
            doc.Text("درجة الثقة: " + d.Confidence, 10);
            if (fixedPart.Text.Trim() != "") doc.Text("ما تم إصلاحه: " + fixedPart.Text.Trim(), 11, true);
            doc.Space(8);
            doc.Text("التشخيص مبني على سجل الأعطال الذي يحفظه الجهاز، ويُؤكَّد بالفحص العملي قبل تبديل أي قطعة.", 9);
        }
        else
        {
            doc.Pair("iOS", d.Ios == "" ? "—" : d.Ios + (d.Build != "" ? $" ({d.Build})" : ""), "وقت البانك", d.Time == "" ? "—" : d.Time);
            doc.Text(d.Title, 12, true);
            doc.Text(d.Summary, 10.5f);
            doc.Text("درجة الثقة: " + d.Confidence + (d.LogCount > 1 ? $" — مبنية على {d.LogCount} سجلات" : ""), 9.5f);
            doc.Space(6);
            doc.Table(new[] { "#", "السبب / القطعة", "الدرجة", "لماذا" }, new[] { 6f, 32, 14, 48 },
                d.Candidates.Select((c, k) => new[] { (k + 1).ToString(), c.Part, c.Label, c.Why }).ToList());
            if (d.Steps.Count > 0)
            {
                doc.Space(6);
                doc.Table(new[] { "#", "خطوات الفحص", "تم" }, new[] { 6f, 84, 10 }, d.Steps.Select((s, k) => new[] { (k + 1).ToString(), s, doneSteps.Contains(s) ? "✓" : "" }).ToList());
            }
            if (notes.Text.Trim() != "") { doc.Space(6); doc.Text("ملاحظات الفحص: " + notes.Text.Trim(), 10); }
        }
        doc.Footer();
        doc.Print();
    }

    void CompareSelected()
    {
        if (shown == null || previous.CurrentRow == null || !previous.Columns.Contains("id")) { Ui.Warn("اختر فحصًا سابقًا من الجدول."); return; }
        var rec = PanicStore.Load(Db.L(previous.CurrentRow.Cells["id"].Value));
        if (rec == null) return;
        var rules = PanicStore.Rules();
        var ds = rec.Logs.Select(x => PanicParser.Parse(x.Raw, x.Source)).Where(l => !l.IsEmpty).Select(l => PanicAnalyzer.Analyze(l, rec.Flags, rules)).ToList();
        if (ds.Count == 0) { Ui.Warn("الفحص السابق لا يحتوي سجلات قابلة للتحليل."); return; }
        using var dlg = new CompareDialog(shown, "الفحص الحالي", PanicAnalyzer.Combine(ds), $"فحص رقم {rec.Id} ({Ui.Cut(rec.Date, 10)})", fixedPart.Text.Trim(), rec.FixedPart);
        dlg.ShowModal();
    }

    public override bool ConfirmClose() =>
        !dirty || logs.Count == 0 || !Session.Can("history") || Ui.Confirm("التحليل لم يُحفظ في سجل الفحوصات. إغلاق بدون حفظ؟");

    public override void OnPageActivated()
    {
        // خبرة المحل قد تغيّرت من شاشة أخرى: يُعاد التحليل بالقواعد الجديدة
        if (logs.Count > 0 && !busy) Reanalyze();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.O: PickFiles(); return true;
            case Keys.Control | Keys.S: SaveRecord(silent: false); return true;
            case Keys.Control | Keys.P: PrintReport(forCustomer: false); return true;
            case Keys.Control | Keys.V when !raw.Focused && ActiveControl is not TextBoxBase: PasteClipboard(); return true;
            case Keys.F9: AnalyzeRawText(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

/// <summary>مقارنة فحصين لنفس الجهاز جنبًا إلى جنب (مثل قبل الإصلاح وبعده)</summary>
public class CompareDialog : DialogShell
{
    public CompareDialog(Diagnosis a, string aTitle, Diagnosis b, string bTitle, string aFixed, string bFixed) : base("مقارنة فحصين", 980, 620, "arrow-left-right")
    {
        var grid = Ui.NewGrid();
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        var dt = new System.Data.DataTable();
        dt.Columns.Add("البند"); dt.Columns.Add(aTitle); dt.Columns.Add(bTitle); dt.Columns.Add("تغيّر؟");
        void Row(string item, string x, string y) => dt.Rows.Add(item, x, y, x == y ? "" : "●");
        string Top(Diagnosis d, int n) => string.Join("\n", d.Candidates.Take(n).Select((c, i) => $"{i + 1}. {c.Part} ({c.Label})"));
        Row("الجهاز", a.Device, b.Device);
        Row("وقت البانك", a.Time, b.Time);
        Row("iOS", a.Ios + (a.Build != "" ? $" ({a.Build})" : ""), b.Ios + (b.Build != "" ? $" ({b.Build})" : ""));
        Row("عدد السجلات", a.LogCount.ToString(), b.LogCount.ToString());
        Row("النوع", a.Kind, b.Kind);
        Row("التشخيص", a.Title, b.Title);
        Row("الحساسات المفقودة", string.Join("، ", a.MissingSensors), string.Join("، ", b.MissingSensors));
        Row("الخدمة المتوقفة", a.WatchdogService, b.WatchdogService);
        Row("بصمة النمط", a.Signature, b.Signature);
        Row("الأسباب الأرجح", Top(a, 3), Top(b, 3));
        Row("الثقة", a.Confidence, b.Confidence);
        Row("القطعة المُصلِحة", aFixed, bFixed);
        grid.DataSource = dt;
        grid.DataBindingComplete += (s, e) =>
        {
            if (grid.Columns.Contains("البند")) grid.Columns["البند"].FillWeight = 18;
            if (grid.Columns.Contains(aTitle)) grid.Columns[aTitle].FillWeight = 38;
            if (grid.Columns.Contains(bTitle)) grid.Columns[bTitle].FillWeight = 38;
            if (grid.Columns.Contains("تغيّر؟")) grid.Columns["تغيّر؟"].FillWeight = 6;
        };
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 40, ForeColor = Theme.Text2, Font = Theme.F(10), TextAlign = ContentAlignment.MiddleLeft,
            Text = a.Signature != "" && a.Signature == b.Signature ? "نفس النمط في الفحصين: المشكلة لم تتغير (إن بُدّلت قطعة بينهما فلم تكن هي السبب)."
                : "النمط تغيّر بين الفحصين: راجع ما بُدّل بينهما.",
        };
        Body.Controls.Add(grid);
        Body.Controls.Add(note);
        AddButton("إغلاق", DialogResult.OK, BtnKind.Secondary, "x");
    }
}
