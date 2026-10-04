using Jellyfin.Plugin.PlaylistCovers;
using SkiaSharp;

// Renders sample covers from synthetic images (no Jellyfin needed): dotnet run --project Demo [outDir] [fontPath]
var outDir = args.Length > 0 ? args[0] : "demo_out";
var font = args.Length > 1 ? args[1] : null;
Directory.CreateDirectory(outDir);

SKBitmap Backdrop(SKColor a, SKColor b, int seed)
{
    var rnd = new Random(seed);
    var bmp = new SKBitmap(1280, 720);
    using var c = new SKCanvas(bmp);
    using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1280, 720), new[] { a, b }, SKShaderTileMode.Clamp);
    using var p = new SKPaint { Shader = shader };
    c.DrawRect(0, 0, 1280, 720, p);
    for (var i = 0; i < 6; i++)
    {
        using var blob = new SKPaint { Color = new SKColor((byte)rnd.Next(30, 220), (byte)rnd.Next(30, 220), (byte)rnd.Next(30, 220)), ImageFilter = SKImageFilter.CreateBlur(30, 30) };
        c.DrawCircle(rnd.Next(1280), rnd.Next(720), rnd.Next(80, 260), blob);
    }
    return bmp;
}

SKBitmap Logo(string text)
{
    var bmp = new SKBitmap(1100, 220);
    using var c = new SKCanvas(bmp);
    c.Clear(SKColors.Transparent);
    using var f = new SKFont(SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold), 90);
    using var p = new SKPaint { Color = SKColors.White, IsAntialias = true };
    c.DrawText(text, 20, 130, SKTextAlign.Left, f, p);
    return bmp;
}

var samples = new (string Name, SKBitmap[] Bd, string[] Logos, int Movies, int Series)[]
{
    ("Marvel Filmreihe", new[] { Backdrop(new(200, 30, 30), new(20, 20, 80), 1), Backdrop(new(230, 160, 30), new(120, 20, 20), 2), Backdrop(new(20, 40, 120), new(200, 40, 60), 3) }, new[] { "IRON MAN", "THOR", "AVENGERS" }, 12, 0),
    ("Weihnachtsklassiker", new[] { Backdrop(new(20, 110, 60), new(180, 30, 30), 4) }, new[] { "GRINCH", "HOME ALONE" }, 7, 0),
    ("Krimi Serien Abend", new[] { Backdrop(new(20, 40, 70), new(10, 10, 20), 5), Backdrop(new(60, 70, 90), new(15, 25, 40), 6) }, new[] { "BROADCHURCH", "TRUE DETECTIVE", "LUTHER" }, 1, 5),
    ("Sonntag Familienfilme mit der ganzen Bande", new[] { Backdrop(new(240, 170, 40), new(200, 90, 40), 7), Backdrop(new(40, 150, 220), new(20, 60, 140), 8) }, Array.Empty<string>(), 9, 1),
};

foreach (var s in samples)
{
    var logos = s.Logos.Select(Logo).ToList();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var jpeg = CoverRenderer.RenderJpeg(s.Name, s.Bd, logos, s.Movies, s.Series, font);
    var file = Path.Combine(outDir, s.Name.Replace(' ', '_') + ".jpg");
    File.WriteAllBytes(file, jpeg);

    using var full = SKBitmap.Decode(jpeg);
    using var sq = new SKBitmap();
    full.ExtractSubset(sq, new SKRectI((full.Width - full.Height) / 2, 0, (full.Width + full.Height) / 2, full.Height));
    using var img = SKImage.FromBitmap(sq);
    File.WriteAllBytes(file.Replace(".jpg", "_square.jpg"), img.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray());
    Console.WriteLine($"{file}  {sw.ElapsedMilliseconds} ms");
}

// ---- portrait 1:2 poster layout ----
SKBitmap FakePoster(string name, SKColor top, SKColor bottom, int seed)
{
    var rnd = new Random(seed);
    var bmp = new SKBitmap(600, 900);
    using var c = new SKCanvas(bmp);
    using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 900), new[] { top, bottom }, SKShaderTileMode.Clamp);
    using (var p = new SKPaint { Shader = shader }) { c.DrawRect(0, 0, 600, 900, p); }
    using (var glow = new SKPaint { Color = new SKColor(255, 255, 255, 70), ImageFilter = SKImageFilter.CreateBlur(40, 40) })
    {
        c.DrawCircle(rnd.Next(150, 450), rnd.Next(250, 450), rnd.Next(90, 170), glow);
    }
    using var f = new SKFont(SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold), 64);
    using var t = new SKPaint { Color = SKColors.White, IsAntialias = true };
    var y = 700;
    foreach (var word in name.Split(' '))
    {
        c.DrawText(word, 40, y, SKTextAlign.Left, f, t);
        y += 70;
    }
    return bmp;
}

var posterSets = new (string Name, string File, SKBitmap[] Posters, SKBitmap[] Bd, string[] Logos, int Movies, int Series)[]
{
    ("Marvel Filmreihe", "poster_marvel", new[]
    {
        FakePoster("IRON MAN", new(180, 20, 30), new(40, 10, 10), 11), FakePoster("THOR", new(30, 60, 150), new(10, 15, 50), 12),
        FakePoster("AVENGERS", new(220, 150, 30), new(90, 30, 20), 13), FakePoster("BLACK PANTHER", new(60, 30, 110), new(15, 10, 35), 14),
        FakePoster("DOCTOR STRANGE", new(150, 40, 120), new(40, 10, 40), 15),
    }, new[] { Backdrop(new(200, 30, 30), new(20, 20, 80), 1) }, new[] { "IRON MAN", "THOR", "AVENGERS" }, 12, 0),
    ("Krimi Serien Abend", "poster_krimi", new[]
    {
        FakePoster("LUTHER", new(40, 60, 80), new(10, 15, 25), 21), FakePoster("BROADCHURCH", new(70, 110, 120), new(15, 30, 35), 22),
        FakePoster("TRUE DETECTIVE", new(110, 80, 40), new(30, 20, 10), 23),
    }, Array.Empty<SKBitmap>(), new[] { "LUTHER", "BROADCHURCH" }, 1, 3),
    ("Sonntag Familienfilme mit der ganzen Bande", "poster_familie", new[]
    {
        FakePoster("COCO", new(240, 140, 30), new(120, 40, 20), 31), FakePoster("UP", new(50, 150, 220), new(20, 60, 130), 32),
    }, Array.Empty<SKBitmap>(), Array.Empty<string>(), 2, 0),
};

foreach (var s in posterSets)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var jpeg = CoverRenderer.RenderPosterJpeg(s.Name, s.Posters, s.Movies, s.Series, font);
    var file = Path.Combine(outDir, s.File + ".jpg");
    File.WriteAllBytes(file, jpeg);
    Console.WriteLine($"{file}  {sw.ElapsedMilliseconds} ms");
}

// ---- text-free styles: wall / mosaic / hero ----
// Procedural stand-ins for real posters: sky gradient, sun/moon, mountains, a figure, title lettering.
SKBitmap FancyPoster(int i)
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

var counts = new[] { ("viele Titel (12)", 12), ("mittel (5)", 5), ("wenige (2)", 2) };
var styles = new[] { "wall", "mosaic", "hero" };
const int cellW = 400, cellH = 600, pad = 24, labelH = 44;
using var sheet = new SKBitmap(pad + (styles.Length * (cellW + pad)), labelH + pad + (counts.Length * (cellH + pad)) + pad);
using (var sc = new SKCanvas(sheet))
{
    sc.Clear(new SKColor(24, 24, 28));
    using var lf = new SKFont(SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold), 22);
    using var lp = new SKPaint { Color = new SKColor(230, 230, 235), IsAntialias = true };
    for (var si = 0; si < styles.Length; si++)
    {
        sc.DrawText(new[] { "A · Poster-Wand", "B · Mosaik", "C · Held + Stapel" }[si], pad + (si * (cellW + pad)), 36, SKTextAlign.Left, lf, lp);
    }

    for (var ri = 0; ri < counts.Length; ri++)
    {
        var posters = Enumerable.Range(0, counts[ri].Item2).Select(FancyPoster).ToList();
        for (var si = 0; si < styles.Length; si++)
        {
            var jpeg = CoverRenderer.RenderStyledJpeg(posters, styles[si]);
            File.WriteAllBytes(Path.Combine(outDir, $"style_{styles[si]}_{counts[ri].Item2}.jpg"), jpeg);
            using var cover = SKBitmap.Decode(jpeg);
            using var img = SKImage.FromBitmap(cover);
            sc.DrawImage(img, new SKRect(pad + (si * (cellW + pad)), labelH + pad + (ri * (cellH + pad)), pad + (si * (cellW + pad)) + cellW, labelH + pad + (ri * (cellH + pad)) + cellH), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        sc.DrawText(counts[ri].Item1, pad, labelH + pad + (ri * (cellH + pad)) + cellH + 18, SKTextAlign.Left, lf, lp);
    }
}

using (var si = SKImage.FromBitmap(sheet))
{
    File.WriteAllBytes(Path.Combine(outDir, "styles_overview.jpg"), si.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray());
}

Console.WriteLine("styles_overview.jpg written");
