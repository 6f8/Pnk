using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kashif;

/// <summary>خدمة في قائمة مراقب النظام: عدد مرات التسجيل الناجح خلال المدة، أو توقفها</summary>
public sealed record ServiceStat(string Name, int Checkins, int Seconds, bool Stopped, int InducedCrashes);

/// <summary>قناة اتصال بين SMC والمعالج الرئيسي (Mailbox N): هل توقفت (OUTBOX not ready)، والمفاتيح التي طُلبت فيها بالترتيب</summary>
public sealed record SmcChannel(int Index, bool NotReady, List<string> Keys);

/// <summary>سطر في الخط الزمني لسجلات الجهاز (التحليل المجمّع)</summary>
public sealed record TimelineItem(DateTimeOffset? Time, string TimeText, string Kind, string Title, string TopPart, string Signature, string Source);

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
    /// <summary>موضع فشل SMC (مثل target/d94/target.cpp:250) ورقم المهمة التي توقفت داخله</summary>
    public string SmcAssert = "", FaultingTask = "";
    /// <summary>خانات «S.sensor array» غير الصفرية (قيم خام من SMC — معناها غير موثّق، تُحفظ للمقارنة)</summary>
    public List<(int Index, long Value)> SensorArray = new();
    /// <summary>قنوات SMC (Mailbox 0، 1 ...) مفصولة: القناة المتوقفة هي مكان العطل، والقنوات السليمة فيها قراءات دورية فقط</summary>
    public List<SmcChannel> SmcChannels = new();
    /// <summary>المفاتيح المذكورة بالاسم في سطر الفشل نفسه (مثل «SMC BSC failure, TAOP TAOC») — أقوى دليل على مكان العطل</summary>
    public List<string> SmcFailedKeys = new();
    /// <summary>
    /// بصمة النمط للتعلّم من نتائج المحل: sensor:Prs0 أو smc:target/d94/target.cpp:250 أو service:wifid أو kind:AOP.
    /// فحصان بنفس البصمة = نفس نوع العطل من ناحية السجل.
    /// </summary>
    public string Signature = "";
    /// <summary>نص يمكن أن تطابقه قاعدة في «خبرة المحل» لهذا النمط (رمز الحساس، اسم الخدمة، موضع ASSERT) — فارغ إن لم يوجد</summary>
    public string LearnPattern = "";
    /// <summary>أماكن الحساس من معلومة خاصة بهذا الموديل (وليس الجيل)</summary>
    public bool ModelSpecific;
    /// <summary>عدد السجلات التي بُني عليها التحليل (أكثر من 1 في التحليل المجمّع)</summary>
    public int LogCount = 1;
    /// <summary>الخط الزمني للسجلات (في التحليل المجمّع) مرتبًا من الأقدم</summary>
    public List<TimelineItem> Timeline = new();
    /// <summary>كل خدمات مراقب النظام في السجل: عدد مرات التسجيل وفي كم ثانية، ومن توقف</summary>
    public List<ServiceStat> Services = new();
    /// <summary>العملية التي كانت تعمل لحظة الانهيار (Panicked task … pid N: name)</summary>
    public string PanickedProcess = "";
    /// <summary>الدرايفرات (kext) في مسار الانهيار، وآخر درايفر بدأ</summary>
    public List<string> BacktraceKexts = new();
    public string LastKext = "";
    /// <summary>المدة من الإقلاع حتى الانهيار (من Epoch Time) بالثواني — null إن لم تُقرأ</summary>
    public double? UptimeSeconds;
    /// <summary>أجوبة الفحص التفاعلي المطبّقة على هذا التحليل</summary>
    public int AnswersApplied;

    public string TopPart => Candidates.Count > 0 ? Candidates[0].Part : "";
    public string TopLabel => Candidates.Count > 0 ? Candidates[0].Label : "";
}

/// <summary>
/// محرك التحليل: يستخرج الحقائق من نص البانك (الحساس المفقود، الخدمة المتوقفة، مفاتيح SMC وموضع فشله، نوع الانهيار)،
/// ثم يرتب الأسباب المحتملة حسب قاعدة المعرفة وخبرة المحل ومعلومات الحالة.
/// لا يعتمد على الواجهة ولا قاعدة البيانات — يُختبر وحده.
/// </summary>
public static class PanicAnalyzer
{
    static readonly Regex Headline = new(@"panic\s*\(\s*cpu\s*\d+\s*caller\s*0x[0-9a-f]+\s*\)\s*:\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Missing = new(@"Missing\s*sensor\s*\(\s*s\s*\)\s*:\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>رمز حساس: حرف ثم حروف/أرقام وفيه رقم واحد على الأقل (Prs0، mic1، TG0B) — فلا تُقرأ كلمات السطر التالي كرموز</summary>
    static readonly Regex SensorToken = new(@"^(?=.*\d)[A-Za-z][A-Za-z0-9]{1,5}$", RegexOptions.Compiled);
    /// <summary>رمز منسوخ من صورة قد يحمل O بدل 0 أو l بدل 1 (PrsO، micl)</summary>
    static readonly Regex OcrSensorToken = new(@"^[A-Za-z][A-Za-z0-9]{1,5}$", RegexOptions.Compiled);
    static readonly Regex NoCheckinsFrom = new(@"no successful checkins from\s+([A-Za-z0-9_.\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex InducedCrashes = new(@"\((\d+)\s+induced crash", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SmcPanic = new(@"SMC PANIC\s*-?\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SmcAssert = new(@"ASSERT\s*:\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex AssertLocation = new(@"ASSERT\s*:\s*([A-Za-z0-9_./\\-]+\.(?:c|cpp|cc|h|m)\s*:\s*\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex FaultingTaskRx = new(@"Faulting task\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SensorArrayRx = new(@"S\.sensor array\s*(\d+)\s*-\s*(\d+)\s*is\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Outbox = new(@"OUTBOX\d*\s+not ready", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex MailboxHead = new(@"Mailbox\s*\(\s*(\d+)\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>نهاية قسم القنوات: سجل RTBuddy (فيه نسخة من رسائل القناة 0) أو نهاية نص البانك</summary>
    static readonly Regex MailboxEnd = new(@"RTBuddy\s*\(|Debugger message", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>«Misc(2) OUTBOX1 not ready» ← القناة 1 هي التي توقفت</summary>
    static readonly Regex OutboxIndex = new(@"OUTBOX\s*(\d+)\s+not\s+ready", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex ChannelNotReady = new(@"OUTBOX\s+not\s+ready", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>ما بعد «failure,» في سطر فشل SMC (قد يذكر المفاتيح التي فشلت قراءتها)</summary>
    static readonly Regex FailureTail = new(@"failure\s*,\s*([^\n]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex KeyToken = new(@"(?<![A-Za-z0-9])([A-Za-z][A-Za-z0-9]{3})(?![A-Za-z0-9])", RegexOptions.Compiled);
    static readonly Regex Hex64 = new(@"0x([0-9a-fA-F]{16})(?![0-9a-fA-F])", RegexOptions.Compiled);
    static readonly Regex RtkitClient = new(@"Client:\s*([A-Za-z0-9_.\-]+)", RegexOptions.Compiled);
    static readonly Regex GenericWatchdog = new(@"\bWDT\b|watchdog timeout", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex ServiceLine = new(@"service:\s*([A-Za-z0-9_.\-]+)\s*(?:\((\d+)\s+induced crash(?:es)?\))?\s*,\s*(?:total successful checkins in\s+(\d+)\s+seconds\s*:\s*(\d+)|no successful checkins in\s+(\d+)\s+seconds)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex PanickedTask = new(@"Panicked task\s+0x[0-9a-fA-F]+:[^\n]*?pid\s+(-?\d+)\s*:\s*([^\n,]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex KextSection = new(@"Kernel Extensions in backtrace:\s*\n((?:[ \t]*com\.apple\.[^\n]*\n?|[ \t]*dependency:[^\n]*\n?)+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex KextId = new(@"(com\.apple\.[A-Za-z0-9_.\-]+)", RegexOptions.Compiled);
    static readonly Regex LastKextRx = new(@"last started kext at \d+:\s*(com\.apple\.[A-Za-z0-9_.\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex EpochBoot = new(@"Boot\s*:\s*0x([0-9a-fA-F]+)", RegexOptions.Compiled);
    static readonly Regex EpochCalendar = new(@"Calendar\s*:\s*0x([0-9a-fA-F]+)", RegexOptions.Compiled);
    static readonly Regex AliveBits = new(@"current\s+([0-9a-fA-F]+)\s*,\s*mask\s+([0-9a-fA-F]+)\s*,\s*expected\s+([0-9a-fA-F]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly (string Label, Regex Rx)[] InfoLines =
    {
        ("رسالة المصحّح", new(@"Debugger message\s*:\s*([^\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("نوع الإصدار", new(@"OS release type\s*:\s*([^\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("إصدار iBoot", new(@"iBoot version\s*:\s*([^\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("الإقلاع الآمن", new(@"secure boot\?\s*:\s*([^\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("إصدار سجل البانك", new(@"Paniclog version\s*:\s*([^\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("Memory ID", new(@"Memory ID\s*:\s*([^\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
    };

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
        if (d.Headline != "") d.Evidence.Add(new("سطر الانهيار", d.Headline, "أول سطر في نص البانك: ما الذي أوقف الجهاز", Cut(d.Headline, 50)));

        ReadSensors(d, ps);

        if (NoCheckinsFrom.Match(ps) is { Success: true } wm)
        {
            d.WatchdogService = wm.Groups[1].Value;
            d.WatchdogSeconds = SecondsFor(ps, d.WatchdogService);
            var induced = InducedCrashes.Match(ps);
            d.Evidence.Add(new("خدمة متوقفة", d.WatchdogService + (induced.Success ? $" ({induced.Groups[1].Value} إعادة تشغيل قسرية)" : ""),
                "مراقب النظام أعاد تشغيل الجهاز لأن هذه الخدمة لم تسجّل حضورها", "checkins from " + d.WatchdogService));
            if (d.WatchdogSeconds > 0)
                d.Evidence.Add(new("مدة الانتظار", d.WatchdogSeconds + " ثانية", RestartText(d.WatchdogSeconds), "checkins in " + d.WatchdogSeconds));
        }

        ReadDeep(d, ps);
        bool smc = SmcPanic.IsMatch(ps);
        if (smc) ReadSmc(d, ps);
        if (RtkitClient.Match(ps) is { Success: true } rc)
            d.Evidence.Add(new("المعالج المساعد", rc.Groups[1].Value, "برنامج المعالج المساعد الذي انهار (RTKit)", "Client:"));

        // ---------- التشخيص الأساسي ----------
        var others = PanicKnowledge.Signatures.Where(s => s.Match.IsMatch(ps)).ToList();
        if (d.MissingSensors.Count > 0) SensorDiagnosis(d, log, ps);
        else if (smc) SmcDiagnosis(d, ps);
        else if (d.WatchdogService != "" && PanicKnowledge.FindService(d.WatchdogService) is { } svc) ServiceDiagnosis(d, svc);
        else if (others.Count > 0) { SignatureDiagnosis(d, others[0], ps); others.RemoveAt(0); }
        else if (d.WatchdogService != "") UnknownService(d);
        else if (GenericWatchdog.IsMatch(ps)) GenericWatchdogDiagnosis(d);
        else Unknown(d, ps);

        // تواقيع أخرى في نفس السجل: أدلة إضافية وأسباب بدرجة أقل
        foreach (var s in others.Where(s => s.Kind != d.Kind))
        {
            d.Evidence.Add(new("علامة إضافية", s.Title, s.Explain, s.Match.Match(ps).Value));
            foreach (var c in s.Choices) Add(d, c.Part, c.Score - 20, c.Why);
        }

        // ---------- الدرايفرات في مسار الانهيار: دليل ثانوي بدرجة منخفضة ----------
        foreach (var k in d.BacktraceKexts.Select(PanicKnowledge.FindKext).Where(k => k != null).Distinct())
            foreach (var c in k.Choices) Add(d, c.Part, c.Score, $"{k.What} ({k.Prefix}) في مسار الانهيار");

        // ---------- جهاز معدّل ----------
        if (log.Rooted && d.Candidates.Count > 0)
        {
            Add(d, Parts.Ios, 55, "الجهاز معدّل (جيلبريك): التعديلات البرمجية سبب شائع للبانك");
            Bump(d, Parts.Ios, 10, "roots installed غير صفر");
            d.Evidence.Add(new("جهاز معدّل", "roots installed = " + log.RootsInstalled, "الجيلبريك والتعديلات البرمجية تسبب بانكات لا علاقة لها بالقطع", "roots"));
        }

        // ---------- علامة إصلاح سابق ----------
        if (log.Repaired && d.Candidates.Count > 0)
        {
            d.Evidence.Add(new("علامة إصلاح في السجل", "repairStatus = " + log.RepairStatus,
                "يُرجَّح أن الجهاز مرّ بإصلاح أو تبديل قطع (المعنى الدقيق للرقم غير موثّق). اسأل عمّا استُبدل: القطعة المستبدلة أول مشتبه به إذا ظهر البانك بعد الإصلاح.",
                "repairStatus"));
            if (d.Candidates.Any(c => c.Part == Parts.LastPart)) Bump(d, Parts.LastPart, 8, "السجل يحمل علامة إصلاح سابق (repairStatus)");
            else Add(d, Parts.LastPart, 45, "السجل يحمل علامة إصلاح سابق (repairStatus)");
        }

        // ---------- ملاحظات القراءة ----------
        d.Warnings.AddRange(log.Notes);
        if (log.BugType != "" && log.BugType != "210") d.Warnings.Add($"bug_type = {log.BugType}: {d.BugInfo}. ملف البانك الصحيح يبدأ اسمه بـ panic-full.");
        if (d.Product == "" && d.Device == "") d.Warnings.Add("لم يُعرف موديل الجهاز من السجل (لا يوجد حقل product).");
        if (AppleDevices.IsIPad(d.Product) && d.MissingSensors.Count > 0)
            d.Warnings.Add("جهاز iPad: أماكن الحساسات تختلف عن الآيفون، فالترتيب مبني على الأماكن العامة — تأكد من مخطط الجهاز.");

        ApplyFlags(d, flags);
        ApplyCustom(d, custom);
        Rank(d);
        return d;
    }

    static string Cut(string s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length > n ? s[..n] : s;

    static void Identify(Diagnosis d, PanicLog log, string ps)
    {
        d.Product = log.Product;
        d.Device = AppleDevices.Name(log.Product, ps);
        if (d.Product == "" && AppleDevices.BoardProduct(ps) is { } bp)
        {
            d.Product = bp;
            d.Evidence.Add(new("رمز البوردة", AppleDevices.BoardCode(ps), "عُرف الجهاز من رمز البوردة في نص البانك", "target"));
        }
        d.Soc = log.SocCode is var sc && sc != "" ? (AppleDevices.SocName(sc) is var sn && sn != "" ? $"{sn} ({sc})" : sc) : "";
        d.Ios = log.IosVersion;
        d.Build = log.IosBuild;
        d.Time = log.Timestamp;
        d.BugInfo = PanicKnowledge.BugTypeInfo(log.BugType);
    }

    /// <summary>
    /// رموز «Missing sensor(s)» حتى أول كلمة ليست رمزًا (إذا ضاعت أسطر النسخ يلتصق بعدها «service: ...» على نفس السطر).
    /// الرمز غير المعروف الذي يصبح معروفًا بتصحيح O←0 أو l/I←1 يُصحَّح ويُذكر ذلك في الأدلة.
    /// </summary>
    static void ReadSensors(Diagnosis d, string ps)
    {
        foreach (Match m in Missing.Matches(ps))
            foreach (var tok in Regex.Split(m.Groups[1].Value.Trim(), @"[\s,;]+"))
            {
                var t = tok.Trim().Trim('"', '\'', '.', ')', '(');
                if (t == "") continue;
                if (PanicKnowledge.FindSensor(t) == null && OcrSensorToken.IsMatch(t) && FixSensor(t) is { } fixedCode)
                {
                    d.Evidence.Add(new("تصحيح رمز", $"{t} ← {fixedCode}", "خطأ نسخ من صورة (O بدل 0 أو l بدل 1) — صُحّح لأنه يطابق رمزًا معروفًا", t));
                    t = fixedCode;
                }
                else if (!SensorToken.IsMatch(t)) break;
                if (!d.MissingSensors.Contains(t, StringComparer.OrdinalIgnoreCase)) d.MissingSensors.Add(t);
            }
    }

    /// <summary>
    /// تصحيح أخطاء قراءة الصور في رمز الحساس: كل حرف يشبه 0 (O، o، e — قارئ ويندوز يقرأ الصفر المنقّط e)
    /// أو يشبه 1 (l، I، i) يُجرَّب بالحالتين، ويُقبل التصحيح فقط إن طابق رمزًا معروفًا في قاعدة المعرفة.
    /// </summary>
    static string FixSensor(string t)
    {
        var options = t.Select(c => c switch
        {
            'O' or 'o' or 'e' => new[] { c, '0' },
            'l' or 'I' or 'i' => new[] { c, '1' },
            _ => new[] { c },
        }).ToList();
        IEnumerable<string> all = new[] { "" };
        foreach (var o in options) all = all.SelectMany(prefix => o.Select(c => prefix + c));
        return all.Where(c => c != t).Take(256).Select(PanicKnowledge.FindSensor).FirstOrDefault(x => x != null)?.Code;
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

    // ============================================================== القراءة العميقة
    /// <summary>
    /// كل ما في نص البانك مما له معنى: قائمة الخدمات، العملية وقت الانهيار، الدرايفرات في مسار الانهيار،
    /// المدة من الإقلاع، وأسطر المعلومات (تُعرض في مجموعة «info» ولا تغيّر الترتيب).
    /// </summary>
    static void ReadDeep(Diagnosis d, string ps)
    {
        foreach (Match m in ServiceLine.Matches(ps))
        {
            string name = m.Groups[1].Value;
            if (d.Services.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            int induced = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            bool stopped = m.Groups[5].Success;
            int seconds = int.Parse(stopped ? m.Groups[5].Value : m.Groups[3].Value, CultureInfo.InvariantCulture);
            int checkins = stopped ? 0 : int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            d.Services.Add(new ServiceStat(name, checkins, seconds, stopped, induced));
        }
        if (d.Services.Count > 0)
        {
            var stoppedList = d.Services.Where(x => x.Stopped).Select(x => x.Name).ToList();
            d.Evidence.Add(new("خدمات النظام", $"عددها {d.Services.Count} — المتوقفة: " + (stoppedList.Count == 0 ? "لا شيء" : string.Join("، ", stoppedList)),
                "كل خدمة تسجّل حضورها كل 10 ثوانٍ تقريبًا؛ المتوقفة هي التي تسببت في إعادة التشغيل — والبقية سليمة", "service:", "info"));
        }
        if (PanickedTask.Match(ps) is { Success: true } pt)
        {
            d.PanickedProcess = $"{pt.Groups[2].Value.Trim()} (pid {pt.Groups[1].Value})";
            d.Evidence.Add(new("العملية وقت الانهيار", d.PanickedProcess,
                pt.Groups[1].Value == "0" ? "kernel_task: الانهيار داخل النواة نفسها أو أحد درايفراتها" : "العملية التي كانت تعمل لحظة الانهيار", "Panicked task", "info"));
        }
        if (KextSection.Match(ps) is { Success: true } ks)
            foreach (Match k in KextId.Matches(ks.Groups[1].Value))
                if (!d.BacktraceKexts.Contains(k.Groups[1].Value)) d.BacktraceKexts.Add(k.Groups[1].Value);
        if (d.BacktraceKexts.Count > 0)
            d.Evidence.Add(new("درايفرات في مسار الانهيار", string.Join("، ", d.BacktraceKexts),
                string.Join("؛ ", d.BacktraceKexts.Select(k => PanicKnowledge.FindKext(k) is { } x ? $"{k} = {x.What}" : $"{k} = غير معروف في القاعدة")),
                "Kernel Extensions in backtrace"));
        if (LastKextRx.Match(ps) is { Success: true } lk)
        {
            d.LastKext = lk.Groups[1].Value;
            d.Evidence.Add(new("آخر درايفر بدأ", d.LastKext, "آخر درايفر حُمّل قبل الانهيار — غالبًا لا علاقة له بالسبب، يُذكر للمقارنة", "last started kext", "info"));
        }
        if (EpochBoot.Match(ps) is { Success: true } eb && EpochCalendar.Match(ps) is { Success: true } ec &&
            long.TryParse(eb.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var boot) &&
            long.TryParse(ec.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cal) && cal > boot && cal - boot < 10_000_000)
        {
            d.UptimeSeconds = cal - boot;
            d.Evidence.Add(new("المدة من الإقلاع", Duration(d.UptimeSeconds.Value),
                d.UptimeSeconds is >= 150 and <= 260 ? "انهار بعد نحو 3 دقائق من الإقلاع — يطابق نمط الحساس المفقود أو الخدمة المتوقفة" : "من Epoch Time: الفرق بين وقت الإقلاع ووقت الانهيار",
                "Calendar", "info"));
        }
        if (AliveBits.Match(ps) is { Success: true } ab)
            d.Evidence.Add(new("حالة الخدمات (is_alive)", $"current {ab.Groups[1].Value} / expected {ab.Groups[3].Value}",
                "قيم خام من مراقب النظام: اختلاف current عن expected يعني أن خدمة لم تسجّل حضورها", "is_alive", "info"));
        foreach (var (label, rx) in InfoLines)
            if (rx.Match(ps) is { Success: true } im)
                d.Evidence.Add(new(label, Cut(im.Groups[1].Value.Trim(), 120), "معلومة من السجل", Cut(im.Value, 40), "info"));
    }

    static string Duration(double seconds) =>
        seconds < 90 ? Count(seconds, "ثانية", "ثوانٍ") : seconds < 5400 ? Count(seconds / 60, "دقيقة", "دقائق") : seconds < 172800 ? Count(seconds / 3600, "ساعة", "ساعات") : Count(seconds / 86400, "يوم", "أيام");

    /// <summary>عدد مع تمييزه: «3 دقائق» (من 3 إلى 10 جمع)، «2.5 دقيقة»، «15 دقيقة»</summary>
    static string Count(double value, string one, string many)
    {
        var v = Math.Round(value, 1);
        var text = v.ToString(v == Math.Floor(v) ? "0" : "0.#", CultureInfo.InvariantCulture);
        return v == Math.Floor(v) && v is >= 3 and <= 10 ? $"{text} {many}" : $"{text} {one}";
    }

    // ============================================================== الفحص التفاعلي
    /// <summary>
    /// تطبيق أجوبة الفني (بالترتيب) على الأسباب: كل جواب يرفع أو يخفض درجات قطع محددة.
    /// الجواب الحاسم (مثل «ركّبت قطعة سليمة فاختفى البانك») يرفع الثقة إلى «عالية» إذا بقيت قطعته في المقدمة بفارق واضح.
    /// </summary>
    public static void ApplyAnswers(Diagnosis d, IReadOnlyList<(string Id, int Answer)> answers)
    {
        if (answers == null || answers.Count == 0) return;
        var decisive = new HashSet<string>();
        foreach (var (id, idx) in answers)
        {
            var q = PanicKnowledge.Current.Questions.FirstOrDefault(x => x.Id == id);
            if (q == null || idx < 0 || idx >= q.Answers.Length) continue;
            var a = q.Answers[idx];
            d.AnswersApplied++;
            d.Evidence.Add(new("جواب الفحص", a.Label + (a.Note != "" ? " — " + a.Note : ""), q.Text, null, "exam"));
            foreach (var (part, delta) in a.Effects)
            {
                var c = d.Candidates.FirstOrDefault(x => x.Part == part);
                if (c == null)
                {
                    if (delta > 0 && a.Add) d.Candidates.Add(new Candidate { Part = part, Score = Math.Clamp(30 + delta, 1, 99), Why = "من الفحص العملي: " + a.Label });
                    continue;
                }
                c.Score = Math.Clamp(c.Score + delta, 1, 99);
                c.Why += $" — الفحص: {a.Label} ({(delta > 0 ? "+" : "")}{delta})";
                if (a.Decisive && delta >= 40) decisive.Add(part);
                if (delta <= -40) decisive.Remove(part);
            }
        }
        Rank(d);
        if (d.Candidates.Count == 0) return;
        bool lead = d.Candidates.Count == 1 || d.Candidates[0].Score - d.Candidates[1].Score >= 20;
        if (decisive.Contains(d.TopPart) && lead)
        {
            d.Confidence = "عالية";
            d.Summary = $"مؤكد بالفحص العملي ← {d.TopPart}.";
        }
        else if (lead && d.Confidence == "منخفضة" && d.AnswersApplied >= 2) d.Confidence = "متوسطة";
        else if (!lead && d.Confidence == "عالية") d.Confidence = "متوسطة";
        if (!decisive.Contains(d.TopPart)) d.Summary = $"{d.Summary.TrimEnd('.')} — بعد {d.AnswersApplied} من أجوبة الفحص: الأرجح الآن {d.TopPart}.";
    }

    /// <summary>
    /// السؤال التالي: من الأسئلة التي تستهدف إحدى القطع الأعلى (درجة 30 فأكثر، أول 4)، غير المجاب عنها وغير المستبعدة،
    /// الأعلى أولًا بدرجة قطعته المستهدفة ثم أولوية السؤال، والمجاني قبل ما يحتاج شراء قطعة. null إذا اكتمل الفحص (تشخيص مؤكد) أو لا يوجد سؤال مناسب.
    /// </summary>
    public static PanicKnowledge.Question NextQuestion(Diagnosis d, IReadOnlyList<(string Id, int Answer)> answers, IEnumerable<string> skip, out int remaining)
    {
        remaining = 0;
        if (d == null || d.Candidates.Count == 0) return null;
        if (d.Confidence == "عالية" && d.Summary.StartsWith("مؤكد بالفحص", StringComparison.Ordinal)) return null;
        var done = (answers ?? Array.Empty<(string, int)>()).Select(a => a.Id).Concat(skip ?? Array.Empty<string>()).ToHashSet();
        var top = d.Candidates.Where(c => c.Score >= 30).Take(4).ToList();
        var ranked = PanicKnowledge.Current.Questions
            .Where(q => !done.Contains(q.Id) && q.Targets.Any(t => top.Any(c => c.Part == t)))
            // الفحص المجاني (سؤال، فصل، تنظيف) قبل الفحص الذي يحتاج شراء قطعة
            .Select(q => (q, w: top.Where(c => q.Targets.Contains(c.Part)).Max(c => c.Score) + q.Priority * 0.2 + (q.Free ? 15 : 0)))
            .OrderByDescending(x => x.w).ThenBy(x => x.q.Id, StringComparer.Ordinal).ToList();
        remaining = ranked.Count;
        return ranked.Count == 0 ? null : ranked[0].q;
    }

    public static string EncodeAnswers(IEnumerable<(string Id, int Answer)> answers) =>
        string.Join(";", (answers ?? Array.Empty<(string, int)>()).Select(a => $"{a.Id}={a.Answer.ToString(CultureInfo.InvariantCulture)}"));

    public static List<(string Id, int Answer)> DecodeAnswers(string s)
    {
        var list = new List<(string, int)>();
        foreach (var part in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0].Trim() != "" && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && list.All(x => x.Item1 != kv[0].Trim()))
                list.Add((kv[0].Trim(), v));
        }
        return list;
    }

    // ============================================================== SMC
    static void ReadSmc(Diagnosis d, string ps)
    {
        var line = SmcPanic.Match(ps).Groups[1].Value.Trim();
        if (!d.Headline.Contains("SMC PANIC", StringComparison.OrdinalIgnoreCase))
            d.Evidence.Add(new("بانك SMC", line == "" ? "SMC PANIC" : line, "معالج SMC (الطاقة والحرارة والحساسات) انهار", "SMC PANIC"));
        if (SmcAssert.Match(ps) is { Success: true } a && !line.Contains(a.Groups[1].Value.Trim(), StringComparison.Ordinal))
            d.Evidence.Add(new("شرط فشل (ASSERT)", a.Groups[1].Value.Trim(), "المكان داخل برنامج SMC الذي توقف عنده", "ASSERT"));
        if (AssertLocation.Match(ps) is { Success: true } loc)
        {
            d.SmcAssert = Regex.Replace(loc.Groups[1].Value, @"\s+", "").Replace('\\', '/');
            d.Evidence.Add(new("موضع الفشل", d.SmcAssert, "الملف والسطر داخل برنامج SMC — نفس الموضع في عدة فحوصات يعني نفس نوع العطل", d.SmcAssert.Split('/').Last()));
        }
        if (FaultingTaskRx.Match(ps) is { Success: true } ft)
        {
            d.FaultingTask = ft.Groups[1].Value;
            d.Evidence.Add(new("المهمة المتوقفة", "Faulting task " + d.FaultingTask, "رقم داخلي لمهمة SMC التي توقفت — معناه غير موثّق، ويُستخدم لمطابقة السجلات المتشابهة", "Faulting task"));
        }
        if (SensorArrayRx.Match(ps) is { Success: true } sa)
        {
            int start = int.Parse(sa.Groups[1].Value, CultureInfo.InvariantCulture);
            var values = sa.Groups[3].Value.Split(',').Select(v => v.Trim().Replace('O', '0').Replace('o', '0')).ToList();
            for (int i = 0; i < values.Count; i++)
                if (ParseNumber(values[i]) is long val && val != 0) d.SensorArray.Add((start + i, val));
            if (d.SensorArray.Count > 0)
                d.Evidence.Add(new("مصفوفة حساسات SMC", string.Join("، ", d.SensorArray.Select(x => $"الخانة {x.Index} = 0x{x.Value:X} (البتات {string.Join("،", Bits(x.Value))})")),
                    "قيمة غير صفرية بجانب رسالة الفشل: SMC يبلّغ عن حساس لم يستطع قراءته. رقم البت لا يُعرف له مكان موثّق، فحدّد الحساس بالعزل — ولا تفترض أنه البطارية.", "S.sensor array"));
        }
        if (AppleDevices.BoardCode(ps) is var board && board != "")
            d.Evidence.Add(new("رمز البوردة", board, "من مسار برنامج SMC في السجل", "target"));
        if (Outbox.IsMatch(ps))
            d.Evidence.Add(new("OUTBOX not ready", "نعم", "SMC توقف عن الرد على المعالج الرئيسي فأُعيد تشغيل الجهاز", "OUTBOX"));
        // المفاتيح المذكورة في سطر الفشل نفسه
        if (FailureTail.Match(ps) is { Success: true } ft2)
            foreach (Match k in KeyToken.Matches(ft2.Groups[1].Value))
            {
                var key = k.Groups[1].Value;
                if (key.Count(char.IsUpper) >= 2 && !d.SmcFailedKeys.Contains(key)) d.SmcFailedKeys.Add(key);
            }
        if (d.SmcFailedKeys.Count > 0)
            d.Evidence.Add(new("مفاتيح في سطر الفشل", string.Join("، ", d.SmcFailedKeys),
                "SMC ذكر بالاسم ما فشل في قراءته: " + string.Join("، ", d.SmcFailedKeys.Select(k => $"{k} = {PanicKnowledge.SmcKeyMeaning(k)}")) + " — هذا أقوى دليل في السجل على مكان العطل",
                d.SmcFailedKeys[0]));

        // القنوات مفصولة: القناة المتوقفة هي مكان العطل، وما في القنوات السليمة قراءات دورية
        ReadChannels(d, ps);
        foreach (var key in DecodeSmcKeys(ps).Concat(d.SmcChannels.SelectMany(c => c.Keys)))
            if (!d.SmcKeys.Contains(key)) d.SmcKeys.Add(key);
        foreach (var c in d.SmcChannels)
        {
            string list = c.Keys.Count == 0 ? "لم تُقرأ رسائلها" : string.Join("، ", c.Keys);
            string meaning = c.NotReady
                ? "هذه القناة هي التي توقفت (OUTBOX not ready) — آخر ما طُلب فيها يشير إلى مكان العطل" +
                  (c.Keys.Count > 0 ? ": " + string.Join("، ", c.Keys.Select(k => $"{k} = {PanicKnowledge.SmcKeyMeaning(k)}")) : "")
                : "قناة كانت تعمل حتى الانهيار: فيها قراءات دورية (الحرارة والبطارية ...) وليست دليلًا على مكان العطل";
            d.Evidence.Add(new(c.NotReady ? $"القناة المتوقفة Mailbox ({c.Index})" : $"قناة سليمة Mailbox ({c.Index})", list, meaning,
                c.Keys.Count > 0 ? "0x" + Convert.ToHexString(Encoding.ASCII.GetBytes(c.Keys[0])) : "OUTBOX", c.NotReady ? "" : "info"));
        }
        if (d.SmcChannels.Count == 0 && d.SmcKeys.Count > 0)
        {
            var first = d.SmcKeys.FirstOrDefault(PanicKnowledge.IsBatteryKey) ?? d.SmcKeys[0];
            d.Evidence.Add(new("مفاتيح SMC", string.Join("، ", d.SmcKeys),
                "فُكّت من رسائل Mailbox بدون معرفة القناة (السجل ناقص): " + string.Join("، ", d.SmcKeys.Select(k => $"{k} = {PanicKnowledge.SmcKeyMeaning(k)}")) +
                " — دليل ضعيف: قد تكون قراءات دورية وليست مكان العطل",
                "0x" + Convert.ToHexString(Encoding.ASCII.GetBytes(first))));
        }
    }

    /// <summary>
    /// قنوات SMC: كل «Mailbox (N)» حتى القناة التالية أو سجل RTBuddy. القناة متوقفة إذا كان فيها «OUTBOX not ready»
    /// أو ذكر رأس البانك رقمها («Misc(2) OUTBOX1 not ready» ← القناة 1). مفاتيحها تُفك بقاعدة أوسع (gP13 مفتاح صالح داخل القناة).
    /// </summary>
    static void ReadChannels(Diagnosis d, string ps)
    {
        int headerFail = OutboxIndex.Match(ps) is { Success: true } om && int.TryParse(om.Groups[1].Value, out var hf) ? hf : -1;
        var heads = MailboxHead.Matches(ps).Cast<Match>().ToList();
        for (int i = 0; i < heads.Count; i++)
        {
            int start = heads[i].Index, end = i + 1 < heads.Count ? heads[i + 1].Index : ps.Length;
            var stop = MailboxEnd.Match(ps, start + 1);
            if (stop.Success && stop.Index < end) end = stop.Index;
            var block = ps[start..end];
            int idx = int.Parse(heads[i].Groups[1].Value, CultureInfo.InvariantCulture);
            bool notReady = ChannelNotReady.IsMatch(block) || idx == headerFail;
            var keys = DecodeSmcKeys(block, relaxed: true);
            var old = d.SmcChannels.FirstOrDefault(c => c.Index == idx);
            if (old != null)
            {
                foreach (var k in keys) if (!old.Keys.Contains(k)) old.Keys.Add(k);
                if (notReady && !old.NotReady) d.SmcChannels[d.SmcChannels.IndexOf(old)] = old with { NotReady = true };
                continue;
            }
            d.SmcChannels.Add(new SmcChannel(idx, notReady, keys));
        }
        // رأس البانك يذكر قناة متوقفة لم يُنسخ قسمها (سجل مقطوع)
        if (headerFail >= 0 && d.SmcChannels.All(c => c.Index != headerFail) && d.SmcChannels.Count > 0)
            d.SmcChannels.Add(new SmcChannel(headerFail, true, new List<string>()));
    }

    static long? ParseNumber(string v)
    {
        v = v.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(v.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : null;
        return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    static IEnumerable<int> Bits(long v)
    {
        for (int b = 0; b < 64; b++) if ((v & (1L << b)) != 0) yield return b;
    }

    /// <summary>
    /// مفاتيح SMC من رسائل Mailbox: قيمة ‎0x‎ من 16 خانة، أول 4 بايت منها حروف مقروءة (مثل ‎0x5447304200006013‎ ← TG0B).
    /// تُقبل فقط إذا كانت 4 حروف/أرقام تبدأ بحرف وفيها حرفان كبيران على الأقل — فلا تُقرأ الأرقام العادية كمفاتيح.
    /// </summary>
    public static List<string> DecodeSmcKeys(string text, bool relaxed = false)
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
            // داخل قناة معروفة يكفي حرف كبير واحد (gP13، ftD0)؛ خارجها حرفان لتجنّب قراءة الأرقام العادية كمفاتيح
            if (!ok || !char.IsLetter(chars[0]) || chars.Count(char.IsUpper) < (relaxed ? 1 : 2)) continue;
            var key = new string(chars);
            if (!keys.Contains(key)) keys.Add(key);
        }
        return keys;
    }

    static void SmcDiagnosis(Diagnosis d, string ps)
    {
        d.Kind = "SMC";
        d.Title = "انهيار معالج الطاقة والحساسات (SMC)";
        d.Signature = "smc:" + (d.SmcAssert != "" ? d.SmcAssert : "?") + (d.FaultingTask != "" ? "|task" + d.FaultingTask : "");
        d.LearnPattern = d.SmcAssert;

        // الدليل بالترتيب: (1) المفاتيح المذكورة في سطر الفشل، (2) مفاتيح القناة المتوقفة، (3) الباقي قراءات دورية أو مجهولة القناة
        var failing = d.SmcChannels.FirstOrDefault(c => c.NotReady);
        List<string> focus;
        string source;
        if (d.SmcFailedKeys.Count > 0) { focus = d.SmcFailedKeys; source = "سطر الفشل"; }
        else if (failing is { Keys.Count: > 0 }) { focus = failing.Keys; source = $"القناة المتوقفة Mailbox ({failing.Index})"; }
        else { focus = new List<string>(); source = ""; }
        bool channelsKnown = d.SmcChannels.Count > 0;
        string Keys(IEnumerable<string> ks) => string.Join("، ", ks.Select(k => $"{k} = {PanicKnowledge.SmcKeyMeaning(k)}"));

        var strong = focus.Where(PanicKnowledge.IsBatteryKey).ToList();
        var weak = focus.Where(PanicKnowledge.IsWeakBatteryKey).ToList();
        bool batteryFocus = false;

        if (strong.Count > 0)
        {
            batteryFocus = true;
            d.Signature += "|battery";
            d.Explanation = $"SMC توقف وهو يقرأ بيانات البطارية ({Keys(strong)}) في {source}. انقطاع الاتصال بشريحة قياس البطارية أو بحساس حرارتها هو الأشيع لهذا الشكل.";
            Add(d, Parts.Battery, 72, $"{source}: SMC توقف عند قراءة البطارية ({string.Join("، ", strong)})");
            Add(d, Parts.BatteryConn, 66, "موصل غير محكم أو متأكسد يقطع الاتصال — افحصه قبل شراء بطارية");
            Add(d, Parts.SmcLine, 42, "إذا بقي البانك مع بطارية سليمة وموصل نظيف");
            Add(d, Parts.LastPart, 40, "إذا فُكّت قطعة قبل ظهور المشكلة");
            d.Confidence = "متوسطة";
            d.Summary = $"SMC توقف عند قراءة البطارية ({source}) ← افحص صحة البطارية ونظّف موصلها أولًا (مجانًا)، ثم جرّب بطارية سليمة من المخزون.";
        }
        else if (weak.Count > 0 && weak.Count == focus.Count)
        {
            d.Signature += "|b?";
            d.Explanation = $"SMC توقف، وما كان يقرأه في {source} مفاتيح تبدأ بحرف B ({string.Join("، ", weak)}) — حرف مفاتيح البطارية في تسمية Apple، لكنها غير موثّقة بالاسم، فالدليل ضعيف.";
            Add(d, Parts.Battery, 52, $"مفاتيح تبدأ بـ B في {source} (غير موثّقة بالاسم)");
            Add(d, Parts.BatteryConn, 50, "موصل البطارية — افحصه ونظّفه أولًا");
            Add(d, Parts.LastPart, 48, "القطعة التي فُكّت أو استُبدلت قبل المشكلة");
            Add(d, Parts.Board, 45, "SMC أو خطوطه على البوردة");
            d.Confidence = "منخفضة";
            d.Summary = "SMC توقف والدليل على البطارية ضعيف ← اختبار العزل ونظافة موصل البطارية قبل شراء أي قطعة.";
        }
        else if (focus.Count > 0)
        {
            d.Signature += "|sensor";
            bool temps = focus.Any(k => k[0] == 'T');
            d.Explanation = $"SMC توقف وهو ينتظر ردًا من {Keys(focus)} ({source}) — وهذه ليست بيانات البطارية." +
                (temps ? " المفاتيح التي تبدأ بـ T حساسات حرارة موزعة على الجهاز (بعضها على فلاتات وقطع خارج البوردة)." : "") +
                " مكان هذا الحساس غير موثّق في قاعدة المعرفة، فالطريقة الصحيحة هي اختبار العزل وليس تبديل القطع بالتخمين." +
                (channelsKnown && d.SmcChannels.Any(c => !c.NotReady && c.Keys.Any(PanicKnowledge.IsBatteryKey))
                    ? " قراءات البطارية ظهرت في قناة سليمة فقط، وهي قراءات دورية لا تدل على العطل." : "");
            Add(d, Parts.LastPart, 62, "أول ما يُفحص: القطعة التي فُكّت أو استُبدلت قبل ظهور المشكلة (الشاشة وحساساتها خاصة)");
            Add(d, Parts.FrontFlex, 58, "حساسات القرب والإضاءة وسماعة المكالمات على خطوط SMC — سبب مؤكد لبانك SMC BSC في حالة iPhone 13 Pro Max");
            Add(d, Parts.ChargingFlex, 46, "فلاتة الشحن عليها حساسات وخطوط طاقة");
            Add(d, Parts.Board, 44, "خطوط SMC على البوردة — إذا بقي الانهيار وكل الفلاتات الطرفية مفصولة");
            Add(d, Parts.Battery, 25, "لا دليل في السجل على البطارية — لا تبدّلها قبل اختبار العزل");
            d.Confidence = "منخفضة";
            d.Summary = $"SMC لم يستطع قراءة {string.Join("، ", focus)} (ليست البطارية) ← اختبار العزل: افصل الفلاتات الطرفية واحدة واحدة قبل شراء أي قطعة.";
        }
        else
        {
            // بلا قنوات معروفة (سجل ناقص): مفاتيح البطارية قد تكون قراءات دورية، فهي دليل ضعيف فقط
            bool anyStrong = !channelsKnown && d.SmcKeys.Any(PanicKnowledge.IsBatteryKey);
            bool anyWeak = !channelsKnown && !anyStrong && d.SmcKeys.Any(PanicKnowledge.IsWeakBatteryKey);
            d.Signature += channelsKnown ? "|routine" : anyStrong ? "|battery?" : anyWeak ? "|b?" : "|nokeys";
            d.Explanation = "معالج SMC يدير الطاقة والحرارة والحساسات. السجل لا يحدد ما الذي توقف عنده" +
                (channelsKnown ? " (القناة المتوقفة لم تُنسخ أو بلا رسائل، وما في القنوات السليمة قراءات دورية)" : "") +
                (anyStrong || anyWeak ? " (في السجل مفاتيح بطارية لكن بلا قناة معروفة — قد تكون قراءات دورية)" : "") +
                "، فالقطعة يحددها اختبار العزل.";
            if (anyStrong)
            {
                Add(d, Parts.Battery, 52, "مفاتيح بطارية موثّقة في السجل، لكن القناة غير معروفة (دليل ضعيف)");
                Add(d, Parts.BatteryConn, 48, "موصل البطارية — افحصه ونظّفه");
            }
            else if (anyWeak)
            {
                Add(d, Parts.Battery, 44, "مفاتيح تبدأ بـ B (غير موثّقة) بلا قناة معروفة — دليل ضعيف");
                Add(d, Parts.BatteryConn, 42, "موصل البطارية — افحصه ونظّفه");
            }
            else Add(d, Parts.Battery, 35, "سبب شائع لانهيار SMC، لكن لا دليل عليه في هذا السجل");
            Add(d, Parts.LastPart, 50, "القطعة التي فُكّت أو استُبدلت قبل المشكلة");
            Add(d, Parts.FrontFlex, 45, "حساسات الشاشة الأمامية على خطوط SMC");
            Add(d, Parts.ChargingFlex, 45, "خطوط الطاقة والحساسات");
            Add(d, Parts.Board, 45, "SMC أو خطوطه على البوردة");
            d.Confidence = "منخفضة";
            d.Summary = anyStrong || anyWeak
                ? "SMC انهار، والدليل على البطارية ضعيف (مفاتيح بلا قناة معروفة) ← افحص موصلها ثم اختبار العزل قبل شراء أي قطعة."
                : "SMC انهار والسجل لا يحدد القطعة ← اختبار العزل قبل شراء أي قطعة.";
        }

        // الانهيار بعد مدة قصيرة من الإقلاع: مهلة ثابتة، غالبًا انتظار حساس لا يرد
        if (d.UptimeSeconds is double up && up is > 0 and <= 1800)
            d.Evidence.Add(new("توقيت الانهيار", Duration(up) + " من الإقلاع",
                "إذا تكرر الانهيار بنفس المدة تقريبًا في كل مرة فهو مهلة ثابتة: SMC ينتظر ردًا لا يصل من حساس أو قطعة، وليس عطلًا عشوائيًا. قس المدة في كل تجربة عزل."));

        SmcSteps(d, batteryFocus);

        // نص فشل معروف (SMC BSC failure ...) من قاعدة المعرفة: حالة مؤكدة لموديل محدد تغلب الترتيب
        if (PanicKnowledge.FindSmcFailure(ps) is not { } f) return;
        var (choices, specific) = f.LocateFor(d.Product);
        d.Evidence.Add(new("نوع فشل SMC", f.Match, f.What + " — " + f.Note + (specific ? " (معلومة خاصة بهذا الموديل)" : ""), f.Match));
        // دليل مباشر في هذا السجل (القناة المتوقفة تقرأ البطارية) يغلب حالة سابقة على نفس الموديل
        foreach (var c in choices)
            Add(d, c.Part, batteryFocus ? c.Score - 25 : c.Score, batteryFocus ? c.Why + " — لكن القناة المتوقفة في هذا السجل تقرأ البطارية" : c.Why);
        if (!specific || batteryFocus) return;
        d.ModelSpecific = true;
        d.Confidence = "متوسطة"; // حالة مؤكدة واحدة على نفس الموديل: ترجيح، وليس تأكيدًا قبل العزل
        d.Signature += "|" + f.Match.Replace(' ', '_');
        var top = choices.OrderByDescending(c => c.Score).First();
        d.Explanation += " وعلى هذا الموديل: " + f.Note;
        d.Summary = $"«{f.Match}» على هذا الموديل ← السبب في حالة مؤكدة سابقة: {top.Part}. أكّده بفصلها قبل التبديل.";
        d.Steps.InsertRange(1, f.Steps);
    }

    /// <summary>خطوات فحص SMC: المجاني أولًا (سؤال، عزل، تنظيف)، وتبديل القطع بعد تحديدها فقط</summary>
    static void SmcSteps(Diagnosis d, bool batteryFocus)
    {
        string wait = d.UptimeSeconds is double up && up is > 0 and <= 1800
            ? $"أطول من مدة الانهيار ({Duration(Math.Max(up * 2, 300))} على الأقل)" : "10 دقائق على الأقل";
        d.Steps.Add("لا تشترِ أي قطعة قبل اختبار العزل: الاختبار مجاني ويحدد القطعة.");
        d.Steps.Add("اسأل صاحب الجهاز: هل فُتح أو استُبدلت فيه قطعة (شاشة، بطارية، فلاتة) قبل ظهور المشكلة؟ ابدأ بتلك القطعة.");
        if (batteryFocus)
        {
            d.Steps.Add("افحص صحة البطارية في الإعدادات (رسالة «قطعة غير معروفة» أو «خدمة» تؤكد الاتجاه).");
            d.Steps.Add("افصل البطارية ونظّف الموصل وأعد تركيبه بإحكام.");
        }
        d.Steps.Add($"اختبار العزل: افصل الفلاتات الطرفية واحدة واحدة (حساسات الشاشة وسماعة المكالمات، فلاتة الشحن، ملف الشحن اللاسلكي، الكاميرات) وشغّل الجهاز {wait} في كل مرة. القطعة التي يتوقف الانهيار بفصلها هي السبب.");
        if (batteryFocus) d.Steps.Add("جرّب بطارية سليمة من المخزون أو مصدر طاقة DC — قبل شراء بطارية جديدة.");
        else d.Steps.Add("افحص موصل البطارية بصريًا ونظّفه (مجاني)، لكن لا تبدّل البطارية بلا دليل.");
        d.Steps.Add("بعد تحديد القطعة بالعزل فقط: بدّلها بقطعة سليمة.");
        d.Steps.Add("إذا بقي الانهيار وكل الفلاتات الطرفية مفصولة: افحص خطوط SMC على البوردة (القياس بوضع الديود ومقارنته بلوحة سليمة).");
    }

    // ============================================================== الحساس المفقود
    static void SensorDiagnosis(Diagnosis d, PanicLog log, string ps)
    {
        d.Kind = "حساس مفقود";
        d.Signature = "sensor:" + string.Join(",", d.MissingSensors.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        d.LearnPattern = d.MissingSensors.Count == 1 ? d.MissingSensors[0] : "";
        var family = AppleDevices.FamilyOf(log.Product, ps);
        var product = d.Product;
        var names = new List<string>();
        var firstParts = new List<string>();
        PanicKnowledge.Choice topChoice = null;
        PanicKnowledge.Sensor top = null;
        foreach (var code in d.MissingSensors)
        {
            var s = PanicKnowledge.FindSensor(code);
            if (s == null)
            {
                d.Evidence.Add(new("حساس مفقود", code, "رمز غير موجود في قاعدة المعرفة — أضف خبرتك عنه من «خبرة المحل»", code));
                Add(d, Parts.LastPart, 55, $"الحساس {code} غير معروف في القاعدة: ابدأ بالفلاتة التي فُكّت أو استُبدلت آخر مرة");
                Add(d, Parts.Board, 35, "خط الحساس على البوردة");
                names.Add(code);
                continue;
            }
            var (choices, specific) = s.LocateFor(product, family);
            if (specific) d.ModelSpecific = true;
            d.Evidence.Add(new("حساس مفقود", s.Code, s.What + " — " + s.Note + (specific ? " (معلومة خاصة بهذا الموديل)" : ""), "Missing sensor"));
            foreach (var c in choices) Add(d, c.Part, c.Score, $"{s.Code}: {c.Why}");
            if (choices.Length > 0) firstParts.Add(choices[0].Part);
            if (top == null && choices.Length > 0) { top = s; topChoice = choices[0]; }
            names.Add($"{s.Code} — {s.What}");
        }

        // أكثر من حساس على قطع مختلفة: غالبًا خط مشترك أو تأكسد
        var distinct = firstParts.Distinct().ToList();
        if (distinct.Count > 1)
        {
            Add(d, Parts.Board, 55, "أكثر من حساس مفقود على قطع مختلفة: خط مشترك أو تأكسد على البوردة");
            d.Evidence.Add(new("عدة حساسات", string.Join("، ", d.MissingSensors), "الحساسات على أكثر من قطعة — افحص الخط المشترك والتأكسد", "Missing sensor"));
        }

        d.Title = d.MissingSensors.Count == 1 ? $"حساس مفقود: {names[0]}" : "حساسات مفقودة: " + string.Join("، ", d.MissingSensors);
        d.Explanation = "النظام لا يجد " + (d.MissingSensors.Count == 1 ? "الحساس" : "الحساسات") + " " + string.Join("، ", names) +
            "، فتتوقف خدمة مراقبة الحرارة (thermalmonitord) عن التسجيل، فيُعيد مراقب النظام تشغيل الجهاز" +
            (d.WatchdogSeconds > 0 ? $" بعد {d.WatchdogSeconds} ثانية تقريبًا من كل إقلاع." : ".");
        d.Confidence = topChoice == null ? "منخفضة" : topChoice.Score >= 85 && distinct.Count <= 1 ? "عالية" : "متوسطة";
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
        d.Signature = "service:" + s.Name;
        d.LearnPattern = "checkins from " + s.Name;
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
        d.Signature = "service:" + d.WatchdogService;
        d.LearnPattern = "checkins from " + d.WatchdogService;
        d.Title = $"توقف خدمة {d.WatchdogService}";
        d.Explanation = $"الخدمة {d.WatchdogService} لم تسجّل حضورها فأُعيد تشغيل الجهاز. هذه الخدمة ليست في قاعدة المعرفة.";
        Add(d, Parts.Ios, 50, "أغلب خدمات النظام تتوقف بسبب خلل برمجي");
        Add(d, Parts.Board, 35, "قطعة تخدمها هذه الخدمة لا تستجيب");
        d.Steps.AddRange(new[] { "حدّث iOS أو أعد تثبيته عبر الكمبيوتر.", "إذا تكرر بعد الاستعادة: ابحث عن القطعة المرتبطة بالخدمة وأضف خبرتك في «خبرة المحل»." });
        d.Confidence = "منخفضة";
        d.Summary = $"الخدمة {d.WatchdogService} لا تستجيب ← ابدأ بتحديث أو استعادة iOS.";
    }

    static void SignatureDiagnosis(Diagnosis d, PanicKnowledge.Signature s, string ps)
    {
        d.Kind = s.Kind;
        d.Signature = "kind:" + s.Kind;
        d.LearnPattern = s.Match.Match(ps).Value.Trim();
        d.Title = s.Title;
        d.Explanation = s.Explain;
        foreach (var c in s.Choices) Add(d, c.Part, c.Score, c.Why);
        d.Steps.AddRange(s.Steps);
        d.Confidence = s.Confidence;
        d.Summary = $"{s.Title} ← ابدأ بـ {s.Choices[0].Part}.";
        d.Evidence.Add(new("نوع الانهيار", s.Title, s.Explain, s.Match.Match(ps).Value));
    }

    static void GenericWatchdogDiagnosis(Diagnosis d)
    {
        d.Kind = "مراقب النظام";
        d.Signature = "watchdog";
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
        d.Signature = ps.Trim() == "" ? "" : "unknown";
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
        if (f.BatteryReplaced || f.ScreenReplaced || f.ChargingFlexReplaced) Bump(d, Parts.LastPart, 10, "قطعة استُبدلت قبل المشكلة");
        if (f.BatteryReplaced) Bump(d, Parts.Battery, 15, "البطارية مستبدلة (قد تكون غير أصلية أو ضعيفة التركيب)");
        if (f.BatteryReplaced) Bump(d, Parts.BatteryConn, 8, "فُكّ الموصل عند تبديل البطارية");
        if (f.ChargingFlexReplaced) Bump(d, Parts.ChargingFlex, 15, "فلاتة الشحن مستبدلة (قد تكون تجارية بلا حساس يعمل)");
        if (f.ScreenReplaced) { Bump(d, Parts.Screen, 15, "الشاشة مستبدلة"); Bump(d, Parts.FrontFlex, 10, "فُكّت فلاتات الجهة الأمامية"); }
        if (f.Dropped) foreach (var p in new[] { Parts.Board, Parts.BatteryConn, Parts.Screen }) Bump(d, p, 8, "بعد سقوط: موصل مفصول أو شرخ في البوردة");
        d.Evidence.Add(new("معلومات الحالة", f.ToString(), "من الفني — غيّرت ترتيب الأسباب"));
    }

    /// <summary>
    /// خبرة المحل: كل قاعدة مطابقة تضيف سببها بدرجتها (مع الأولوية). عند تعارض قاعدتين على قطعتين مختلفتين
    /// تغلب الأعلى أولوية ثم الأعلى درجة؛ ويُذكر التعارض في الأدلة.
    /// </summary>
    static void ApplyCustom(Diagnosis d, IEnumerable<CustomRule> custom)
    {
        if (custom == null) return;
        var ps = d.Log?.PanicString ?? "";
        var hits = custom.Where(r => r.Matches(ps, d.MissingSensors, d.Device, d.Product))
                         .OrderByDescending(r => r.Priority).ThenByDescending(r => r.Score).ToList();
        foreach (var r in hits)
        {
            var why = "خبرة المحل" + (r.Name != "" ? $" ({r.Name})" : "") + (r.Note != "" ? ": " + r.Note : "");
            Add(d, r.Part.Trim(), r.Score, why);
            d.Evidence.Add(new("خبرة المحل", r.Pattern.Trim(), $"{r.Part} — {r.Level}" + (r.Priority != 0 ? $" — أولوية {r.Priority}" : "") + (r.Note != "" ? " — " + r.Note : ""),
                r.IsRegex ? null : r.Pattern.Trim()));
        }
        var parts = hits.Select(r => r.Part.Trim()).Distinct().ToList();
        if (parts.Count > 1)
            d.Evidence.Add(new("تعارض قواعد", string.Join(" / ", parts), $"أكثر من قاعدة في خبرة المحل تنطبق — غلبت «{hits[0].Name}» (الأولوية ثم الدرجة)"));
        if (hits.Count > 0 && hits[0].Level == "مؤكد" && d.Confidence != "عالية") d.Confidence = "عالية";
    }

    /// <summary>اسم الدرجة: «الأرجح» للأول فقط إذا تقدّم بوضوح (10 درجات على الأقل) وكانت درجته 70 فأكثر</summary>
    public static string LabelOf(int score, bool leadsClearly) =>
        leadsClearly && score >= 70 ? "الأرجح" : score >= 60 ? "مرجّح" : score >= 35 ? "محتمل" : "احتمال بعيد";

    /// <summary>ترتيب الأسباب وتسميتها</summary>
    static void Rank(Diagnosis d)
    {
        d.Candidates = d.Candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Part, StringComparer.Ordinal).ToList();
        for (int i = 0; i < d.Candidates.Count; i++)
            d.Candidates[i].Label = LabelOf(d.Candidates[i].Score, i == 0 && (d.Candidates.Count == 1 || d.Candidates[0].Score - d.Candidates[1].Score >= 10));
        if (d.Candidates.Count > 0 && d.Candidates[0].Label != "الأرجح" && d.Confidence == "عالية") d.Confidence = "متوسطة";
    }

    // ============================================================== هوية الجهاز
    /// <summary>
    /// نفس الجهاز؟ مفتاح التقارير (crashReporterKey) متطابق، أو يختلف بحرفين على الأكثر (خطأ نسخ من صورة) مع نفس الموديل.
    /// إن غاب المفتاح في أحدهما: يُقارن الموديل فقط.
    /// </summary>
    public static bool SameDevice(PanicLog a, PanicLog b)
    {
        if (a == null || b == null) return false;
        string ka = a.CrashReporterKey.ToLowerInvariant(), kb = b.CrashReporterKey.ToLowerInvariant();
        if (ka != "" && kb != "")
        {
            if (ka == kb) return true;
            bool sameModel = a.Product == "" || b.Product == "" || a.Product.Equals(b.Product, StringComparison.OrdinalIgnoreCase);
            return sameModel && ka.Length == kb.Length && ka.Length >= 20 && ka.Zip(kb).Count(p => p.First != p.Second) <= 2;
        }
        return a.DeviceKey != "" && a.DeviceKey.Equals(b.DeviceKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>تجميع السجلات حسب الجهاز (بالمطابقة التقريبية): يعيد رقم مجموعة لكل سجل</summary>
    public static int[] GroupDevices(IList<PanicLog> logs)
    {
        var group = new int[logs.Count];
        for (int i = 0; i < logs.Count; i++) group[i] = -1;
        int next = 0;
        for (int i = 0; i < logs.Count; i++)
        {
            if (group[i] >= 0) continue;
            group[i] = next;
            for (int j = i + 1; j < logs.Count; j++)
                if (group[j] < 0 && SameDevice(logs[i], logs[j])) group[j] = next;
            next++;
        }
        return group;
    }

    // ============================================================== عدة سجلات
    /// <summary>
    /// تحليل مجمّع لسجلات جهاز واحد، مرتبة زمنيًا:
    /// تكرار نفس النمط يرفع الثقة، واختلاف الأنواع يخفضها ويرجّح سببًا عامًا. السجلات الأحدث وزنها أكبر
    /// (تعبّر عن حالة الجهاز الآن، خصوصًا بعد تبديل قطعة). المدة بين البانكات تُحسب من أوقاتها.
    /// </summary>
    public static Diagnosis Combine(IList<Diagnosis> input)
    {
        if (input == null || input.Count == 0) return null;
        if (input.Count == 1) return input[0];
        // ترتيب زمني ثابت: السجلات بلا وقت في النهاية بترتيبها الأصلي
        var items = input.Select((x, i) => (x, i)).OrderBy(p => p.x.Log?.Time == null ? 1 : 0).ThenBy(p => p.x.Log?.Time).ThenBy(p => p.i).Select(p => p.x).ToList();
        var first = items[0];
        var last = items[^1];
        var d = new Diagnosis
        {
            Log = last.Log, Device = last.Device, Product = last.Product, Soc = last.Soc, Ios = last.Ios, Build = last.Build,
            Time = string.Join(" ← ", new[] { first.Time, last.Time }.Where(t => t != "").Distinct()),
            BugInfo = last.BugInfo, LogCount = items.Count,
        };

        var groups = GroupDevices(items.Select(x => x.Log).ToList());
        if (groups.Distinct().Count() > 1)
            d.Warnings.Add($"السجلات من {groups.Distinct().Count()} أجهزة مختلفة (مفتاح التقارير أو الموديل مختلف) — حلّل سجلات كل جهاز وحدها.");

        // الوزن: 1 للأقدم حتى 2 للأحدث
        var weights = items.Select((x, i) => 1.0 + (double)i / (items.Count - 1)).ToList();
        double total = weights.Sum();
        foreach (var g in items.SelectMany((x, i) => x.Candidates.Select(c => (c, w: weights[i]))).GroupBy(p => p.c.Part))
        {
            int n = g.Count();
            int avg = (int)Math.Round(g.Sum(p => p.c.Score * p.w) / total);
            var why = g.OrderByDescending(p => p.c.Score).First().c.Why;
            d.Candidates.Add(new Candidate { Part = g.Key, Score = Math.Clamp(avg, 1, 99), Why = n == items.Count ? why : $"{why} (ظهر في {n} من {items.Count} سجلات)" });
        }

        d.MissingSensors = items.SelectMany(x => x.MissingSensors).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        d.SmcKeys = items.SelectMany(x => x.SmcKeys).Distinct().ToList();
        d.WatchdogService = string.Join("، ", items.Select(x => x.WatchdogService).Where(s => s != "").Distinct());
        d.WatchdogSeconds = items.Select(x => x.WatchdogSeconds).FirstOrDefault(s => s > 0);
        d.Timeline = items.Select(x => new TimelineItem(x.Log?.Time, x.Time, x.Kind, x.Title, x.TopPart, x.Signature, x.Log?.Source ?? "")).ToList();
        foreach (var x in items)
            d.Evidence.Add(new("سجل " + (x.Time != "" ? x.Time : x.Log?.Source ?? ""), x.Title, $"{x.Kind} — الأرجح: {(x.TopPart == "" ? "—" : x.TopPart)}"));

        var rank = new Dictionary<string, int> { ["منخفضة"] = 0, ["متوسطة"] = 1, ["عالية"] = 2 };
        int conf = items.Min(x => rank.TryGetValue(x.Confidence, out var r) ? r : 0);

        // ---------- المدة بين البانكات ----------
        var times = items.Select(x => x.Log?.Time).Where(t => t != null).Select(t => t.Value).ToList();
        bool regular3 = false;
        if (times.Count >= 3)
        {
            var gaps = times.Zip(times.Skip(1), (a, b) => (b - a).TotalMinutes).Where(m => m > 0).ToList();
            if (gaps.Count >= 2)
            {
                var sorted = gaps.OrderBy(x => x).ToList();
                double median = sorted[sorted.Count / 2];
                regular3 = gaps.All(g => g is >= 2 and <= 6);
                bool irregular = sorted[^1] / Math.Max(0.1, sorted[0]) > 5;
                d.Evidence.Add(new("المدة بين البانكات", $"الوسيط {Minutes(median)} (من {Minutes(sorted[0])} إلى {Minutes(sorted[^1])})",
                    regular3 ? "منتظمة كل 3 دقائق تقريبًا — نمط الحساس المفقود أو الخدمة المتوقفة"
                    : irregular ? "غير منتظمة — يرجّح طاقة غير مستقرة أو بوردة أو نظام، أكثر من قطعة واحدة"
                    : "متقاربة"));
            }
        }

        // ---------- تغيّر النمط مع الوقت ----------
        var sigs = items.Select(x => x.Signature).ToList();
        int change = Enumerable.Range(1, items.Count - 1).FirstOrDefault(i => sigs[i] != sigs[i - 1]);
        // تغيّر حقيقي: نفس الجهاز، ونمطان ثابتان كل منهما في سجلين على الأقل (سجل واحد مختلف لا يكفي للحكم)
        bool changedOnce = change >= 2 && items.Count - change >= 2 && groups.Distinct().Count() == 1
                           && sigs.Skip(change).Distinct().Count() == 1 && sigs.Take(change).Distinct().Count() == 1;

        var kinds = items.Select(x => x.Kind).Distinct().ToList();
        var sensorSets = items.Select(x => string.Join(",", x.MissingSensors.Select(s => s.ToLowerInvariant()).OrderBy(s => s))).Distinct().ToList();
        if (changedOnce)
        {
            // نمط قديم ثم نمط جديد ثابت: غالبًا بعد تبديل قطعة أو تغيّر حالة الجهاز — الحالي هو المهم
            d.Kind = last.Kind;
            d.Signature = last.Signature;
            d.LearnPattern = last.LearnPattern;
            d.Title = first.Kind == last.Kind ? $"تغيّر النمط: {first.Title} ← {last.Title}" : $"تغيّر نوع البانك: {first.Kind} ← {last.Kind}";
            d.Explanation = $"أول {change} سجلات: {first.Title}. ومن {items[change].Time} صار: {last.Title}.\n" +
                "إذا بُدّلت قطعة بين الوقتين فالنمط القديم حُلّ وظهر عطل آخر (أو كانت القطعة الجديدة هي السبب). التشخيص الحالي مبني على السجلات الأحدث فقط.";
            d.Steps.AddRange(last.Steps);
            d.Evidence.Add(new("تغيّر النمط", $"بعد {items[change].Time}", $"قبل: {first.Title} — بعد: {last.Title}"));
            // الترتيب من السجلات الأحدث وحدها: النمط القديم لم يعد يصف الجهاز
            d.Candidates = PanicAnalyzer.Combine(items.Skip(change).ToList()).Candidates.Select(c => c.Clone()).ToList();
            conf = rank.TryGetValue(last.Confidence, out var lc) ? lc : 0;
        }
        else if (kinds.Count == 1)
        {
            d.Kind = last.Kind;
            d.Signature = sigs.Distinct().Count() == 1 ? last.Signature : "";
            d.LearnPattern = sigs.Distinct().Count() == 1 ? last.LearnPattern : "";
            bool sameSensors = sensorSets.Count == 1 && sensorSets[0] != "";
            d.Title = last.Title + $" — تكرر في {items.Count} سجلات";
            d.Explanation = last.Explanation + (sameSensors ? $"\nنفس الحساس ({string.Join("، ", last.MissingSensors)}) ظهر في كل السجلات: هذا يؤكد مسار القطعة." : "");
            bool sameSmc = last.Kind == "SMC" && sigs.Distinct().Count() == 1 && items.All(x => x.SmcKeys.Any(PanicKnowledge.IsBatteryKey));
            if ((sameSensors || sameSmc) && conf < 2) conf++;
            if (regular3 && last.Kind is "حساس مفقود" or "مراقب النظام" && conf < 2) conf++;
            d.Steps.AddRange(last.Steps);
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

    static string Minutes(double m) => Duration(m * 60);

    // ============================================================== التقارير النصية
    /// <summary>تقرير الفني: كل التفاصيل والأدلة (للنسخ أو الحفظ)</summary>
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
        Line("بصمة النمط", d.Signature);
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
        if (d.Timeline.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("الخط الزمني:");
            foreach (var t in d.Timeline) sb.AppendLine($"  • {(t.TimeText == "" ? t.Source : t.TimeText)}: {t.Kind} — {t.TopPart}");
        }
        if (d.Evidence.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("الأدلة من السجل:");
            foreach (var e in d.Evidence.Where(e => !e.IsInfo)) sb.AppendLine($"  • {e.What}: {e.Value}" + (e.What == "سطر الانهيار" || e.Meaning == "" ? "" : $" — {e.Meaning}"));
            var info = d.Evidence.Where(e => e.IsInfo).ToList();
            if (info.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("معلومات أخرى من السجل:");
                foreach (var e in info) sb.AppendLine($"  • {e.What}: {e.Value}");
            }
        }
        if (d.Warnings.Count > 0)
        {
            sb.AppendLine();
            foreach (var w in d.Warnings) sb.AppendLine("تنبيه: " + w);
        }
        return sb.ToString();
    }

    /// <summary>عبارة بسيطة للزبون حسب نوع العطل (بلا رموز تقنية)</summary>
    public static string CustomerProblem(Diagnosis d) => d.Kind switch
    {
        "حساس مفقود" => "الجهاز يعيد التشغيل تلقائيًا كل بضع دقائق لأن أحد الحساسات الداخلية لا يعمل.",
        "SMC" => "الشريحة المسؤولة عن الطاقة والحرارة في الجهاز توقفت عن الاستجابة، فيعيد الجهاز التشغيل.",
        "مراقب النظام" => "إحدى خدمات النظام الأساسية توقفت عن العمل، فيعيد الجهاز التشغيل لحماية نفسه.",
        "النواة (برمجي)" => "خطأ في نظام التشغيل نفسه، وغالبًا يُحل بتحديث النظام أو إعادة تثبيته.",
        "أنواع مختلفة" => "الجهاز يتوقف لأسباب متغيرة، وهذا يدل غالبًا على مشكلة عامة في الطاقة أو اللوحة الأم أو النظام.",
        "غير معروف" => "الجهاز أعاد التشغيل بشكل مفاجئ، ويحتاج فحصًا عمليًا لتحديد السبب.",
        _ => "الجهاز أعاد التشغيل بسبب توقف في: " + d.Title + ".",
    };

    /// <summary>تقرير الزبون: لغة بسيطة، القطعة المرجّحة، ودرجة الثقة — بلا رموز ولا أدلة تقنية</summary>
    public static string CustomerReport(Diagnosis d, string shopName = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("تقرير فحص الجهاز" + (string.IsNullOrWhiteSpace(shopName) ? "" : " — " + shopName));
        sb.AppendLine();
        if (d.Device != "") sb.AppendLine("الجهاز: " + d.Device);
        sb.AppendLine("المشكلة: " + CustomerProblem(d));
        if (d.TopPart != "") sb.AppendLine("السبب المرجّح: " + d.TopPart + (d.Candidates.Count > 1 ? " (وقد يكون: " + d.Candidates[1].Part + ")" : ""));
        sb.AppendLine("درجة الثقة: " + d.Confidence + (d.LogCount > 1 ? $" (من {d.LogCount} سجلات)" : ""));
        sb.AppendLine();
        sb.AppendLine("ملاحظة: التشخيص مبني على سجل الأعطال الذي يحفظه الجهاز، ويُؤكَّد بالفحص العملي قبل تبديل أي قطعة.");
        return sb.ToString();
    }
}
