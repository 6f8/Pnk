using System.Data;

namespace Kashif;

/// <summary>
/// تحليل البانك — الشاشة الرئيسية للبرنامج (F2)، بتصميم الواجهة 6 «أين العطل داخل الجهاز؟»:
/// يمين: الجهاز والتشخيص، ثم أزرار الأسباب مرتبة. يسار: مخطط الجهاز تُلوَّن فيه أماكن القطع المشتبه بها.
/// النقر على سبب أو قطعة (أو زر «الفحص والخطوات») يفتح نافذة فيها دليل القطعة، الخلاصة، ما حدث للجهاز،
/// سؤال الفحص التفاعلي، خطوات الفحص، والأدلة من السجل (النقر على الدليل يفتح نص السجل مظللًا عند مكانه).
/// قائمة السجلات جانبية تُطوى، وبقية التفاصيل (نص السجل، النتيجة، التقرير، الخط الزمني، الفحوصات السابقة) في نوافذ عند الطلب.
/// </summary>
public class AnalyzeForm : BaseForm
{
    public const string PageTitle = "تحليل البانك";
    const long MaxFileBytes = 20 * 1024 * 1024;

    readonly DataGridView logsGrid = Ui.NewGrid();
    readonly Toggle tLiquid = new() { Text = "تعرض لسوائل", Width = 150 }, tBattery = new() { Text = "بطارية مستبدلة", Width = 160 },
        tFlex = new() { Text = "فلاتة شحن مستبدلة", Width = 180 }, tScreen = new() { Text = "شاشة مستبدلة", Width = 150 }, tDrop = new() { Text = "سقوط أو ضربة", Width = 150 };
    readonly Toggle tCombine = new() { Text = "تجميع سجلات الجهاز", Width = 190 };
    readonly Toggle tAllEvidence = new() { Text = "كل التفاصيل", Width = 130 };

    readonly VerdictHero verdict = new() { Dock = DockStyle.Top, Height = 204, ShowTopCauses = false };
    readonly InterviewView interview = new() { Dock = DockStyle.Fill };
    readonly CardStack causes = new() { Dock = DockStyle.Fill, BackColor = Palette6.Ground, EmptyText = "لا توجد أسباب — افتح سجل بانك" };
    readonly DeviceMap map = new() { Dock = DockStyle.Fill };
    readonly Label deviceLine = new() { Dock = DockStyle.Top, Height = 28, AutoSize = false, Font = Theme.F(11), ForeColor = Palette6.Muted, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label heading = new() { Dock = DockStyle.Top, Height = 60, AutoSize = false, Text = "أين العطل داخل الجهاز؟", Font = Theme.F(21, FontStyle.Bold), ForeColor = Palette6.Ink, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label hint = new() { Dock = DockStyle.Top, Height = 58, AutoSize = false, Font = Theme.F(11.5f), ForeColor = Palette6.Muted, TextAlign = ContentAlignment.TopLeft };
    readonly FlowLayoutPanel flags = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Surface, WrapContents = true, Padding = new Padding(4, 5, 4, 0) };
    readonly TableLayoutPanel lists = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Surface, Margin = new Padding(0) };
    readonly CardStack steps = new() { Dock = DockStyle.Fill, Padding = new Padding(2), EmptyText = "لا توجد خطوات" };
    readonly CardStack evidence = new() { Dock = DockStyle.Fill, Padding = new Padding(2), EmptyText = "لا توجد أدلة" };
    readonly CardPanel logsCard, stepsCard, evidenceCard;
    readonly ModernButton bOpen, bPaste, bDevice, bExam, bRaw, bClear, bCustomer, bSave, bReport, bMore, bLogs, bRemove;
    readonly ContextMenuStrip moreMenu = new() { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };

    // ---------- حالة الفحص ----------
    readonly List<PanicLog> logs = new();
    readonly List<Diagnosis> diags = new();
    readonly HashSet<string> doneSteps = new();
    readonly List<(string Id, int Answer)> answers = new();
    readonly HashSet<string> skipped = new();
    string customerName = "", phoneText = "", notesText = "", statusText = PanicStore.Statuses[0], fixedPartText = "";
    Diagnosis shown;
    List<PanicLog> shownLogs = new();
    long recordId;
    bool dirty, loading, busy, logsToggledByUser, detailsScaled;

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

        // ---------- شريط الأدوات ----------
        var bar = Theme.Bar();
        bOpen = Theme.Btn("فتح ملفات", Theme.Brand, 130, "folder-open");
        bPaste = Theme.Btn("لصق", Theme.Brand, 90, "clipboard-list");
        bDevice = Theme.Btn("من الآيفون", Theme.Brand, 130, "smartphone");
        bExam = Theme.Btn("الفحص والخطوات", Theme.Brand, 170, "list-checks");
        bRaw = Theme.Btn("نص السجل", Theme.Gray, 120, "scroll-text");
        bClear = Theme.Btn("فحص جديد", Theme.Gray, 120, "plus");
        bCustomer = Theme.Btn("النتيجة", Theme.Gray, 110, "clipboard-check");
        bSave = Theme.Btn("حفظ في السجل", Theme.Success, 140, "save");
        bReport = Theme.Btn("التقرير", Theme.Gray, 110, "file-text");
        bMore = Theme.Btn("المزيد", Theme.Gray, 100, "ellipsis");
        bLogs = Theme.Btn("السجلات", Theme.Gray, 110, "panel-right");
        tCombine.Margin = new Padding(12, 8, 6, 2);
        // «نص السجل» و«السجلات» في قائمة «المزيد» حتى يبقى الشريط سطرًا واحدًا
        bar.Controls.AddRange(new Control[] { bOpen, bPaste, bDevice, bExam, bClear, tCombine, bCustomer, bSave, bReport, bMore });
        foreach (var b in bar.Controls.OfType<ModernButton>()) { b.Height = 40; b.Margin = new Padding(4, 3, 4, 3); }
        moreMenu.Font = Theme.F(10.5f);
        moreMenu.Items.Add("نص السجل", null, (s, e) => ShowRaw(null));
        var logsItem = new ToolStripMenuItem("إظهار قائمة السجلات", null, (s, e) => { logsToggledByUser = true; SetLogsVisible(!logsCard.Visible); });
        moreMenu.Items.Add(logsItem);
        moreMenu.Opening += (s, e) => logsItem.Text = logsCard.Visible ? "إخفاء قائمة السجلات" : "إظهار قائمة السجلات";
        moreMenu.Items.Add(new ToolStripSeparator());
        moreMenu.Items.Add("الخط الزمني للسجلات", null, (s, e) => ShowTimeline());
        moreMenu.Items.Add("فحوصات سابقة لهذا الجهاز", null, (s, e) => ShowPrevious());
        moreMenu.Items.Add(new ToolStripSeparator());
        moreMenu.Items.Add("طباعة تقرير الفني", null, (s, e) => PrintReport(false));
        moreMenu.Items.Add("طباعة تقرير الزبون", null, (s, e) => PrintReport(true));
        moreMenu.Items.Add("نسخ تقرير الزبون", null, (s, e) => CopyReport(true));
        moreMenu.Items.Add("نسخ تقرير الفني", null, (s, e) => CopyReport(false));

        // ---------- ما حدث للجهاز ----------
        // (في نافذة «الفحص والخطوات» مع سؤال الفحص — كلاهما يعيد ترتيب الأسباب)
        flags.Controls.Add(new Label { Text = "ما حدث للجهاز:", AutoSize = false, Width = 130, Height = 34, Font = Theme.FS(10), ForeColor = Theme.Text2, TextAlign = ContentAlignment.MiddleLeft });
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) { t.Margin = new Padding(6, 0, 14, 0); flags.Controls.Add(t); }

        // ---------- قائمة السجلات (جانبية، تُطوى) ----------
        logsCard = new CardPanel { Dock = DockStyle.Right, Width = 320, Title = "السجلات", IconName = "file-text", Subtitle = "اسحب الملفات إلى هنا", Visible = false };
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

        // ---------- الواجهة 6: «أين العطل داخل الجهاز؟» ----------
        // يمين: سطر الجهاز، العنوان، الإرشاد، ثم أزرار الأسباب. يسار: مخطط الجهاز بطول الشاشة.
        // النقر على سبب أو قطعة يفتح «الفحص والخطوات»: دليلها، الخلاصة، ما حدث للجهاز، سؤال الفحص، الخطوات، الأدلة.
        var main = new Panel { Dock = DockStyle.Fill, BackColor = Palette6.Ground, Margin = new Padding(0) };
        main.Controls.Add(causes);
        main.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 14, BackColor = Palette6.Ground });
        main.Controls.Add(hint);
        main.Controls.Add(heading);
        main.Controls.Add(deviceLine);

        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Palette6.Ground };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        map.Margin = new Padding(28, 0, 0, 0);
        columns.Controls.Add(main, 0, 0);
        columns.Controls.Add(map, 1, 0);

        var page = new Panel { Dock = DockStyle.Fill, BackColor = Palette6.Ground, Padding = new Padding(40, 28, 40, 20) };
        page.Controls.Add(columns);

        // خطوات الفحص والأدلة (تُعرض في نافذة «الفحص والخطوات»)
        stepsCard = new CardPanel { Dock = DockStyle.Fill, Title = "خطوات الفحص", IconName = "list-checks", Subtitle = "انقر على الخطوة عند إنجازها" };
        stepsCard.Controls.Add(steps);
        evidenceCard = new CardPanel { Dock = DockStyle.Fill, Title = "الأدلة من السجل", IconName = "search", Subtitle = "انقر على الدليل لترى مكانه في نص السجل" };
        var evTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.Surface, WrapContents = false };
        tAllEvidence.Margin = new Padding(2, 2, 2, 2);
        evTop.Controls.Add(tAllEvidence);
        evidenceCard.Controls.Add(evidence);
        evidenceCard.Controls.Add(evTop);
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        stepsCard.Margin = new Padding(0, 0, 0, 12);
        evidenceCard.Margin = new Padding(0);
        lists.Controls.Add(stepsCard, 0, 0);
        lists.Controls.Add(evidenceCard, 0, 1);

        var work = new Panel { Dock = DockStyle.Fill, BackColor = Palette6.Ground };
        work.Controls.Add(page);
        work.Controls.Add(logsCard);

        Controls.Add(work);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        Controls.Add(bar);

        // ---------- الأحداث ----------
        bOpen.Click += (s, e) => PickFiles();
        bPaste.Click += (s, e) => PasteClipboard();
        bDevice.Click += (s, e) => PullFromDevice();
        bExam.Click += (s, e) => ShowDetails(null);
        bRaw.Click += (s, e) => ShowRaw(null);
        bClear.Click += (s, e) => ClearAll(ask: true);
        bCustomer.Click += (s, e) => EditCustomer();
        bSave.Click += (s, e) => SaveRecord(silent: false);
        bReport.Click += (s, e) => ShowReport();
        bMore.Click += (s, e) => moreMenu.Show(bMore, new Point(bMore.Width, bMore.Height), ToolStripDropDownDirection.BelowLeft);
        bLogs.Click += (s, e) => { logsToggledByUser = true; SetLogsVisible(!logsCard.Visible); };
        bRemove.Click += (s, e) => RemoveSelected();
        tCombine.CheckedChanged += (s, e) => ShowResult();
        tAllEvidence.CheckedChanged += (s, e) => FillEvidence();
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) t.CheckedChanged += (s, e) => { if (!loading) { dirty = true; Reanalyze(); } };
        logsGrid.SelectionChanged += (s, e) => { if (!loading) ShowResult(); };
        interview.Clicked += OnInterview;
        map.PartClicked += ShowCause;
        DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (s, e) => Drop(e.Data);

        // السجل المحفوظ يُحمَّل عند ظهور الشاشة (بعد ربط الجداول بالنافذة)
        if (rec != null) Load += (s, e) => LoadRecord(rec);
        ShowResult();
    }

    void SetLogsVisible(bool on)
    {
        logsCard.Visible = on;
        bLogs.Text = on ? "إخفاء السجلات" : "السجلات";
        bLogs.FitWidth(110);
        bLogs.Invalidate();
    }

    /// <summary>النقر على قطعة في خريطة الجهاز: تمرير قائمة الأسباب إلى زرها ثم فتح دليلها وخطوات الفحص</summary>
    void ShowCause(string part)
    {
        if (causes.Controls.OfType<CauseButton>().FirstOrDefault(c => c.Part == part) is { } card) causes.ScrollControlIntoView(card);
        ShowDetails(part);
    }

    /// <summary>
    /// «الفحص والخطوات»: دليل القطعة المختارة، الخلاصة، ما حدث للجهاز وسؤال الفحص التفاعلي (يمين)، وخطوات الفحص والأدلة (يسار).
    /// نفس عناصر الشاشة تُنقل إلى النافذة ثم تُعاد بعد إغلاقها، فكل جواب يحدّث الأسباب والخريطة خلفها مباشرة.
    /// </summary>
    void ShowDetails(string part)
    {
        if (shown == null) return;
        var cause = part == null ? null : shown.Candidates.FirstOrDefault(c => c.Part == part);
        using var dlg = new DialogShell(cause?.Part ?? "الفحص والخطوات", 1180, 800, "list-checks");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Surface };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = new Padding(0) };
        grid.Controls.Add(right, 0, 0);

        // العناصر المشتركة تُكبَّر حسب دقة العرض مرة واحدة فقط: في أول فتح تُضاف قبل تكبير النافذة فتُكبَّر معها،
        // وبعدها تُضاف بعد التكبير (في Load) بمقاساتها الفعلية
        void Attach(bool scaled)
        {
            int S(int v) => scaled ? Dpi.S(v) : v;
            right.Controls.Add(interview);
            right.Controls.Add(new Panel { Dock = DockStyle.Top, Height = S(10), BackColor = Theme.Surface });
            right.Controls.Add(flags);
            right.Controls.Add(new Panel { Dock = DockStyle.Top, Height = S(10), BackColor = Theme.Surface });
            right.Controls.Add(verdict);
            if (cause != null)
            {
                var why = $"{cause.Label} — {cause.Why}";
                int h = TextRenderer.MeasureText(why, Theme.F(11), new Size(Dpi.S(500), int.MaxValue), TextFormatFlags.WordBreak).Height;
                right.Controls.Add(new Label
                {
                    Dock = DockStyle.Top, AutoSize = false, Text = why, Font = Theme.F(11), ForeColor = Palette6.PetrolText, BackColor = Palette6.PetrolSoft,
                    Padding = new Padding(S(12), S(8), S(12), S(8)), Height = (scaled ? h : Dpi.U(h)) + S(22), TextAlign = ContentAlignment.MiddleLeft,
                });
            }
            lists.Margin = new Padding(S(14), 0, 0, 0);
            grid.Controls.Add(lists, 1, 0);
        }
        if (!detailsScaled) { Attach(false); detailsScaled = true; }
        else dlg.Load += (s, e) => Attach(true);
        dlg.Body.Controls.Add(grid);
        dlg.AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        try { dlg.ShowModal(); }
        finally
        {
            // إعادة العناصر المشتركة قبل إغلاق النافذة حتى لا تُتلف معها
            foreach (var c in new Control[] { interview, flags, verdict, lists }) c.Parent?.Controls.Remove(c);
        }
    }

    // ============================================================== الإدخال
    void PickFiles()
    {
        if (busy || !Session.Guard("analyze")) return;
        using var ofd = new OpenFileDialog
        {
            Multiselect = true, Title = "اختر ملفات البانك",
            Filter = "سجلات البانك أو صورها (*.ips;*.txt;*.json;*.log;*.panic;*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.heic)|*.ips;*.txt;*.json;*.log;*.panic;*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.heic|كل الملفات (*.*)|*.*",
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
        if (!logsCard.Visible) SetLogsVisible(true);
        var progress = new Progress<int>(n => logsCard.Subtitle = $"جارٍ قراءة الملفات: {n} من {list.Count}…");
        (List<PanicLog> Found, List<string> Skipped) result;
        try
        {
            result = await Task.Run(async () =>
            {
                var found = new List<PanicLog>();
                var skippedFiles = new List<string>();
                IProgress<int> p = progress;
                // الصور (لقطات شاشة للسجل): تُقرأ بقارئ النصوص بترتيب أسمائها ثم تُحلَّل نصًا واحدًا،
                // فالسجل الطويل المصوَّر على عدة لقطات يُجمع كاملًا
                var images = list.Where(ImageText.IsImage).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
                if (images.Count > 0)
                {
                    var texts = new List<string>();
                    foreach (var f in images)
                    {
                        try
                        {
                            if (new FileInfo(f).Length > MaxFileBytes) { skippedFiles.Add($"{Path.GetFileName(f)}: حجمه أكبر من 20 ميغابايت"); continue; }
                            var t = await ImageText.ReadAsync(f);
                            if (t == null) { skippedFiles.Add(ImageText.NoEngine); break; }
                            texts.Add(t);
                        }
                        catch (Exception ex) { skippedFiles.Add($"{Path.GetFileName(f)}: تعذرت قراءة الصورة — {ex.Message}"); }
                    }
                    if (texts.Count > 0)
                    {
                        var joined = string.Join("\n", texts);
                        var name = images.Count == 1 ? Path.GetFileName(images[0]) + " (صورة)" : $"{images.Count} صور ({Path.GetFileName(images[0])} …)";
                        var fromImages = PanicParser.ParseMany(joined, name);
                        if (fromImages.Count == 0) skippedFiles.Add($"{name}: لم يُقرأ منها سجل بانك — صوّر السجل كاملًا وبوضوح، أو انسخ نصه والصقه.");
                        found.AddRange(fromImages);
                    }
                }
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (ImageText.IsImage(f)) { p.Report(i + 1); continue; }
                    try
                    {
                        var info = new FileInfo(f);
                        if (!info.Exists) continue;
                        if (info.Length > MaxFileBytes) { skippedFiles.Add($"{info.Name}: حجمه أكبر من 20 ميغابايت"); continue; }
                        var text = File.ReadAllText(f, System.Text.Encoding.UTF8);
                        var found1 = PanicParser.ParseMany(text, info.Name);
                        if (found1.Count == 0) skippedFiles.Add($"{info.Name}: " + (PanicParser.ExplainNonPanic(text, info.Name) ?? "لا يحتوي سجل بانك"));
                        found.AddRange(found1);
                    }
                    catch (Exception ex) { skippedFiles.Add($"{Path.GetFileName(f)}: {ex.Message}"); }
                    p.Report(i + 1);
                }
                return (found, skippedFiles);
            });
        }
        finally { busy = false; }
        if (IsDisposed) return;
        Merge(result.Found);
        // سجل واحد: القائمة الجانبية تُطوى بعد القراءة حتى تبقى المساحة للأعمدة الثلاثة
        if (!logsToggledByUser && logs.Count <= 1 && logsCard.Visible) SetLogsVisible(false);
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
        if (!logsToggledByUser && logs.Count > 1 && !logsCard.Visible) SetLogsVisible(true);
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
            if (Clipboard.ContainsImage() && !Clipboard.ContainsText()) { PasteImage(Clipboard.GetImage()); return; }
            if (!Clipboard.ContainsText()) { Ui.Warn("الحافظة لا تحتوي نصًا. انسخ نص السجل أولًا — أو افتح «نص السجل» والصقه هناك."); return; }
            AddText(Clipboard.GetText(), "نص ملصوق");
        }
        catch (Exception ex) { Ui.Warn("تعذرت قراءة الحافظة: " + ex.Message); }
    }

    /// <summary>
    /// سحب سجلات البانك من الآيفون الموصول بالكيبل (يحتاج خدمة Apple Mobile Device من iTunes أو Apple Devices،
    /// وأن يكون الجهاز قد وثق بهذا الكمبيوتر). قراءة فقط: لا يُحذف شيء من الجهاز.
    /// </summary>
    async void PullFromDevice()
    {
        if (busy || !Session.Guard("analyze")) return;
        busy = true;
        UpdateButtons();
        if (!logsCard.Visible) SetLogsVisible(true);
        var old = logsCard.Subtitle;
        var progress = new Progress<string>(t => logsCard.Subtitle = t);
        Kashif.Device.DevicePull pull;
        try { pull = await Task.Run(() => Kashif.Device.CrashReports.Pull(progress: progress)); }
        catch (Kashif.Device.DeviceException ex) { Dialogs.Warn(ex.Message, "السحب من الآيفون"); return; }
        catch (Exception ex) { Dialogs.Warn("تعذر السحب من الآيفون: " + ex.Message, "السحب من الآيفون"); return; }
        finally
        {
            busy = false;
            if (!IsDisposed) { logsCard.Subtitle = old; UpdateButtons(); }
        }
        if (IsDisposed) return;
        var device = string.Join(" · ", new[] { pull.DeviceName, AppleDevices.Name(pull.ProductType), pull.Version == "" ? "" : "iOS " + pull.Version }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (pull.Logs.Count == 0)
        {
            Dialogs.Message($"{device}\n\nلا توجد سجلات بانك في الجهاز.\nإن كان الجهاز يعيد التشغيل فعلًا، انتظر حتى يحدث البانك مرة ثم أعد السحب.", "السحب من الآيفون", Tone.Info);
            if (logs.Count <= 1 && !logsToggledByUser) SetLogsVisible(false);
            return;
        }
        var found = pull.Logs.SelectMany(l => PanicParser.ParseMany(l.Text, l.Name)).ToList();
        Merge(found);
        var notes = new List<string>();
        if (pull.Older > 0) notes.Add($"سُحب أحدث {pull.Logs.Count} سجل، وفي الجهاز {pull.Older} سجلات أقدم لم تُسحب.");
        if (pull.Failed > 0) notes.Add($"تعذرت قراءة {pull.Failed} سجل.");
        Toast.Show($"سُحب {pull.Logs.Count} سجل من {device}");
        if (notes.Count > 0) Dialogs.Message(string.Join("\n", notes), "السحب من الآيفون", Tone.Info);
    }

    /// <summary>صورة في الحافظة (لقطة شاشة للسجل): تُقرأ بقارئ النصوص في الخلفية ثم تُحلَّل</summary>
    async void PasteImage(Image img)
    {
        if (img == null) return;
        byte[] png;
        using (var ms = new MemoryStream()) { img.Save(ms, System.Drawing.Imaging.ImageFormat.Png); png = ms.ToArray(); }
        img.Dispose();
        busy = true;
        UpdateButtons();
        string text;
        try { text = await Task.Run(() => ImageText.ReadAsync(png)); }
        catch (Exception ex) { Ui.Warn("تعذرت قراءة الصورة: " + ex.Message); return; }
        finally { busy = false; if (!IsDisposed) UpdateButtons(); }
        if (IsDisposed) return;
        if (text == null) { Ui.Warn(ImageText.NoEngine); return; }
        AddText(text, "صورة ملصوقة");
    }

    void Drop(IDataObject data)
    {
        if (busy || !Session.Can("analyze")) return;
        if (data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
        else if (data.GetData(DataFormats.UnicodeText) is string text) AddText(text, "نص مسحوب");
        else if (data.GetData(DataFormats.Bitmap) is Image img) PasteImage(img);
    }

    /// <summary>نافذة نص السجل: عرض السجل المحدد (مظللًا عند الدليل)، أو لصق/تعديل نص ثم تحليله</summary>
    void ShowRaw(Evidence focus)
    {
        int i = SelectedIndex();
        var current = i >= 0 ? logs[i].Raw : "";
        using var dlg = new RawDialog(current, focus?.Needle, i >= 0 ? logs[i].Source : "نص جديد");
        if (dlg.ShowModal() != DialogResult.OK || !Session.Guard("analyze")) return;
        var text = dlg.EditedText;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (i < 0) { AddText(text, "نص ملصوق"); return; }
        if (logs[i].Raw == PanicParser.Normalize(text)) return;
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
        customerName = phoneText = notesText = fixedPartText = "";
        statusText = PanicStore.Statuses[0];
        foreach (var t in new[] { tLiquid, tBattery, tFlex, tScreen, tDrop }) t.Checked = false;
        doneSteps.Clear();
        answers.Clear();
        skipped.Clear();
        logsToggledByUser = false;
        SetLogsVisible(false);
        loading = false;
        dirty = false;
        Text = PageTitle;
        Reanalyze();
    }

    void LoadRecord(PanicStore.Record rec)
    {
        loading = true;
        recordId = rec.Id;
        customerName = rec.Customer; phoneText = rec.Phone; notesText = rec.Notes; fixedPartText = rec.FixedPart;
        statusText = PanicStore.Statuses.Contains(rec.Status) ? rec.Status : PanicStore.Statuses[0];
        tLiquid.Checked = rec.Flags.Liquid; tBattery.Checked = rec.Flags.BatteryReplaced; tFlex.Checked = rec.Flags.ChargingFlexReplaced;
        tScreen.Checked = rec.Flags.ScreenReplaced; tDrop.Checked = rec.Flags.Dropped;
        answers.AddRange(rec.Answers.Where(a => a.Answer >= 0));
        foreach (var a in rec.Answers.Where(a => a.Answer < 0)) skipped.Add(a.Id);
        foreach (var (source, text) in rec.Logs)
        {
            var one = PanicParser.Parse(text, source);
            if (!one.IsEmpty) logs.Add(one);
        }
        if (logs.Count > 1) SetLogsVisible(true);
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

    /// <summary>
    /// النتيجة المعروضة: تحليل السجل المحدد (أو المجمّع لكل سجلات نفس الجهاز)، ثم أجوبة الفحص التفاعلي فوقه.
    /// يُبنى تحليل جديد في كل مرة حتى لا تتراكم الأجوبة على نفس الكائن.
    /// </summary>
    void ShowResult()
    {
        int i = SelectedIndex();
        if (i < 0)
        {
            shown = null;
            shownLogs = new();
            verdict.Show(null, null);
            interview.Set(null, 0, Array.Empty<string>(), false);
            interview.Visible = false;
            deviceLine.Text = "لم يُفتح سجل بعد";
            hint.Text = "افتح ملف panic-full من «فتح ملفات» أو الصق نصه من «لصق» — أو اسحب الملف أو لقطة شاشة للسجل إلى هنا.";
            causes.SetItems(Array.Empty<StackItem>());
            map.Set(null);
            steps.SetItems(Array.Empty<StackItem>());
            evidence.SetItems(Array.Empty<StackItem>());
            UpdateButtons();
            return;
        }
        var groups = PanicAnalyzer.GroupDevices(logs);
        var group = Enumerable.Range(0, logs.Count).Where(k => groups[k] == groups[i]).ToList();
        if (tCombine.Checked && group.Count > 1)
        {
            shown = PanicAnalyzer.Combine(group.Select(k => diags[k]).ToList());
            shownLogs = group.Select(k => logs[k]).ToList();
        }
        else
        {
            shown = PanicAnalyzer.Analyze(logs[i], Flags(), PanicStore.Rules());
            shownLogs = new List<PanicLog> { logs[i] };
        }
        PanicAnalyzer.ApplyAnswers(shown, answers);

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

        // الفحص التفاعلي
        var skip = skipped.ToList();
        if (tLiquid.Checked) skip.Add("liquid_seen");
        var next = PanicAnalyzer.NextQuestion(shown, answers, skip, out int remaining);
        var answeredLines = answers.Select(a => PanicKnowledge.Current.Questions.FirstOrDefault(q => q.Id == a.Id) is { } q && a.Answer < q.Answers.Length
            ? $"{Ui.Cut(q.Text, 60)}{(q.Text.Length > 60 ? "…" : "")} ← {q.Answers[a.Answer].Label}" : null).Where(x => x != null);
        bool confirmed = shown.Summary.StartsWith("مؤكد بالفحص", StringComparison.Ordinal);
        interview.Set(next, remaining, answeredLines, confirmed);
        interview.Visible = shown.Candidates.Count > 0;

        deviceLine.Text = string.Join(" · ", new[] { shown.Device, shown.Title }.Where(x => !string.IsNullOrWhiteSpace(x)));
        hint.Text = "اللون الأغمق = الأرجح. اضغط على أي قطعة لترى الدليل وخطوة فحصها.";

        causes.SetItems(shown.Candidates.Select((c, k) =>
        {
            var card = new CauseButton(k + 1, c);
            card.Click += (o, e) => { map.Select(card.Part); ShowDetails(card.Part); };
            return (StackItem)card;
        }));
        map.Set(shown);
        steps.SetItems(shown.Steps.Select((s, k) =>
        {
            var card = new StepCard(k + 1, s, doneSteps.Contains(s));
            card.DoneChanged += (o, e) => { if (card.Done) doneSteps.Add(card.StepText); else doneSteps.Remove(card.StepText); };
            return (StackItem)card;
        }));
        stepsCard.Subtitle = shown.Steps.Count == 0 ? "" : $"{shown.Steps.Count} خطوات — انقر على الخطوة عند إنجازها";
        FillEvidence();
        UpdateButtons();
    }

    /// <summary>الأدلة الأساسية وأجوبة الفحص — ومعلومات السجل الإضافية عند تفعيل «كل التفاصيل»</summary>
    void FillEvidence()
    {
        if (shown == null) { evidence.SetItems(Array.Empty<StackItem>()); return; }
        var list = shown.Evidence.Where(e => tAllEvidence.Checked || !e.IsInfo).ToList();
        evidence.SetItems(list.Select(e =>
        {
            var card = new EvidenceCard(e);
            card.Open += (s, a) => ShowRaw(e);
            return (StackItem)card;
        }));
        int info = shown.Evidence.Count(e => e.IsInfo);
        evidenceCard.Subtitle = tAllEvidence.Checked || info == 0 ? "انقر على الدليل لترى مكانه في نص السجل" : $"انقر على الدليل لترى مكانه — و{info} معلومات أخرى في «كل التفاصيل»";
    }

    void OnInterview(int action)
    {
        if (shown == null) return;
        switch (action)
        {
            case InterviewView.Undo:
                if (answers.Count > 0) answers.RemoveAt(answers.Count - 1);
                break;
            case InterviewView.Reset:
                if (!Ui.Confirm("إعادة الفحص التفاعلي من البداية؟ تُمسح كل الأجوبة.")) return;
                answers.Clear();
                skipped.Clear();
                break;
            case InterviewView.Skip:
                if (interview.Question != null) skipped.Add(interview.Question.Id);
                break;
            default:
                if (interview.Question != null && action >= 0 && action < interview.Question.Answers.Length) answers.Add((interview.Question.Id, action));
                break;
        }
        dirty = true;
        ShowResult();
    }

    void UpdateButtons()
    {
        foreach (var b in new[] { bOpen, bPaste, bDevice, bRaw, bClear }) b.Enabled = !busy;
        bSave.Enabled = !busy && shown != null && Session.Can("history");
        bReport.Enabled = bCustomer.Enabled = bExam.Enabled = shown != null;
        bRemove.Enabled = !busy && logs.Count > 0;
        bSave.Text = recordId > 0 ? "تحديث السجل" : "حفظ في السجل";
        bSave.Invalidate();
    }

    // ============================================================== الزبون والحفظ والتقارير
    void EditCustomer()
    {
        using var dlg = new CustomerDialog(statusText, fixedPartText, notesText, shown);
        if (dlg.ShowModal() != DialogResult.OK) return;
        statusText = dlg.Status; fixedPartText = dlg.FixedPart; notesText = dlg.Notes;
        dirty = true;
        if (dlg.SaveNow) SaveRecord(silent: false);
    }

    void SaveRecord(bool silent)
    {
        if (shown == null) { if (!silent) Ui.Warn("لا يوجد تحليل لحفظه."); return; }
        if (!Session.Guard("history")) return;
        try
        {
            bool isNew = recordId == 0;
            var all = answers.Concat(skipped.Select(id => (id, -1))).ToList();
            // السجل الواحد يخص جهازًا واحدًا: تُحفظ سجلات الجهاز المعروض فقط
            recordId = PanicStore.Save(recordId, shown, shownLogs, Flags(), customerName, phoneText, notesText, statusText, fixedPartText, all);
            if (isNew) Db.Audit("حفظ فحص", $"رقم {recordId}: {shown.Device} — {shown.TopPart}");
            if (fixedPartText.Trim() != "") Db.Audit("نتيجة فحص", $"رقم {recordId}: الأرجح {shown.TopPart} — أُصلح بـ {fixedPartText.Trim()}");
            dirty = false;
            Text = $"فحص رقم {recordId}";
            UpdateButtons();
            if (!silent) Toast.Show(isNew ? $"حُفظ الفحص برقم {recordId}" : "تم تحديث السجل");
        }
        catch (Exception ex) { if (!silent) Ui.Warn("تعذر الحفظ: " + ex.Message); }
    }

    string ReportText(bool forCustomer) => shown == null ? "" : forCustomer ? PanicAnalyzer.CustomerReport(shown, Settings.ShopName) : PanicAnalyzer.Report(shown);

    void ShowReport()
    {
        if (shown == null) return;
        using var dlg = new ReportDialog(ReportText(false), ReportText(true));
        dlg.ShowModal();
        if (dlg.PrintRequest is bool customer) PrintReport(customer);
    }

    void CopyReport(bool forCustomer)
    {
        if (shown == null) return;
        try { Clipboard.SetText(ReportText(forCustomer)); Toast.Show(forCustomer ? "نُسخ تقرير الزبون — الصقه في رسالة" : "نُسخ تقرير الفني"); }
        catch (Exception ex) { Ui.Warn("تعذر النسخ: " + ex.Message); }
    }

    /// <summary>طباعة تقرير الفني (كل الأسباب والخطوات وأجوبة الفحص) أو تقرير الزبون (مبسّط بلا رموز)</summary>
    void PrintReport(bool forCustomer)
    {
        if (shown == null || !Session.Guard("print")) return;
        var d = shown;
        var doc = PrintDoc.Header(forCustomer ? "تقرير فحص الجهاز" : "تقرير فحص الجهاز — للفني");
        doc.ForceA4 = true;
        doc.Pair("الجهاز", d.Device == "" ? "غير معروف" : d.Device);
        doc.Line();
        if (forCustomer)
        {
            doc.Text("المشكلة: " + PanicAnalyzer.CustomerProblem(d), 11);
            if (d.TopPart != "") doc.Text("السبب المرجّح: " + d.TopPart + (d.Candidates.Count > 1 ? " (وقد يكون: " + d.Candidates[1].Part + ")" : ""), 11, true);
            doc.Text("درجة الثقة: " + d.Confidence + (d.Summary.StartsWith("مؤكد بالفحص", StringComparison.Ordinal) ? " — مؤكد بالفحص العملي" : ""), 10);
            if (fixedPartText.Trim() != "") doc.Text("ما تم إصلاحه: " + fixedPartText.Trim(), 11, true);
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
            var exam = d.Evidence.Where(e => e.IsExam).ToList();
            if (exam.Count > 0)
            {
                doc.Space(6);
                doc.Table(new[] { "سؤال الفحص", "الجواب" }, new[] { 60f, 40 }, exam.Select(e => new[] { e.Meaning, e.Value }).ToList());
            }
            if (d.Steps.Count > 0)
            {
                doc.Space(6);
                doc.Table(new[] { "#", "خطوات الفحص", "تم" }, new[] { 6f, 84, 10 }, d.Steps.Select((s, k) => new[] { (k + 1).ToString(), s, doneSteps.Contains(s) ? "✓" : "" }).ToList());
            }
            if (notesText.Trim() != "") { doc.Space(6); doc.Text("ملاحظات الفحص: " + notesText.Trim(), 10); }
        }
        doc.Footer();
        doc.Print();
    }

    void ShowTimeline()
    {
        if (shown == null) return;
        var dt = new DataTable();
        foreach (var c in new[] { "الوقت", "النوع", "التشخيص", "الأرجح", "المصدر" }) dt.Columns.Add(c);
        var items = shown.Timeline.Count > 0 ? shown.Timeline
            : new List<TimelineItem> { new(shown.Log?.Time, shown.Time, shown.Kind, shown.Title, shown.TopPart, shown.Signature, shown.Log?.Source ?? "") };
        foreach (var t in items) dt.Rows.Add(t.TimeText, t.Kind, t.Title, t.TopPart, t.Source);
        var gap = shown.Evidence.FirstOrDefault(e => e.What == "المدة بين البانكات");
        var note = items.Count < 2 ? "سجل واحد — افتح سجلات أخرى لنفس الجهاز وفعّل «تجميع سجلات الجهاز» لترى تكرار البانك مع الوقت."
            : gap != null ? $"{items.Count} سجلات — المدة بين البانكات: {gap.Value} — {gap.Meaning}" : $"{items.Count} سجلات مرتبة من الأقدم";
        using var dlg = new GridDialog("الخط الزمني للسجلات", "history", dt, note, ("الوقت", 22), ("النوع", 14), ("التشخيص", 34), ("الأرجح", 22), ("المصدر", 8));
        dlg.ShowModal();
    }

    void ShowPrevious()
    {
        if (shown == null) return;
        if (!Session.Can("history")) { Ui.Warn("ليس لديك صلاحية سجل الفحوصات."); return; }
        var key = shown.Log?.DeviceKey ?? "";
        var prev = PanicStore.Previous(key, recordId);
        var note = key == "" ? "لا يوجد مفتاح جهاز في السجل لمطابقة الفحوصات السابقة."
            : prev.Rows.Count == 0 ? "لم يُفحص هذا الجهاز من قبل في المحل." : $"فُحص هذا الجهاز {prev.Rows.Count} مرة من قبل — اختر فحصًا ثم «فتح» أو «مقارنة».";
        using var dlg = new GridDialog("فحوصات سابقة لهذا الجهاز", "repeat", prev, note, ("التاريخ", 16), ("التشخيص", 30), ("الأرجح", 20), ("القطعة المُصلِحة", 20), ("الحالة", 14));
        var bOpenRec = dlg.AddButton("فتح الفحص", DialogResult.Yes, BtnKind.Secondary, "eye");
        var bCmp = dlg.AddButton("مقارنة مع الحالي", DialogResult.Retry, BtnKind.Primary, "arrow-left-right");
        bOpenRec.Enabled = bCmp.Enabled = prev.Rows.Count > 0;
        var r = dlg.ShowModal();
        long id = dlg.SelectedId;
        if (id <= 0) return;
        if (r == DialogResult.Yes) OpenRecord(id);
        else if (r == DialogResult.Retry) Compare(id);
    }

    void Compare(long id)
    {
        var rec = PanicStore.Load(id);
        if (rec == null) return;
        var rules = PanicStore.Rules();
        var ds = rec.Logs.Select(x => PanicParser.Parse(x.Raw, x.Source)).Where(l => !l.IsEmpty).Select(l => PanicAnalyzer.Analyze(l, rec.Flags, rules)).ToList();
        if (ds.Count == 0) { Ui.Warn("الفحص السابق لا يحتوي سجلات قابلة للتحليل."); return; }
        var other = PanicAnalyzer.Combine(ds);
        if (ds.Count == 1) other = PanicAnalyzer.Analyze(ds[0].Log, rec.Flags, rules);
        PanicAnalyzer.ApplyAnswers(other, rec.Answers.Where(a => a.Answer >= 0).ToList());
        using var dlg = new CompareDialog(shown, "الفحص الحالي", other, $"فحص رقم {rec.Id} ({Ui.Cut(rec.Date, 10)})", fixedPartText.Trim(), rec.FixedPart);
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
            case Keys.Control | Keys.V when ActiveControl is not TextBoxBase: PasteClipboard(); return true;
            case Keys.F9: ShowRaw(null); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

// ============================================================== النوافذ المساعدة

/// <summary>نص السجل: عرض وتعديل، وتظليل مكان دليل محدد</summary>
public class RawDialog : DialogShell
{
    readonly RichTextBox raw = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, WordWrap = false, DetectUrls = false, HideSelection = false,
        RightToLeft = RightToLeft.No, BackColor = Theme.SurfaceAlt, ForeColor = Theme.Ink, ScrollBars = RichTextBoxScrollBars.Both,
    };

    public string EditedText => raw.Text;

    public RawDialog(string text, string needle, string source) : base("نص السجل — " + source, 1100, 760, "scroll-text")
    {
        try { raw.Font = new Font("Consolas", 10.5f); } catch { raw.Font = Theme.F(10); }
        raw.Text = text ?? "";
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Muted, Font = Theme.F(9.5f), TextAlign = ContentAlignment.MiddleLeft,
            Text = string.IsNullOrEmpty(text) ? "الصق نص البانك هنا ثم «تحليل النص»." : "عدّل النص إن لزم ثم «تحليل النص» — أو أغلق النافذة بلا تغيير.",
        };
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Padding = new Padding(8) };
        host.Controls.Add(raw);
        Body.Controls.Add(host);
        Body.Controls.Add(hint);
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary, "x");
        AddButton("تحليل النص", DialogResult.OK, BtnKind.Primary, "scan-line");
        Shown += (s, e) => { Highlight(needle); raw.Focus(); };
    }

    /// <summary>يظلّل أول ظهور للنص (يُبحث أيضًا بصيغة JSON حيث «/» مكتوبة «\/»)</summary>
    void Highlight(string needle)
    {
        if (string.IsNullOrEmpty(needle) || raw.TextLength == 0) return;
        var text = raw.Text;
        int at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase), len = needle.Length;
        if (at < 0) { var alt = needle.Replace("/", "\\/"); at = text.IndexOf(alt, StringComparison.OrdinalIgnoreCase); len = alt.Length; }
        if (at < 0) { Toast.Show("لم يُعثر على مكان الدليل في النص", Tone.Info); return; }
        raw.Select(at, len);
        raw.SelectionBackColor = Theme.Amber;
        raw.ScrollToCaret();
    }
}

/// <summary>نتيجة الإصلاح: حالة الجهاز، والقطعة التي أصلحته فعلًا (تحسب دقة البرنامج وتقترح قواعد جديدة)، والملاحظات</summary>
public class CustomerDialog : DialogShell
{
    readonly ComboBox status = Ui.Combo(220);
    readonly ComboBox fixedPart = new() { Width = 540, DropDownStyle = ComboBoxStyle.DropDown, Font = Theme.F(10) };
    readonly TextBox notes = new() { Width = 760, Height = 100, Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "ما وجدته عند الفحص، ما جرّبته، النتيجة..." };

    public string Status => status.Text;
    public string FixedPart => fixedPart.Text.Trim();
    public string Notes => notes.Text.Trim();
    public bool SaveNow { get; private set; }

    public CustomerDialog(string st, string fixedText, string n, Diagnosis d) : base("النتيجة", 840, 500, "clipboard-check")
    {
        status.Items.AddRange(PanicStore.Statuses);
        status.SelectedIndex = Math.Max(0, Array.IndexOf(PanicStore.Statuses, st));
        // الأسباب المقترحة أولًا ثم بقية القطع
        var suggested = d?.Candidates.Select(x => x.Part).ToList() ?? new List<string>();
        fixedPart.Items.AddRange(suggested.Concat(Parts.All.Where(x => !suggested.Contains(x)).OrderBy(x => x)).Cast<object>().ToArray());
        fixedPart.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        fixedPart.AutoCompleteSource = AutoCompleteSource.ListItems;
        fixedPart.Text = fixedText; notes.Text = n;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, AutoScroll = true, Padding = new Padding(4, 6, 4, 4) };
        flow.Controls.Add(Ui.Labeled("حالة الجهاز", status));
        flow.SetFlowBreak(flow.Controls[^1], true);
        flow.Controls.Add(Ui.Labeled("القطعة التي أصلحت الجهاز فعلًا (بعد الإصلاح)", fixedPart));
        flow.SetFlowBreak(flow.Controls[^1], true);
        flow.Controls.Add(new Label
        {
            Text = "تسجيلها يحسب دقة البرنامج في محلك (التقارير ← دقة التشخيص) ويقترح قواعد جديدة لخبرة المحل.",
            AutoSize = false, Width = 760, Height = 28, ForeColor = Theme.Muted, Font = Theme.F(9), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 0, 6, 6),
        });
        flow.SetFlowBreak(flow.Controls[^1], true);
        flow.Controls.Add(Ui.Labeled("ملاحظات الفحص", notes));
        Body.Controls.Add(flow);
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary, "x");
        AddButton("موافق", DialogResult.OK, BtnKind.Secondary, "check");
        var save = AddButton("موافق وحفظ في السجل", DialogResult.OK, BtnKind.Primary, "save");
        save.Click += (s, e) => SaveNow = true;
        Shown += (s, e) => fixedPart.Focus();
    }
}

/// <summary>التقرير: للفني (كل التفاصيل) أو للزبون (مبسّط) — نسخ أو طباعة</summary>
public class ReportDialog : DialogShell
{
    readonly TextBox box = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Ink };
    readonly Toggle tCustomer = new() { Text = "تقرير الزبون (مبسّط)", Width = 220 };

    /// <summary>طلب طباعة بعد الإغلاق: true للزبون، false للفني، null بلا طباعة</summary>
    public bool? PrintRequest { get; private set; }

    public ReportDialog(string tech, string customer) : base("التقرير", 980, 740, "file-text")
    {
        box.Font = Theme.F(10.5f);
        void Fill() => box.Text = (tCustomer.Checked ? customer : tech).Replace("\n", "\r\n");
        Fill();
        tCustomer.CheckedChanged += (s, e) => Fill();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Surface, WrapContents = false };
        tCustomer.Margin = new Padding(4, 4, 4, 2);
        top.Controls.Add(tCustomer);
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(4) };
        host.Controls.Add(box);
        Body.Controls.Add(host);
        Body.Controls.Add(top);
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary, "x");
        var copy = AddButton("نسخ", DialogResult.None, BtnKind.Secondary, "copy");
        copy.Click += (s, e) =>
        {
            try { Clipboard.SetText(tCustomer.Checked ? customer : tech); Toast.Show("نُسخ التقرير"); }
            catch (Exception ex) { Ui.Warn("تعذر النسخ: " + ex.Message); }
        };
        var print = AddButton("طباعة", DialogResult.OK, BtnKind.Primary, "printer");
        print.Click += (s, e) => PrintRequest = tCustomer.Checked;
    }
}

/// <summary>جدول في نافذة (الخط الزمني، الفحوصات السابقة) مع ملاحظة، وأزرار يضيفها المستدعي</summary>
public class GridDialog : DialogShell
{
    readonly DataGridView grid = Ui.NewGrid();

    /// <summary>رقم السجل المحدد (إن كان في الجدول عمود id)</summary>
    public long SelectedId => grid.CurrentRow != null && grid.Columns.Contains("id") ? Db.L(grid.CurrentRow.Cells["id"].Value) : 0;

    public GridDialog(string title, string icon, DataTable data, string note, params (string Name, float Weight)[] cols) : base(title, 1000, 620, icon)
    {
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.ScrollBars = ScrollBars.Vertical;
        grid.DataBindingComplete += (s, e) =>
        {
            foreach (var (name, w) in cols)
                if (grid.Columns.Contains(name)) { grid.Columns[name].FillWeight = w; grid.Columns[name].MinimumWidth = Dpi.S(60); }
        };
        grid.DataSource = data;
        var lbl = new Label { Dock = DockStyle.Top, Height = 36, Text = note, ForeColor = Theme.Text2, Font = Theme.F(10), TextAlign = ContentAlignment.MiddleLeft };
        Body.Controls.Add(grid);
        Body.Controls.Add(lbl);
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary, "x");
    }
}

/// <summary>مقارنة فحصين لنفس الجهاز جنبًا إلى جنب (مثل قبل الإصلاح وبعده)</summary>
public class CompareDialog : DialogShell
{
    public CompareDialog(Diagnosis a, string aTitle, Diagnosis b, string bTitle, string aFixed, string bFixed) : base("مقارنة فحصين", 1000, 640, "arrow-left-right")
    {
        var grid = Ui.NewGrid();
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        var dt = new DataTable();
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
        Row("أجوبة الفحص", a.AnswersApplied.ToString(), b.AnswersApplied.ToString());
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
