using System.Text;
using Kashif;
using Microsoft.Data.Sqlite;

namespace Kashif.Tests;

/// <summary>
/// الاختبارات الإضافية: قاعدة المعرفة، السجلات مع نتائجها المتوقعة، اللقطات (Snapshots)، التحمّل،
/// الحفظ والفتح على SQLite حقيقية، وميزات القراءة والتجميع الجديدة.
/// </summary>
static partial class Program
{
    static void RunMore()
    {
        Run("تناسق قاعدة المعرفة", KnowledgeConsistency);
        Run("رموز موثّقة من iFixit (الإصدار 6)", VerifiedCodes);
        Run("تصدير الحالات المؤكدة", ExportCases);
        Run("تاريخ الإصلاح وثبات مدة الانهيار", RepairAndUptime);
        Run("خبرة المحل: نفس البصمة ونفس مصفوفة الحساسات", ShopHistory);
        Run("البصمة الدقيقة على نفس الموديل", DetailKeys);
        Run("إصدار iOS بين السجلات والقطعة المشتركة", BuildsAndShared);
        Run("ما يحسم بين أعلى سببين", Decider);
        Run("الحل المختصر: نهائي فقط عند الثقة العالية", ShortSolution);
        Run("البطارية غير الأصلية من السعة المبرمجة", BatteryOrigin);
        Run("إخفاء معرّفات الجهاز دون تغيير التشخيص", Anonymize);
        Run("السجلات ونتائجها المتوقعة (.expected)", ExpectedSamples);
        Run("اللقطات: ترتيب الأسباب لم يتغير دون قصد", Snapshots);
        Run("تحمّل: نصوص تالفة وعشوائية", Fuzz);
        Run("الحفظ والفتح والتقارير على SQLite", StoreRoundTrip);
        Run("تصحيح أخطاء النسخ من الصور", OcrFixes);
        Run("كشف السجل المقطوع والسجلات غير البانك", TruncatedAndOther);
        Run("منع التكرار وهوية السجل", DuplicatesAndIdentity);
        Run("جهاز معدّل (جيلبريك) و iPad", RootedAndIPad);
        Run("مطابقة الجهاز التقريبية", FuzzyDevice);
        Run("الترتيب الزمني والمدة بين البانكات وتغيّر النمط", Chronology);
        Run("قواعد المحل: تعبير منتظم وأولوية وتعارض", RulesAdvanced);
        Run("مفاتيح SMC: القوية والضعيفة", SmcStrongWeak);
        Run("SMC: القناة المتوقفة لا القنوات السليمة (خطأ iPhone 13 Pro Max)", SmcChannelsRegression);
        Run("تقرير الزبون وأدلة التظليل", CustomerAndNeedles);
        RunExam();
    }

    static string ProjectDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Kashif.Tests.csproj"))) return dir.FullName;
        throw new DirectoryNotFoundException("Kashif.Tests.csproj");
    }

    // ------------------------------------------------------------------ 25
    static void KnowledgeConsistency()
    {
        var kb = PanicKnowledge.Current;
        Check(kb.Problems.Count == 0, "مشكلات في knowledge.json: " + string.Join(" | ", kb.Problems));
        Check(kb.Sensors.Count >= 6 && kb.Services.Count >= 8 && kb.Signatures.Count >= 14 && kb.SmcKeys.Count >= 8,
            $"أعداد القاعدة: {kb.Sensors.Count} حساس، {kb.Services.Count} خدمة، {kb.Signatures.Count} توقيع، {kb.SmcKeys.Count} مفتاح");
        foreach (var s in kb.Sensors)
        {
            Check(s.Source != "" && s.Level is "مؤكد" or "شائع" or "محتمل", $"الحساس {s.Code}: مصدر أو درجة ناقصة");
            Check(s.Locate(AppleDevices.Family.Unknown).Length > 0, $"الحساس {s.Code}: لا موضع افتراضي");
        }
        foreach (var s in kb.Signatures) Check(s.Source != "" && s.Steps.Length > 0, $"التوقيع {s.Kind}: مصدر أو خطوات ناقصة");
        foreach (var s in kb.Services) Check(s.Source != "" && s.Steps.Length > 0, $"الخدمة {s.Name}: مصدر أو خطوات ناقصة");
        Check(Parts.ByKey.Count >= 24 && Parts.ByKey["ChargingFlex"] == Parts.ChargingFlex, "قائمة القطع بالمفاتيح");
        var broken = PanicKnowledge.Load("{\"sensors\":[{\"code\":\"X1\",\"families\":{\"Nope\":[{\"part\":\"Unknown\",\"score\":150}]}}],\"signatures\":[{\"kind\":\"k\",\"regex\":\"(\",\"choices\":[],\"steps\":[]}]}");
        Check(broken.Problems.Count >= 4, "القاعدة التالفة تُكشف مشكلاتها: " + broken.Problems.Count);
    }

    // ------------------------------------------------------------------ 22
    static readonly Dictionary<string, Diagnosis> sampleResults = new();

    static void ExpectedSamples()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Samples");
        int n = 0;
        foreach (var exp in Directory.GetFiles(dir, "*.expected").OrderBy(f => f))
        {
            var file = exp[..^".expected".Length];
            var name = Path.GetFileName(file);
            if (!File.Exists(file)) { Check(false, $"{name}: ملف السجل غير موجود"); continue; }
            var logs = PanicParser.ParseMany(File.ReadAllText(file, Encoding.UTF8), name);
            Check(logs.Count >= 1, $"{name}: لم يُقرأ أي سجل");
            if (logs.Count == 0) continue;
            var d = logs.Count == 1 ? PanicAnalyzer.Analyze(logs[0]) : PanicAnalyzer.Combine(logs.Select(l => PanicAnalyzer.Analyze(l)).ToList());
            sampleResults[name] = d;
            n++;
            foreach (var line in File.ReadAllLines(exp, Encoding.UTF8).Where(l => l.Contains('=')))
            {
                var key = line[..line.IndexOf('=')].Trim();
                var val = line[(line.IndexOf('=') + 1)..].Trim();
                switch (key)
                {
                    case "device": Check(d.Device == val, $"{name}: الجهاز {d.Device} ≠ {val}"); break;
                    case "kind": Check(d.Kind == val, $"{name}: النوع {d.Kind} ≠ {val}"); break;
                    case "confidence": Check(d.Confidence == val, $"{name}: الثقة {d.Confidence} ≠ {val}"); break;
                    case "top":
                        var want = val == "" ? "" : Parts.ByKey.TryGetValue(val, out var p) ? p : val;
                        Check(d.TopPart == want, $"{name}: الأرجح «{d.TopPart}» ≠ «{want}»");
                        break;
                }
            }
        }
        Check(n >= 5, "عدد السجلات ذات النتيجة المتوقعة: " + n);
    }

    // ------------------------------------------------------------------ 24
    static void Snapshots()
    {
        var dir = Path.Combine(ProjectDir(), "Snapshots");
        Directory.CreateDirectory(dir);
        bool update = Environment.GetEnvironmentVariable("KASHIF_UPDATE_SNAPSHOTS") == "1";
        foreach (var (name, d) in sampleResults.OrderBy(x => x.Key))
        {
            var path = Path.Combine(dir, name + ".txt");
            var now = PanicAnalyzer.Report(d).Replace("\r\n", "\n");
            if (update || !File.Exists(path)) { File.WriteAllText(path, now, new UTF8Encoding(false)); if (!update) Console.WriteLine($"  + أُنشئت لقطة {name}"); continue; }
            var old = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
            if (old == now) { Check(true, ""); continue; }
            var a = old.Split('\n'); var b = now.Split('\n');
            int i = 0; while (i < Math.Min(a.Length, b.Length) && a[i] == b[i]) i++;
            Check(false, $"{name}: تغيّر التقرير عند السطر {i + 1}:\n      قبل: {(i < a.Length ? a[i] : "")}\n      بعد: {(i < b.Length ? b[i] : "")}\n      (بعد المراجعة: KASHIF_UPDATE_SNAPSHOTS=1)");
        }
    }

    // ------------------------------------------------------------------ 26
    static void Fuzz()
    {
        var rnd = new Random(20260926);
        var seeds = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Samples")).Where(f => !f.EndsWith(".expected") && !f.EndsWith(".md"))
            .Select(f => File.ReadAllText(f, Encoding.UTF8)).ToList();
        const string junk = "{}()[]\":,\\n0x\n\t ؟ابتث🙂‏\u0000PrsO Missing sensor(s): SMC PANIC ASSERT: 0x5447304200006013";
        int errors = 0;
        for (int i = 0; i < 1500; i++)
        {
            var s = new StringBuilder(seeds[rnd.Next(seeds.Count)]);
            int edits = rnd.Next(1, 40);
            for (int e = 0; e < edits && s.Length > 0; e++)
            {
                int pos = rnd.Next(s.Length);
                switch (rnd.Next(4))
                {
                    case 0: s.Remove(pos, Math.Min(rnd.Next(1, 200), s.Length - pos)); break;
                    case 1: s.Insert(pos, junk.Substring(rnd.Next(junk.Length - 5), 5)); break;
                    case 2: s[pos] = (char)rnd.Next(1, 0x7FF); break;
                    default: s.Length = pos; break;
                }
            }
            try
            {
                var logs = PanicParser.ParseMany(s.ToString(), "fuzz");
                var ds = logs.Select(l => PanicAnalyzer.Analyze(l, new CaseFlags { Liquid = rnd.Next(2) == 0 },
                    new[] { new CustomRule { Pattern = "(a+)+$", IsRegex = true, Part = "x" }, new CustomRule { Pattern = "Prs", Part = "y" } })).ToList();
                PanicAnalyzer.Combine(ds);
                foreach (var d in ds) { PanicAnalyzer.Report(d); PanicAnalyzer.CustomerReport(d); }
                PanicParser.ExplainNonPanic(s.ToString());
            }
            catch (Exception ex)
            {
                if (errors++ < 3) Console.WriteLine("  ✗ استثناء في التحمّل: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        Check(errors == 0, $"{errors} استثناء من 1500 نص تالف");
    }

    // ------------------------------------------------------------------ 23 + 1 + 2 + 9 + 21
    static void StoreRoundTrip()
    {
        using var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        void Exec(string sql, params object[] p)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < p.Length; i++) cmd.Parameters.AddWithValue("@p" + i, p[i] ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        List<object[]> Query(string sql, params object[] p)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < p.Length; i++) cmd.Parameters.AddWithValue("@p" + i, p[i] ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            var rows = new List<object[]>();
            while (r.Read()) { var v = new object[r.FieldCount]; r.GetValues(v); rows.Add(v); }
            return rows;
        }
        object Scalar(string sql, params object[] p) => Query(sql, p).FirstOrDefault()?[0];

        // قاعدة إصدار 1 ثم الترقية (كما يحدث عند تحديث البرنامج عند محل يستخدمه)
        Exec(StoreSql.Schema);
        foreach (var (table, col, def) in StoreSql.Migrations)
        {
            bool exists = Query($"PRAGMA table_info({table})").Any(r => (string)r[1] == col);
            if (!exists) Exec($"ALTER TABLE {table} ADD COLUMN {col} {def}");
        }
        Exec(StoreSql.Indexes);

        var text = Sample("prs0_iphone11_ocr.txt");
        var logs = PanicParser.ParseMany(text, "prs0.txt");
        var d = PanicAnalyzer.Analyze(logs[0]);
        var vals = StoreSql.Values(d, logs, new CaseFlags { Dropped = true }, "علي", "0770", "ملاحظة", "قيد الفحص", "", "", PanicAnalyzer.Report(d));
        Exec(StoreSql.Insert, vals.Append("2026-09-26 10:00:00").Append(1L).ToArray());
        long id = (long)Scalar("SELECT last_insert_rowid()");
        var row = Query(StoreSql.Select, id).Single();
        var cols = Query("PRAGMA table_info(analyses)").Select(r => (string)r[1]).ToList();
        object Col(string name) => row[cols.IndexOf(name)];
        Check((string)Col("customer") == "علي" && (string)Col("signature") == "sensor:Prs0" && (string)Col("learn_pattern") == "Prs0", "الحقول بعد الحفظ");
        var back = StoreSql.DecodeLogs((string)Col("raw"));
        Check(back.Count == 1 && back[0].Raw == logs[0].Raw, "نص السجل يعود كما هو");
        var again = PanicAnalyzer.Analyze(PanicParser.Parse(back[0].Raw, back[0].Source));
        Check(again.TopPart == d.TopPart && again.Kind == d.Kind && again.Confidence == d.Confidence, "إعادة التحليل بعد الفتح تعطي نفس النتيجة");
        Check(CaseFlags.Decode((string)Col("flags")).Dropped, "معلومات الحالة تُحفظ");

        // تسجيل القطعة المُصلِحة (تحديث)
        var upd = StoreSql.Values(d, logs, new CaseFlags(), "علي", "0770", "", "جاهز", Parts.ChargingFlex, "2026-09-27 12:00:00", "r");
        Exec(StoreSql.Update, upd.Append(id).ToArray());
        Check((string)Scalar("SELECT fixed_part FROM analyses WHERE id=@p0", id) == Parts.ChargingFlex, "القطعة المُصلِحة تُحفظ");

        // دقة التشخيص: فحص صحيح + فحصان أخطأ فيهما الأرجح (أُصلحا بالبوردة) لكن البوردة ضمن أول 3
        for (int k = 0; k < 3; k++)
        {
            var v = StoreSql.Values(d, logs, null, "زبون" + k, "", "", "جاهز", Parts.Board, "2026-09-27 12:00:00", "r");
            Exec(StoreSql.Insert, v.Append($"2026-09-2{k} 10:00:00").Append(1L).ToArray());
        }
        var acc = Query(StoreSql.Accuracy, "2000-01-01", "2100-01-01");
        var all = acc.First(r => (string)r[1] == "الكل");
        Check(Convert.ToInt64(all[2]) == 4 && Convert.ToInt64(all[3]) == 1 && Convert.ToInt64(all[4]) == 4, $"الدقة: {all[2]} فحوصات، {all[3]} صحيح، {all[4]} ضمن أول 3");

        // قاعدة مقترحة: Prs0 ← البوردة تكرر 3 مرات والأرجح كان فلاتة الشحن
        var sug = Query(StoreSql.Suggestions, 3L);
        Check(sug.Count == 1 && (string)sug[0][1] == "Prs0" && (string)sug[0][3] == Parts.Board, "قاعدة مقترحة واحدة: Prs0 ← البوردة");
        Exec("INSERT INTO kb_rules(name,pattern,device,part,level,active) VALUES('x','Prs0','iPhone 11',@p0,'شائع',1)", Parts.Board);
        Check(Query(StoreSql.Suggestions, 3L).Count == 0, "لا تُقترح قاعدة موجودة");
        Check(Query(StoreSql.RulesActive).Single()[7] is long r0 && r0 == 0, "القواعد القديمة: is_regex = 0");

        // نفس النمط بنفس رقم البناء على أجهزة أخرى
        var other = PanicParser.Parse(text.Replace("4e8ed2fecd31cd250805d279f713938d19728d75", "ffffffffffffffffffffffffffffffffffffffff"), "b");
        var dOther = PanicAnalyzer.Analyze(other);
        Exec(StoreSql.Insert, StoreSql.Values(dOther, new[] { other }, null, "", "", "", "", "", "", "r").Append("2026-09-26 11:00:00").Append(1L).ToArray());
        Check(Convert.ToInt64(Scalar(StoreSql.SameBuildDevices, d.Build, d.Signature, d.Log.DeviceKey)) == 1, "جهاز آخر بنفس البناء والنمط");

        // تجربة قاعدة على المحفوظ
        var rows = Query(StoreSql.RuleTestRows).Select(r => ((long)r[0], (string)r[1], (string)r[2], (string)r[3], (string)r[4])).ToList();
        var t = StoreSql.TestRule(new CustomRule { Pattern = "Prs0", Part = Parts.Board }, rows);
        Check(t.Checked == 5 && t.Matched == 5 && t.SamePart == 3 && t.OtherPart == 1 && t.NoOutcome == 1, $"تجربة القاعدة: {t.Checked}/{t.Matched}/{t.SamePart}/{t.OtherPart}/{t.NoOutcome}");
        var t2 = StoreSql.TestRule(new CustomRule { Pattern = "Prs0", Part = Parts.Board, Device = "iPhone 13" }, rows);
        Check(t2.Matched == 0, "فلتر الجهاز في التجربة");
    }

    // ------------------------------------------------------------------ 12
    static void OcrFixes()
    {
        var text = "panic(cpu 0 caller 0x0): SMC PANIC - ASSERT: target/d94/target.cpp:250: 0\\n [TX] 0x544730420OOO6O13 [RX] 0x423041560000901l\\nDebugger message: panic";
        var l = PanicParser.Parse(text, "ocr");
        Check(l.OcrFixes == 2, "عدد التصحيحات: " + l.OcrFixes);
        var d = PanicAnalyzer.Analyze(l);
        Check(d.SmcKeys.Contains("TG0B") && d.SmcKeys.Contains("B0AV"), "المفاتيح بعد التصحيح: " + string.Join(",", d.SmcKeys));
        var s = PanicAnalyzer.Analyze(PanicParser.Parse("panic(cpu 0 caller 0x0): userspace watchdog timeout: no successful checkins from thermalmonitord\\nMissing sensor(s): PrsO micl\\nservice: thermalmonitord, no successful checkins in 180 seconds\\nDebugger message: panic", "x"));
        Check(s.MissingSensors.SequenceEqual(new[] { "Prs0", "mic1" }), "رموز الحساسات بعد التصحيح: " + string.Join(",", s.MissingSensors));
        Check(s.Evidence.Count(e => e.What == "تصحيح رمز") == 2, "التصحيح مذكور في الأدلة");
        var good = PanicParser.Parse(Sample("mic1_iphonex_valid.ips"), "v");
        Check(good.OcrFixes == 0, "الملف السليم لا يُعدَّل");
    }

    // ------------------------------------------------------------------ 13 + 15
    static void TruncatedAndOther()
    {
        var smc = PanicParser.Parse(Sample("smc_d94_ocr.txt"), "smc");
        Check(smc.Truncated && smc.Notes.Any(n => n.Contains("مقطوع")), "سجل SMC المنسوخ مقطوع (لا يوجد Debugger message)");
        var full = PanicParser.Parse(Sample("mic1_iphonex_valid.ips"), "v");
        Check(!full.Truncated, "الملف الكامل غير مقطوع");
        var noService = PanicParser.Parse("panic(cpu 0 caller 0x0): userspace watchdog timeout: no successful checkins from thermalmonitord\\nDebugger message: panic", "x");
        Check(noService.Notes.Any(n => n.Contains("قائمة الخدمات غير كاملة")), "سطر الخدمة ناقص");
        Check(PanicParser.ExplainNonPanic("ResetCounter-2026-09-20.diag")?.Contains("ResetCounter") == true, "ResetCounter");
        Check(PanicParser.ExplainNonPanic("{\"bug_type\":\"298\",\"name\":\"JetsamEvent\"}")?.Contains("JetsamEvent") == true, "JetsamEvent");
        Check(PanicParser.ExplainNonPanic(Sample("app_crash_309.ips"))?.Contains("كراش تطبيق") == true, "كراش تطبيق");
        Check(PanicParser.ExplainNonPanic("{\"bug_type\":\"288\"} stackshot")?.Contains("Stackshot") == true, "Stackshot");
        Check(PanicParser.ExplainNonPanic("hello") == null, "نص عادي بلا تفسير");
    }

    // ------------------------------------------------------------------ 14
    static void DuplicatesAndIdentity()
    {
        var a = PanicParser.Parse(Sample("prs0_iphone11_ocr.txt"), "a");
        var b = PanicParser.Parse(Sample("prs0_iphone11_ocr.txt"), "b (نسخة)");
        Check(a.Identity == b.Identity && a.Identity.StartsWith("id:"), "نفس رقم الحادثة = نفس السجل");
        var c = PanicParser.Parse("panic(cpu 0 caller 0x0): x\\nDebugger message: panic", "c");
        var e = PanicParser.Parse("panic(cpu 0 caller 0x0):   x\\nDebugger message: panic", "e");
        Check(c.Identity == e.Identity && c.Identity.StartsWith("text:"), "بلا رقم حادثة: بصمة النص (المسافات لا تهم)");
        Check(a.Time?.Offset == TimeSpan.FromHours(-7) && a.Time?.Hour == 2, "قراءة الوقت والمنطقة الزمنية: " + a.Time);
    }

    // ------------------------------------------------------------------ 10 + 16
    static void RootedAndIPad()
    {
        var text = Sample("prs0_iphone11_ocr.txt").Replace("\"roots_installed\":0", "\"roots_installed\":1");
        var l = PanicParser.Parse(text, "r");
        Check(l.Rooted, "roots_installed=1 ← جهاز معدّل");
        var d = PanicAnalyzer.Analyze(l);
        Check(d.Candidates.Any(c => c.Part == Parts.Ios) && d.Evidence.Any(e => e.What == "جهاز معدّل"), "الجيلبريك يضيف السبب البرمجي والدليل");
        Check(!PanicParser.Parse(Sample("prs0_iphone11_ocr.txt"), "x").Rooted, "roots_installed=0 ليس معدّلًا");
        var ipad = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("prs0_iphone11_ocr.txt").Replace("iPhone12,1", "iPad13,1"), "i"));
        Check(ipad.Device == "iPad Air (4th gen)", "iPad: " + ipad.Device);
        Check(ipad.Warnings.Any(w => w.Contains("iPad")) && ipad.Confidence != "عالية", "iPad: تحذير وثقة أقل");
        Check(AppleDevices.Name("iPad16,5") == "iPad Pro 13-inch (M4)" && AppleDevices.SocName("T8103") == "M1", "جداول iPad والمعالجات");
    }

    // ------------------------------------------------------------------ 11
    static void FuzzyDevice()
    {
        var a = PanicParser.Parse(Sample("prs0_iphone11_ocr.txt"), "a");
        var b = PanicParser.Parse(Sample("prs0_iphone11_ocr.txt").Replace("4e8ed2fecd31", "4e8ed2fecdЗ1".Replace("З", "3")).Replace("d19728d75", "d19728d7s"), "b");
        Check(b.CrashReporterKey != a.CrashReporterKey && PanicAnalyzer.SameDevice(a, b), "حرف مختلف (خطأ نسخ) = نفس الجهاز");
        var c = PanicParser.Parse(Sample("prs0_iphone11_ocr.txt").Replace("4e8ed2fecd31cd25", "0000000000000000"), "c");
        Check(!PanicAnalyzer.SameDevice(a, c), "مفتاح مختلف كثيرًا = جهاز آخر");
        var dIpad = PanicParser.Parse(Sample("prs0_iphone11_ocr.txt").Replace("iPhone12,1", "iPhone13,2").Replace("d19728d75", "d19728d7s"), "d");
        Check(!PanicAnalyzer.SameDevice(a, dIpad), "نفس المفتاح تقريبًا لكن موديل مختلف = جهاز آخر");
        var groups = PanicAnalyzer.GroupDevices(new List<PanicLog> { a, c, b });
        Check(groups[0] == groups[2] && groups[0] != groups[1], "التجميع: " + string.Join(",", groups));
    }

    // ------------------------------------------------------------------ 7 + 8
    static void Chronology()
    {
        string P(string time, string sensor) => Sample("prs0_iphone11_ocr.txt").Replace("2026-09-21 02:53:31.00", time).Replace("Missing sensor(s): Prs0", "Missing sensor(s): " + sensor)
            .Replace("3F390461-D748-432A-B90E-3ED6479DEC29", Guid.NewGuid().ToString().ToUpperInvariant());
        // مرتبة عشوائيًا — يجب أن تُرتب زمنيًا
        var times = new[] { "2026-09-21 02:59:40.00", "2026-09-21 02:53:31.00", "2026-09-21 02:56:35.00", "2026-09-21 03:02:45.00" };
        var ds = times.Select(t => PanicAnalyzer.Analyze(PanicParser.Parse(P(t, "Prs0"), t))).ToList();
        var c = PanicAnalyzer.Combine(ds);
        Check(c.Timeline.Select(x => x.TimeText[11..16]).SequenceEqual(new[] { "02:53", "02:56", "02:59", "03:02" }), "الخط الزمني مرتب: " + string.Join(",", c.Timeline.Select(x => x.TimeText)));
        Check(c.Evidence.Any(e => e.What == "المدة بين البانكات" && e.Meaning.Contains("3 دقائق")), "المدة المنتظمة كل 3 دقائق");
        Check(c.Confidence == "عالية" && c.TopPart == Parts.ChargingFlex, "التكرار المنتظم: ثقة عالية");

        // تغيّر النمط: سجلان Prs0 ثم سجلان TG0B (بعد تبديل فلاتة الشحن مثلًا)
        var mixed = new[] { ("2026-09-21 02:53:31.00", "Prs0"), ("2026-09-21 02:56:35.00", "Prs0"), ("2026-09-22 10:00:00.00", "TG0B"), ("2026-09-22 10:03:05.00", "TG0B") }
            .Select(x => PanicAnalyzer.Analyze(PanicParser.Parse(P(x.Item1, x.Item2), x.Item1))).Reverse().ToList();
        var m = PanicAnalyzer.Combine(mixed);
        Check(m.Title.StartsWith("تغيّر نوع البانك") || m.Evidence.Any(e => e.What == "تغيّر النمط"), "كشف تغيّر النمط: " + m.Title);
        Check(m.TopPart == Parts.Battery, "النمط الأحدث (TG0B ← البطارية) هو الأرجح: " + m.TopPart);
        Check(m.Signature == "sensor:TG0B", "بصمة المجمّع = النمط الأحدث: " + m.Signature);

        // سجلان من نوعين مختلفين فقط: لا يكفي للحكم بتغيّر النمط
        var two = PanicAnalyzer.Combine(new List<Diagnosis> { mixed[0], mixed[^1] });
        Check(two.Kind == "حساس مفقود" && !two.Title.StartsWith("تغيّر"), "سجلان فقط: " + two.Title);
    }

    // ------------------------------------------------------------------ 19 + 20
    static void RulesAdvanced()
    {
        var l = PanicParser.Parse(Sample("smc_d94_ocr.txt"), "smc");
        var rules = new[]
        {
            new CustomRule { Name = "أ", Pattern = @"target\.cpp:25\d", IsRegex = true, Part = "آيسي الشحن التجاري", Level = "شائع", Priority = 5 },
            new CustomRule { Name = "ب", Pattern = "SMC BSC failure", Part = "فلاتة البطارية التجارية", Level = "شائع", Priority = 0 },
            new CustomRule { Name = "ج", Pattern = "[", IsRegex = true, Part = "لا يجب", Level = "مؤكد" },
        };
        Check(rules[2].PatternError() != null && rules[0].PatternError() == null, "كشف التعبير غير الصالح");
        var d = PanicAnalyzer.Analyze(l, null, rules);
        var a = d.Candidates.FirstOrDefault(c => c.Part == "آيسي الشحن التجاري");
        var b = d.Candidates.FirstOrDefault(c => c.Part == "فلاتة البطارية التجارية");
        Check(a != null && b != null && a.Score > b.Score, "الأولوية تغلب عند التعارض");
        Check(d.Evidence.Any(e => e.What == "تعارض قواعد" && e.Meaning.Contains("«أ»")), "التعارض مذكور في الأدلة");
        Check(d.Candidates.All(c => c.Part != "لا يجب"), "القاعدة ذات التعبير التالف لا تنطبق");
        Check(new CustomRule { Level = "مؤكد", Priority = 50 }.Score == 99 && new CustomRule { Level = "محتمل", Priority = -50 }.Score == 32, "حدود الأولوية");
    }

    // ------------------------------------------------------------------ 4
    static void SmcStrongWeak()
    {
        string Smc(string keys) => "panic(cpu 0 caller 0x0): SMC PANIC - ASSERT: target/d94/target.cpp:250: 0\\n" + keys + "\\nDebugger message: panic";
        var strong = PanicAnalyzer.Analyze(PanicParser.Parse(Smc("0x5447304200006013"), "s"));
        // بلا قنوات (سجل ناقص) لا يُعرف إن كان المفتاح من القناة المتوقفة أم قراءة دورية: الثقة منخفضة
        Check(strong.Confidence == "منخفضة" && strong.TopPart == Parts.Battery && strong.Signature.EndsWith("|battery?"), "مفتاح مؤكد بلا قناة (TG0B): " + strong.Signature);
        var weak = PanicAnalyzer.Analyze(PanicParser.Parse(Smc("0x4244443100003013 0x4251583100005013"), "w"));
        Check(weak.Confidence == "منخفضة" && weak.Summary.Contains("ضعيف") && weak.Signature.EndsWith("|b?"), "مفاتيح B غير موثّقة: دليل ضعيف — " + weak.Summary);
        Check(weak.Candidates[0].Score < strong.Candidates[0].Score, "الدليل الضعيف درجته أقل");
        var none = PanicAnalyzer.Analyze(PanicParser.Parse(Smc(""), "n"));
        Check(none.Confidence == "منخفضة" && none.SmcAssert == "target/d94/target.cpp:250" && none.LearnPattern == "target/d94/target.cpp:250", "موضع الفشل بلا مفاتيح");
        var real = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("smc_d94_ocr.txt"), "r"));
        Check(real.SensorArray.Count == 1 && real.SensorArray[0] == (1, 3145728L), "مصفوفة الحساسات: " + string.Join(",", real.SensorArray));
        Check(real.FaultingTask == "2", "المهمة المتوقفة: " + real.FaultingTask);
    }

    /// <summary>
    /// حالة حقيقية: iPhone 13 Pro Max ‏«SMC BSC failure». التشخيص الأول قال «البطارية» لأنه قرأ مفاتيح البطارية من القناة السليمة،
    /// فاستُبدلت البطارية بلا فائدة. السبب كان حساس الشاشة. هذه الاختبارات تمنع تكرار الخطأ.
    /// </summary>
    static void SmcChannelsRegression()
    {
        var d = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("smc_bsc_d64_screen_sensor.ips"), "d64"));
        var ch0 = d.SmcChannels.FirstOrDefault(c => c.Index == 0);
        var ch1 = d.SmcChannels.FirstOrDefault(c => c.Index == 1);
        Check(ch0 != null && !ch0.NotReady && ch0.Keys.Contains("B0AV"), "القناة 0 سليمة وفيها قراءات البطارية الدورية");
        Check(ch1 != null && ch1.NotReady && ch1.Keys.SequenceEqual(new[] { "gP13", "gP12", "rBK9", "gP01" }),
            "القناة 1 متوقفة ومفاتيحها كاملة (gP13 لا يُسقط): " + (ch1 == null ? "-" : string.Join(",", ch1.Keys)));
        Check(!d.Candidates.Take(2).Any(c => c.Part == Parts.Battery), "البطارية ليست في أول سببين: " + string.Join(" > ", d.Candidates.Select(c => c.Part)));
        Check(d.Candidates.First(c => c.Part == Parts.Battery).Score < 40, "درجة البطارية منخفضة بلا دليل");
        Check(d.SensorArray.Contains((1, 0x1000L)), "مصفوفة الحساسات بالست عشري تُقرأ: " + string.Join(",", d.SensorArray));
        Check(d.Evidence.Any(e => e.What == "علامة إصلاح في السجل") && d.Log.RepairStatus == "3", "repairStatus يُقرأ ويظهر دليلًا");
        Check(d.Evidence.Any(e => e.What == "توقيت الانهيار"), "توقيت الانهيار الثابت يُذكر");
        Check(d.Steps.Count > 0 && d.Steps[0].Contains("لا تشترِ"), "أول خطوة: لا شراء قبل العزل");
        Check(d.Steps.FindIndex(x => x.Contains("اختبار العزل")) < d.Steps.FindIndex(x => x.Contains("بدّلها")), "العزل قبل التبديل");
        var q = PanicAnalyzer.NextQuestion(d, new List<(string, int)>(), null, out _);
        Check(q != null && q.Free && q.Id != "battery_swap", "أول سؤال مجاني: " + q?.Id);

        // البطارية سليمة وبقي البانك ← لا تبقى البطارية في المقدمة
        var after = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("smc_bsc_d64_screen_sensor.ips"), "d64"));
        PanicAnalyzer.ApplyAnswers(after, new List<(string, int)> { ("battery_swap", 1) });
        Check(after.TopPart != Parts.Battery && after.TopPart != Parts.BatteryConn, "بعد بطارية سليمة بلا فائدة: " + after.TopPart);

        // القناة المتوقفة فيها قراءة البطارية ← البطارية أولًا (الدليل في المكان الصحيح)
        const string head = "panic(cpu 0 caller 0x0): SMC PANIC - ASSERT: target/d64/target.cpp:263: 0, SMC BSC failure\n - Misc(2) OUTBOX1 not ready\n";
        string Box(int i, string msgs, bool stuck) => $"Mailbox ({i}): ({i})\n" + (stuck ? "OUTBOX not ready \n" : "") + msgs + "\n";
        var bat = PanicAnalyzer.Analyze(PanicParser.Parse(head + Box(0, "[RX] user01 0x0000000110d3de20 0x5456424500003013", false)
            + Box(1, "[RX] user01 0x000000011124e550 0x5447304200006013", true) + "Debugger message: panic", "b"));
        Check(bat.TopPart == Parts.Battery && bat.Confidence == "متوسطة", "مفتاح البطارية في القناة المتوقفة: " + bat.TopPart);
        // البطارية في القناة السليمة فقط ← ليست الأرجح
        var routine = PanicAnalyzer.Analyze(PanicParser.Parse(head + Box(0, "[RX] user01 0x0000000110d3de20 0x5447304200006013", false)
            + Box(1, "[RX] user01 0x000000011124e550 0x6750313300040011", true) + "Debugger message: panic", "r"));
        Check(routine.TopPart != Parts.Battery, "البطارية في القناة السليمة فقط: " + routine.TopPart);

        // تكلفة الأسئلة
        Check(PanicKnowledge.Current.Questions.First(x => x.Id == "battery_swap").Free == false, "تبديل البطارية يحتاج شراء");
        Check(PanicKnowledge.Current.Questions.First(x => x.Id == "front_flex_unplug").Free, "فصل الفلاتة مجاني");
    }

    // ------------------------------------------------------------------ 27 + 28
    static void CustomerAndNeedles()
    {
        var raw = Sample("smc_d94_ocr.txt");
        var d = PanicAnalyzer.Analyze(PanicParser.Parse(raw, "smc"));
        var cr = PanicAnalyzer.CustomerReport(d, "محل النور");
        Check(cr.Contains("محل النور") && cr.Contains("iPhone 16 Pro Max") && cr.Contains(PanicAnalyzer.Solve(d).Customer) && !cr.Contains("0x") && !cr.Contains("SMC"), "تقرير الزبون بسيط بلا رموز");
        int found = 0, total = 0;
        foreach (var e in d.Evidence.Where(e => !string.IsNullOrEmpty(e.Needle)))
        {
            total++;
            if (raw.Contains(e.Needle, StringComparison.OrdinalIgnoreCase) || raw.Contains(e.Needle.Replace("/", "\\/"), StringComparison.OrdinalIgnoreCase)) found++;
            else Console.WriteLine($"  ! لا يوجد في النص: {e.What} ← «{e.Needle}»");
        }
        Check(total >= 6 && found == total, $"كل دليل له موضع في النص: {found}/{total}");
    }

    /// <summary>الرموز المضافة من ويكي iFixit «iPhone Kernel Panics»: كل رمز يصل إلى القطعة التي يذكرها المصدر</summary>
    static void VerifiedCodes()
    {
        Diagnosis One(string text)
        {
            var logs = PanicParser.ParseMany(text, "نص");
            Check(logs.Count == 1, $"سجل واحد (وجد {logs.Count})");
            return PanicAnalyzer.Analyze(logs[0]);
        }
        var bosch = One("panic(cpu 0 caller 0xfffffff0283a1b2c): AOP PANIC - K2 - Bosch control channel write failure");
        Check(bosch.TopPart == Parts.ChargingFlex, "Bosch: الأرجح=" + bosch.TopPart);
        var nmi = One("panic(cpu 1 caller 0xfffffff0283a1b2c): AOP NMI POWER");
        Check(nmi.TopPart == Parts.PowerFlex, "NMI POWER: الأرجح=" + nmi.TopPart);
        Check(nmi.Candidates.Any(c => c.Part == Parts.FrontFlex), "NMI POWER: فلاتة الكاميرا الأمامية بين الأسباب");
        var sepRom = One("panic(cpu 0 caller 0xfffffff0283a1b2c): SEP ROM boot panic");
        Check(sepRom.TopPart == Parts.Board, "SEP ROM: الأرجح=" + sepRom.TopPart);
        var hot = One("panic(cpu 2 caller 0xfffffff0283a1b2c): AppleSocHot: hot hot hot");
        Check(hot.TopPart == Parts.Board, "AppleSocHot: الأرجح=" + hot.TopPart);

        // TG0V: على 11 Pro Max فلاتة الشحن بين الأسباب (موصل البطارية الثاني)، وعلى غيره لا
        var mic1 = Sample("mic1_iphonex_valid.ips").Replace("Missing sensor(s): mic1", "Missing sensor(s): TG0V");
        var pm = PanicAnalyzer.Analyze(PanicParser.ParseMany(mic1.Replace("iPhone10,3", "iPhone12,5"), "11pm")[0]);
        Check(pm.TopPart == Parts.Battery, "TG0V على 11 Pro Max: الأرجح=" + pm.TopPart);
        Check(pm.Candidates.Any(c => c.Part == Parts.ChargingFlex), "TG0V على 11 Pro Max: فلاتة الشحن بين الأسباب");
        var x = PanicAnalyzer.Analyze(PanicParser.ParseMany(mic1, "x")[0]);
        Check(x.TopPart == Parts.Battery, "TG0V على iPhone X: الأرجح=" + x.TopPart);
    }

    static void ExportCases()
    {
        var raw = System.Text.Json.JsonSerializer.Serialize(new List<string[]> { new[] { "panic-full-2026.ips", "panic text" }, new[] { "نص/ملصوق:1", "second" } });
        var cases = new[]
        {
            new CaseExport.Case(7, "2026-09-27", "iPhone 13 Pro Max", "iPhone14,3", "26.0", "SMC", "انهيار", Parts.FrontFlex,
                Parts.FrontFlex + "\n" + Parts.LastPart, Parts.FrontFlex, "2026-09-28", "screen", "prior_repair=0", "بدّلت الحساس", raw),
            new CaseExport.Case(8, "2026-09-27", "iPhone 11", "iPhone12,1", "17", "حساس", "x", Parts.ChargingFlex, "", Parts.Battery, "", "", "", "", ""),
        };
        using var ms = new MemoryStream();
        int n = CaseExport.Write(ms, cases);
        Check(n == 1, $"حالة واحدة لها سجلات (صُدّر {n})");
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Check(names.Contains("case-0007/case.txt"), "case.txt: " + string.Join(", ", names));
        Check(names.Contains("case-0007/01-panic-full-2026.ips"), "اسم السجل الأصلي محفوظ");
        Check(names.Any(x => x.StartsWith("case-0007/02-") && !x[13..].Contains('/')), "مصدر فيه / يُنظَّف");
        using var r = new StreamReader(zip.GetEntry("case-0007/case.txt").Open());
        var txt = r.ReadToEnd();
        Check(txt.Contains("القطعة التي أصلحت الجهاز فعلًا: " + Parts.FrontFlex) && txt.Contains("ترتيبها عند كاشف: 1"), "نتيجة الحالة وترتيبها");
    }

    static void RepairAndUptime()
    {
        var f = new CaseFlags { ScreenReplaced = true, RepairDate = new DateTime(2026, 9, 1) };
        var back = CaseFlags.Decode(f.Encode());
        Check(back.ScreenReplaced && back.RepairDate == new DateTime(2026, 9, 1) && !back.Liquid, "ترميز تاريخ الإصلاح: " + f.Encode());
        Check(CaseFlags.Decode("10000").RepairDate == null && CaseFlags.Decode("10000").Liquid, "الترميز القديم بلا تاريخ");

        // سجل iPhone 11 بتاريخ 2026-09-21
        Diagnosis Fresh() => PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("prs0_iphone11_ocr.txt"), "x")[0]);
        var t = Fresh().Log.Time;
        Check(t?.Date.Year == 2026, "تاريخ السجل: " + t);

        var after = Fresh();
        PanicAnalyzer.ApplyRepairDate(after, new DateTime(2026, 9, 10), new[] { t });
        var lp = after.Candidates.FirstOrDefault(c => c.Part == Parts.LastPart);
        Check(lp != null && lp.Why.Contains("بعد آخر إصلاح"), "أول بانك بعد الإصلاح ← آخر قطعة ترتفع");
        Check(after.Evidence.Any(e => e.What == "أول بانك بعد الإصلاح" && e.Value.Contains("11 يوم")), "عدد الأيام");

        var both = Fresh();
        PanicAnalyzer.ApplyRepairDate(both, new DateTime(2026, 9, 15), new[] { t, t?.AddDays(-10) });
        Check(both.Evidence.Any(e => e.What == "بانك قبل الإصلاح"), "بانك قبل الإصلاح وبعده");

        var none = Fresh();
        PanicAnalyzer.ApplyRepairDate(none, new DateTime(2026, 9, 15), new DateTimeOffset?[] { null });
        Check(none.Evidence.Any(e => e.What == "تاريخ الإصلاح"), "بلا تاريخ في السجل");

        // ثلاثة سجلات بمهلة ثابتة ~180 ثانية
        var items = Enumerable.Range(0, 3).Select(i => { var d = Fresh(); d.UptimeSeconds = 180 + i * 3; return d; }).ToList();
        var c = PanicAnalyzer.Combine(items);
        Check(c.Evidence.Any(e => e.What == "المدة من الإقلاع إلى الانهيار" && e.Meaning.StartsWith("ثابتة")), "مهلة ثابتة");
        var mixed = new[] { 40.0, 200, 900 }.Select(u => { var d = Fresh(); d.UptimeSeconds = u; return d; }).ToList();
        Check(PanicAnalyzer.Combine(mixed).Evidence.Any(e => e.What == "المدة من الإقلاع إلى الانهيار" && e.Meaning.StartsWith("متفاوتة")), "مدد متفاوتة");
    }

    static void ShopHistory()
    {
        using var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        void Exec(string sql, params object[] p)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < p.Length; i++) cmd.Parameters.AddWithValue("@p" + i, p[i] ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        List<(string Part, int Count)> Rows(string sql, params object[] p)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < p.Length; i++) cmd.Parameters.AddWithValue("@p" + i, p[i] ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            var rows = new List<(string, int)>();
            while (r.Read()) rows.Add((r.GetString(0), r.GetInt32(1)));
            return rows;
        }
        Exec(StoreSql.Schema);
        foreach (var (table, col, def) in StoreSql.Migrations) Exec($"ALTER TABLE {table} ADD COLUMN {col} {def}");

        var logs = PanicParser.ParseMany(Sample("smc_bsc_d64_screen_sensor.ips"), "smc");
        Diagnosis Fresh() => PanicAnalyzer.Analyze(logs[0]);
        var d0 = Fresh();
        Check(PanicAnalyzer.SensorArrayKey(d0) == "1=0x1000", "مفتاح المصفوفة: " + PanicAnalyzer.SensorArrayKey(d0));
        // ثلاث حالات مؤكدة سابقة لنفس الموديل والمصفوفة: حساس الشاشة مرتين والبطارية مرة
        foreach (var part in new[] { Parts.FrontFlex, Parts.FrontFlex, Parts.Battery })
            Exec(StoreSql.Insert, StoreSql.Values(d0, logs, null, "", "", "", "جاهز", part, "2026-09-28", "r").Append("2026-09-28 10:00:00").Append(1L).ToArray());
        // حالة بلا نتيجة لا تُحسب
        Exec(StoreSql.Insert, StoreSql.Values(d0, logs, null, "", "", "", "قيد الفحص", "", "", "r").Append("2026-09-29 10:00:00").Append(1L).ToArray());

        var byArr = Rows(StoreSql.HistoryBySensorArray, d0.Product, "1=0x1000", 999L);
        Check(byArr.Count == 2 && byArr[0] == (Parts.FrontFlex, 2) && byArr[1] == (Parts.Battery, 1), "المصفوفة: " + string.Join(", ", byArr));
        Check(Rows(StoreSql.HistoryBySensorArray, "iPhone15,2", "1=0x1000", 999L).Count == 0, "موديل آخر لا يُحسب");
        Check(Rows(StoreSql.HistoryBySensorArray, d0.Product, "1=0x1000", 1L).Sum(r => r.Count) == 2, "الفحص الحالي يُستثنى");

        var d = Fresh();
        PanicAnalyzer.ApplyShopHistory(d, byArr, true);
        Check(d.TopPart == Parts.FrontFlex && d.Candidates[0].Score >= 85, $"حالتان من 3 ← حساس الشاشة الأرجح ({d.Candidates[0].Score})");
        Check(d.Evidence.Any(e => e.What == "خبرة المحل: مصفوفة الحساسات" && e.IsExam), "دليل خبرة المحل");

        var split = Fresh();
        int bat = split.Candidates.First(x => x.Part == Parts.Battery).Score;
        PanicAnalyzer.ApplyShopHistory(split, new List<(string, int)> { (Parts.Battery, 1), (Parts.FrontFlex, 1) }, false);
        Check(split.Candidates.First(x => x.Part == Parts.Battery).Score == bat + 5, "حالة واحدة ← +5 فقط");

        var free = Fresh();
        var before = string.Join(",", free.Candidates.Select(x => x.Part + x.Score));
        PanicAnalyzer.ApplyShopHistory(free, new List<(string, int)> { ("فلاتة صينية", 4) }, false);
        Check(string.Join(",", free.Candidates.Select(x => x.Part + x.Score)) == before, "قطعة بنص حر: دليل فقط");
    }

    static void DetailKeys()
    {
        Diagnosis A(string text) => PanicAnalyzer.Analyze(PanicParser.Parse(text, "x"));
        var sensors = A("panic(cpu 0 caller 0x0): userspace watchdog timeout: no successful checkins from thermalmonitord\nMissing sensor(s): Prs0 mic1\nservice: thermalmonitord, no successful checkins in 180 seconds");
        Check(sensors.DetailKey == "sensors:mic1,prs0", "الحساسات معًا مرتبة: " + sensors.DetailKey);
        var i2c = A("panic(cpu 1 caller 0xfffffff012345678): i2c3 bus stuck SDA low");
        Check(i2c.Kind == "خط I2C" && i2c.DetailKey == "i2c:3", $"رقم خط I2C: {i2c.Kind} {i2c.DetailKey}");
        var aop = A("panic(cpu 0 caller 0xfffffff012345678): AOP PANIC - SCMto: 0 - prox");
        Check(aop.DetailKey == "aop:scmto: 0 - prox", "سطر AOP كما هو: " + aop.DetailKey);
        var h1 = A("panic(cpu 2 caller 0xfffffff0aaaa1111): some new failure at 0xfffffff0deadbeef count 123456");
        var h2 = A("panic(cpu 4 caller 0xfffffff0bbbb2222): some new failure at 0xfffffff012345678 count 654321");
        Check(h1.DetailKey != "" && h1.DetailKey == h2.DetailKey, "سطر الانهيار بلا عناوين ولا أرقام طويلة: " + h1.DetailKey);
        Check(A("").DetailKey == "", "سجل فارغ بلا بصمة");

        using var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        void Exec(string sql, params object[] p)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < p.Length; i++) cmd.Parameters.AddWithValue("@p" + i, p[i] ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        List<(string Part, int Count)> Rows(string sql, params object[] p)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < p.Length; i++) cmd.Parameters.AddWithValue("@p" + i, p[i] ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            var rows = new List<(string, int)>();
            while (r.Read()) rows.Add((r.GetString(0), r.GetInt32(1)));
            return rows;
        }
        Exec(StoreSql.Schema);
        foreach (var (table, col, def) in StoreSql.Migrations) Exec($"ALTER TABLE {table} ADD COLUMN {col} {def}");
        var logs = PanicParser.ParseMany("{\"bug_type\":\"210\"}\n{\"product\":\"iPhone12,1\",\"panicString\":\"panic(cpu 0 caller 0x0): AOP PANIC - SCMto: 0 - prox\"}", "aop");
        var d0 = PanicAnalyzer.Analyze(logs[0]);
        Check(d0.Product == "iPhone12,1" && d0.DetailKey.StartsWith("aop:"), $"سجل AOP من ملف: {d0.Product} {d0.DetailKey}");
        foreach (var part in new[] { Parts.FrontFlex, Parts.FrontFlex })
            Exec(StoreSql.Insert, StoreSql.Values(d0, logs, null, "", "", "", "جاهز", part, "2026-09-28", "r").Append("2026-09-28 10:00:00").Append(1L).ToArray());
        var rows = Rows(StoreSql.HistoryByDetail, "iPhone12,1", d0.DetailKey, 999L);
        Check(rows.Count == 1 && rows[0] == (Parts.FrontFlex, 2), "نفس التفاصيل: " + string.Join(", ", rows));
        Check(Rows(StoreSql.HistoryByDetail, "iPhone13,2", d0.DetailKey, 999L).Count == 0, "موديل آخر لا يُحسب");
        var d = PanicAnalyzer.Analyze(logs[0]);
        PanicAnalyzer.ApplyShopHistory(d, rows, HistoryScope.Detail);
        Check(d.TopPart == Parts.FrontFlex && d.Candidates[0].Score == 78, $"حالتان ← 60+9×2 ({d.TopPart} {d.Candidates[0].Score})");
        Check(d.Evidence.Any(e => e.What == "خبرة المحل: نفس التفاصيل على نفس الموديل" && e.IsExam), "دليل البصمة الدقيقة");
    }

    static void BuildsAndShared()
    {
        var logs = PanicParser.ParseMany(Sample("smc_bsc_d64_screen_sensor.ips"), "smc");
        Diagnosis With(string sig, string build) { var d = PanicAnalyzer.Analyze(logs[0]); d.Signature = sig; d.Build = build; return d; }
        var update = PanicAnalyzer.Combine(new[] { With("a", "20A"), With("a", "20A"), With("b", "21A"), With("b", "21A") });
        Check(update.Evidence.Any(e => e.What == "تغيّر النمط مع تحديث iOS") && update.Candidates.Any(c => c.Part == Parts.Ios && c.Score >= 50), "النمط تغيّر مع الإصدار ← احتمال برمجي");
        var same = PanicAnalyzer.Combine(new[] { With("a", "20A"), With("a", "20A"), With("a", "21A") });
        Check(same.Evidence.Any(e => e.What == "نفس النمط على أكثر من إصدار iOS"), "نفس النمط على إصدارين ← عتاد");
        var one = PanicAnalyzer.Combine(new[] { With("a", "20A"), With("a", "20A") });
        Check(!one.Evidence.Any(e => e.What.Contains("iOS")), "إصدار واحد ← لا حكم");

        Diagnosis Kind(string kind, params (string, int)[] c) => new() { Kind = kind, Signature = kind, Candidates = c.Select(x => new Candidate { Part = x.Item1, Score = x.Item2, Why = "t" }).ToList() };
        var mixed = PanicAnalyzer.Combine(new[] { Kind("أ", (Parts.ChargingFlex, 70), (Parts.Screen, 50)), Kind("ب", (Parts.AudioParts, 70), (Parts.ChargingFlex, 60)) });
        Check(mixed.Kind == "أنواع مختلفة" && mixed.Evidence.Any(e => e.What == "قطعة مشتركة" && e.Value == Parts.ChargingFlex), "قطعة مشتركة بين الأنواع");
        Check(mixed.Candidates.First(c => c.Part == Parts.ChargingFlex).Score >= 60, "القطعة المشتركة ترتفع");
    }

    static void Decider()
    {
        Diagnosis D(params (string, int)[] c) => new() { Summary = "", Candidates = c.Select(x => new Candidate { Part = x.Item1, Score = x.Item2, Why = "t" }).ToList() };
        Check(PanicAnalyzer.WhatDecides(D((Parts.ChargingFlex, 80), (Parts.Board, 40))) == "", "الأول متقدم بوضوح ← لا شيء");
        var close = PanicAnalyzer.WhatDecides(D((Parts.Screen, 70), (Parts.SocRam, 65)));
        Check(close.Contains(Parts.Screen) && close.Contains(Parts.SocRam), "متقاربان: " + close);
        var confirmed = D((Parts.Screen, 70), (Parts.SocRam, 65));
        confirmed.Summary = "مؤكد بالفحص العملي ← x.";
        Check(PanicAnalyzer.WhatDecides(confirmed) == "", "المؤكد بالفحص لا يحتاج");
        var sensor = D((Parts.Board, 66), (Parts.SocRam, 64));
        sensor.MissingSensors.Add("mic1");
        var text = PanicAnalyzer.WhatDecides(sensor);
        Check(text != "", "حساس مفقود متقارب: " + text);
    }

    static void ShortSolution()
    {
        var mic = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("mic1_iphonex_valid.ips"), "m")[0]);
        var s = PanicAnalyzer.Solve(mic);
        Check(s.Final && s.Action == "بدّل فلاتة الشحن" && s.Reason.StartsWith("mic1"), $"mic1 مؤكد ← {s.Action} — {s.Reason}");
        Check(s.Customer == "العطل في فلاتة الشحن، والإصلاح بتبديلها.", "جملة الزبون: " + s.Customer);
        Check(PanicAnalyzer.MainCauses(mic, true).Count == 1, "النهائي سبب واحد");

        var smc = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("smc_bsc_d64_screen_sensor.ips"), "s")[0]);
        var u = PanicAnalyzer.Solve(smc);
        Check(!u.Final && !u.Action.StartsWith("بدّل"), "غير المؤكد لا يبدأ بالتبديل: " + u.Action);
        Check(u.Customer.Contains("يُؤكَّد بفحص عملي"), "الزبون: غير نهائي");
        var main = PanicAnalyzer.MainCauses(smc, false);
        Check(main.Count is >= 1 and <= 3 && main.All(c => c.Score >= smc.Candidates[0].Score - 20), "الأسباب القريبة فقط (حتى 3)");

        var empty = PanicAnalyzer.Solve(new Diagnosis());
        Check(!empty.Final && empty.Action.StartsWith("لا يكفي"), "بلا أسباب ← لا حكم");
        Check(PanicAnalyzer.ShortName(Parts.Biometric, new Diagnosis { Product = "iPhone12,8" }) == "Touch ID", "SE 2 ← Touch ID");
        Check(PanicAnalyzer.ShortName(Parts.Biometric, new Diagnosis { Product = "iPhone14,2" }) == "Face ID", "13 Pro ← Face ID");
        Check(Parts.All.All(p => PanicAnalyzer.ShortName(p) != "" && PanicAnalyzer.ShortName(p).Length <= 30), "كل قطعة لها اسم مختصر");
    }

    static void BatteryOrigin()
    {
        Check(PanicKnowledge.Current.Problems.Count == 0, "قاعدة المعرفة سليمة: " + string.Join(" | ", PanicKnowledge.Current.Problems));
        Check(PanicKnowledge.FindBatteryDesign("iPhone14,3")?.Mah == 4352, "iPhone 13 Pro Max = 4352");
        Check(PanicKnowledge.FindBatteryDesign("iPhone12,3") == null, "iPhone 11 Pro غير مضاف (المصادر مختلفة)");

        Diagnosis Fresh() => PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("smc_bsc_d64_screen_sensor.ips"), "x")[0]);
        var fake = Fresh();
        int bat = fake.Candidates.First(c => c.Part == Parts.Battery).Score;
        PanicAnalyzer.ApplyBatteryOrigin(fake, "iPhone14,3", 5000);
        Check(fake.Evidence.Any(e => e.What == "بطارية غير أصلية على الأرجح") && fake.Candidates.First(c => c.Part == Parts.Battery).Score == bat + 10, "5000 بدل 4352 ← غير أصلية +10");
        var real = Fresh();
        PanicAnalyzer.ApplyBatteryOrigin(real, "iPhone14,3", 4352);
        Check(real.Evidence.Any(e => e.What == "سعة البطارية الأصلية") && real.Candidates.First(c => c.Part == Parts.Battery).Score == bat, "تطابق ← دليل فقط");
        var unknown = Fresh();
        PanicAnalyzer.ApplyBatteryOrigin(unknown, "iPhone12,3", 1000);
        Check(!unknown.Evidence.Any(e => e.What.Contains("البطارية")), "موديل بلا سعة معروفة ← لا حكم");
    }

    static void Anonymize()
    {
        foreach (var name in new[] { "prs0_iphone11_ocr.txt", "smc_bsc_d64_screen_sensor.ips", "mic1_iphonex_valid.ips", "kernel_wlan_backtrace_valid.ips" })
        {
            var raw = Sample(name);
            var orig = PanicParser.ParseMany(raw, name);
            var anon = new Anonymizer();
            var clean = anon.Clean(raw);
            var back = PanicParser.ParseMany(clean, name);
            Check(back.Count == orig.Count, $"{name}: نفس عدد السجلات");
            if (back.Count == 0) continue;
            var d1 = PanicAnalyzer.Analyze(orig[0]);
            var d2 = PanicAnalyzer.Analyze(back[0]);
            Check(d1.Device == d2.Device && d1.Signature == d2.Signature && d1.TopPart == d2.TopPart && d1.Candidates.Count == d2.Candidates.Count,
                $"{name}: التشخيص لم يتغير ({d2.Device} · {d2.Signature} · {d2.TopPart})");
            if (orig[0].CrashReporterKey.Length == 40)
            {
                Check(!clean.Contains(orig[0].CrashReporterKey, StringComparison.OrdinalIgnoreCase), $"{name}: المفتاح الأصلي اختفى");
                Check(back[0].CrashReporterKey.Length == 40 && back[0].CrashReporterKey.All(Uri.IsHexDigit), $"{name}: البديل بنفس الشكل");
                Check(anon.Clean(raw) == clean, $"{name}: البديل ثابت داخل نفس العملية (التجميع يعمل)");
                Check(new Anonymizer().Clean(raw) != clean, $"{name}: مختلف بين عملية وأخرى");
            }
            if (orig[0].IncidentId != "") Check(!clean.Contains(orig[0].IncidentId, StringComparison.OrdinalIgnoreCase), $"{name}: incident_id اختفى");
        }
        var notes = new Anonymizer().Clean("الجهاز الموصول: iPhone · الرقم التسلسلي F2LXK0Q1ABCD · IMEI 356000000000001 · 00008110-000A1B2C3D4E5F6A");
        Check(!notes.Contains("F2LXK0Q1ABCD") && !notes.Contains("356000000000001") && !notes.Contains("000A1B2C3D4E5F6A") && notes.Contains("iPhone"),
            "الملاحظات: الرقم التسلسلي و IMEI و UDID: " + notes);
    }
}
