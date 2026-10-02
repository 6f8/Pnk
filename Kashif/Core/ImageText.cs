using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Kashif;

/// <summary>
/// قراءة نص سجل البانك من صورة (لقطة شاشة يرسلها الزبون) بقارئ النصوص المدمج في ويندوز 10 و 11 (Windows.Media.Ocr).
/// السجل بالإنجليزية فيُفضَّل قارئ اللغة الإنجليزية؛ أخطاء القراءة الشائعة (O/0، l/1 ...) يصححها PanicParser بعد ذلك.
/// </summary>
public static class ImageText
{
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic" };

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

    /// <summary>رسالة عندما لا يتوفر قارئ نصوص في ويندوز</summary>
    public const string NoEngine = "قارئ النصوص في ويندوز غير متوفر لأي لغة مثبتة.\n" +
        "أضف اللغة الإنجليزية: الإعدادات ← الوقت واللغة ← اللغة والمنطقة ← إضافة لغة ← English (United States)، ثم أعد المحاولة.";

    static OcrEngine Engine()
    {
        foreach (var tag in new[] { "en-US", "en-GB", "en" })
        {
            try
            {
                var lang = new Windows.Globalization.Language(tag);
                if (OcrEngine.IsLanguageSupported(lang) && OcrEngine.TryCreateFromLanguage(lang) is { } e) return e;
            }
            catch { }
        }
        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    /// <summary>نص الصورة سطرًا سطرًا، أو null إن لم يتوفر قارئ نصوص</summary>
    public static async Task<string> ReadAsync(string path) => await ReadAsync(File.ReadAllBytes(path));

    public static async Task<string> ReadAsync(byte[] bytes)
    {
        var engine = Engine();
        if (engine == null) return null;
        using var src = await Decode(bytes);
        var sb = new System.Text.StringBuilder();
        // اللقطات الطويلة تُقسَّم شرائح بارتفاع لا يتجاوز حد القارئ، والصور الصغيرة تُكبَّر لتحسين القراءة
        uint max = OcrEngine.MaxImageDimension;
        double scale = src.PixelWidth < 1400 ? Math.Min(2.0, (double)max / src.PixelWidth) : 1.0;
        if (src.PixelWidth * scale > max) scale = (double)max / src.PixelWidth;
        int sliceSrc = (int)Math.Max(200, (max / scale) - 200);
        for (int top = 0; top < src.PixelHeight; top += sliceSrc - 60)
        {
            int h = Math.Min(sliceSrc, src.PixelHeight - top);
            using var slice = await Crop(bytes, (uint)top, (uint)h, scale, (uint)src.PixelWidth);
            var result = await engine.RecognizeAsync(slice);
            foreach (var line in result.Lines) sb.AppendLine(line.Text);
            if (top + h >= src.PixelHeight) break;
        }
        return sb.ToString();
    }

    static async Task<IRandomAccessStream> Stream(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var w = new DataWriter(stream))
        {
            w.WriteBytes(bytes);
            await w.StoreAsync();
            w.DetachStream();
        }
        stream.Seek(0);
        return stream;
    }

    static async Task<SoftwareBitmap> Decode(byte[] bytes)
    {
        using var stream = await Stream(bytes);
        var dec = await BitmapDecoder.CreateAsync(stream);
        return await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>شريحة من الصورة (من top بارتفاع h) مكبَّرة بالنسبة scale</summary>
    static async Task<SoftwareBitmap> Crop(byte[] bytes, uint top, uint h, double scale, uint width)
    {
        using var stream = await Stream(bytes);
        var dec = await BitmapDecoder.CreateAsync(stream);
        var t = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Round(dec.PixelWidth * scale),
            ScaledHeight = (uint)Math.Round(dec.PixelHeight * scale),
            InterpolationMode = BitmapInterpolationMode.Fant,
            Bounds = new BitmapBounds
            {
                X = 0, Y = (uint)Math.Round(top * scale),
                Width = (uint)Math.Round(width * scale), Height = (uint)Math.Round(h * scale),
            },
        };
        // الحدود يجب أن تبقى داخل الصورة بعد التكبير
        var b = t.Bounds;
        if (b.Y + b.Height > t.ScaledHeight) b.Height = t.ScaledHeight - b.Y;
        if (b.Width > t.ScaledWidth) b.Width = t.ScaledWidth;
        t.Bounds = b;
        return await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, t,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
    }
}
