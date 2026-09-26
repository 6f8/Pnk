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
        Run("تقرير الزبون وأدلة التظليل", CustomerAndNeedles);
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
        Check(strong.Confidence == "متوسطة" && strong.TopPart == Parts.Battery && strong.Signature.EndsWith("|battery"), "مفتاح مؤكد (TG0B): " + strong.Signature);
        var weak = PanicAnalyzer.Analyze(PanicParser.Parse(Smc("0x4244443100003013 0x4251583100005013"), "w"));
        Check(weak.Confidence == "منخفضة" && weak.Summary.Contains("ضعيف") && weak.Signature.EndsWith("|b?"), "مفاتيح B غير موثّقة: دليل ضعيف — " + weak.Summary);
        Check(weak.Candidates[0].Score < strong.Candidates[0].Score, "الدليل الضعيف درجته أقل");
        var none = PanicAnalyzer.Analyze(PanicParser.Parse(Smc(""), "n"));
        Check(none.Confidence == "منخفضة" && none.SmcAssert == "target/d94/target.cpp:250" && none.LearnPattern == "target/d94/target.cpp:250", "موضع الفشل بلا مفاتيح");
        var real = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("smc_d94_ocr.txt"), "r"));
        Check(real.SensorArray.Count == 1 && real.SensorArray[0] == (1, 3145728L), "مصفوفة الحساسات: " + string.Join(",", real.SensorArray));
        Check(real.FaultingTask == "2", "المهمة المتوقفة: " + real.FaultingTask);
    }

    // ------------------------------------------------------------------ 27 + 28
    static void CustomerAndNeedles()
    {
        var raw = Sample("smc_d94_ocr.txt");
        var d = PanicAnalyzer.Analyze(PanicParser.Parse(raw, "smc"));
        var cr = PanicAnalyzer.CustomerReport(d, "محل النور");
        Check(cr.Contains("محل النور") && cr.Contains("iPhone 16 Pro Max") && cr.Contains(Parts.Battery) && !cr.Contains("0x") && !cr.Contains("SMC"), "تقرير الزبون بسيط بلا رموز");
        int found = 0, total = 0;
        foreach (var e in d.Evidence.Where(e => !string.IsNullOrEmpty(e.Needle)))
        {
            total++;
            if (raw.Contains(e.Needle, StringComparison.OrdinalIgnoreCase) || raw.Contains(e.Needle.Replace("/", "\\/"), StringComparison.OrdinalIgnoreCase)) found++;
            else Console.WriteLine($"  ! لا يوجد في النص: {e.What} ← «{e.Needle}»");
        }
        Check(total >= 6 && found == total, $"كل دليل له موضع في النص: {found}/{total}");
    }
}
