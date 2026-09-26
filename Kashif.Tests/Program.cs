using System.Text;
using Kashif;

namespace Kashif.Tests;

/// <summary>اختبارات محرك التحليل على سجلات حقيقية (منسوخة من صور) وسجلات ‎.ips‎ سليمة</summary>
static class Program
{
    static int failed, passed;

    static void Check(bool ok, string what)
    {
        if (ok) passed++;
        else { failed++; Console.WriteLine("  ✗ " + what); }
    }

    static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name), Encoding.UTF8);

    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Run("SMC iPhone 16 Pro Max (OCR)", SmcD94);
        Run("Prs0 iPhone 11 (OCR)", Prs0);
        Run("mic1 iPhone X (.ips سليم)", Mic1Valid);
        Run("Kernel data abort (.ips سليم)", KernelAbort);
        Run("كراش تطبيق bug_type 309", AppCrash);
        Run("عدة سجلات ملصوقة معًا", Many);
        Run("نص البانك وحده", PanicStringOnly);
        Run("تجميع سجلات نفس الجهاز", CombineSame);
        Run("تجميع أنواع مختلفة", CombineMixed);
        Run("معلومات الحالة: سوائل", Flags);
        Run("خبرة المحل", Custom);
        Run("فك مفاتيح SMC", SmcKeys);
        Run("سطر الحساسات بلا فواصل أسطر", SensorsNoNewline);
        Run("كلمات تشبه التواقيع", NoFalseSignatures);
        Run("نصوص فارغة وغريبة", Garbage);
        Console.WriteLine($"\nنجح {passed}، فشل {failed}");
        return failed == 0 ? 0 : 1;
    }

    static void Run(string name, Action test)
    {
        Console.WriteLine("• " + name);
        try { test(); }
        catch (Exception ex) { failed++; Console.WriteLine("  ✗ استثناء: " + ex); }
    }

    static void SmcD94()
    {
        var logs = PanicParser.ParseMany(Sample("smc_d94_ocr.txt"), "smc");
        Check(logs.Count == 1, $"سجل واحد (وجد {logs.Count})");
        var l = logs[0];
        Check(l.BugType == "210", "bug_type=" + l.BugType);
        Check(l.Product == "iPhone17,2", "product=" + l.Product);
        Check(l.CrashReporterKey == "cf51b62909d78364a371188b1ec48636351141c8", "crashReporterKey=" + l.CrashReporterKey);
        Check(l.IosVersion == "26.5.2" && l.IosBuild == "23F84", $"iOS={l.IosVersion} ({l.IosBuild})");
        Check(l.SocCode == "T8140", "soc=" + l.SocCode);
        Check(!l.FromJson, "قراءة مرنة");
        var d = PanicAnalyzer.Analyze(l);
        Check(d.Device == "iPhone 16 Pro Max", "device=" + d.Device);
        Check(d.Kind == "SMC", "kind=" + d.Kind);
        foreach (var k in new[] { "TG0B", "B0AV", "BDD1", "BQX1", "BISS", "B0SS", "B0RS" })
            Check(d.SmcKeys.Contains(k), "مفتاح SMC مفقود: " + k + " — وجد: " + string.Join(",", d.SmcKeys));
        Check(!d.SmcKeys.Any(k => k.Contains('?') || k.Contains('+')), "مفاتيح غير صالحة: " + string.Join(",", d.SmcKeys));
        Check(d.TopPart == Parts.Battery, "الأرجح=" + d.TopPart);
        Check(d.Candidates.Any(c => c.Part == Parts.BatteryConn), "موصل البطارية ضمن الأسباب");
        Check(d.Evidence.Any(e => e.What == "OUTBOX not ready"), "OUTBOX not ready");
        Check(d.Evidence.Any(e => e.What == "رمز البوردة" && e.Value == "D94"), "رمز البوردة D94");
        Print(d);
    }

    static void Prs0()
    {
        var logs = PanicParser.ParseMany(Sample("prs0_iphone11_ocr.txt"), "prs0");
        Check(logs.Count == 1, $"سجل واحد (وجد {logs.Count})");
        var l = logs[0];
        Check(l.Product == "iPhone12,1", "product=" + l.Product);
        Check(l.IosVersion == "27.0" && l.IosBuild == "24A437", $"iOS={l.IosVersion} ({l.IosBuild})");
        Check(l.SocCode == "T8030", "soc=" + l.SocCode);
        var d = PanicAnalyzer.Analyze(l);
        Check(d.Device == "iPhone 11", "device=" + d.Device);
        Check(d.Kind == "حساس مفقود", "kind=" + d.Kind);
        Check(d.MissingSensors.SequenceEqual(new[] { "Prs0" }), "sensors=" + string.Join(",", d.MissingSensors));
        Check(d.WatchdogService == "thermalmonitord", "service=" + d.WatchdogService);
        Check(d.WatchdogSeconds == 196, "seconds=" + d.WatchdogSeconds);
        Check(d.TopPart == Parts.ChargingFlex && d.TopLabel == "الأرجح", $"الأرجح={d.TopPart} ({d.TopLabel})");
        Check(d.Confidence == "عالية", "confidence=" + d.Confidence);
        Check(d.Soc.StartsWith("A13"), "soc name=" + d.Soc);
        Print(d);
    }

    static void Mic1Valid()
    {
        var logs = PanicParser.ParseMany(Sample("mic1_iphonex_valid.ips"), "mic1");
        Check(logs.Count == 1, $"سجل واحد (وجد {logs.Count})");
        var l = logs[0];
        Check(l.FromJson, "قراءة JSON سليمة");
        Check(l.Notes.Count == 0, "بلا ملاحظات قراءة: " + string.Join(" | ", l.Notes));
        var d = PanicAnalyzer.Analyze(l);
        Check(d.Device == "iPhone X", "device=" + d.Device);
        Check(d.MissingSensors.SequenceEqual(new[] { "mic1" }), "sensors=" + string.Join(",", d.MissingSensors));
        Check(d.TopPart == Parts.ChargingFlex, "الأرجح=" + d.TopPart);
        Check(d.WatchdogSeconds == 185, "seconds=" + d.WatchdogSeconds);
        Check(d.Evidence.Any(e => e.Meaning.Contains("كل 3 دقائق")), "نمط 3 دقائق");
    }

    static void KernelAbort()
    {
        var d = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("kernel_abort_valid.ips"), "k")[0]);
        Check(d.Kind == "النواة (برمجي)", "kind=" + d.Kind);
        Check(d.TopPart == Parts.Ios, "الأرجح=" + d.TopPart);
        Check(d.Confidence == "منخفضة", "confidence=" + d.Confidence);
        Check(d.Device == "iPhone 12", "device=" + d.Device);
    }

    static void AppCrash()
    {
        var logs = PanicParser.ParseMany(Sample("app_crash_309.ips"), "app");
        Check(logs.Count == 1, $"سجل واحد (وجد {logs.Count})");
        var d = PanicAnalyzer.Analyze(logs[0]);
        Check(d.Warnings.Any(w => w.Contains("309")), "تحذير bug_type 309");
        Check(d.Kind == "غير معروف", "kind=" + d.Kind);
    }

    static void Many()
    {
        var text = Sample("mic1_iphonex_valid.ips") + "\n\n" + Sample("kernel_abort_valid.ips") + "\n" + Sample("prs0_iphone11_ocr.txt");
        var logs = PanicParser.ParseMany(text, "لصق");
        Check(logs.Count == 3, $"3 سجلات (وجد {logs.Count})");
        Check(logs.Select(l => l.Product).SequenceEqual(new[] { "iPhone10,3", "iPhone13,2", "iPhone12,1" }), "الترتيب: " + string.Join(",", logs.Select(l => l.Product)));
        Check(logs[0].FromJson && logs[1].FromJson, "الأول والثاني JSON سليم");
    }

    static void PanicStringOnly()
    {
        var text = "panic(cpu 0 caller 0xfffffff041e0b124): userspace watchdog timeout: no successful checkins from thermalmonitord\\nMissing sensor(s): TG0B \\nservice: thermalmonitord, no successful checkins in 180 seconds";
        var logs = PanicParser.ParseMany(text, "نص");
        Check(logs.Count == 1, $"سجل واحد (وجد {logs.Count})");
        var d = PanicAnalyzer.Analyze(logs[0]);
        Check(d.MissingSensors.SequenceEqual(new[] { "TG0B" }), "sensors=" + string.Join(",", d.MissingSensors));
        Check(d.TopPart == Parts.Battery, "الأرجح=" + d.TopPart);
        Check(d.WatchdogSeconds == 180, "seconds=" + d.WatchdogSeconds);
        Check(d.Warnings.Any(w => w.Contains("موديل")), "تحذير الموديل غير معروف");
    }

    static void CombineSame()
    {
        var a = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("prs0_iphone11_ocr.txt"), "1")[0]);
        var b = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("prs0_iphone11_ocr.txt").Replace("02:53:31", "02:56:40"), "2")[0]);
        var c = PanicAnalyzer.Combine(new List<Diagnosis> { a, b });
        Check(c.LogCount == 2, "count=" + c.LogCount);
        Check(c.Kind == "حساس مفقود", "kind=" + c.Kind);
        Check(c.TopPart == Parts.ChargingFlex, "الأرجح=" + c.TopPart);
        Check(c.Confidence == "عالية", "confidence=" + c.Confidence);
        Check(c.Warnings.All(w => !w.Contains("أجهزة مختلفة")), "بلا تحذير أجهزة مختلفة");
    }

    static void CombineMixed()
    {
        var a = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("prs0_iphone11_ocr.txt"), "1")[0]);
        var b = PanicAnalyzer.Analyze(PanicParser.ParseMany(Sample("smc_d94_ocr.txt"), "2")[0]);
        var c = PanicAnalyzer.Combine(new List<Diagnosis> { a, b });
        Check(c.Warnings.Any(w => w.Contains("أجهزة مختلفة")), "تحذير أجهزة مختلفة (الجهازان من المحادثة مختلفان)");
        Check(c.Kind == "أنواع مختلفة", "kind=" + c.Kind);
    }

    static void Flags()
    {
        var l = PanicParser.ParseMany(Sample("smc_d94_ocr.txt"), "smc")[0];
        var d = PanicAnalyzer.Analyze(l, new CaseFlags { Liquid = true });
        Check(d.TopPart == Parts.Liquid, "الأرجح مع السوائل=" + d.TopPart);
        Check(d.Steps[0].Contains("لا تشحن"), "أول خطوة: لا تشحن");
        var f = CaseFlags.Decode(new CaseFlags { Liquid = true, ScreenReplaced = true }.Encode());
        Check(f.Liquid && f.ScreenReplaced && !f.Dropped, "ترميز معلومات الحالة");
    }

    static void Custom()
    {
        var l = PanicParser.ParseMany(Sample("prs0_iphone11_ocr.txt"), "p")[0];
        var rules = new[]
        {
            new CustomRule { Name = "تجربة", Pattern = "Prs0", Device = "iPhone 11", Part = "فلاتة شحن تجارية بلا حساس", Level = "مؤكد", Note = "تكررت عندنا" },
            new CustomRule { Pattern = "Prs0", Device = "iPhone 13", Part = "لا يجب أن تظهر", Level = "مؤكد" },
        };
        var d = PanicAnalyzer.Analyze(l, null, rules);
        Check(d.TopPart == "فلاتة شحن تجارية بلا حساس", "خبرة المحل أولًا: " + d.TopPart);
        Check(d.Candidates.All(c => c.Part != "لا يجب أن تظهر"), "فلتر الجهاز");
    }

    static void SmcKeys()
    {
        var keys = PanicAnalyzer.DecodeSmcKeys("0x5447304200006013 0x4230415600009013 0x0000000110d51414 0x3f472b0200040000 0x42305253000010131");
        Check(keys.SequenceEqual(new[] { "TG0B", "B0AV" }), "keys=" + string.Join(",", keys));
        Check(PanicKnowledge.SmcKeyMeaning("B0AV") == "متوسط جهد البطارية", "معنى B0AV");
        Check(PanicKnowledge.SmcKeyMeaning("BZZZ").Contains("بطارية"), "معنى مفتاح B غير معروف");
    }

    static void SensorsNoNewline()
    {
        var one = "panic(cpu 0 caller 0xfffffff041e0b124): userspace watchdog timeout: no successful checkins from thermalmonitord Missing sensor(s): Prs0 mic1 service: backboardd, total successful checkins in 196 seconds: 19 service: thermalmonitord, no successful checkins in 196 seconds";
        var d = PanicAnalyzer.Analyze(PanicParser.Parse(one, "x"));
        Check(d.MissingSensors.SequenceEqual(new[] { "Prs0", "mic1" }), "sensors=" + string.Join(",", d.MissingSensors));
        Check(d.WatchdogSeconds == 196, "seconds=" + d.WatchdogSeconds);
        var two = "panic(cpu 0 caller 0x0): userspace watchdog timeout: no successful checkins from thermalmonitord\nMissing sensor(s): \nservice: thermalmonitord, no successful checkins in 180 seconds";
        var d2 = PanicAnalyzer.Analyze(PanicParser.Parse(two, "x"));
        Check(d2.MissingSensors.Count == 0, "سطر حساسات فارغ: " + string.Join(",", d2.MissingSensors));
        Check(d2.Kind == "مراقب النظام", "بلا حساس ← خدمة الحرارة: " + d2.Kind);
    }

    static void NoFalseSignatures()
    {
        var d = PanicAnalyzer.Analyze(PanicParser.Parse("panic(cpu 0 caller 0x0): something separate and unrelated caused a panic here\nDebugger message: panic", "x"));
        Check(d.Kind == "غير معروف", "لا يطابق SEP داخل كلمة: " + d.Kind);
    }

    static void Garbage()
    {
        Check(PanicParser.ParseMany("", "x").Count == 0, "نص فارغ");
        Check(PanicParser.ParseMany("مرحبا هذا ليس سجلا", "x").Count == 0, "نص عادي");
        Check(PanicParser.ParseMany("{\"bug_type\":\"210\"", "x").Count == 0, "رأس بلا محتوى");
        var d = PanicAnalyzer.Analyze(PanicParser.Parse("{\"bug_type\":\"210\",\"product\":\"iPhone99,9\"}", "x"));
        Check(d.Device == "iPhone99,9", "موديل غير معروف يبقى كما هو");
        Check(d.Title.Contains("لا يوجد نص بانك"), "title=" + d.Title);
        Check(PanicAnalyzer.Combine(new List<Diagnosis>()) == null, "تجميع قائمة فارغة");
        Check(PanicParser.Unescape("a\\nb\\/c\\u0041\\") == "a\nb/cA\\", "فك الهروب");
    }

    static void Print(Diagnosis d)
    {
        if (Environment.GetEnvironmentVariable("KASHIF_VERBOSE") == "1") Console.WriteLine(PanicAnalyzer.Report(d));
    }
}
