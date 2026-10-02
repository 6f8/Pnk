using System.Buffers.Binary;
using System.Text;

namespace Kashif.Device;

/// <summary>
/// AFC (Apple File Conduit): قراءة الملفات من خدمة داخل الآيفون — هنا خدمة سجلات الأعطال com.apple.crashreportcopymobile.
/// الحزمة: رأس 40 بايت (CFA6LPAA، الطول الكلي، طول الرأس مع المعاملات، رقم الحزمة، العملية) ثم المعاملات ثم البيانات.
/// قراءة فقط: لا يحذف كاشف ولا يغيّر شيئًا في الجهاز.
/// </summary>
public sealed class Afc
{
    public const ulong OpStatus = 0x1, OpData = 0x2, OpReadDir = 0x3, OpGetFileInfo = 0xA, OpFileOpen = 0xD, OpFileOpenRes = 0xE,
        OpFileRead = 0xF, OpFileClose = 0x14;
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("CFA6LPAA");
    public const int HeaderSize = 40;
    const int Chunk = 64 * 1024;

    readonly Stream s;
    ulong packet;

    public Afc(Stream stream) { s = stream; }

    public static byte[] Packet(ulong op, ulong num, byte[] header, byte[] payload)
    {
        header ??= Array.Empty<byte>();
        payload ??= Array.Empty<byte>();
        var b = new byte[HeaderSize + header.Length + payload.Length];
        Magic.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8), (ulong)b.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16), (ulong)(HeaderSize + header.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(24), num);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(32), op);
        header.CopyTo(b, HeaderSize);
        payload.CopyTo(b, HeaderSize + header.Length);
        return b;
    }

    /// <summary>قراءة حزمة: العملية، المعاملات، البيانات</summary>
    public static (ulong Op, byte[] Header, byte[] Payload) Read(Stream s)
    {
        var h = Io.ReadExact(s, HeaderSize);
        if (!h.AsSpan(0, 8).SequenceEqual(Magic)) throw new DeviceException("رد غير صالح من خدمة سجلات الأعطال");
        ulong entire = BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(8)), thisLen = BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(16));
        ulong op = BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(32));
        if (thisLen < HeaderSize || entire < thisLen || entire > 64UL * 1024 * 1024) throw new DeviceException("رد غير صالح من خدمة سجلات الأعطال");
        var header = Io.ReadExact(s, (int)(thisLen - HeaderSize));
        var payload = Io.ReadExact(s, (int)(entire - thisLen));
        return (op, header, payload);
    }

    (ulong Op, byte[] Header, byte[] Payload) Call(ulong op, byte[] header, byte[] payload = null)
    {
        s.Write(Packet(op, packet++, header, payload));
        s.Flush();
        var r = Read(s);
        if (r.Op == OpStatus)
        {
            var code = r.Header.Length >= 8 ? BinaryPrimitives.ReadUInt64LittleEndian(r.Header) : 0;
            if (code != 0) throw new AfcException(code);
        }
        return r;
    }

    static byte[] PathBytes(string path) => Encoding.UTF8.GetBytes(path + "\0");

    static IEnumerable<string> Strings(byte[] data) =>
        Encoding.UTF8.GetString(data).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    public List<string> ReadDirectory(string path) =>
        Strings(Call(OpReadDir, PathBytes(path)).Payload).Where(n => n != "." && n != "..").ToList();

    public Dictionary<string, string> FileInfo(string path)
    {
        var parts = Strings(Call(OpGetFileInfo, PathBytes(path)).Payload).ToList();
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < parts.Count; i += 2) d[parts[i]] = parts[i + 1];
        return d;
    }

    public byte[] ReadFile(string path, long maxBytes = 32L * 1024 * 1024)
    {
        var open = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(open, 1);   // AFC_FOPEN_RDONLY
        var r = Call(OpFileOpen, open.Concat(PathBytes(path)).ToArray());
        if (r.Op != OpFileOpenRes || r.Header.Length < 8) throw new DeviceException("تعذر فتح الملف في الآيفون: " + path);
        ulong handle = BinaryPrimitives.ReadUInt64LittleEndian(r.Header);
        try
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var req = new byte[16];
                BinaryPrimitives.WriteUInt64LittleEndian(req, handle);
                BinaryPrimitives.WriteUInt64LittleEndian(req.AsSpan(8), Chunk);
                var data = Call(OpFileRead, req).Payload;
                if (data.Length == 0) break;
                ms.Write(data);
                if (ms.Length > maxBytes) throw new DeviceException("الملف أكبر من المسموح: " + path);
            }
            return ms.ToArray();
        }
        finally
        {
            var close = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(close, handle);
            try { Call(OpFileClose, close); } catch { }
        }
    }
}

public sealed class AfcException : Exception
{
    public ulong Code { get; }
    public AfcException(ulong code) : base("خطأ AFC رقم " + code) { Code = code; }
}
