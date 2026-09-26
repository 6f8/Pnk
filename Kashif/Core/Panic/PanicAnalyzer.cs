using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kashif;

/// <summary>نتيجة تحليل سجل (أو عدة سجلات لنفس الجهاز)</summary>
public sealed class Diagnosis
{
    public PanicLog Log;
    public string Device = "", Product = "", Soc = "", Ios = "", Build = "", Time = "", BugInfo = "";
    /// <summary>تصنيف قصير للتقارير: حساس مفقود، SMC، مراقب النظام، التخزين NAND، ...</summary>
    public string Kind = "غير معروف";
    public string Title = "", Summary = "", Explanation = "", Headline = "";
    /// <summary>عالية / متوسطة / منخفضة</summary>
    public string Confidence = "منخفضة";
    public List<Candidate> Candidates = new();
    public List<string> Steps = new();
    public List<Evidence> Evidence = new();
    public List<string> Warnings = new();
    public List<string> MissingSensors = new();
    public string WatchdogService = "";
    public int WatchdogSeconds;
    public List<string> SmcKeys = new();
    /// <summary>عدد السجلات التي بُني عليها التحليل (أكثر من 1 في التحليل المجمّع)</summary>
    public int LogCount = 1;

    public string TopPart => Candidates.Count > 0 ? Candidates[0].Part : "";
    public string TopLabel => Candidates.Count > 0 ? Candidates[0].Label : "";
}

/// <summary>
/// محرك التحليل: يستخرج الحقائق من نص البانك (الحساس المفقود، الخدمة المتوقفة، مفاتيح SMC، نوع الانهيار)،
/// ثم يرتب الأسباب المحتملة حسب قاعدة المعرفة وخبرة المحل ومعلومات الحالة.
/// لا يعتمد على الواجهة ولا قاعدة البيانات — يُختبر وحده.
/// </summary>
public static class PanicAnalyzer
{
    static readonly Regex Headline = new(@"panic\s*\(\s*cpu\s*\d+\s*caller\s*0x[0-9a-f]+\s*\)\s*:\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Missing = new(@"Missing\s*sensor\s*\(\s*s\s*\)\s*:\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>رمز حساس: حرف ثم حروف/أرقام وفيه رقم واحد على الأقل (Prs0، mic1، TG0B) — فلا تُقرأ كلمات السطر التالي كرموز</summary>
    static readonly Regex SensorToken = new(@"^(?=.*\d)[A-Za-z][A-Za-z0-9]{1,5}$", RegexOptions.Compiled);
    static readonly Regex NoCheckinsFrom = new(@"no successful checkins from\s+([A-Za-z0-9_.\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex InducedCrashes = new(@"\((\d+)\s+induced crash", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SmcPanic = new(@"SMC PANIC\s*-?\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SmcAssert = new(@"ASSERT\s*:\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Outbox = new(@"OUTBOX\d*\s+not ready", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Hex64 = new(@"0x([0-9a-fA-F]{16})(?![0-9a-fA-F])", RegexOptions.Compiled);
    static readonly Regex RtkitClient = new(@"Client:\s*([A-Za-z0-9_.\-]+)", RegexOptions.Compiled);
    static readonly Regex GenericWatchdog = new(@"\bWDT\b|watchdog timeout", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ============================================================== سجل واحد
    public static Diagnosis Analyze(PanicLog log, CaseFlags flags = null, IEnumerable<CustomRule> custom = null)
    {
        var d = new Diagnosis { Log = log };
        var ps = log.PanicString ?? "";
        Identify(d, log, ps);

        // ---------- الحقائق ----------
        d.Headline = Headline.Match(ps) is { Success: true } hm ? hm.Groups[1].Value.Trim()
            : ps.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x != "") ?? "";
        if (d.Headline.Length > 220) d.Headline = d.Headline[..220] + "…";
        if (d.Headline != "") d.Evidence.Add(new("سطر الانهيار", d.Headline, "أول سطر في نص البانك: ما الذي أوقف الجهاز"));

        // الرموز حتى أول كلمة ليست رمزًا (إذا ضاعت أسطر النسخ يلتصق بعدها «service: ...» على نفس السطر)
        foreach (Match m in Missing.Matches(ps))
            foreach (var tok in Regex.Split(m.Groups[1].Value.Trim(), @"[\s,;]+"))
            {
                var t = tok.Trim().Trim('"', '\'', '.', ')', '(');
                if (t == "") continue;
                if (!SensorToken.IsMatch(t)) break;
                if (!d.MissingSensors.Contains(t, StringComparer.OrdinalIgnoreCase)) d.MissingSensors.Add(t);
            }

        if (NoCheckinsFrom.Match(ps) is { Success: true } wm)
        {
            d.WatchdogService = wm.Groups[1].Value;
            d.WatchdogSeconds = SecondsFor(ps, d.WatchdogService);
            var induced = InducedCrashes.Match(ps);
            d.Evidence.Add(new("خدمة متوقفة", d.WatchdogService + (induced.Success ? $" ({induced.Groups[1].Value} إعادة تشغيل قسرية)" : ""),
                "مراقب النظام أعاد تشغيل الجهاز لأن هذه الخدمة لم تسجّل حضورها"));
            if (d.WatchdogSeconds > 0)
                d.Evidence.Add(new("مدة الانتظار", d.WatchdogSeconds + " ثانية", RestartText(d.WatchdogSeconds)));
        }

        bool smc = SmcPanic.IsMatch(ps);
        if (smc) ReadSmc(d, ps);
        if (RtkitClient.Match(ps) is { Success: true } rc)
            d.Evidence.Add(new("المعالج المساعد", rc.Groups[1].Value, "برنامج المعالج المساعد الذي انهار (RTKit)"));

        // ---------- التشخيص الأساسي ----------
        var others = PanicKnowledge.Signatures.Where(s => s.Match.IsMatch(ps)).ToList();
        if (d.MissingSensors.Count > 0) SensorDiagnosis(d, log, ps);
        else if (smc) SmcDiagnosis(d);
        else if (d.WatchdogService != "" && PanicKnowledge.FindService(d.WatchdogService) is { } svc) ServiceDiagnosis(d, svc);
        else if (others.Count > 0) { SignatureDiagnosis(d, others[0]); others.RemoveAt(0); }
        else if (d.WatchdogService != "") UnknownService(d);
        else if (GenericWatchdog.IsMatch(ps)) GenericWatchdogDiagnosis(d);
        else Unknown(d, ps);

        // تواقيع أخرى في نفس السجل: أدلة إضافية وأسباب بدرجة أقل
        foreach (var s in others.Where(s => s.Kind != d.Kind))
        {
            d.Evidence.Add(new("علامة إضافية", s.Title, s.Explain));
            foreach (var c in s.Choices) Add(d, c.Part, c.Score - 20, c.Why);
        }

        // ---------- ملاحظات القراءة ----------
        d.Warnings.AddRange(log.Notes);
        if (log.BugType != "" && log.BugType != "210") d.Warnings.Add($"bug_type = {log.BugType}: {d.BugInfo}. ملف البانك الصحيح يبدأ اسمه بـ panic-full.");
        if (d.Product == "" && d.Device == "") d.Warnings.Add("لم يُعرف موديل الجهاز من السجل (لا يوجد حقل product).");

        ApplyFlags(d, flags);
        ApplyCustom(d, custom);
        Rank(d);
        return d;
    }

    static void Identify(Diagnosis d, PanicLog log, string ps)
    {
        d.Product = log.Product;
        d.Device = AppleDevices.Name(log.Product, ps);
        if (d.Product == "" && AppleDevices.BoardProduct(ps) is { } bp)
        {
            d.Product = bp;
            d.Evidence.Add(new("رمز البوردة", AppleDevices.BoardCode(ps), "عُرف الجهاز من رمز البوردة في نص البانك"));
        }
        d.Soc = log.SocCode is var sc && sc != "" ? (AppleDevices.SocName(sc) is var sn && sn != "" ? $"{sn} ({sc})" : sc) : "";
        d.Ios = log.IosVersion;
        d.Build = log.IosBuild;
        d.Time = log.Timestamp;
        d.BugInfo = PanicKnowledge.BugTypeInfo(log.BugType);
    }

    /// <summary>ثواني الانتظار في سطر الخدمة نفسها: «service: X ..., no successful checkins in 196 seconds»</summary>
    static int SecondsFor(string ps, string service)
    {
        var m = Regex.Match(ps, @"service:\s*" + Regex.Escape(service) + @"[^\n]*?no successful checkins in\s+(\d+)\s+seconds", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(ps, @"no successful checkins in\s+(\d+)\s+seconds", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;
    }

    static string RestartText(int seconds) =>
        seconds is >= 150 and <= 240 ? "يعيد الجهاز التشغيل كل 3 دقائق تقريبًا — النمط المعروف للحساس المفقود"
        : $"يعيد الجهاز التشغيل بعد نحو {Math.Max(1, (int)Math.Round(seconds / 60.0))} دقيقة من الإقلاع";

    // ============================================================== SMC
    static void ReadSmc(Diagnosis d, string ps)
    {
        var line = SmcPanic.Match(ps).Groups[1].Value.Trim();
        if (!d.Headline.Contains("SMC PANIC", StringComparison.OrdinalIgnoreCase))
            d.Evidence.Add(new("بانك SMC", line == "" ? "SMC PANIC" : line, "معالج SMC (الطاقة والحرارة والحساسات) انهار"));
        if (SmcAssert.Match(ps) is { Success: true } a && !line.Contains(a.Groups[1].Value.Trim(), StringComparison.Ordinal))
            d.Evidence.Add(new("شرط فشل (ASSERT)", a.Groups[1].Value.Trim(), "المكان داخل برنامج SMC الذي توقف عنده"));
        if (AppleDevices.BoardCode(ps) is var board && board != "")
            d.Evidence.Add(new("رمز البوردة", board, "من مسار برنامج SMC في السجل"));
        if (Outbox.IsMatch(ps))
            d.Evidence.Add(new("OUTBOX not ready", "نعم", "SMC توقف عن الرد على المعالج الرئيسي فأُعيد تشغيل الجهاز"));
        foreach (var key in DecodeSmcKeys(ps))
            if (!d.SmcKeys.Contains(key)) d.SmcKeys.Add(key);
        if (d.SmcKeys.Count > 0)
            d.Evidence.Add(new("آخر مفاتيح SMC", string.Join("، ", d.SmcKeys),
                "فُكّت من رسائل Mailbox: " + string.Join("، ", d.SmcKeys.Select(k => $"{k} = {PanicKnowledge.SmcKeyMeaning(k)}"))));
    }

    /// <summary>
    /// مفاتيح SMC من رسائل Mailbox: قيمة ‎0x‎ من 16 خانة، أول 4 بايت منها حروف مقروءة (مثل ‎0x5447304200006013‎ ← TG0B).
    /// تُقبل فقط إذا كانت 4 حروف/أرقام تبدأ بحرف وفيها حرفان كبيران على الأقل — فلا تُقرأ الأرقام العادية كمفاتيح.
    /// </summary>
    public static List<string> DecodeSmcKeys(string text)
    {
        var keys = new List<string>();
        foreach (Match m in Hex64.Matches(text ?? ""))
        {
            var hex = m.Groups[1].Value;
            var chars = new char[4];
            bool ok = true;
            for (int i = 0; i < 4 && ok; i++)
            {
                int b = int.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                chars[i] = (char)b;
                ok = b is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            }
            if (!ok || !char.IsLetter(chars[0]) || chars.Count(char.IsUpper) < 2) continue;
            var key = new string(chars);
            if (!keys.Contains(key)) keys.Add(key);
        }
        return keys;
    }

    static void SmcDiagnosis(Diagnosis d)
    {
        d.Kind = "SMC";
        d.Title = "انهيار معالج الطاقة والحساسات (SMC)";
        var battery = d.SmcKeys.Where(PanicKnowledge.IsBatteryKey).ToList();
        if (battery.Count > 0)
        {
            // المفاتيح المعروفة بالاسم أولًا (مثل B0AV وTG0B)، ثم بقية مفاتيح البطارية
            var named = battery.Where(PanicKnowledge.SmcKeys.ContainsKey).Select(k => $"{k} = {PanicKnowledge.SmcKeys[k]}").ToList();
            d.Explanation = "معالج SMC توقف أثناء قراءة بيانات البطارية (" + string.Join("، ", named.Count > 0 ? named : battery) +
                "). انقطاع الاتصال بشريحة قياس البطارية أو بحساس حرارتها هو السبب الأشيع لهذا الشكل.";
            Add(d, Parts.Battery, 75, "آخر ما قرأه SMC قبل الانهيار كان بيانات البطارية");
            Add(d, Parts.BatteryConn, 65, "موصل غير محكم أو متأكسد يقطع الاتصال");
            Add(d, Parts.SmcLine, 42, "إذا بقي البانك مع بطارية سليمة وموصل نظيف");
            Add(d, Parts.ChargingFlex, 38, "بعض خطوط الطاقة والحساسات تمر عبرها");
            d.Confidence = "متوسطة";
            d.Summary = "SMC انهار أثناء قراءة البطارية ← ابدأ بالبطارية وموصلها.";
            d.Steps.AddRange(new[]
            {
                "افحص صحة البطارية في الإعدادات (رسالة «قطعة غير معروفة» أو «خدمة» تؤكد الاتجاه).",
                "افصل البطارية ونظّف الموصل وأعد تركيبها بإحكام.",
                "جرّب بطارية سليمة معروفة، أو شغّل الجهاز على مصدر طاقة DC.",
                "إذا بقي نفس البانك: جرّب فلاتة شحن سليمة.",
                "إذا استمر: فحص خط الاتصال بين SMC وشريحة قياس البطارية على البوردة.",
            });
        }
        else
        {
            d.Explanation = "معالج SMC يدير الطاقة والحرارة والحساسات. لم تظهر في السجل مفاتيح تحدد القطعة، فالترتيب عام.";
            Add(d, Parts.Battery, 50, "أشيع سبب لانهيار SMC");
            Add(d, Parts.ChargingFlex, 45, "خطوط الطاقة والحساسات");
            Add(d, Parts.Board, 50, "SMC أو خطوطه على البوردة");
            d.Confidence = "منخفضة";
            d.Summary = "SMC انهار بدون مفاتيح واضحة ← جرّب البطارية ثم فلاتة الشحن ثم البوردة.";
            d.Steps.AddRange(new[]
            {
                "جرّب بطارية سليمة أو مصدر طاقة DC.",
                "جرّب فلاتة شحن سليمة.",
                "اجمع عدة سجلات بانك وحلّلها معًا: تكرار نفس المفتاح يحدد القطعة.",
                "إذا استمر: فحص البوردة.",
            });
        }
    }

    // ============================================================== الحساس المفقود
    static void SensorDiagnosis(Diagnosis d, PanicLog log, string ps)
    {
        d.Kind = "حساس مفقود";
        var family = AppleDevices.FamilyOf(log.Product, ps);
        var names = new List<string>();
        foreach (var code in d.MissingSensors)
        {
            var s = PanicKnowledge.FindSensor(code);
            if (s == null)
            {
                d.Evidence.Add(new("حساس مفقود", code, "رمز غير موجود في قاعدة المعرفة — أضف خبرتك عنه من «خبرة المحل»"));
                Add(d, Parts.LastPart, 55, $"الحساس {code} غير معروف في القاعدة: ابدأ بالفلاتة التي فُكّت أو استُبدلت آخر مرة");
                Add(d, Parts.Board, 35, "خط الحساس على البوردة");
                names.Add(code);
                continue;
            }
            d.Evidence.Add(new("حساس مفقود", s.Code, s.What + " — " + s.Note));
            foreach (var c in s.Locate(family)) Add(d, c.Part, c.Score, $"{s.Code}: {c.Why}");
            names.Add($"{s.Code} — {s.What}");
        }

        // أكثر من حساس على قطع مختلفة: غالبًا خط مشترك أو تأكسد
        var parts = d.MissingSensors.Select(c => PanicKnowledge.FindSensor(c)?.Locate(family).FirstOrDefault()?.Part).Where(p => p != null).Distinct().ToList();
        if (parts.Count > 1)
        {
            Add(d, Parts.Board, 55, "أكثر من حساس مفقود على قطع مختلفة: خط مشترك أو تأكسد على البوردة");
            d.Evidence.Add(new("عدة حساسات", string.Join("، ", d.MissingSensors), "الحساسات على أكثر من قطعة — افحص الخط المشترك والتأكسد"));
        }

        var top = d.MissingSensors.Select(PanicKnowledge.FindSensor).FirstOrDefault(s => s != null);
        var topChoice = top?.Locate(family).FirstOrDefault();
        d.Title = d.MissingSensors.Count == 1 ? $"حساس مفقود: {names[0]}" : "حساسات مفقودة: " + string.Join("، ", d.MissingSensors);
        d.Explanation = "النظام لا يجد " + (d.MissingSensors.Count == 1 ? "الحساس" : "الحساسات") + " " + string.Join("، ", names) +
            "، فتتوقف خدمة مراقبة الحرارة (thermalmonitord) عن التسجيل، فيُعيد مراقب النظام تشغيل الجهاز" +
            (d.WatchdogSeconds > 0 ? $" بعد {d.WatchdogSeconds} ثانية تقريبًا من كل إقلاع." : ".");
        d.Confidence = topChoice == null ? "منخفضة" : topChoice.Score >= 85 && parts.Count <= 1 ? "عالية" : "متوسطة";
        d.Summary = topChoice != null
            ? $"الحساس {top.Code} غير موجود ← السبب الأرجح: {topChoice.Part}."
            : $"الحساس {d.MissingSensors[0]} غير موجود ← ابدأ بآخر فلاتة فُكّت أو استُبدلت.";
        d.Steps.AddRange(new[]
        {
            topChoice != null ? $"افحص {topChoice.Part}: الموصل محكم؟ نظيف بلا تأكسد؟ هل استُبدلت بقطعة غير أصلية؟" : "افحص الفلاتة التي فُكّت أو استُبدلت آخر مرة.",
            "اختبار سريع: شغّل الجهاز والقطعة المشتبه بها مفصولة — يظهر نفس رمز الحساس في البانك، فهذا مسارها.",
            "ركّب قطعة سليمة: إن اختفى البانك فالقطعة هي السبب.",
            "إذا بقي البانك مع قطعة سليمة: خط الحساس على البوردة (فحص بالمجهر وقياس الخط).",
        });
    }

    // ============================================================== الخدمات والتواقيع
    static void ServiceDiagnosis(Diagnosis d, PanicKnowledge.Service s)
    {
        d.Kind = "مراقب النظام";
        d.Title = $"توقف {s.What} ({s.Name})";
        d.Explanation = $"مراقب النظام أعاد تشغيل الجهاز لأن {s.What} ({s.Name}) لم تسجّل حضورها" +
            (d.WatchdogSeconds > 0 ? $" خلال {d.WatchdogSeconds} ثانية." : ".");
        foreach (var c in s.Choices) Add(d, c.Part, c.Score, c.Why);
        d.Steps.AddRange(s.Steps);
        d.Confidence = s.Confidence;
        d.Summary = $"{s.What} ({s.Name}) لا تستجيب ← ابدأ بـ {s.Choices[0].Part}.";
    }

    static void UnknownService(Diagnosis d)
    {
        d.Kind = "مراقب النظام";
        d.Title = $"توقف خدمة {d.WatchdogService}";
        d.Explanation = $"الخدمة {d.WatchdogService} لم تسجّل حضورها فأُعيد تشغيل الجهاز. هذه الخدمة ليست في قاعدة المعرفة.";
        Add(d, Parts.Ios, 50, "أغلب خدمات النظام تتوقف بسبب خلل برمجي");
        Add(d, Parts.Board, 35, "قطعة تخدمها هذه الخدمة لا تستجيب");
        d.Steps.AddRange(new[] { "حدّث iOS أو أعد تثبيته عبر الكمبيوتر.", "إذا تكرر بعد الاستعادة: ابحث عن القطعة المرتبطة بالخدمة وأضف خبرتك في «خبرة المحل»." });
        d.Confidence = "منخفضة";
        d.Summary = $"الخدمة {d.WatchdogService} لا تستجيب ← ابدأ بتحديث أو استعادة iOS.";
    }

    static void SignatureDiagnosis(Diagnosis d, PanicKnowledge.Signature s)
    {
        d.Kind = s.Kind;
        d.Title = s.Title;
        d.Explanation = s.Explain;
        foreach (var c in s.Choices) Add(d, c.Part, c.Score, c.Why);
        d.Steps.AddRange(s.Steps);
        d.Confidence = s.Confidence;
        d.Summary = $"{s.Title} ← ابدأ بـ {s.Choices[0].Part}.";
        d.Evidence.Add(new("نوع الانهيار", s.Title, s.Explain));
    }

    static void GenericWatchdogDiagnosis(Diagnosis d)
    {
        d.Kind = "مراقب النظام";
        d.Title = "تعليق النظام (Watchdog)";
        d.Explanation = "مراقب النظام أعاد تشغيل الجهاز لأن النظام تعلّق، ولم يُذكر اسم خدمة محددة.";
        Add(d, Parts.Ios, 50, "تعليق برمجي");
        Add(d, Parts.Board, 40, "قطعة لا تستجيب");
        d.Steps.AddRange(new[] { "حدّث iOS أو أعد تثبيته عبر الكمبيوتر.", "اجمع عدة سجلات وحلّلها معًا." });
        d.Confidence = "منخفضة";
        d.Summary = "تعليق عام في النظام ← ابدأ بتحديث أو استعادة iOS.";
    }

    static void Unknown(Diagnosis d, string ps)
    {
        d.Kind = "غير معروف";
        d.Title = ps.Trim() == "" ? "لا يوجد نص بانك للتحليل" : "نوع بانك غير موجود في قاعدة المعرفة";
        d.Explanation = ps.Trim() == ""
            ? "السجل لا يحتوي نص البانك (panicString). انسخ الملف كاملًا من: الإعدادات ← الخصوصية والأمان ← التحليلات والتحسينات ← بيانات التحليلات ← panic-full."
            : "لم يطابق النص أي توقيع معروف. راجع سطر الانهيار أدناه، وأضف ما تعرفه في «خبرة المحل» ليُعرف في المرات القادمة.";
        if (ps.Trim() != "")
        {
            Add(d, Parts.Ios, 45, "كثير من الأنواع غير المعروفة برمجية");
            Add(d, Parts.LastPart, 40, "ابدأ بما فُكّ أو استُبدل مؤخرًا");
            d.Steps.AddRange(new[] { "حدّث iOS أو أعد تثبيته عبر الكمبيوتر.", "اجمع عدة سجلات وحلّلها معًا — التكرار يكشف القطعة." });
        }
        d.Confidence = "منخفضة";
        d.Summary = d.Title;
    }

    // ============================================================== الترتيب
    static void Add(Diagnosis d, string part, int score, string why)
    {
        score = Math.Clamp(score, 1, 99);
        var c = d.Candidates.FirstOrDefault(x => x.Part == part);
        if (c == null) { d.Candidates.Add(new Candidate { Part = part, Score = score, Why = why }); return; }
        if (score > c.Score) { c.Score = score; c.Why = why; }
        else if (!c.Why.Contains(why, StringComparison.Ordinal) && c.Why.Length < 300) c.Why += " — " + why;
    }

    static void Bump(Diagnosis d, string part, int by, string why)
    {
        var c = d.Candidates.FirstOrDefault(x => x.Part == part);
        if (c == null) return;
        c.Score = Math.Clamp(c.Score + by, 1, 99);
        c.Why += " — " + why;
    }

    static void ApplyFlags(Diagnosis d, CaseFlags f)
    {
        if (f == null || !f.Any || d.Candidates.Count == 0) return;
        if (f.Liquid)
        {
            Add(d, Parts.Liquid, Math.Min(90, d.Candidates.Max(c => c.Score) + 5), "الجهاز تعرّض لسوائل: التأكسد في الموصلات والفلاتات يسبب هذا النوع من الانقطاع");
            foreach (var p in new[] { Parts.ChargingFlex, Parts.BatteryConn, Parts.Board, Parts.SmcLine }) Bump(d, p, 10, "مع السوائل: افحص التأكسد");
            d.Steps.Insert(0, "لا تشحن الجهاز: افصل البطارية ونظّف البوردة والموصلات بالكحول الأيزوبروبيلي 99% (أو الألتراسونك) قبل أي تبديل.");
        }
        if (f.BatteryReplaced) Bump(d, Parts.Battery, 15, "البطارية مستبدلة (قد تكون غير أصلية أو ضعيفة التركيب)");
        if (f.BatteryReplaced) Bump(d, Parts.BatteryConn, 8, "فُكّ الموصل عند تبديل البطارية");
        if (f.ChargingFlexReplaced) Bump(d, Parts.ChargingFlex, 15, "فلاتة الشحن مستبدلة (قد تكون تجارية بلا حساس يعمل)");
        if (f.ScreenReplaced) { Bump(d, Parts.Screen, 15, "الشاشة مستبدلة"); Bump(d, Parts.FrontFlex, 10, "فُكّت فلاتات الجهة الأمامية"); }
        if (f.Dropped) foreach (var p in new[] { Parts.Board, Parts.BatteryConn, Parts.Screen }) Bump(d, p, 8, "بعد سقوط: موصل مفصول أو شرخ في البوردة");
        d.Evidence.Add(new("معلومات الحالة", f.ToString(), "من الفني — غيّرت ترتيب الأسباب"));
    }

    static void ApplyCustom(Diagnosis d, IEnumerable<CustomRule> custom)
    {
        if (custom == null) return;
        var ps = d.Log?.PanicString ?? "";
        foreach (var r in custom)
        {
            if (string.IsNullOrWhiteSpace(r.Pattern) || string.IsNullOrWhiteSpace(r.Part)) continue;
            var pat = r.Pattern.Trim();
            bool hit = ps.Contains(pat, StringComparison.OrdinalIgnoreCase) || d.MissingSensors.Contains(pat, StringComparer.OrdinalIgnoreCase);
            if (!hit) continue;
            if (!string.IsNullOrWhiteSpace(r.Device))
            {
                var dev = r.Device.Trim();
                if (!(d.Device.Contains(dev, StringComparison.OrdinalIgnoreCase) || d.Product.Contains(dev, StringComparison.OrdinalIgnoreCase))) continue;
            }
            var why = "خبرة المحل" + (r.Name != "" ? $" ({r.Name})" : "") + (r.Note != "" ? ": " + r.Note : "");
            Add(d, r.Part.Trim(), r.Score, why);
            d.Evidence.Add(new("خبرة المحل", pat, $"{r.Part} — {r.Level}" + (r.Note != "" ? " — " + r.Note : "")));
            if (r.Level == "مؤكد" && d.Confidence != "عالية") d.Confidence = "عالية";
        }
    }

    /// <summary>اسم الدرجة: «الأرجح» للأول فقط إذا تقدّم بوضوح (10 درجات على الأقل) وكانت درجته 70 فأكثر</summary>
    public static string LabelOf(int score, bool leadsClearly) =>
        leadsClearly && score >= 70 ? "الأرجح" : score >= 60 ? "مرجّح" : score >= 35 ? "محتمل" : "احتمال بعيد";

    /// <summary>ترتيب الأسباب وتسميتها: «الأرجح» للأول فقط إذا تقدّم بوضوح، ثم مرجّح/محتمل/احتمال بعيد</summary>
    static void Rank(Diagnosis d)
    {
        d.Candidates = d.Candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Part, StringComparer.Ordinal).ToList();
        for (int i = 0; i < d.Candidates.Count; i++)
            d.Candidates[i].Label = LabelOf(d.Candidates[i].Score, i == 0 && (d.Candidates.Count == 1 || d.Candidates[0].Score - d.Candidates[1].Score >= 10));
        if (d.Candidates.Count > 0 && d.Candidates[0].Label != "الأرجح" && d.Confidence == "عالية") d.Confidence = "متوسطة";
    }

    // ============================================================== عدة سجلات
    /// <summary>
    /// تحليل مجمّع لسجلات جهاز واحد: تكرار نفس الحساس أو نفس النوع يرفع الثقة، واختلاف الأنواع يخفضها
    /// ويرجّح سببًا عامًا (بوردة أو طاقة أو نظام). السجلات من أجهزة مختلفة لا تُجمع (تحذير).
    /// </summary>
    public static Diagnosis Combine(IList<Diagnosis> items)
    {
        if (items == null || items.Count == 0) return null;
        if (items.Count == 1) return items[0];
        var first = items[0];
        var d = new Diagnosis
        {
            Log = first.Log, Device = first.Device, Product = first.Product, Soc = first.Soc, Ios = first.Ios, Build = first.Build,
            Time = string.Join(" ← ", new[] { items.Select(x => x.Time).Where(t => t != "").DefaultIfEmpty("").Min(), items.Select(x => x.Time).Where(t => t != "").DefaultIfEmpty("").Max() }.Where(t => t != "").Distinct()),
            BugInfo = first.BugInfo, LogCount = items.Count,
        };

        var devices = items.Select(x => x.Log?.DeviceKey ?? "").Where(k => k != "").Distinct().ToList();
        if (devices.Count > 1)
            d.Warnings.Add($"السجلات من {devices.Count} أجهزة مختلفة (مفتاح التقارير أو الموديل مختلف) — حلّل سجلات كل جهاز وحدها.");

        // الأسباب: متوسط الدرجة عبر السجلات (السبب الذي لا يظهر في سجل يُحسب صفرًا فيه)
        foreach (var g in items.SelectMany(x => x.Candidates).GroupBy(c => c.Part))
        {
            int n = g.Count();
            int avg = (int)Math.Round(g.Sum(c => (double)c.Score) / items.Count);
            var why = g.OrderByDescending(c => c.Score).First().Why;
            d.Candidates.Add(new Candidate { Part = g.Key, Score = Math.Clamp(avg, 1, 99), Why = n == items.Count ? why : $"{why} (ظهر في {n} من {items.Count} سجلات)" });
        }

        var kinds = items.Select(x => x.Kind).Distinct().ToList();
        var sensorSets = items.Select(x => string.Join(",", x.MissingSensors.Select(s => s.ToLowerInvariant()).OrderBy(s => s))).Distinct().ToList();
        d.MissingSensors = items.SelectMany(x => x.MissingSensors).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        d.SmcKeys = items.SelectMany(x => x.SmcKeys).Distinct().ToList();
        d.WatchdogService = string.Join("، ", items.Select(x => x.WatchdogService).Where(s => s != "").Distinct());
        d.WatchdogSeconds = items.Select(x => x.WatchdogSeconds).FirstOrDefault(s => s > 0);

        foreach (var x in items)
            d.Evidence.Add(new("سجل " + (x.Time != "" ? x.Time : x.Log?.Source ?? ""), x.Title, $"{x.Kind} — الأرجح: {(x.TopPart == "" ? "—" : x.TopPart)}"));

        var rank = new Dictionary<string, int> { ["منخفضة"] = 0, ["متوسطة"] = 1, ["عالية"] = 2 };
        int conf = items.Min(x => rank.TryGetValue(x.Confidence, out var r) ? r : 0);

        if (kinds.Count == 1)
        {
            d.Kind = first.Kind;
            bool sameSensors = sensorSets.Count == 1 && sensorSets[0] != "";
            d.Title = first.Title + $" — تكرر في {items.Count} سجلات";
            d.Explanation = first.Explanation + (sameSensors ? $"\nنفس الحساس ({string.Join("، ", first.MissingSensors)}) ظهر في كل السجلات: هذا يؤكد مسار القطعة." : "");
            if ((sameSensors || first.Kind == "SMC" && items.All(x => x.SmcKeys.Any(PanicKnowledge.IsBatteryKey))) && conf < 2) conf++;
            d.Steps.AddRange(first.Steps);
            if (!sameSensors && d.MissingSensors.Count > 1)
            {
                Add(d, Parts.Board, 60, "الحساسات المفقودة تتغير بين السجلات: خط مشترك أو تأكسد أو مشكلة طاقة");
                d.Explanation += "\nالحساسات المفقودة تختلف بين السجلات — هذا يرجّح خطًا مشتركًا أو تأكسدًا بدل قطعة واحدة.";
            }
        }
        else
        {
            d.Kind = "أنواع مختلفة";
            d.Title = $"{kinds.Count} أنواع بانك مختلفة في {items.Count} سجلات";
            d.Explanation = "كل مرة يتوقف الجهاز بسبب مختلف (" + string.Join("، ", kinds) + "). هذا غالبًا سبب عام: بطارية أو طاقة غير مستقرة، تأكسد، مشكلة في البوردة، أو نظام تالف — وليس قطعة واحدة.";
            Add(d, Parts.Battery, 55, "طاقة غير مستقرة تسبب انهيارات متنوعة");
            Add(d, Parts.Board, 55, "مشكلة عامة في البوردة أو تأكسد");
            Add(d, Parts.Ios, 45, "نظام تالف: جرّب الاستعادة عبر الكمبيوتر");
            d.Steps.AddRange(new[]
            {
                "أعد تثبيت iOS عبر الكمبيوتر وجرّب قبل استعادة النسخة الاحتياطية.",
                "جرّب بطارية سليمة أو مصدر طاقة DC.",
                "افحص البوردة بحثًا عن تأكسد أو آثار ضربة.",
            });
            if (conf > 0) conf--;
        }
        d.Confidence = rank.First(kv => kv.Value == conf).Key;
        foreach (var w in items.SelectMany(x => x.Warnings).Distinct()) if (!d.Warnings.Contains(w)) d.Warnings.Add(w);
        Rank(d);
        d.Summary = d.Candidates.Count > 0 ? $"{items.Count} سجلات ← {d.Title}. الأرجح: {d.Candidates[0].Part}." : d.Title;
        return d;
    }

    // ============================================================== تقرير نصي
    /// <summary>تقرير نصي كامل (للنسخ أو الإرسال للزبون أو الحفظ)</summary>
    public static string Report(Diagnosis d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("تقرير تحليل البانك — كاشف");
        sb.AppendLine(new string('─', 40));
        void Line(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) sb.AppendLine($"{k}: {v}"); }
        Line("الجهاز", d.Device + (d.Product != "" && d.Product != d.Device ? $" ({d.Product})" : ""));
        Line("المعالج", d.Soc);
        Line("iOS", d.Ios + (d.Build != "" ? $" ({d.Build})" : ""));
        Line("وقت البانك", d.Time);
        if (d.LogCount > 1) Line("عدد السجلات", d.LogCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
        Line("التشخيص", d.Title);
        Line("الخلاصة", d.Summary);
        Line("درجة الثقة", d.Confidence);
        sb.AppendLine();
        sb.AppendLine("الأسباب مرتبة:");
        for (int i = 0; i < d.Candidates.Count; i++) sb.AppendLine($"  {i + 1}. {d.Candidates[i].Part} — {d.Candidates[i].Label}: {d.Candidates[i].Why}");
        if (d.Steps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("خطوات الفحص:");
            for (int i = 0; i < d.Steps.Count; i++) sb.AppendLine($"  {i + 1}. {d.Steps[i]}");
        }
        if (d.Evidence.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("الأدلة من السجل:");
            foreach (var e in d.Evidence) sb.AppendLine($"  • {e.What}: {e.Value}" + (e.What == "سطر الانهيار" || e.Meaning == "" ? "" : $" — {e.Meaning}"));
        }
        if (d.Warnings.Count > 0)
        {
            sb.AppendLine();
            foreach (var w in d.Warnings) sb.AppendLine("تنبيه: " + w);
        }
        return sb.ToString();
    }
}
