using Jellyfin.Plugin.PlaylistCovers;
using SkiaSharp;

// Renders sample covers from procedural stand-in posters (no Jellyfin needed):
//   dotnet run --project Demo -c Release -- [outDir]
// Writes cover_<style>_<n>.jpg, an overview sheet and a card_preview.jpg that mimics how the web UI shows the cards.
var outDir = args.Length > 0 ? args[0] : "demo_out";
Directory.CreateDirectory(outDir);

SKBitmap FakePoster(int i)
{
    string[] names = { "NACHTZUG", "SILBERFLUSS", "DER LETZTE SOMMER", "KOMET", "EISENHERZ", "STADT AUS GLAS", "WÜSTENFUCHS", "ROTE LATERNE", "TIEFSEE", "DAS ECHO", "FRÜHLINGSSTURM", "MONDSCHEIN" };
    SKColor[][] palettes =
    {
        new SKColor[] { new(18, 30, 70), new(230, 120, 60) }, new SKColor[] { new(10, 60, 80), new(200, 230, 220) },
        new SKColor[] { new(240, 170, 60), new(160, 50, 40) }, new SKColor[] { new(20, 10, 50), new(120, 70, 200) },
        new SKColor[] { new(120, 20, 25), new(30, 10, 15) }, new SKColor[] { new(40, 90, 120), new(190, 220, 235) },
        new SKColor[] { new(210, 140, 70), new(110, 50, 30) }, new SKColor[] { new(150, 20, 40), new(250, 150, 70) },
        new SKColor[] { new(5, 30, 60), new(20, 130, 150) }, new SKColor[] { new(60, 60, 70), new(190, 190, 200) },
        new SKColor[] { new(70, 130, 70), new(230, 220, 140) }, new SKColor[] { new(15, 20, 50), new(210, 210, 235) },
    };
    var rnd = new Random(100 + i);
    var pal = palettes[i % palettes.Length];
    var bmp = new SKBitmap(600, 900);
    using var c = new SKCanvas(bmp);
    using (var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 900), new[] { pal[0], pal[1] }, SKShaderTileMode.Clamp))
    using (var p = new SKPaint { Shader = sky }) { c.DrawRect(0, 0, 600, 900, p); }
    using (var sun = new SKPaint { Color = new SKColor(255, 245, 220, 200), IsAntialias = true, ImageFilter = SKImageFilter.CreateBlur(3, 3) })
    { c.DrawCircle(rnd.Next(150, 450), rnd.Next(220, 420), rnd.Next(55, 100), sun); }
    for (var layer = 0; layer < 3; layer++)
    {
        using var path = new SKPath();
        var baseY = 520 + (layer * 90);
        path.MoveTo(0, 900); path.LineTo(0, baseY);
        for (var x = 0; x <= 600; x += 60) { path.LineTo(x, baseY - rnd.Next(20, 120)); }
        path.LineTo(600, 900); path.Close();
        var shade = (byte)(60 - (layer * 20));
        using var m = new SKPaint { Color = new SKColor(shade, shade, (byte)(shade + 10), 230), IsAntialias = true };
        c.DrawPath(path, m);
    }
    using (var fig = new SKPaint { Color = new SKColor(8, 8, 12), IsAntialias = true })
    {
        var fx = rnd.Next(180, 420);
        c.DrawOval(fx, 600, 14, 14, fig);
        c.DrawRoundRect(new SKRect(fx - 16, 614, fx + 16, 690), 8, 8, fig);
    }
    using var face = SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold);
    using var f = new SKFont(face, names[i % names.Length].Length > 11 ? 40 : 54);
    using var t = new SKPaint { Color = new SKColor(255, 255, 255, 235), IsAntialias = true };
    var title = names[i % names.Length];
    c.DrawText(title, 300 - (f.MeasureText(title) / 2), 800, SKTextAlign.Left, f, t);
    using var bill = new SKPaint { Color = new SKColor(255, 255, 255, 90) };
    c.DrawRect(120, 835, 360, 5, bill);
    return bmp;
}

var samples = new[] { (Label: "3 Titel", Count: 3), (Label: "5 Titel", Count: 5), (Label: "12 Titel", Count: 12) };
var styles = new[] { "Hero", "Mosaic", "Wall" };
var labels = new Dictionary<string, string> { ["Hero"] = "Held + Stapel", ["Mosaic"] = "Mosaik", ["Wall"] = "Poster-Wand" };

// --- overview sheet: every style for every poster count ---
const int cellW = 400, cellH = 600, pad = 24, labelH = 44;
using var sheet = new SKBitmap(pad + (styles.Length * (cellW + pad)), labelH + pad + (samples.Length * (cellH + pad + 28)));
using var sc = new SKCanvas(sheet);
sc.Clear(new SKColor(24, 24, 28));
using var lf = new SKFont(SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold), 22);
using var lp = new SKPaint { Color = new SKColor(230, 230, 235), IsAntialias = true };
for (var si = 0; si < styles.Length; si++) { sc.DrawText(labels[styles[si]], pad + (si * (cellW + pad)), 36, SKTextAlign.Left, lf, lp); }

var covers = new Dictionary<(string, int), SKBitmap>();
for (var ri = 0; ri < samples.Length; ri++)
{
    var posters = Enumerable.Range(0, samples[ri].Count).Select(FakePoster).ToList();
    for (var si = 0; si < styles.Length; si++)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var jpeg = CoverRenderer.RenderJpeg(posters, styles[si]);
        File.WriteAllBytes(Path.Combine(outDir, $"cover_{styles[si].ToLowerInvariant()}_{samples[ri].Count}.jpg"), jpeg);
        var cover = SKBitmap.Decode(jpeg);
        covers[(styles[si], samples[ri].Count)] = cover;
        using var img = SKImage.FromBitmap(cover);
        var top = labelH + pad + (ri * (cellH + pad + 28));
        sc.DrawImage(img, new SKRect(pad + (si * (cellW + pad)), top, pad + (si * (cellW + pad)) + cellW, top + cellH), new SKSamplingOptions(SKCubicResampler.Mitchell));
        Console.WriteLine($"{styles[si],-7} {samples[ri].Count,2} posters -> {CoverRenderer.ResolveStyle(samples[ri].Count, styles[si]),-6} {sw.ElapsedMilliseconds} ms");
    }

    sc.DrawText(samples[ri].Label, pad, labelH + pad + (ri * (cellH + pad + 28)) + cellH + 22, SKTextAlign.Left, lf, lp);
}

using (var si = SKImage.FromBitmap(sheet)) { File.WriteAllBytes(Path.Combine(outDir, "styles_overview.jpg"), si.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray()); }

// --- card preview: the Auto style at the size Jellyfin's web UI shows it (≈300×450), with its overlays ---
const int cw = 300, ch = 450, cgap = 40;
var auto = new[] { 3, 5, 12 };
using var card = new SKBitmap((auto.Length * (cw + cgap)) + cgap, ch + 120);
using var cc = new SKCanvas(card);
cc.Clear(new SKColor(22, 27, 40));
using var small = new SKFont(SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Normal), 20);
using var white = new SKPaint { Color = new SKColor(210, 215, 225), IsAntialias = true };
for (var k = 0; k < auto.Length; k++)
{
    var x = cgap + (k * (cw + cgap));
    var rect = new SKRect(x, 40, x + cw, 40 + ch);
    var cover = covers[("Wall", auto[k])];
    var resolved = CoverRenderer.ResolveStyle(auto[k], "Auto");
    cover = covers[(resolved, auto[k])];
    using var img = SKImage.FromBitmap(cover);
    cc.Save();
    cc.ClipRoundRect(new SKRoundRect(rect, 24), SKClipOperation.Intersect, true);
    cc.DrawImage(img, rect, new SKSamplingOptions(SKCubicResampler.Mitchell));
    using (var bar = new SKPaint { Color = new SKColor(20, 40, 55) }) { cc.DrawRect(x, rect.Bottom - 9, cw, 9, bar); }
    using (var done = new SKPaint { Color = new SKColor(60, 150, 210) }) { cc.DrawRect(x, rect.Bottom - 9, 100, 9, done); }
    cc.Restore();
    using (var badge = new SKPaint { Color = new SKColor(40, 80, 190), IsAntialias = true }) { cc.DrawCircle(x + cw - 46, 40 + 44, 27, badge); }
    using (var bf = new SKFont(SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold), 22)) { cc.DrawText(auto[k].ToString(), x + cw - 46, 40 + 52, SKTextAlign.Center, bf, new SKPaint { Color = SKColors.White, IsAntialias = true }); }
    cc.DrawText("Playlist", x + (cw / 2f), 40 + ch + 36, SKTextAlign.Center, small, white);
}

using (var si = SKImage.FromBitmap(card)) { File.WriteAllBytes(Path.Combine(outDir, "card_preview.jpg"), si.Encode(SKEncodedImageFormat.Jpeg, 92).ToArray()); }
Console.WriteLine("done");
