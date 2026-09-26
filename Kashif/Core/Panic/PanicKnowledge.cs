using System.Text.RegularExpressions;

namespace Kashif;

/// <summary>أسماء القطع والأسباب الموحّدة (تُستخدم في الترتيب والتقارير — نفس الاسم في كل مكان)</summary>
public static class Parts
{
    public const string ChargingFlex = "فلاتة الشحن السفلية";
    public const string PowerFlex = "فلاتة زر التشغيل والفلاش";
    public const string FrontFlex = "فلاتة الكاميرا الأمامية وحساسات القرب / سماعة المكالمات";
    public const string Battery = "البطارية";
    public const string BatteryConn = "موصل البطارية أو فلاتتها";
    public const string Board = "البوردة (خط الحساس أو الآيسي)";
    public const string SmcLine = "خط اتصال SMC بشريحة قياس البطارية (البوردة)";
    public const string Ios = "نظام iOS (سبب برمجي)";
    public const string Screen = "الشاشة أو موصلها";
    public const string Biometric = "Face ID / Touch ID (الموديول أو فلاتته)";
    public const string TouchId = "زر البصمة Touch ID أو فلاتته";
    public const string Nand = "الذاكرة الداخلية NAND";
    public const string Wifi = "شريحة الواي فاي والبلوتوث";
    public const string Baseband = "البيسباند (الشبكة) على البوردة";
    public const string Camera = "الكاميرا أو فلاتتها";
    public const string ChargeIc = "آيسي الشحن (Tristar / Hydra / منفذ USB-C)";
    public const string AudioIc = "آيسي الصوت على البوردة";
    public const string AudioParts = "السماعات أو الميكروفونات وفلاتاتها";
    public const string Pmu = "آيسي الطاقة PMU";
    public const string SocRam = "المعالج أو الذاكرة RAM (البوردة)";
    public const string Liquid = "تأكسد بسبب السوائل (موصلات وفلاتات)";
    public const string LastPart = "آخر قطعة فُكّت أو استُبدلت";
    public const string Accessory = "شاحن أو كيبل أو ملحق تالف";
    public const string Sensors = "حساسات الحركة والموقع (البوردة)";
}

/// <summary>سبب محتمل بدرجة (0–100). الدرجة للترتيب فقط وليست نسبة مئوية دقيقة.</summary>
public sealed class Candidate
{
    public string Part = "";
    public int Score;
    public string Why = "";
    /// <summary>الأرجح / مرجّح / محتمل / احتمال بعيد</summary>
    public string Label = "";
    public Candidate Clone() => new() { Part = Part, Score = Score, Why = Why, Label = Label };
}

/// <summary>دليل من نص السجل: ماذا وُجد، وقيمته، ومعناه</summary>
public sealed record Evidence(string What, string Value, string Meaning);

/// <summary>ما يعرفه الفني عن الحالة (يغيّر ترتيب الأسباب — لا يضيف قطعًا بلا دليل إلا السوائل)</summary>
public sealed class CaseFlags
{
    public bool Liquid, BatteryReplaced, ChargingFlexReplaced, ScreenReplaced, Dropped;
    public bool Any => Liquid || BatteryReplaced || ChargingFlexReplaced || ScreenReplaced || Dropped;

    public override string ToString() => string.Join("، ", new[]
    {
        Liquid ? "تعرض لسوائل" : null, BatteryReplaced ? "بطارية مستبدلة" : null, ChargingFlexReplaced ? "فلاتة شحن مستبدلة" : null,
        ScreenReplaced ? "شاشة مستبدلة" : null, Dropped ? "سقوط أو ضربة" : null,
    }.Where(x => x != null));

    public string Encode() => $"{(Liquid ? 1 : 0)}{(BatteryReplaced ? 1 : 0)}{(ChargingFlexReplaced ? 1 : 0)}{(ScreenReplaced ? 1 : 0)}{(Dropped ? 1 : 0)}";

    public static CaseFlags Decode(string s)
    {
        s ??= "";
        bool B(int i) => i < s.Length && s[i] == '1';
        return new CaseFlags { Liquid = B(0), BatteryReplaced = B(1), ChargingFlexReplaced = B(2), ScreenReplaced = B(3), Dropped = B(4) };
    }
}

/// <summary>خبرة المحل: نص يظهر في السجل (أو رمز حساس) ← القطعة التي كانت السبب</summary>
public sealed class CustomRule
{
    public long Id;
    public string Name = "", Pattern = "", Device = "", Part = "", Level = "شائع", Note = "";

    public static readonly string[] Levels = { "مؤكد", "شائع", "محتمل" };

    public int Score => Level switch { "مؤكد" => 96, "شائع" => 78, _ => 52 };
}

/// <summary>
/// قاعدة المعرفة المدمجة: رموز الحساسات، مفاتيح SMC، خدمات المراقبة (watchdog)، وتواقيع أنواع البانك.
/// كل معلومة لها درجة: الدرجة العالية لما هو معروف ومتكرر في ورش الصيانة، والمنخفضة لما يحتاج تحققًا بالتبديل.
/// </summary>
public static class PanicKnowledge
{
    public sealed record Choice(string Part, int Score, string Why);

    /// <summary>حساس في سطر «Missing sensor(s)»</summary>
    public sealed record Sensor(string Code, string What, Func<AppleDevices.Family, Choice[]> Locate, string Note);

    public static readonly Sensor[] Sensors =
    {
        new("Prs0", "حساس الضغط الجوي (Barometer)", f => f == AppleDevices.Family.X11
            ? new[] { new Choice(Parts.ChargingFlex, 92, "في iPhone X إلى 11 Pro Max حساس الضغط مركّب على فلاتة الشحن"), new Choice(Parts.Board, 30, "إذا بقي البانك مع فلاتة سليمة: خط الحساس على البوردة") }
            : new[] { new Choice(Parts.ChargingFlex, 70, "الموضع الأشيع لحساس الضغط — تأكد بتبديل الفلاتة"), new Choice(Parts.Board, 38, "خط الحساس على البوردة") },
            "الحساس لا يُقرأ فتتوقف خدمة الحرارة thermalmonitord ويعيد الجهاز التشغيل كل 3 دقائق تقريبًا."),
        new("mic1", "الميكروفون السفلي (mic1)", f => new[]
            { new Choice(Parts.ChargingFlex, f == AppleDevices.Family.X11 ? 88 : 75, "الميكروفون السفلي على فلاتة الشحن"), new Choice(Parts.Board, 32, "خط الميكروفون أو آيسي الصوت على البوردة") },
            "الميكروفون جزء من قائمة الحساسات التي يراقبها النظام؛ غيابه يسبب نفس نمط إعادة التشغيل."),
        new("mic2", "ميكروفون ثانوي (mic2)", f => new[]
            { new Choice(Parts.PowerFlex, f == AppleDevices.Family.X11 ? 72 : 60, "في أغلب الموديلات الميكروفون الخلفي/العلوي على فلاتة زر التشغيل والفلاش"), new Choice(Parts.ChargingFlex, 38, "في بعض الموديلات على فلاتة الشحن — تحقّق من مخطط الجهاز"), new Choice(Parts.Board, 30, "خط الميكروفون على البوردة") },
            "تأكد من الفلاتة التي فُكّت آخر مرة قبل تبديل أي قطعة."),
        new("mic3", "ميكروفون إضافي (mic3)", f => new[]
            { new Choice(Parts.FrontFlex, 55, "الميكروفون الأمامي غالبًا مع فلاتة سماعة المكالمات/الكاميرا الأمامية"), new Choice(Parts.PowerFlex, 40, "أو على فلاتة زر التشغيل حسب الموديل"), new Choice(Parts.Board, 30, "خط الميكروفون على البوردة") },
            "موضعه يختلف بين الموديلات: ابدأ بالفلاتة التي فُكّت آخر مرة."),
        new("TG0B", "حساس حرارة البطارية (TG0B)", f => new[]
            { new Choice(Parts.Battery, 80, "حساس الحرارة داخل البطارية: بطارية تالفة أو غير أصلية"), new Choice(Parts.BatteryConn, 62, "الموصل غير محكم أو متأكسد"), new Choice(Parts.Board, 30, "خط الحساس على البوردة") },
            "جرّب بطارية سليمة معروفة؛ إن اختفى البانك فالبطارية هي السبب."),
        new("TG0V", "حساس حرارة مرتبط بالبطارية (TG0V)", f => new[]
            { new Choice(Parts.Battery, 75, "حساس حرارة مرتبط بالبطارية"), new Choice(Parts.BatteryConn, 62, "الموصل أو الفلاتة"), new Choice(Parts.Board, 32, "خط الحساس على البوردة") },
            "جرّب بطارية سليمة معروفة وافحص الموصل."),
    };

    public static Sensor FindSensor(string code) =>
        Sensors.FirstOrDefault(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>مفاتيح SMC المعروفة (أربعة أحرف تظهر في رسائل Mailbox داخل بانك SMC)</summary>
    public static readonly Dictionary<string, string> SmcKeys = new(StringComparer.Ordinal)
    {
        ["TG0B"] = "حرارة البطارية",
        ["TG0V"] = "حرارة مرتبطة بالبطارية",
        ["B0AV"] = "متوسط جهد البطارية",
        ["B0AC"] = "متوسط تيار البطارية",
        ["B0RM"] = "السعة المتبقية في البطارية",
        ["B0FC"] = "السعة الكاملة للبطارية",
        ["B0CT"] = "عدد دورات الشحن",
        ["BBIN"] = "هل البطارية موجودة",
    };

    /// <summary>معنى المفتاح: من القائمة، وإلا من الحرف الأول حسب تسمية Apple لمفاتيح SMC</summary>
    public static string SmcKeyMeaning(string key)
    {
        if (SmcKeys.TryGetValue(key, out var m)) return m;
        return key[0] switch
        {
            'B' => "بطارية / شريحة قياس الشحن (حسب الحرف الأول)",
            'T' => "حرارة (حسب الحرف الأول)",
            'V' => "جهد (حسب الحرف الأول)",
            'I' => "تيار (حسب الحرف الأول)",
            'P' => "طاقة (حسب الحرف الأول)",
            _ => "مفتاح غير معروف",
        };
    }

    public static bool IsBatteryKey(string key) => key.StartsWith('B') || key is "TG0B" or "TG0V";

    /// <summary>خدمة لم تسجّل حضورها لدى مراقب النظام (userspace watchdog)</summary>
    public sealed record Service(string Name, string What, Choice[] Choices, string[] Steps, string Confidence);

    public static readonly Service[] Services =
    {
        new("thermalmonitord", "خدمة مراقبة الحرارة والحساسات",
            new[] { new Choice(Parts.LastPart, 55, "حساس لا يُقرأ ولم يُذكر اسمه في السجل — غالبًا في فلاتة فُكّت أو استُبدلت"), new Choice(Parts.Battery, 45, "حساسات حرارة البطارية"), new Choice(Parts.Ios, 35, "نادرًا خلل برمجي بعد التحديث") },
            new[] { "افصل كل فلاتة فُكّت مؤخرًا وأعد تركيبها ونظّف موصلها.", "جرّب بطارية وفلاتة شحن سليمتين واحدة تلو الأخرى.", "إذا استمر: افحص خطوط الحساسات على البوردة." }, "منخفضة"),
        new("wifid", "خدمة الواي فاي",
            new[] { new Choice(Parts.Wifi, 70, "الخدمة تعلّق عندما لا تستجيب شريحة الواي فاي"), new Choice(Parts.Ios, 45, "إعدادات أو تحديث تالف") },
            new[] { "هل الواي فاي رمادي أو لا يعمل؟ هذا يؤكد الشريحة.", "أعد تعيين إعدادات الشبكة ثم حدّث أو أعد تثبيت iOS.", "إذا بقي الواي فاي معطلًا: الشريحة أو لحامها على البوردة." }, "متوسطة"),
        new("bluetoothd", "خدمة البلوتوث",
            new[] { new Choice(Parts.Wifi, 65, "البلوتوث والواي فاي في شريحة واحدة غالبًا"), new Choice(Parts.Ios, 45, "خلل برمجي") },
            new[] { "افحص عمل البلوتوث والواي فاي.", "حدّث أو أعد تثبيت iOS.", "إذا استمر: شريحة الواي فاي/البلوتوث على البوردة." }, "متوسطة"),
        new("backboardd", "خدمة الشاشة واللمس والأزرار",
            new[] { new Choice(Parts.Screen, 60, "شاشة غير أصلية أو موصل غير محكم يسبب تعليق الخدمة"), new Choice(Parts.Ios, 45, "خلل برمجي"), new Choice(Parts.FrontFlex, 30, "حساسات القرب والإضاءة") },
            new[] { "جرّب شاشة سليمة (أو الأصلية إن استُبدلت).", "افحص موصل الشاشة واللمس.", "حدّث أو أعد تثبيت iOS." }, "متوسطة"),
        new("mediaserverd", "خدمة الصوت والوسائط",
            new[] { new Choice(Parts.AudioIc, 60, "آيسي الصوت لا يستجيب"), new Choice(Parts.AudioParts, 45, "سماعة أو ميكروفون تالف على الخط"), new Choice(Parts.Ios, 35, "خلل برمجي") },
            new[] { "اختبر المكالمات والسماعة الخارجية والتسجيل الصوتي.", "افصل فلاتات السماعات والميكروفونات واحدة تلو الأخرى.", "إذا استمر: آيسي الصوت على البوردة." }, "متوسطة"),
        new("audiomxd", "خدمة الصوت",
            new[] { new Choice(Parts.AudioIc, 60, "آيسي الصوت لا يستجيب"), new Choice(Parts.AudioParts, 45, "سماعة أو ميكروفون تالف على الخط"), new Choice(Parts.Ios, 35, "خلل برمجي") },
            new[] { "اختبر المكالمات والسماعة الخارجية والتسجيل الصوتي.", "افصل فلاتات السماعات والميكروفونات واحدة تلو الأخرى.", "إذا استمر: آيسي الصوت على البوردة." }, "متوسطة"),
        new("CommCenter", "خدمة الشبكة والاتصال",
            new[] { new Choice(Parts.Baseband, 60, "البيسباند لا يستجيب"), new Choice(Parts.Ios, 40, "خلل برمجي أو إعدادات الشبكة") },
            new[] { "هل تظهر الشبكة؟ هل يظهر رقم IMEI في الإعدادات؟", "أعد تعيين إعدادات الشبكة وجرّب شريحة أخرى.", "إذا لا شبكة ولا IMEI: البيسباند على البوردة." }, "متوسطة"),
        new("locationd", "خدمة الموقع",
            new[] { new Choice(Parts.Sensors, 45, "حساسات الحركة أو GPS"), new Choice(Parts.Ios, 45, "خلل برمجي") },
            new[] { "اختبر البوصلة والموقع.", "حدّث أو أعد تثبيت iOS." }, "منخفضة"),
    };

    public static Service FindService(string name) =>
        Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>توقيع نوع بانك: يُطابق نص البانك بتعبير منتظم</summary>
    public sealed record Signature(string Kind, string Title, Regex Match, string Explain, Choice[] Choices, string[] Steps, string Confidence);

    static Regex R(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>بالترتيب: الأدق أولًا (يُعتمد أول توقيع مطابق، والبقية تظهر كأدلة إضافية)</summary>
    public static readonly Signature[] Signatures =
    {
        new("AOP", "انهيار المعالج دائم التشغيل AOP", R(@"AOP PANIC|\bAOP\b[^\n]{0,60}(?:panic|timeout|crash)|Client:\s*AppleAOP"),
            "معالج AOP يدير الحساسات (الحركة، القرب، الإضاءة، الميكروفونات في وضع الاستعداد). انهياره غالبًا بسبب حساس أو فلاتة على خطه.",
            new[] { new Choice(Parts.FrontFlex, 55, "حساس القرب والإضاءة وميكروفون سماعة المكالمات"), new Choice(Parts.Sensors, 45, "حساسات الحركة على البوردة"), new Choice(Parts.Ios, 30, "خلل برمجي") },
            new[] { "افصل فلاتة سماعة المكالمات/الكاميرا الأمامية وشغّل الجهاز: تغيّر شكل البانك يدل على الفلاتة.", "جرّب فلاتة سليمة.", "إذا استمر: خطوط AOP على البوردة." }, "متوسطة"),
        new("SEP", "انهيار المعالج الآمن SEP", R(@"\bSEP\b[^\n]{0,30}panic|AppleSEP[^\n]{0,40}(?:panic|fail|error)"),
            "المعالج الآمن يدير Face ID وTouch ID والتشفير. انهياره يرتبط غالبًا بموديول البصمة أو الوجه أو بمشكلة في البوردة.",
            new[] { new Choice(Parts.Biometric, 55, "موديول Face ID / Touch ID أو فلاتته"), new Choice(Parts.Board, 45, "المعالج الآمن أو الذاكرة على البوردة"), new Choice(Parts.Ios, 35, "خلل برمجي") },
            new[] { "هل يعمل Face ID / Touch ID؟", "افصل فلاتة Face ID أو البصمة وجرّب.", "حدّث أو أعد تثبيت iOS عبر الكمبيوتر.", "إذا استمر: فحص البوردة." }, "متوسطة"),
        new("التخزين NAND", "خطأ في الذاكرة الداخلية (NAND)", R(@"\bANS2\b|AppleANS|\bANS\b[^\n]{0,40}(?:panic|timeout|fail|assert)|\bNVMe\b|\bnvme\b|\bNAND\b"),
            "المعالج لم يستطع التعامل مع الذاكرة الداخلية. قد يكون تلفًا في ملفات النظام أو في الذاكرة نفسها أو لحامها.",
            new[] { new Choice(Parts.Nand, 60, "الذاكرة الداخلية أو لحامها"), new Choice(Parts.Ios, 45, "ملفات نظام تالفة") },
            new[] { "خذ نسخة احتياطية فورًا إن أمكن.", "تأكد أن المساحة غير ممتلئة.", "أعد تثبيت iOS عبر الكمبيوتر (استعادة كاملة، ولا تستعد النسخة الاحتياطية مباشرة للتجربة).", "إذا تكرر بعد الاستعادة: الذاكرة NAND على البوردة." }, "متوسطة"),
        new("الشاشة DCP", "انهيار معالج العرض DCP", R(@"\bDCP\b[^\n]{0,40}(?:panic|timeout|crash|assert)|AppleDCP|DCP PANIC"),
            "معالج العرض يدير الشاشة. الشاشات غير الأصلية والموصلات غير المحكمة من أشيع الأسباب.",
            new[] { new Choice(Parts.Screen, 60, "شاشة غير أصلية أو موصل غير محكم"), new Choice(Parts.Ios, 45, "خلل برمجي"), new Choice(Parts.Board, 30, "خطوط العرض على البوردة") },
            new[] { "جرّب شاشة سليمة (أو الأصلية إن استُبدلت).", "افحص موصل الشاشة.", "حدّث أو أعد تثبيت iOS." }, "متوسطة"),
        new("خط I2C", "خط اتصال I2C عالق", R(@"\bi2c\d*\b[^\n]{0,80}(?:stuck|timeout|SDA|SCL)|_checkBusStatus"),
            "قطعة على خط I2C تمسك الخط فلا يستطيع المعالج التحدث مع بقية القطع عليه.",
            new[] { new Choice(Parts.LastPart, 55, "فلاتة تالفة أو غير أصلية على نفس الخط"), new Choice(Parts.Board, 55, "آيسي أو خط I2C على البوردة") },
            new[] { "افصل الفلاتات (الشحن، الشاشة، الكاميرات، البطارية) واحدة تلو الأخرى وأعد التشغيل.", "القطعة التي يختفي البانك عند فصلها هي السبب.", "إذا لم يتغير شيء: الخط على البوردة." }, "متوسطة"),
        new("الواي فاي", "انهيار شريحة الواي فاي", R(@"AppleBCMWLAN|bcmwlan|\bWLAN\b[^\n]{0,40}(?:trap|fail|timeout|firmware)"),
            "شريحة الواي فاي والبلوتوث توقفت عن الاستجابة.",
            new[] { new Choice(Parts.Wifi, 70, "الشريحة أو لحامها"), new Choice(Parts.Ios, 35, "خلل برمجي") },
            new[] { "هل الواي فاي رمادي؟", "حدّث أو أعد تثبيت iOS.", "إذا بقي معطلًا: الشريحة على البوردة." }, "متوسطة"),
        new("الشبكة", "انهيار البيسباند (الشبكة)", R(@"\bbaseband\b|\bBBU\b|AppleBaseband|\bbbpv\b"),
            "معالج الشبكة (البيسباند) لم يستجب.",
            new[] { new Choice(Parts.Baseband, 65, "البيسباند على البوردة"), new Choice(Parts.Ios, 35, "خلل برمجي") },
            new[] { "هل تظهر الشبكة ورقم IMEI؟", "أعد تعيين إعدادات الشبكة وجرّب شريحة أخرى.", "إذا لا شبكة ولا IMEI: البيسباند على البوردة." }, "متوسطة"),
        new("الكاميرا", "انهيار معالج الكاميرا", R(@"\bISP\b[^\n]{0,40}(?:panic|timeout|crash|fail)|AppleH\d+CamIn|AppleCamIn|RTBuddy\(ISP\)"),
            "معالج الصور (ISP) لم يستجب — غالبًا كاميرا أو فلاتة تالفة.",
            new[] { new Choice(Parts.Camera, 60, "الكاميرا الخلفية أو الأمامية أو فلاتتها"), new Choice(Parts.Ios, 30, "خلل برمجي"), new Choice(Parts.Board, 30, "خطوط الكاميرا على البوردة") },
            new[] { "افصل الكاميرات واحدة تلو الأخرى وأعد التشغيل.", "جرّب كاميرا سليمة.", "إذا استمر: خطوط الكاميرا على البوردة." }, "متوسطة"),
        new("البصمة", "خطأ في حساس البصمة Touch ID", R(@"\bMesa\b|AppleMesa"),
            "حساس البصمة (Mesa) لم يستجب.",
            new[] { new Choice(Parts.TouchId, 70, "زر البصمة أو فلاتته"), new Choice(Parts.Board, 30, "خطوط البصمة على البوردة") },
            new[] { "افصل زر البصمة وشغّل الجهاز.", "افحص فلاتة الزر.", "إذا استمر: خطوط البصمة على البوردة." }, "متوسطة"),
        new("آيسي الشحن", "خطأ في آيسي الشحن أو المنفذ", R(@"Tristar|AppleTriStar|\bHydra\b|AppleHPM|\bHPM\b[^\n]{0,40}(?:fail|timeout|error)"),
            "آيسي الشحن (Tristar / Hydra أو متحكم USB-C) لم يستجب.",
            new[] { new Choice(Parts.ChargeIc, 65, "آيسي الشحن على البوردة"), new Choice(Parts.ChargingFlex, 45, "منفذ أو فلاتة الشحن"), new Choice(Parts.Accessory, 30, "شاحن أو كيبل غير أصلي") },
            new[] { "جرّب شاحنًا وكيبلًا أصليين.", "جرّب فلاتة شحن سليمة.", "إذا استمر: آيسي الشحن على البوردة." }, "متوسطة"),
        new("الرسوميات", "خطأ في معالج الرسوميات GPU", R(@"\bAGX\b|GPU Panic|AppleAGX|\bgfx\b[^\n]{0,40}(?:panic|fault|timeout)"),
            "معالج الرسوميات توقف. غالبًا برمجي (تطبيق أو تحديث)، ونادرًا المعالج نفسه.",
            new[] { new Choice(Parts.Ios, 55, "تطبيق أو تحديث"), new Choice(Parts.SocRam, 40, "المعالج على البوردة") },
            new[] { "هل يحدث مع تطبيق معيّن؟", "حدّث أو أعد تثبيت iOS.", "إذا تكرر بعد الاستعادة: المعالج على البوردة." }, "منخفضة"),
        new("الطاقة PMU", "خطأ في دائرة الطاقة PMU", R(@"\bPMU\b[^\n]{0,60}(?:fault|fail|panic|timeout|error)|\bSPMI\b|AppleSPMI"),
            "آيسي الطاقة أو خط اتصاله لم يستجب.",
            new[] { new Choice(Parts.Pmu, 55, "آيسي الطاقة أو خطه على البوردة"), new Choice(Parts.Battery, 45, "بطارية ضعيفة تسبب هبوط الجهد") },
            new[] { "جرّب بطارية سليمة أو شغّل الجهاز على مصدر طاقة DC.", "إذا استمر: دائرة الطاقة على البوردة." }, "متوسطة"),
        new("ارتفاع الحرارة", "ارتفاع حرارة شديد", R(@"thermal (?:shutdown|emergency)|overheat"),
            "الجهاز أُطفئ لحماية نفسه من الحرارة.",
            new[] { new Choice(Parts.Battery, 45, "بطارية منتفخة أو تالفة"), new Choice(Parts.Board, 40, "قصر في البوردة يسبب سخونة") },
            new[] { "المس الجهاز: أين تتركز السخونة؟", "جرّب بطارية سليمة.", "افحص البوردة بكاميرا حرارية أو بمصدر DC." }, "منخفضة"),
        new("النواة (برمجي)", "خطأ في نواة النظام", R(@"Kernel data abort|kernel instruction fetch abort|Unaligned kernel|zone[^\n]{0,40}(?:corrupt|element|free)|double free|stack overflow|Kernel trap|assertion failed"),
            "خطأ داخل نواة iOS. غالبًا برمجي (تحديث، تطبيق، جيلبريك)، وإذا تكرر بأشكال مختلفة بعد الاستعادة فقد يكون المعالج أو الذاكرة.",
            new[] { new Choice(Parts.Ios, 60, "تحديث أو تطبيق أو تعديل برمجي"), new Choice(Parts.SocRam, 35, "المعالج أو الذاكرة RAM") },
            new[] { "حدّث iOS.", "أعد تثبيت iOS عبر الكمبيوتر وجرّب قبل استعادة النسخة الاحتياطية.", "إذا تكرر بأشكال مختلفة بعد الاستعادة: البوردة." }, "منخفضة"),
    };

    /// <summary>رسائل bug_type الشائعة</summary>
    public static string BugTypeInfo(string bugType) => bugType switch
    {
        "210" => "بانك كامل (panic-full) — الملف الصحيح للتحليل",
        "288" => "Stackshot — ليس بانك: لقطة للنظام عند التعليق",
        "309" or "109" => "كراش تطبيق — ليس بانك النظام (لا يعيد تشغيل الجهاز)",
        "" => "",
        _ => "نوع سجل غير معروف",
    };
}
