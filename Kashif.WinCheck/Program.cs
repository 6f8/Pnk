using System.Drawing;
using System.Drawing.Imaging;
using Kashif;

// قارئ النصوص في ويندوز على لقطة شاشة مرسومة من سجل حقيقي (iPhone 11، Prs0): هل يصل كاشف لنفس النتيجة؟
// النتيجة: 0 نجح، 1 فشل، 0 مع SKIP إن لم يتوفر قارئ نصوص في نظام التشغيل.
var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "prs0_iphone11_ocr.txt"));
var lines = new List<string>();
foreach (var raw in text.Replace("\\n", "\n").Split('\n'))
{
    var l = raw.TrimEnd();
    while (l.Length > 90) { lines.Add(l[..90]); l = l[90..]; }
    lines.Add(l);
}
// خط قريب من خط الآيفون (صفر بلا نقطة) — قارئ ويندوز يقرأ صفر Consolas المنقّط حرف e
using var font = new Font("Segoe UI", 15f);
int lineH = 26, w = 1300, h = lines.Count * lineH + 40;
using var bmp = new Bitmap(w, h);
using (var g = Graphics.FromImage(bmp))
{
    g.Clear(Color.White);
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    for (int i = 0; i < lines.Count; i++) g.DrawString(lines[i], font, Brushes.Black, 20, 20 + i * lineH);
}
var png = Path.Combine(AppContext.BaseDirectory, "ocr-sample.png");
bmp.Save(png, ImageFormat.Png);

var ocr = await ImageText.ReadAsync(png);
if (ocr == null) { Console.WriteLine("SKIP: no OCR engine on this Windows"); return 0; }
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ocr-result.txt"), ocr);
Console.WriteLine($"OCR read {ocr.Length} chars, {ocr.Split('\n').Length} lines");
Console.WriteLine("----- OCR text -----");
Console.WriteLine(ocr);
Console.WriteLine("--------------------");

var logs = PanicParser.ParseMany(ocr, "ocr-sample.png");
bool ok = logs.Count == 1;
var d = ok ? PanicAnalyzer.Analyze(logs[0]) : null;
ok = ok && d.Device == "iPhone 11" && d.MissingSensors.Contains("Prs0") && d.TopPart == Parts.ChargingFlex;
Console.WriteLine(ok ? $"OK: {d.Device}, sensors={string.Join(",", d.MissingSensors)}, top={d.TopPart}"
                     : $"FAIL: logs={logs.Count} device={d?.Device} sensors={string.Join(",", d?.MissingSensors ?? new())} top={d?.TopPart}");
return ok ? 0 : 1;
