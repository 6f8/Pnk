using System.IO.Compression;
using System.Text;

namespace Kashif;

/// <summary>
/// تصدير الحالات المؤكدة (فحوصات سُجّلت لها القطعة التي أصلحت الجهاز فعلًا) إلى ملف zip:
/// لكل حالة مجلد فيه السجلات الأصلية وملف case.txt (الموديل، ما رجّحه كاشف، القطعة المُصلِحة، أجوبة الفحص).
/// لا يُصدَّر اسم الزبون ولا هاتفه. الغرض: إرسال حالات حقيقية لتحسين قاعدة المعرفة.
/// </summary>
public static class CaseExport
{
    public const string Query = @"SELECT id, date, device, product, ios, kind, title, top_part, top3, fixed_part, fixed_date, flags, answers, notes, raw
        FROM analyses WHERE TRIM(IFNULL(fixed_part,''))<>'' ORDER BY id";

    public sealed record Case(long Id, string Date, string Device, string Product, string Ios, string Kind, string Title,
        string TopPart, string Top3, string FixedPart, string FixedDate, string Flags, string Answers, string Notes, string Raw);

    /// <summary>يكتب الحالات إلى zip ويعيد عددها (الحالات بلا سجلات محفوظة تُتجاوز)</summary>
    /// <param name="anonymize">إخفاء معرّفات الجهاز من السجلات والملاحظات (البديل ثابت داخل نفس الملف)</param>
    public static int Write(Stream output, IEnumerable<Case> cases, bool anonymize = true)
    {
        var anon = anonymize ? new Anonymizer() : null;
        string A(string t) => anon == null ? t : anon.Clean(t);
        int n = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var c in cases)
        {
            var logs = StoreSql.DecodeLogs(c.Raw);
            if (logs.Count == 0) continue;
            n++;
            string dir = $"case-{c.Id:D4}/";
            var top3 = (c.Top3 ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int rank = Array.IndexOf(top3, c.FixedPart.Trim());
            var sb = new StringBuilder()
                .AppendLine($"الجهاز: {c.Device}")
                .AppendLine($"الموديل: {c.Product}")
                .AppendLine($"iOS: {c.Ios}")
                .AppendLine($"تاريخ الفحص: {c.Date}")
                .AppendLine($"نوع البانك: {c.Kind} — {c.Title}")
                .AppendLine($"الأرجح عند كاشف: {c.TopPart}")
                .AppendLine($"أول 3 أسباب: {string.Join(" | ", top3)}")
                .AppendLine($"القطعة التي أصلحت الجهاز فعلًا: {c.FixedPart.Trim()}")
                .AppendLine($"ترتيبها عند كاشف: {(rank >= 0 ? (rank + 1).ToString() : "لم تكن في أول 3")}")
                .AppendLine($"تاريخ الإصلاح: {c.FixedDate}")
                .AppendLine($"ما حدث للجهاز: {c.Flags}")
                .AppendLine($"أجوبة الفحص: {c.Answers}")
                .AppendLine($"ملاحظات الفني: {A(c.Notes)}")
                .AppendLine(anonymize ? "معرّفات الجهاز (المفتاح، UDID، الرقم التسلسلي، IMEI) مستبدلة ببدائل." : "");
            Entry(zip, dir + "case.txt", sb.ToString());
            for (int i = 0; i < logs.Count; i++)
            {
                var name = Safe(logs[i].Source);
                Entry(zip, $"{dir}{i + 1:D2}-{(name == "" ? "log" : name)}{(name.Contains('.') ? "" : ".txt")}", A(logs[i].Raw));
            }
        }
        return n;
    }

    static void Entry(ZipArchive zip, string path, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        w.Write(text ?? "");
    }

    /// <summary>اسم ملف آمن من مصدر السجل (اسم الملف الأصلي أو «نص ملصوق»)</summary>
    static string Safe(string s)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var t = new string((s ?? "").Where(ch => !bad.Contains(ch) && !char.IsControl(ch)).ToArray()).Trim().Trim('.');
        return t.Length > 60 ? t[..60] : t;
    }
}
