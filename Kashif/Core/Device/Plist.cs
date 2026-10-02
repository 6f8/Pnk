using System.Globalization;
using System.Security;
using System.Text;
using System.Xml;

namespace Kashif.Device;

/// <summary>
/// قوائم الخصائص (plist) التي يتخاطب بها الآيفون: كتابة XML، وقراءة XML والصيغة الثنائية (bplist00).
/// الأنواع: Dictionary&lt;string,object&gt;، List&lt;object&gt;، string، long، double، bool، byte[]، DateTime.
/// </summary>
public static class Plist
{
    // ------------------------------------------------------------------ كتابة XML
    public static byte[] ToXml(object root)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
        sb.Append("<plist version=\"1.0\">\n");
        Write(sb, root);
        sb.Append("</plist>\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    static void Write(StringBuilder sb, object v)
    {
        switch (v)
        {
            case null: sb.Append("<string></string>"); break;
            case string s: sb.Append("<string>").Append(SecurityElement.Escape(s)).Append("</string>"); break;
            case bool b: sb.Append(b ? "<true/>" : "<false/>"); break;
            case int or long or uint or ushort or short or byte:
                sb.Append("<integer>").Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)).Append("</integer>"); break;
            case ulong u: sb.Append("<integer>").Append(u.ToString(CultureInfo.InvariantCulture)).Append("</integer>"); break;
            case double d: sb.Append("<real>").Append(d.ToString("R", CultureInfo.InvariantCulture)).Append("</real>"); break;
            case byte[] data: sb.Append("<data>").Append(Convert.ToBase64String(data)).Append("</data>"); break;
            case DateTime t: sb.Append("<date>").Append(t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append("</date>"); break;
            case IDictionary<string, object> dict:
                sb.Append("<dict>");
                foreach (var kv in dict) { sb.Append("<key>").Append(SecurityElement.Escape(kv.Key)).Append("</key>"); Write(sb, kv.Value); }
                sb.Append("</dict>");
                break;
            case System.Collections.IEnumerable list:
                sb.Append("<array>");
                foreach (var x in list) Write(sb, x);
                sb.Append("</array>");
                break;
            default: throw new ArgumentException("نوع غير مدعوم في plist: " + v.GetType().Name);
        }
    }

    // ------------------------------------------------------------------ قراءة (XML أو ثنائي)
    public static object Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) throw new FormatException("plist فارغ");
        if (bytes.Length >= 8 && Encoding.ASCII.GetString(bytes, 0, 6) == "bplist") return Binary.Parse(bytes);
        return ParseXml(bytes);
    }

    public static Dictionary<string, object> ParseDict(byte[] bytes) =>
        Parse(bytes) as Dictionary<string, object> ?? throw new FormatException("plist ليس قاموسًا");

    static object ParseXml(byte[] bytes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true };
        using var r = XmlReader.Create(new MemoryStream(bytes), settings);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.Name == "plist")
            {
                if (r.IsEmptyElement) return null;
                r.Read();
                return ReadValue(r);
            }
        throw new FormatException("لا يوجد عنصر plist");
    }

    static object ReadValue(XmlReader r)
    {
        while (r.NodeType != XmlNodeType.Element) if (!r.Read()) throw new FormatException("plist ناقص");
        string name = r.Name;
        bool empty = r.IsEmptyElement;
        switch (name)
        {
            case "true": r.Read(); return true;
            case "false": r.Read(); return false;
            case "string": return empty ? Skip(r, "") : r.ReadElementContentAsString();
            case "integer":
                var it = r.ReadElementContentAsString().Trim();
                return it.StartsWith('-') ? long.Parse(it, CultureInfo.InvariantCulture) : (object)(long)ulong.Parse(it, CultureInfo.InvariantCulture);
            case "real": return double.Parse(r.ReadElementContentAsString().Trim(), CultureInfo.InvariantCulture);
            case "date": return DateTime.Parse(r.ReadElementContentAsString().Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            case "data":
                if (empty) return Skip(r, Array.Empty<byte>());
                var b64 = new string(r.ReadElementContentAsString().Where(c => !char.IsWhiteSpace(c)).ToArray());
                return Convert.FromBase64String(b64);
            case "array":
            {
                var list = new List<object>();
                if (empty) { r.Read(); return list; }
                r.Read();
                while (r.NodeType != XmlNodeType.EndElement) list.Add(ReadValue(r));
                r.Read();
                return list;
            }
            case "dict":
            {
                var dict = new Dictionary<string, object>();
                if (empty) { r.Read(); return dict; }
                r.Read();
                while (r.NodeType != XmlNodeType.EndElement)
                {
                    if (r.NodeType != XmlNodeType.Element || r.Name != "key") throw new FormatException("مفتاح متوقع في dict");
                    var key = r.ReadElementContentAsString();
                    dict[key] = ReadValue(r);
                }
                r.Read();
                return dict;
            }
            default: throw new FormatException("عنصر plist غير معروف: " + name);
        }
    }

    static T Skip<T>(XmlReader r, T value) { r.Read(); return value; }

    // ------------------------------------------------------------------ الصيغة الثنائية bplist00
    static class Binary
    {
        public static object Parse(byte[] b)
        {
            if (b.Length < 40) throw new FormatException("bplist قصير");
            int t = b.Length - 32;
            int offSize = b[t + 6], refSize = b[t + 7];
            long count = BE(b, t + 8, 8), top = BE(b, t + 16, 8), table = BE(b, t + 24, 8);
            if (offSize < 1 || offSize > 8 || refSize < 1 || refSize > 8 || count <= 0 || top >= count || table + count * offSize > t)
                throw new FormatException("bplist تالف");
            var offsets = new long[count];
            for (long i = 0; i < count; i++) offsets[i] = BE(b, (int)(table + i * offSize), offSize);
            return Obj(b, offsets, refSize, top, 0);
        }

        static object Obj(byte[] b, long[] offsets, int refSize, long index, int depth)
        {
            if (depth > 64 || index < 0 || index >= offsets.Length) throw new FormatException("bplist تالف");
            int p = (int)offsets[index];
            byte m = b[p];
            int hi = m >> 4, lo = m & 0xF;
            switch (hi)
            {
                case 0x0: return lo == 0x9 ? true : lo == 0x8 ? false : null;
                case 0x1: { int n = 1 << lo; return n == 16 ? BE(b, p + 9, 8) : BE(b, p + 1, n); }
                case 0x2: return lo == 2 ? BitConverter.ToSingle(Rev(b, p + 1, 4), 0) : BitConverter.ToDouble(Rev(b, p + 1, 8), 0);
                case 0x3: return new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(BitConverter.ToDouble(Rev(b, p + 1, 8), 0));
                case 0x4: { var (len, at) = Len(b, p, lo); return b.AsSpan(at, (int)len).ToArray(); }
                case 0x5: { var (len, at) = Len(b, p, lo); return Encoding.ASCII.GetString(b, at, (int)len); }
                case 0x6: { var (len, at) = Len(b, p, lo); return Encoding.BigEndianUnicode.GetString(b, at, (int)len * 2); }
                case 0x8: return BE(b, p + 1, lo + 1);
                case 0xA:
                {
                    var (len, at) = Len(b, p, lo);
                    var list = new List<object>((int)len);
                    for (int i = 0; i < len; i++) list.Add(Obj(b, offsets, refSize, BE(b, at + i * refSize, refSize), depth + 1));
                    return list;
                }
                case 0xD:
                {
                    var (len, at) = Len(b, p, lo);
                    var dict = new Dictionary<string, object>();
                    for (int i = 0; i < len; i++)
                    {
                        var key = Obj(b, offsets, refSize, BE(b, at + i * refSize, refSize), depth + 1) as string ?? throw new FormatException("مفتاح bplist ليس نصًا");
                        dict[key] = Obj(b, offsets, refSize, BE(b, at + (int)(len + i) * refSize, refSize), depth + 1);
                    }
                    return dict;
                }
                default: throw new FormatException($"نوع bplist غير مدعوم 0x{m:X2}");
            }
        }

        static (long Len, int At) Len(byte[] b, int p, int lo)
        {
            if (lo != 0xF) return (lo, p + 1);
            int n = 1 << (b[p + 1] & 0xF);
            return (BE(b, p + 2, n), p + 2 + n);
        }

        static byte[] Rev(byte[] b, int p, int n)
        {
            var x = b.AsSpan(p, n).ToArray();
            if (BitConverter.IsLittleEndian) Array.Reverse(x);
            return x;
        }

        static long BE(byte[] b, int p, int n)
        {
            long v = 0;
            for (int i = 0; i < n; i++) v = (v << 8) | b[p + i];
            return v;
        }
    }

    // ------------------------------------------------------------------ مساعدات قراءة القيم
    public static string Str(this Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var v) ? v as string ?? "" : "";
    public static long Long(this Dictionary<string, object> d, string key, long def = 0) =>
        d != null && d.TryGetValue(key, out var v) && v is long l ? l : def;
    public static bool Bool(this Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var v) && v is bool b && b;
    public static byte[] Data(this Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var v) ? v as byte[] : null;
}
