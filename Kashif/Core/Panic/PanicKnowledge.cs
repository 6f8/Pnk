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

    /// <summary>كل الأسماء بمفاتيحها (ChargingFlex ← «فلاتة الشحن السفلية») — قاعدة المعرفة تستخدم المفاتيح</summary>
    public static readonly IReadOnlyDictionary<string, string> ByKey = typeof(Parts)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue());

    public static IEnumerable<string> All => ByKey.Values;
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

/// <summary>دليل من نص السجل: ماذا وُجد، وقيمته، ومعناه — Needle نص يُبحث عنه في السجل لتظليل مكانه</summary>
public sealed record Evidence(string What, string Value, string Meaning, string Needle = null, string Group = "")
{
    /// <summary>مجموعة الدليل: "" أساسي، "info" معلومة من السجل بلا أثر على الترتيب، "exam" جواب من الفحص العملي</summary>
    public bool IsInfo => Group == "info";
    public bool IsExam => Group == "exam";
}

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

/// <summary>خبرة المحل: نص يظهر في السجل (أو رمز حساس، أو تعبير منتظم) ← القطعة التي كانت السبب</summary>
public sealed class CustomRule
{
    public long Id;
    public string Name = "", Pattern = "", Device = "", Part = "", Level = "شائع", Note = "";
    /// <summary>النمط تعبير منتظم (Regex) بدل نص ثابت</summary>
    public bool IsRegex;
    /// <summary>الأولوية من -20 إلى 20: تُضاف إلى الدرجة فتغلب القاعدة الأعلى عند تعارض قاعدتين</summary>
    public int Priority;

    public static readonly string[] Levels = { "مؤكد", "شائع", "محتمل" };

    public int Score => Math.Clamp((Level switch { "مؤكد" => 96, "شائع" => 78, _ => 52 }) + Math.Clamp(Priority, -20, 20), 1, 99);

    /// <summary>خطأ في النمط (تعبير منتظم غير صالح) أو null</summary>
    public string PatternError()
    {
        if (!IsRegex) return null;
        try { _ = new Regex(Pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }

    /// <summary>هل تنطبق القاعدة على نص البانك أو على أحد رموز الحساسات؟ (مع فلتر الجهاز إن وُجد)</summary>
    public bool Matches(string panicText, IEnumerable<string> sensors, string device, string product)
    {
        var pat = (Pattern ?? "").Trim();
        if (pat == "" || string.IsNullOrWhiteSpace(Part)) return false;
        bool hit;
        if (IsRegex)
        {
            try { hit = Regex.IsMatch(panicText ?? "", pat, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException) { return false; }
            catch (RegexMatchTimeoutException) { return false; }
        }
        else hit = (panicText ?? "").Contains(pat, StringComparison.OrdinalIgnoreCase) || sensors.Contains(pat, StringComparer.OrdinalIgnoreCase);
        if (!hit) return false;
        var dev = (Device ?? "").Trim();
        return dev == "" || (device ?? "").Contains(dev, StringComparison.OrdinalIgnoreCase) || (product ?? "").Contains(dev, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// قاعدة المعرفة: تُقرأ من ملف البيانات المدمج Data/knowledge.json (رموز الحساسات، مفاتيح SMC، خدمات المراقبة، تواقيع أنواع البانك).
/// لكل معلومة مصدر ودرجة (مؤكد / شائع / محتمل). أسماء القطع في الملف مفاتيح ثابتة من <see cref="Parts"/>.
/// </summary>
public static class PanicKnowledge
{
    public sealed record Choice(string Part, int Score, string Why);

    /// <summary>حساس في سطر «Missing sensor(s)»: موضعه حسب الجيل، أو حسب موديل محدد إن وُثّق</summary>
    public sealed class Sensor
    {
        public string Code = "", What = "", Note = "", Source = "", Level = "";
        public Dictionary<string, Choice[]> Families = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Choice[]> Models = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>الأماكن حسب الجيل فقط</summary>
        public Choice[] Locate(AppleDevices.Family f) =>
            Families.TryGetValue(f.ToString(), out var c) ? c : Families.TryGetValue("default", out var d) ? d : Array.Empty<Choice>();

        /// <summary>الأماكن لموديل محدد: معلومة الموديل تغلب الجيل (ModelSpecific = true)</summary>
        public (Choice[] Choices, bool ModelSpecific) LocateFor(string product, AppleDevices.Family f) =>
            !string.IsNullOrEmpty(product) && Models.TryGetValue(product, out var m) ? (m, true) : (Locate(f), false);
    }

    public sealed record SmcKey(string Key, string Meaning, bool Battery, string Source);
    public sealed record Service(string Name, string What, Choice[] Choices, string[] Steps, string Confidence, string Source, string Level);
    public sealed record Signature(string Kind, string Title, Regex Match, string Explain, Choice[] Choices, string[] Steps, string Confidence, string Source, string Level);
    /// <summary>درايفر (kext) يظهر في مسار الانهيار ← القطع المرتبطة به (دليل ثانوي)</summary>
    public sealed record Kext(string Prefix, string What, Choice[] Choices, string Source);
    /// <summary>جواب في الفحص التفاعلي: أثره على درجة كل قطعة (بالاسم)، وهل هو اختبار حاسم</summary>
    public sealed record AnswerOption(string Label, IReadOnlyDictionary<string, int> Effects, bool Decisive, string Note, bool Add);
    /// <summary>سؤال في الفحص التفاعلي: يُسأل عندما تكون إحدى قطعه المستهدفة بين الأسباب الأعلى</summary>
    public sealed record Question(string Id, string Text, string Hint, string[] Targets, int Priority, AnswerOption[] Answers, string Source);

    /// <summary>قاعدة معرفة كاملة مقروءة من ملف</summary>
    public sealed class Base
    {
        public int Version;
        public List<Sensor> Sensors = new();
        public Dictionary<string, SmcKey> SmcKeys = new(StringComparer.Ordinal);
        public List<Service> Services = new();
        public List<Signature> Signatures = new();
        public List<Kext> Kexts = new();
        public List<Question> Questions = new();
        /// <summary>مشكلات في الملف (قطعة غير معروفة، درجة خارج الحدود، تعبير غير صالح...) — فارغة إذا كان الملف سليمًا</summary>
        public List<string> Problems = new();
    }

    static Base current;
    static readonly object gate = new();

    /// <summary>قاعدة المعرفة المدمجة في البرنامج</summary>
    public static Base Current
    {
        get
        {
            if (current != null) return current;
            lock (gate)
            {
                if (current != null) return current;
                using var s = typeof(PanicKnowledge).Assembly.GetManifestResourceStream("Kashif.knowledge.json")
                    ?? throw new InvalidOperationException("ملف قاعدة المعرفة knowledge.json غير مدمج في البرنامج.");
                using var r = new StreamReader(s, System.Text.Encoding.UTF8);
                current = Load(r.ReadToEnd());
                return current;
            }
        }
    }

    public static IReadOnlyList<Sensor> Sensors => Current.Sensors;
    public static IReadOnlyList<Service> Services => Current.Services;
    public static IReadOnlyList<Signature> Signatures => Current.Signatures;
    public static IReadOnlyDictionary<string, string> SmcKeys => Current.SmcKeys.ToDictionary(k => k.Key, k => k.Value.Meaning);

    /// <summary>قراءة قاعدة معرفة من نص JSON — لا ترمي استثناءً لخطأ في عنصر واحد بل تسجّله في Problems</summary>
    public static Base Load(string json)
    {
        var b = new Base();
        using var doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
        var root = doc.RootElement;
        b.Version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out var vi) ? vi : 0;

        string S(System.Text.Json.JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() ?? "" : "";
        string[] Strings(System.Text.Json.JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Array ? p.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
        Choice[] Choices(System.Text.Json.JsonElement arr, string where)
        {
            var list = new List<Choice>();
            if (arr.ValueKind != System.Text.Json.JsonValueKind.Array) { b.Problems.Add($"{where}: قائمة الأسباب ليست مصفوفة"); return list.ToArray(); }
            foreach (var c in arr.EnumerateArray())
            {
                var key = S(c, "part");
                int score = c.TryGetProperty("score", out var sc) && sc.TryGetInt32(out var si) ? si : -1;
                if (!Parts.ByKey.TryGetValue(key, out var name)) { b.Problems.Add($"{where}: قطعة غير معروفة «{key}»"); continue; }
                if (score is < 1 or > 99) { b.Problems.Add($"{where}: الدرجة {score} خارج 1–99 للقطعة {key}"); score = Math.Clamp(score, 1, 99); }
                list.Add(new Choice(name, score, S(c, "why")));
            }
            if (list.Count == 0) b.Problems.Add($"{where}: لا توجد أسباب");
            return list.ToArray();
        }

        if (root.TryGetProperty("sensors", out var sensors))
            foreach (var e in sensors.EnumerateArray())
            {
                var s = new Sensor { Code = S(e, "code"), What = S(e, "what"), Note = S(e, "note"), Source = S(e, "source"), Level = S(e, "level") };
                if (e.TryGetProperty("families", out var fam))
                    foreach (var f in fam.EnumerateObject())
                    {
                        if (f.Name != "default" && !Enum.TryParse<AppleDevices.Family>(f.Name, out _)) b.Problems.Add($"الحساس {s.Code}: جيل غير معروف «{f.Name}»");
                        s.Families[f.Name] = Choices(f.Value, $"الحساس {s.Code} ({f.Name})");
                    }
                if (e.TryGetProperty("models", out var models))
                    foreach (var m in models.EnumerateObject())
                    {
                        if (!AppleDevices.IsKnown(m.Name)) b.Problems.Add($"الحساس {s.Code}: موديل غير معروف «{m.Name}»");
                        s.Models[m.Name] = Choices(m.Value, $"الحساس {s.Code} ({m.Name})");
                    }
                if (!s.Families.ContainsKey("default")) b.Problems.Add($"الحساس {s.Code}: لا يوجد موضع افتراضي (default)");
                if (b.Sensors.Any(x => x.Code.Equals(s.Code, StringComparison.OrdinalIgnoreCase))) b.Problems.Add($"الحساس {s.Code} مكرر");
                b.Sensors.Add(s);
            }

        if (root.TryGetProperty("smcKeys", out var keys))
            foreach (var e in keys.EnumerateArray())
            {
                var k = new SmcKey(S(e, "key"), S(e, "meaning"), e.TryGetProperty("battery", out var bt) && bt.ValueKind == System.Text.Json.JsonValueKind.True, S(e, "source"));
                if (k.Key.Length != 4) b.Problems.Add($"مفتاح SMC «{k.Key}» ليس 4 أحرف");
                b.SmcKeys[k.Key] = k;
            }

        if (root.TryGetProperty("services", out var services))
            foreach (var e in services.EnumerateArray())
            {
                var name = S(e, "name");
                var steps = Strings(e, "steps");
                if (steps.Length == 0) b.Problems.Add($"الخدمة {name}: لا توجد خطوات فحص");
                b.Services.Add(new Service(name, S(e, "what"), Choices(e.GetProperty("choices"), $"الخدمة {name}"), steps, S(e, "confidence"), S(e, "source"), S(e, "level")));
            }

        if (root.TryGetProperty("signatures", out var sigs))
            foreach (var e in sigs.EnumerateArray())
            {
                var kind = S(e, "kind");
                Regex rx;
                try { rx = new Regex(S(e, "regex"), RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
                catch (ArgumentException ex) { b.Problems.Add($"التوقيع {kind}: تعبير غير صالح — {ex.Message}"); continue; }
                var steps = Strings(e, "steps");
                if (steps.Length == 0) b.Problems.Add($"التوقيع {kind}: لا توجد خطوات فحص");
                b.Signatures.Add(new Signature(kind, S(e, "title"), rx, S(e, "explain"), Choices(e.GetProperty("choices"), $"التوقيع {kind}"), steps, S(e, "confidence"), S(e, "source"), S(e, "level")));
            }

        if (root.TryGetProperty("kexts", out var kexts))
            foreach (var e in kexts.EnumerateArray())
            {
                var prefix = S(e, "prefix");
                if (!prefix.StartsWith("com.apple.", StringComparison.Ordinal)) b.Problems.Add($"الدرايفر «{prefix}» لا يبدأ بـ com.apple.");
                b.Kexts.Add(new Kext(prefix, S(e, "what"), Choices(e.GetProperty("choices"), $"الدرايفر {prefix}"), S(e, "source")));
            }

        if (root.TryGetProperty("questions", out var questions))
            foreach (var e in questions.EnumerateArray())
            {
                var id = S(e, "id");
                var targets = new List<string>();
                foreach (var t in Strings(e, "targets"))
                    if (Parts.ByKey.TryGetValue(t, out var tn)) targets.Add(tn); else b.Problems.Add($"السؤال {id}: قطعة مستهدفة غير معروفة «{t}»");
                var answers = new List<AnswerOption>();
                if (e.TryGetProperty("answers", out var arr))
                    foreach (var a in arr.EnumerateArray())
                    {
                        var effects = new Dictionary<string, int>();
                        if (a.TryGetProperty("effects", out var ef))
                            foreach (var p in ef.EnumerateObject())
                            {
                                if (!Parts.ByKey.TryGetValue(p.Name, out var pn)) { b.Problems.Add($"السؤال {id}: قطعة غير معروفة في الأثر «{p.Name}»"); continue; }
                                int delta = p.Value.TryGetInt32(out var dv) ? dv : 0;
                                if (delta is < -60 or > 60) b.Problems.Add($"السؤال {id}: أثر {delta} خارج -60..60");
                                effects[pn] = Math.Clamp(delta, -60, 60);
                            }
                        bool Flag(string n) => a.TryGetProperty(n, out var f) && f.ValueKind == System.Text.Json.JsonValueKind.True;
                        answers.Add(new AnswerOption(S(a, "label"), effects, Flag("decisive"), S(a, "note"), Flag("add")));
                    }
                if (answers.Count < 2) b.Problems.Add($"السؤال {id}: أقل من جوابين");
                if (b.Questions.Any(q => q.Id == id)) b.Problems.Add($"السؤال {id} مكرر");
                int pr = e.TryGetProperty("priority", out var pe) && pe.TryGetInt32(out var pv) ? pv : 0;
                b.Questions.Add(new Question(id, S(e, "text"), S(e, "hint"), targets.ToArray(), pr, answers.ToArray(), S(e, "source")));
            }

        foreach (var c in b.Services.Select(x => x.Confidence).Concat(b.Signatures.Select(x => x.Confidence)))
            if (c is not ("عالية" or "متوسطة" or "منخفضة")) b.Problems.Add($"درجة ثقة غير صالحة «{c}»");
        return b;
    }

    public static Sensor FindSensor(string code) =>
        Current.Sensors.FirstOrDefault(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>الدرايفر الأطول تطابقًا مع اسم الحزمة (com.apple.driver.AppleHPM يغلب com.apple.driver.AppleH)</summary>
    public static Kext FindKext(string bundleId) =>
        string.IsNullOrEmpty(bundleId) ? null
        : Current.Kexts.Where(k => bundleId.StartsWith(k.Prefix, StringComparison.Ordinal)).OrderByDescending(k => k.Prefix.Length).FirstOrDefault();

    public static Service FindService(string name) =>
        Current.Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>معنى المفتاح: من القاعدة، وإلا من الحرف الأول حسب تسمية Apple لمفاتيح SMC</summary>
    public static string SmcKeyMeaning(string key)
    {
        if (Current.SmcKeys.TryGetValue(key, out var m)) return m.Meaning;
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

    /// <summary>مفتاح بطارية مؤكد بالاسم (مثل TG0B و B0AV) — دليل قوي</summary>
    public static bool IsBatteryKey(string key) => Current.SmcKeys.TryGetValue(key, out var k) && k.Battery;

    /// <summary>مفتاح يبدأ بحرف B وليس في القاعدة — دليل ضعيف فقط (قد لا يكون للبطارية)</summary>
    public static bool IsWeakBatteryKey(string key) => !IsBatteryKey(key) && key.StartsWith('B');

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
