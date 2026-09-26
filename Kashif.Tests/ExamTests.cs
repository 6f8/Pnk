using Kashif;
using Microsoft.Data.Sqlite;

namespace Kashif.Tests;

/// <summary>اختبارات القراءة العميقة للسجل والفحص التفاعلي بالأسئلة</summary>
static partial class Program
{
    static void RunExam()
    {
        Run("القراءة العميقة: الخدمات والعملية والدرايفرات والمدة من الإقلاع", DeepRead);
        Run("قاعدة المعرفة: الدرايفرات والأسئلة", ExamKnowledge);
        Run("الفحص التفاعلي: اختيار السؤال التالي", NextQuestion);
        Run("الفحص التفاعلي: أثر الأجوبة والتأكيد", AnswerEffects);
        Run("الفحص التفاعلي: ترميز الأجوبة وحفظها", AnswersPersist);
    }

    static void DeepRead()
    {
        var d = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("kernel_wlan_backtrace_valid.ips"), "k")[0]);
        Check(d.PanickedProcess == "kernel_task (pid 0)", "العملية: " + d.PanickedProcess);
        Check(d.BacktraceKexts.SequenceEqual(new[] { "com.apple.driver.AppleBCMWLANCore", "com.apple.iokit.IO80211Family" }), "الدرايفرات: " + string.Join(",", d.BacktraceKexts));
        Check(d.LastKext == "com.apple.driver.AppleSMC", "آخر درايفر: " + d.LastKext);
        Check(d.UptimeSeconds == 180, "المدة من الإقلاع: " + d.UptimeSeconds);
        Check(d.Kind == "الواي فاي" && d.Evidence.Any(e => e.What == "درايفرات في مسار الانهيار"), "درايفر الواي فاي في المسار ← تشخيص الواي فاي: " + d.Kind);
        // بلا توقيع مطابق: الدرايفرات تضيف أسبابًا ثانوية فقط، والنواة (برمجي) تبقى الأرجح
        var sep = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("kernel_wlan_backtrace_valid.ips").Replace("AppleBCMWLANCore", "AppleSEPManager"), "k2")[0]);
        Check(sep.Kind == "النواة (برمجي)" && sep.TopPart == Parts.Ios, "بلا توقيع: " + sep.Kind + " / " + sep.TopPart);
        Check(sep.Candidates.Any(c => c.Part == Parts.Biometric && c.Score == 40) && sep.Candidates.Any(c => c.Part == Parts.Wifi && c.Score == 45),
            "الدرايفرات أسباب ثانوية: " + string.Join("، ", sep.Candidates.Select(c => $"{c.Part}={c.Score}")));
        Check(d.Candidates.All(c => c.Part != Parts.SmcLine), "آخر درايفر بدأ لا يغيّر الترتيب");
        Check(d.Evidence.Any(e => e.What == "المدة من الإقلاع" && e.IsInfo && e.Meaning.Contains("3 دقائق")), "دليل المدة من الإقلاع");
        Check(d.Evidence.Count(e => e.IsInfo) >= 6, "معلومات إضافية: " + d.Evidence.Count(e => e.IsInfo));
        Check(PanicKnowledge.FindKext("com.apple.driver.AppleHPMLightning")?.Prefix == "com.apple.driver.AppleHPM", "أطول بادئة تغلب (AppleHPM لا AppleH)");

        var p = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("prs0_iphone11_ocr.txt"), "p"));
        Check(p.Services.Count == 9 && p.Services.Single(s => s.Stopped).Name == "thermalmonitord", "قائمة الخدمات: " + p.Services.Count);
        var wd = p.Services.First(s => s.Name == "backboardd");
        Check(wd.Checkins == 19 && wd.Seconds == 196 && !wd.Stopped, $"backboardd: {wd.Checkins} في {wd.Seconds}");
        Check(p.Services.Single(s => s.Stopped).InducedCrashes == 2, "عدد إعادات التشغيل القسرية");
        var rep = PanicAnalyzer.Report(p);
        Check(rep.Contains("معلومات أخرى من السجل") && rep.Contains("mBoot-20457.2.37"), "المعلومات في قسم منفصل من التقرير");
    }

    static void ExamKnowledge()
    {
        var kb = PanicKnowledge.Current;
        Check(kb.Problems.Count == 0, "مشكلات: " + string.Join(" | ", kb.Problems));
        Check(kb.Kexts.Count >= 20 && kb.Questions.Count >= 15, $"{kb.Kexts.Count} درايفر، {kb.Questions.Count} سؤال");
        foreach (var q in kb.Questions)
        {
            Check(q.Targets.Length > 0 && q.Answers.Length >= 2 && q.Source != "", $"السؤال {q.Id}: ناقص");
            Check(q.Answers.Any(a => a.Effects.Count == 0), $"السؤال {q.Id}: يجب أن يكون فيه جواب محايد (لم أجرّب / لم أتحقق)");
            Check(q.Answers.Where(a => a.Decisive).All(a => a.Effects.Values.Any(v => v >= 40)), $"السؤال {q.Id}: جواب حاسم بلا أثر قوي");
        }
        // كل قطعة ترشّحها القاعدة لها سؤال فحص واحد على الأقل (عدا الأسباب العامة)
        var exempt = new HashSet<string> { Parts.Board, Parts.SocRam, Parts.SmcLine, Parts.Sensors };
        var targeted = kb.Questions.SelectMany(q => q.Targets).ToHashSet();
        var parts = kb.Sensors.SelectMany(s => s.Families.Values.SelectMany(c => c)).Concat(kb.Services.SelectMany(s => s.Choices)).Concat(kb.Signatures.SelectMany(s => s.Choices))
            .Select(c => c.Part).Distinct().Where(p => !exempt.Contains(p) && !targeted.Contains(p)).ToList();
        Check(parts.Count == 0, "قطع بلا سؤال فحص: " + string.Join("، ", parts));
    }

    static void NextQuestion()
    {
        var d = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("prs0_iphone11_ocr.txt"), "p"));
        var none = new List<(string, int)>();
        var q1 = PanicAnalyzer.NextQuestion(d, none, null, out int rem);
        Check(q1 != null && q1.Targets.Contains(Parts.ChargingFlex), "السؤال الأول يستهدف فلاتة الشحن: " + q1?.Id);
        Check(rem >= 2, "أسئلة متبقية: " + rem);
        var q2 = PanicAnalyzer.NextQuestion(d, new List<(string, int)> { (q1.Id, 2) }, null, out _);
        Check(q2 != null && q2.Id != q1.Id, "لا يتكرر السؤال المجاب");
        var q3 = PanicAnalyzer.NextQuestion(d, none, new[] { q1.Id }, out _);
        Check(q3 != null && q3.Id != q1.Id, "السؤال المتخطى لا يعود");
        var smc = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("smc_d94_ocr.txt"), "s"));
        var qs = PanicAnalyzer.NextQuestion(smc, none, new[] { "liquid_seen" }, out _);
        Check(qs != null && qs.Targets.Contains(Parts.Battery), "في بانك SMC البطارية أولًا: " + qs?.Id);
        Check(PanicAnalyzer.NextQuestion(new Diagnosis(), none, null, out _) == null, "لا أسئلة بلا أسباب");
    }

    static void AnswerEffects()
    {
        PanicLog L() => PanicParser.Parse(Sample("prs0_iphone11_ocr.txt"), "p");
        // الفلاتة السليمة أنهت البانك ← مؤكد بالفحص
        var ok = PanicAnalyzer.Analyze(L());
        PanicAnalyzer.ApplyAnswers(ok, new List<(string, int)> { ("flex_swap", 0) });
        Check(ok.TopPart == Parts.ChargingFlex && ok.Confidence == "عالية" && ok.Summary.StartsWith("مؤكد بالفحص"), "مؤكد: " + ok.Summary);
        Check(PanicAnalyzer.NextQuestion(ok, new List<(string, int)> { ("flex_swap", 0) }, null, out _) == null, "اكتمل الفحص بعد التأكيد");
        Check(ok.Evidence.Any(e => e.IsExam && e.Value.StartsWith("اختفى البانك")), "الجواب في الأدلة");

        // بقي البانك مع فلاتة سليمة ← البوردة
        var no = PanicAnalyzer.Analyze(L());
        PanicAnalyzer.ApplyAnswers(no, new List<(string, int)> { ("flex_swap", 1) });
        Check(no.TopPart == Parts.Board, "بعد بقاء البانك مع فلاتة سليمة: الأرجح " + no.TopPart);
        Check(no.Confidence != "عالية", "بلا تأكيد: " + no.Confidence);
        Check(no.Candidates.First(c => c.Part == Parts.ChargingFlex).Score < 50, "فلاتة الشحن تنخفض");

        // جواب محايد لا يغيّر الترتيب
        var base0 = PanicAnalyzer.Analyze(L());
        var neutral = PanicAnalyzer.Analyze(L());
        PanicAnalyzer.ApplyAnswers(neutral, new List<(string, int)> { ("flex_swap", 2) });
        Check(neutral.Candidates.Select(c => (c.Part, c.Score)).SequenceEqual(base0.Candidates.Select(c => (c.Part, c.Score))), "«لم أجرّب» بلا أثر");

        // تأكسد ← يُضاف سبب السوائل ويصبح الأرجح
        var smc = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("smc_d94_ocr.txt"), "s"));
        PanicAnalyzer.ApplyAnswers(smc, new List<(string, int)> { ("liquid_seen", 0) });
        Check(smc.TopPart == Parts.Liquid, "التأكسد المرئي: " + smc.TopPart);

        // البطارية السليمة أنهت البانك في SMC ← البطارية مؤكدة
        var bat = PanicAnalyzer.Analyze(PanicParser.Parse(Sample("smc_d94_ocr.txt"), "s"));
        PanicAnalyzer.ApplyAnswers(bat, new List<(string, int)> { ("battery_swap", 0) });
        Check(bat.TopPart == Parts.Battery && bat.Confidence == "عالية", "البطارية مؤكدة: " + bat.Confidence);

        // أجوبة غير صالحة (سؤال محذوف أو رقم خارج الحدود) تُتجاهل بأمان
        var bad = PanicAnalyzer.Analyze(L());
        PanicAnalyzer.ApplyAnswers(bad, new List<(string, int)> { ("no_such_question", 0), ("flex_swap", 99), ("flex_swap", -1) });
        Check(bad.AnswersApplied == 0 && bad.TopPart == Parts.ChargingFlex, "أجوبة غير صالحة تُتجاهل");

        // الأجوبة على التحليل المجمّع
        var ds = new[] { L(), L() }.Select(l => PanicAnalyzer.Analyze(l)).ToList();
        var comb = PanicAnalyzer.Combine(ds);
        PanicAnalyzer.ApplyAnswers(comb, new List<(string, int)> { ("flex_swap", 0) });
        Check(comb.Confidence == "عالية" && comb.AnswersApplied == 1, "الأجوبة على المجمّع");
    }

    static void AnswersPersist()
    {
        var list = new List<(string, int)> { ("flex_swap", 1), ("battery_health", 3), ("liquid_seen", -1) };
        var enc = PanicAnalyzer.EncodeAnswers(list);
        Check(enc == "flex_swap=1;battery_health=3;liquid_seen=-1", "ترميز: " + enc);
        Check(PanicAnalyzer.DecodeAnswers(enc).SequenceEqual(list), "فك الترميز");
        Check(PanicAnalyzer.DecodeAnswers("a=1;;b=x;=2;a=5;c=-1").SequenceEqual(new List<(string, int)> { ("a", 1), ("c", -1) }), "نص تالف يُقرأ بأمان");

        using var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        using (var cmd = c.CreateCommand()) { cmd.CommandText = StoreSql.Schema; cmd.ExecuteNonQuery(); }
        foreach (var (table, col, def) in StoreSql.Migrations)
            using (var cmd = c.CreateCommand()) { cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {col} {def}"; cmd.ExecuteNonQuery(); }
        var log = PanicParser.Parse(Sample("prs0_iphone11_ocr.txt"), "p");
        var d = PanicAnalyzer.Analyze(log);
        var vals = StoreSql.Values(d, new[] { log }, null, "", "", "", "", "", "", "r", list).Append("2026-09-26 10:00:00").Append(1L).ToArray();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = StoreSql.Insert;
            for (int i = 0; i < vals.Length; i++) cmd.Parameters.AddWithValue("@p" + i, vals[i] ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT answers FROM analyses WHERE id=1";
            Check((string)cmd.ExecuteScalar() == enc, "الأجوبة تُحفظ في قاعدة البيانات");
        }
    }
}
