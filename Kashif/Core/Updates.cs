using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kashif;

/// <summary>
/// التنبيه بنسخة جديدة: صفحة Releases في GitHub (الإصدار «latest» يبنيه GitHub بعد كل دمج).
/// يعمل فقط إن كان المستودع عامًا — المستودع الخاص يرد 404 بلا تسجيل دخول، فلا يظهر شيء ولا خطأ.
/// </summary>
public static class Updates
{
    public const string Repo = "6f8/Pnk";
    public sealed record Release(Version Version, string Url, string Name);

    /// <summary>رقم الإصدار من اسم النسخة («كاشف 1.0.6») أو الوسم</summary>
    public static Release Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string S(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
            var name = S("name");
            var m = Regex.Match(name + " " + S("tag_name"), @"(\d+)\.(\d+)\.(\d+)");
            if (!m.Success) return null;
            return new Release(new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)), S("html_url"), name);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>نسخة أحدث من الحالية؟ (الحالية مثل «1.0.5+abc»)</summary>
    public static bool IsNewer(Release r, string current)
    {
        if (r == null) return false;
        var m = Regex.Match(current ?? "", @"(\d+)\.(\d+)\.(\d+)");
        if (!m.Success) return false;
        return r.Version > new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
    }

    /// <summary>آخر نسخة منشورة، أو null (لا اتصال، مستودع خاص، أو رد غير متوقع)</summary>
    public static async Task<Release> LatestAsync(HttpClient http = null)
    {
        var own = http == null;
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("Kashif");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            return Parse(await res.Content.ReadAsStringAsync());
        }
        catch (Exception) { return null; }
        finally { if (own) http.Dispose(); }
    }
}
