using System.Text.RegularExpressions;

namespace Kashif;

/// <summary>
/// أسماء أجهزة iPhone من رمز الموديل الداخلي (product مثل iPhone12,1)، ومن رمز البوردة (مثل d94 في سطر SMC)،
/// واسم المعالج من رمزه في سطر النواة (مثل T8030). الرمز غير الموجود هنا يظهر كما هو ولا يُخمَّن.
/// </summary>
public static class AppleDevices
{
    /// <summary>الأجيال المتقاربة في أماكن الحساسات (تُستخدم في قاعدة المعرفة)</summary>
    public enum Family { Unknown, Early, X11, Later }

    public sealed record Model(string Name, Family Family);

    static readonly Dictionary<string, Model> Products = new(StringComparer.OrdinalIgnoreCase)
    {
        ["iPhone8,1"] = new("iPhone 6s", Family.Early),
        ["iPhone8,2"] = new("iPhone 6s Plus", Family.Early),
        ["iPhone8,4"] = new("iPhone SE (1st gen)", Family.Early),
        ["iPhone9,1"] = new("iPhone 7", Family.Early),
        ["iPhone9,3"] = new("iPhone 7", Family.Early),
        ["iPhone9,2"] = new("iPhone 7 Plus", Family.Early),
        ["iPhone9,4"] = new("iPhone 7 Plus", Family.Early),
        ["iPhone10,1"] = new("iPhone 8", Family.Early),
        ["iPhone10,4"] = new("iPhone 8", Family.Early),
        ["iPhone10,2"] = new("iPhone 8 Plus", Family.Early),
        ["iPhone10,5"] = new("iPhone 8 Plus", Family.Early),
        ["iPhone10,3"] = new("iPhone X", Family.X11),
        ["iPhone10,6"] = new("iPhone X", Family.X11),
        ["iPhone11,2"] = new("iPhone XS", Family.X11),
        ["iPhone11,4"] = new("iPhone XS Max", Family.X11),
        ["iPhone11,6"] = new("iPhone XS Max", Family.X11),
        ["iPhone11,8"] = new("iPhone XR", Family.X11),
        ["iPhone12,1"] = new("iPhone 11", Family.X11),
        ["iPhone12,3"] = new("iPhone 11 Pro", Family.X11),
        ["iPhone12,5"] = new("iPhone 11 Pro Max", Family.X11),
        ["iPhone12,8"] = new("iPhone SE (2nd gen)", Family.Early),
        ["iPhone13,1"] = new("iPhone 12 mini", Family.Later),
        ["iPhone13,2"] = new("iPhone 12", Family.Later),
        ["iPhone13,3"] = new("iPhone 12 Pro", Family.Later),
        ["iPhone13,4"] = new("iPhone 12 Pro Max", Family.Later),
        ["iPhone14,4"] = new("iPhone 13 mini", Family.Later),
        ["iPhone14,5"] = new("iPhone 13", Family.Later),
        ["iPhone14,2"] = new("iPhone 13 Pro", Family.Later),
        ["iPhone14,3"] = new("iPhone 13 Pro Max", Family.Later),
        ["iPhone14,6"] = new("iPhone SE (3rd gen)", Family.Early),
        ["iPhone14,7"] = new("iPhone 14", Family.Later),
        ["iPhone14,8"] = new("iPhone 14 Plus", Family.Later),
        ["iPhone15,2"] = new("iPhone 14 Pro", Family.Later),
        ["iPhone15,3"] = new("iPhone 14 Pro Max", Family.Later),
        ["iPhone15,4"] = new("iPhone 15", Family.Later),
        ["iPhone15,5"] = new("iPhone 15 Plus", Family.Later),
        ["iPhone16,1"] = new("iPhone 15 Pro", Family.Later),
        ["iPhone16,2"] = new("iPhone 15 Pro Max", Family.Later),
        ["iPhone17,3"] = new("iPhone 16", Family.Later),
        ["iPhone17,4"] = new("iPhone 16 Plus", Family.Later),
        ["iPhone17,1"] = new("iPhone 16 Pro", Family.Later),
        ["iPhone17,2"] = new("iPhone 16 Pro Max", Family.Later),
        ["iPhone17,5"] = new("iPhone 16e", Family.Later),
        ["iPhone18,3"] = new("iPhone 17", Family.Later),
        ["iPhone18,1"] = new("iPhone 17 Pro", Family.Later),
        ["iPhone18,2"] = new("iPhone 17 Pro Max", Family.Later),
        ["iPhone18,4"] = new("iPhone Air", Family.Later),
    };

    /// <summary>رمز البوردة (يظهر في مسارات SMC مثل target/d94/target.cpp) ← رمز الموديل</summary>
    static readonly Dictionary<string, string> Boards = new(StringComparer.OrdinalIgnoreCase)
    {
        ["d22"] = "iPhone10,3", ["d221"] = "iPhone10,6",
        ["d321"] = "iPhone11,2", ["d331"] = "iPhone11,4", ["d331p"] = "iPhone11,6", ["n841"] = "iPhone11,8",
        ["n104"] = "iPhone12,1", ["d421"] = "iPhone12,3", ["d431"] = "iPhone12,5", ["d79"] = "iPhone12,8",
        ["d52g"] = "iPhone13,1", ["d53g"] = "iPhone13,2", ["d53p"] = "iPhone13,3", ["d54p"] = "iPhone13,4",
        ["d16"] = "iPhone14,4", ["d17"] = "iPhone14,5", ["d63"] = "iPhone14,2", ["d64"] = "iPhone14,3", ["d49"] = "iPhone14,6",
        ["d27"] = "iPhone14,7", ["d28"] = "iPhone14,8", ["d73"] = "iPhone15,2", ["d74"] = "iPhone15,3",
        ["d37"] = "iPhone15,4", ["d38"] = "iPhone15,5", ["d83"] = "iPhone16,1", ["d84"] = "iPhone16,2",
        ["d47"] = "iPhone17,3", ["d48"] = "iPhone17,4", ["d93"] = "iPhone17,1", ["d94"] = "iPhone17,2",
    };

    static readonly Dictionary<string, string> Socs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["T8010"] = "A10 Fusion", ["T8015"] = "A11 Bionic", ["T8020"] = "A12 Bionic", ["T8030"] = "A13 Bionic",
        ["T8101"] = "A14 Bionic", ["T8110"] = "A15 Bionic", ["T8120"] = "A16 Bionic", ["T8130"] = "A17 Pro", ["T8140"] = "A18 / A18 Pro",
    };

    static readonly Regex BoardInText = new(@"target[\\/]+([dn]\d{2,3}[a-z]?)[\\/]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static Model Find(string product) =>
        !string.IsNullOrEmpty(product) && Products.TryGetValue(product.Trim(), out var m) ? m : null;

    /// <summary>اسم الجهاز: من رمز الموديل، وإلا من رمز البوردة في نص البانك — وإلا الرمز كما هو</summary>
    public static string Name(string product, string panicText = null) =>
        Find(product)?.Name ?? (BoardProduct(panicText) is { } p ? Find(p).Name : product ?? "");

    public static Family FamilyOf(string product, string panicText = null) =>
        Find(product)?.Family ?? (BoardProduct(panicText) is { } p ? Find(p).Family : Family.Unknown);

    /// <summary>رمز الموديل المستنتج من رمز البوردة في النص (null إن لم يوجد أو لم يُعرف)</summary>
    public static string BoardProduct(string panicText)
    {
        if (string.IsNullOrEmpty(panicText)) return null;
        var m = BoardInText.Match(panicText);
        return m.Success && Boards.TryGetValue(m.Groups[1].Value, out var p) ? p : null;
    }

    public static string BoardCode(string panicText)
    {
        if (string.IsNullOrEmpty(panicText)) return "";
        var m = BoardInText.Match(panicText);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
    }

    public static string SocName(string code) => !string.IsNullOrEmpty(code) && Socs.TryGetValue(code, out var n) ? n : "";

    public static bool IsKnown(string product) => Find(product) != null;
}
