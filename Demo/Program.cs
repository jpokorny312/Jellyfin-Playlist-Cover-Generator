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
    var bmp = new SKBitmap(600, 220);
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
