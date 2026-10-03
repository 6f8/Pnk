using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Kashif;

/// <summary>
/// إخفاء معرّفات الجهاز من نص السجل قبل مشاركته: مفتاح التقارير (crashReporterKey)، UDID، المعرّفات UUID،
/// الرقم التسلسلي، IMEI، ECID. كل معرّف يُستبدل ببديل بنفس الشكل والطول، والبديل ثابت داخل نفس العملية
/// (نفس الجهاز في عدة سجلات يبقى جهازًا واحدًا فيعمل التجميع)، ومختلف بين عملية وأخرى فلا يُربط بالأصل.
/// التشخيص لا يتغير: لا يُمس الموديل ولا نص البانك ولا الأرقام التقنية.
/// </summary>
public sealed class Anonymizer
{
    readonly byte[] salt = RandomNumberGenerator.GetBytes(32);
    readonly Dictionary<string, string> map = new(StringComparer.Ordinal);

    static readonly (Regex Rx, int Group)[] Patterns =
    {
        (new(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{40}(?![0-9A-Fa-f])"), 0),                                           // crashReporterKey، UDID القديم
        (new(@"(?<![0-9A-Fa-f-])[0-9A-Fa-f]{8}-[0-9A-Fa-f]{16}(?![0-9A-Fa-f-])"), 0),                           // UDID الحديث
        (new(@"(?<![0-9A-Fa-f-])[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}(?![0-9A-Fa-f-])"), 0),     // UUID
        (new(@"""(?:serial|serialNumber|SerialNumber|sn)""\s*:\s*""([A-Za-z0-9]{8,14})""", RegexOptions.IgnoreCase), 1),
        (new(@"Serial\s*Number\s*[:=]\s*([A-Za-z0-9]{8,14})", RegexOptions.IgnoreCase), 1),
        (new(@"الرقم التسلسلي\s*:?\s*([A-Za-z0-9]{8,14})"), 1),                                                 // ملاحظات الفحص من الكيبل
        (new(@"ECID\W{0,4}[:=]\W{0,4}((?:0x)?[0-9A-Fa-f]{8,16})", RegexOptions.IgnoreCase), 1),
        (new(@"(?<!\d)\d{15}(?!\d)"), 0),                                                                         // IMEI
    };

    public string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        foreach (var (rx, group) in Patterns)
            text = rx.Replace(text, m =>
            {
                var g = m.Groups[group];
                return m.Value[..(g.Index - m.Index)] + Replace(g.Value) + m.Value[(g.Index - m.Index + g.Length)..];
            });
        return text;
    }

    /// <summary>بديل ثابت بنفس الشكل: الأرقام أرقام، الحروف الست عشرية تبقى ست عشرية بنفس حالتها، والفواصل كما هي</summary>
    string Replace(string value)
    {
        if (map.TryGetValue(value, out var r)) return r;
        // تدفق بايتات من HMAC (المفتاح عشوائي لكل عملية)
        var stream = new List<byte>();
        using (var h = new HMACSHA256(salt))
            for (int block = 0; stream.Count < value.Length; block++)
                stream.AddRange(h.ComputeHash(Encoding.UTF8.GetBytes(value.ToUpperInvariant() + "#" + block)));
        bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || value.All(c => Uri.IsHexDigit(c) || c == '-');
        bool upper = value.Any(char.IsUpper) && !value.Any(char.IsLower);
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            int b = stream[i];
            if (c == '-' || (i == 1 && (c is 'x' or 'X') && value[0] == '0')) sb.Append(c);
            else if (hex) { var d = "0123456789abcdef"[b % 16]; sb.Append(upper ? char.ToUpperInvariant(d) : d); }
            else if (char.IsDigit(c)) sb.Append((char)('0' + b % 10));
            else if (char.IsUpper(c)) sb.Append((char)('A' + b % 26));
            else if (char.IsLower(c)) sb.Append((char)('a' + b % 26));
            else sb.Append(c);
        }
        return map[value] = sb.ToString();
    }
}
