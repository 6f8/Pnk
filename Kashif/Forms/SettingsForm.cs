namespace Kashif;

/// <summary>الإعدادات مقسّمة إلى أقسام واضحة بعناصر مفهومة (مفاتيح تشغيل، قوائم، اختيار مجلدات)</summary>
public class SettingsForm : BaseForm
{
    readonly Dictionary<string, Func<string>> getters = new();
    readonly ModernTabs tabs = new(vertical: true) { Dock = DockStyle.Fill };
    FlowLayoutPanel cur;

    readonly List<string> sections = new();
    readonly List<FlowLayoutPanel> flows = new();

    /// <summary>الإعدادات، أو قسم محدد منها (مثل «الطباعة والتقارير» من قسم الأدوات)</summary>
    public SettingsForm(string section = null)
    {
        Section("إعدادات النظام", "store", "بيانات المحل وصوره والمظهر");
        Images();
        TextField("shop_name", 360);
        TextField("shop_city", 360);
        TextField("shop_phone", 360);
        TextField("shop_activity", 360);
        TextField("shop_address", 736);
        Choice("ui_theme", Theme.Palettes.Select(p => (p.Key, p.Name)).ToArray());
        Hint("يُطبَّق المظهر الجديد بعد إعادة تشغيل البرنامج.");

        Section("التحليل", "scan-line", "طريقة تحليل السجلات وحفظها");
        Switch("analyze_combine", "تجميع سجلات نفس الجهاز في تحليل واحد (افتراضيًا)");
        Hint("التجميع: تكرار نفس الحساس في عدة سجلات يرفع الثقة، واختلاف أنواع البانك يرجّح سببًا عامًا (بطارية، طاقة، تأكسد، بوردة).");
        Switch("analyze_autosave", "حفظ كل تحليل في سجل الفحوصات تلقائيًا");
        Hint("قاعدة المعرفة مدمجة في البرنامج. أضف ما تتأكد منه بنفسك من: قاعدة المعرفة ← خبرة المحل.");

        Section("الطباعة والتقارير", "printer", "طريقة طباعة تقارير الفحص والجداول");
        Choice("print_mode", ("A4", "ورق A4 عادي"), ("80mm", "طابعة إيصالات حرارية 80 ملم"));
        Printer("printer_name");
        Switch("print_preview", "عرض معاينة قبل الطباعة");
        TextField("invoice_footer", 736);
        Hint("رأس المطبوعات: صورة الترويسة إن وُجدت (من «إعدادات النظام»)، وإلا الشعار واسم المحل وبياناته.");
        Act("طباعة صفحة تجريبية", "printer", TestPrint);

        Section("النسخ الاحتياطي", "cloud-upload", "حماية بياناتك من الضياع");
        Folder("backup_dir", "افتراضيًا داخل مجلد البرنامج");
        Folder("cloud_dir", "مجلد Google Drive أو OneDrive (اختياري)");
        Number("backup_keep", 175);
        Switch("backup_on_exit", "نسخة احتياطية تلقائية عند إغلاق البرنامج");
        Act("نسخ احتياطي الآن", "cloud-upload", BackupNow, BtnKind.Soft);
        Act("استعادة نسخة", "rotate-ccw", RestoreBackup, BtnKind.Danger);
        Act("فتح مجلد النسخ", "folder-open", () =>
        {
            Directory.CreateDirectory(Backup.LocalDir);
            System.Diagnostics.Process.Start("explorer.exe", Backup.LocalDir);
        }, BtnKind.Secondary);

        Section("حول البرنامج", "info", "الإصدار ومكان حفظ البيانات");
        Hint($"كاشف — الإصدار {Application.ProductVersion.Split('+')[0]}");
        Hint("قاعدة البيانات: " + Db.FilePath);
        Hint("الخطوط: IBM Plex Sans Arabic (رخصة OFL) — الأيقونات: Lucide (رخصة ISC)");
        Act("فتح مجلد البيانات", "folder-open", () => System.Diagnostics.Process.Start("explorer.exe", Db.DataDir), BtnKind.Secondary);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Bg, Padding = new Padding(0, 14, 0, 0) };
        var bSave = new ModernButton { Text = "حفظ الإعدادات", IconName = "save", Dock = DockStyle.Right, Width = 170, Height = 44 };
        bSave.Click += (s, e) => Save();
        // الترتيب مهم: عنصر الملء أولاً ثم العنصر الملتصق بالحافة
        bar.Controls.Add(new Label
        {
            Text = "التغييرات لا تُحفظ حتى تضغط «حفظ الإعدادات»", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.F(9.5f),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0)
        });
        bar.Controls.Add(bSave);

        Controls.Add(tabs);
        Controls.Add(bar);
        if (section != null && sections.IndexOf(section) is var si and >= 0) tabs.SelectedIndex = si;
        // الأقسام تبدأ من أعلاها (لا تمرير تلقائي إلى أول زر)
        Shown += (s, e) => { ActiveControl = null; foreach (var f in flows) f.AutoScrollPosition = Point.Empty; };
    }

    // ---------------- بناء الأقسام ----------------
    void Section(string title, string icon, string subtitle)
    {
        var card = new CardPanel { Title = title, Subtitle = subtitle, IconName = icon, Dock = DockStyle.Top };
        cur = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, AutoScroll = true, Padding = new Padding(0, 4, 0, 4) };
        flows.Add(cur);
        var flow = cur;
        // الحقول العريضة تتبع عرض البطاقة (لا شريط تمرير أفقي في الشاشات الصغيرة)
        flow.Resize += (s, e) =>
        {
            int w = Math.Max(Dpi.S(300), flow.ClientSize.Width - Dpi.S(24));
            foreach (Control c in flow.Controls)
                if (c.Tag as string == "wide") c.Width = Math.Min(Dpi.S(740), w);
                else if (c.Width > w) c.Width = w;
        };
        card.Controls.Add(cur);
        var page = new Panel { BackColor = Theme.Bg };
        card.Dock = DockStyle.Fill;
        page.Controls.Add(card);
        tabs.Add(title, page, icon);
        sections.Add(title);
    }

    /// <summary>الشعار وصورة ترويسة القوائم (20 × 4.5 سم) مع «تغيير الصورة» و«إزالة»</summary>
    void Images()
    {
        Control Box(string caption, string path, int w, int h)
        {
            var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(6, 4, 18, 8) };
            var pic = new PictureBox { Size = new Size(w, h), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.SurfaceAlt, Image = Branding.Load(path) };
            host.Controls.Add(new Label { Text = caption, AutoSize = false, Width = w, Height = 24, Font = Theme.FS(9.5f), ForeColor = Theme.Text2 });
            host.Controls.Add(pic);
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            var bSet = new ModernButton { Text = "تغيير الصورة", IconName = "folder-open", Kind = BtnKind.Success, Height = 38, Margin = new Padding(0, 0, 6, 0) }; bSet.FitWidth(130);
            var bClear = new ModernButton { Text = "إزالة", IconName = "trash-2", Kind = BtnKind.Secondary, Height = 38 }; bClear.FitWidth(90);
            bSet.Click += (s, e) =>
            {
                if (!Session.Guard("settings")) return;
                using var ofd = new OpenFileDialog { Filter = "صور|*.png;*.jpg;*.jpeg;*.bmp" };
                if (ofd.ShowDialog() != DialogResult.OK) return;
                try { Branding.Set(path, ofd.FileName); pic.Image = Branding.Load(path); Toast.Show("تم تغيير الصورة"); }
                catch (Exception ex) { Ui.Warn(ex.Message); }
            };
            bClear.Click += (s, e) => { if (Session.Guard("settings")) { Branding.Clear(path); pic.Image = null; } };
            row.Controls.Add(bSet);
            row.Controls.Add(bClear);
            host.Controls.Add(row);
            return host;
        }
        Add(Box("صورة ترويسة القوائم (20 × 4.5 سم) — تحل محل اسم المحل في رأس المطبوعات", Branding.HeaderPath, 440, 100));
        Add(Box("الشعار", Branding.LogoPath, 130, 100));
        cur.SetFlowBreak(cur.Controls[^1], true);
    }

    static string Caption(string key) => Settings.All.First(x => x.Key == key).Caption;

    void Add(Control c) { if (c.Width >= 700) c.Tag = "wide"; cur.Controls.Add(c); }

    void TextField(string key, int width)
    {
        var t = new TextBox { Width = width, Text = Settings.Get(key) };
        Add(Ui.Labeled(Caption(key), t));
        getters[key] = () => t.Text.Trim();
    }

    void Secret(string key)
    {
        var t = new TextBox { Width = 360, Text = Settings.Get(key), UseSystemPasswordChar = true };
        Add(Ui.Labeled(Caption(key), t));
        getters[key] = () => t.Text.Trim();
    }

    void Memo(string key)
    {
        var t = new TextBox { Width = 736, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = Settings.Get(key) };
        Add(Ui.Labeled(Caption(key), t));
        getters[key] = () => t.Text.Trim();
    }

    void Number(string key, int width)
    {
        var t = new TextBox { Width = width, Text = Settings.Get(key), TextAlign = HorizontalAlignment.Center };
        t.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.') e.Handled = true; };
        Add(Ui.Labeled(Caption(key), t));
        getters[key] = () => t.Text.Trim();
    }

    void Choice(string key, params (string Value, string Text)[] options)
    {
        var cb = Ui.Combo(360);
        foreach (var o in options) cb.Items.Add(o.Text);
        int i = Array.FindIndex(options, o => o.Value == Settings.Get(key));
        cb.SelectedIndex = i >= 0 ? i : 0;
        Add(Ui.Labeled(Caption(key), cb));
        getters[key] = () => options[Math.Max(0, cb.SelectedIndex)].Value;
    }

    void Printer(string key)
    {
        var cb = Ui.Combo(360);
        cb.Items.Add("الطابعة الافتراضية لويندوز");
        try { foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) cb.Items.Add(p); } catch { }
        var saved = Settings.Get(key);
        if (saved != "" && !cb.Items.Contains(saved)) cb.Items.Add(saved);
        cb.SelectedIndex = saved == "" ? 0 : cb.Items.IndexOf(saved);
        Add(Ui.Labeled(Caption(key), cb));
        getters[key] = () => cb.SelectedIndex <= 0 ? "" : Convert.ToString(cb.SelectedItem);
    }

    void Switch(string key, string text)
    {
        var t = new Toggle { Text = text, Width = 736, Checked = Settings.Get(key) == "1", Margin = new Padding(6, 10, 6, 6) };
        Add(t);
        getters[key] = () => t.Checked ? "1" : "0";
    }

    void Folder(string key, string placeholder)
    {
        var t = new TextBox { Width = 600, Text = Settings.Get(key), PlaceholderText = placeholder };
        Add(Ui.Labeled(Caption(key), t));
        var b = new ModernButton { Text = "اختيار...", Kind = BtnKind.Secondary, IconName = "folder-open", Margin = new Padding(6, 31, 6, 4), Height = 40 };
        b.FitWidth(120);
        b.Click += (s, e) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = t.Text, UseDescriptionForTitle = true, Description = Caption(key) };
            if (d.ShowDialog() == DialogResult.OK) t.Text = d.SelectedPath;
        };
        Add(b);
        getters[key] = () => t.Text.Trim();
    }

    void Hint(string text) => Add(new Label
    {
        Text = text, AutoSize = false, Width = 736, Height = 30, ForeColor = Theme.Muted, Font = Theme.F(9),
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 2, 6, 2)
    });

    void Act(string text, string icon, Action act, BtnKind kind = BtnKind.Secondary)
    {
        if (cur.Controls.Count > 0 && cur.Controls[^1] is not ModernButton) cur.SetFlowBreak(cur.Controls[^1], true);
        var b = new ModernButton { Text = text, IconName = icon, Kind = kind, Margin = new Padding(6, 14, 6, 4) };
        b.FitWidth(150);
        b.Click += (s, e) => act();
        Add(b);
    }

    // ---------------- الإجراءات ----------------
    void Save()
    {
        if (!Session.Guard("settings")) return;
        foreach (var kv in getters) Settings.Set(kv.Key, kv.Value());
        Toast.Show("تم حفظ الإعدادات");
    }

    static void TestPrint()
    {
        var d = PrintDoc.Header("صفحة تجريبية");
        d.Pair("حجم الورق", Settings.Get("print_mode"), "الطابعة", Settings.Get("printer_name") == "" ? "الافتراضية" : Settings.Get("printer_name"));
        d.Table(new[] { "#", "السبب / القطعة", "الدرجة", "لماذا" }, new[] { 6f, 32, 14, 48 },
            new List<string[]> { new[] { "1", Parts.ChargingFlex, "الأرجح", "الحساس Prs0 على فلاتة الشحن في iPhone 11" }, new[] { "2", Parts.Board, "احتمال بعيد", "خط الحساس على البوردة" } });
        d.Footer();
        d.Print();
    }

    static void BackupNow()
    {
        if (!Session.Guard("backup")) return;
        try { Dialogs.Info("تم حفظ النسخة الاحتياطية في:\n" + Backup.Run(), "نسخ احتياطي"); }
        catch (Exception ex) { Ui.Warn("فشل النسخ: " + ex.Message); }
    }

    static void RestoreBackup()
    {
        if (!Session.Guard("backup")) return;
        using var ofd = new OpenFileDialog { Filter = "نسخة كاشف (*.db)|*.db", InitialDirectory = Backup.LocalDir };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        if (!Backup.IsValidBackup(ofd.FileName, out var err)) { Ui.Warn(err); return; }
        if (!Dialogs.Confirm("سيتم استبدال جميع البيانات الحالية بالنسخة المختارة.\nتُحفظ نسخة أمان من البيانات الحالية تلقائيًا قبل الاستعادة.", "استعادة نسخة", "نعم، استعد النسخة", danger: true)) return;
        try { Backup.Restore(ofd.FileName); }
        catch (Exception ex) { Ui.Warn("تعذرت الاستعادة: " + ex.Message); return; }
        Dialogs.Info("تمت الاستعادة بنجاح. سيُعاد تشغيل البرنامج الآن.", "استعادة نسخة");
        Program.Restart();
    }
}
