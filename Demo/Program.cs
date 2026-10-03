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
    var jpeg = CoverRenderer.RenderPosterJpeg(s.Name, s.Posters, s.Bd, s.Logos.Select(Logo).ToList(), s.Movies, s.Series, font);
    var file = Path.Combine(outDir, s.File + ".jpg");
    File.WriteAllBytes(file, jpeg);
    Console.WriteLine($"{file}  {sw.ElapsedMilliseconds} ms");
}
