using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.PlaylistCovers;

/// <summary>
/// Renders a streaming-style 16:9 cover. Title and logos sit in the centred square so that
/// clients cropping playlists to 1:1 still show the complete cover.
/// </summary>
public static partial class CoverRenderer
{
    /// <summary>Canvas width.</summary>
    public const int Width = 1920;

    /// <summary>Canvas height (also the side of the centred safe square).</summary>
    public const int Height = 1080;

    private const int Safe = Height;

    /// <summary>Renders the cover and returns it as JPEG.</summary>
    /// <param name="title">Playlist name.</param>
    /// <param name="backdrops">Backdrop images (first three are used).</param>
    /// <param name="logos">Transparent title logos (first four are used).</param>
    /// <param name="movies">Number of movies.</param>
    /// <param name="series">Number of series.</param>
    /// <param name="fontPath">Optional font file.</param>
    /// <param name="upperCase">Render title in upper case.</param>
    /// <returns>JPEG bytes.</returns>
    public static byte[] RenderJpeg(
        string title,
        IReadOnlyList<SKBitmap> backdrops,
        IReadOnlyList<SKBitmap> logos,
        int movies,
        int series,
        string? fontPath = null,
        bool upperCase = true)
    {
        using var bitmap = Render(title, backdrops, logos, movies, series, fontPath, upperCase);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        return data.ToArray();
    }

    /// <summary>Renders the cover as bitmap.</summary>
    /// <param name="title">Playlist name.</param>
    /// <param name="backdrops">Backdrop images.</param>
    /// <param name="logos">Title logos.</param>
    /// <param name="movies">Number of movies.</param>
    /// <param name="series">Number of series.</param>
    /// <param name="fontPath">Optional font file.</param>
    /// <param name="upperCase">Render title in upper case.</param>
    /// <returns>The rendered bitmap (caller disposes).</returns>
    public static SKBitmap Render(
        string title,
        IReadOnlyList<SKBitmap> backdrops,
        IReadOnlyList<SKBitmap> logos,
        int movies,
        int series,
        string? fontPath = null,
        bool upperCase = true)
    {
        var shots = backdrops.Take(3).ToList();
        var accent = DominantColor(shots);

        using var background = Background(shots, Width, Height);
        var result = Grade(background, accent, Width, Height);

        using var canvas = new SKCanvas(result);
        using var typeface = LoadTypeface(fontPath);

        var text = upperCase ? title.ToUpperInvariant() : title;
        var (font, lines) = FitTitle(text, typeface, Safe - 220);
        using (font)
        {
            var lineHeight = font.Size * 1.12f;
            var blockHeight = lineHeight * lines.Count;
            var hasLogos = logos.Count > 0;
            var y = ((Height - blockHeight) / 2f) - (hasLogos ? 70f : 30f);

            using var titlePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                ImageFilter = SKImageFilter.CreateDropShadow(0, 5, 10, 10, new SKColor(0, 0, 0, 170))
            };

            foreach (var line in lines)
            {
                var w = font.MeasureText(line);
                canvas.DrawText(line, (Width - w) / 2f, y - font.Metrics.Ascent, SKTextAlign.Left, font, titlePaint);
                y += lineHeight;
            }

            var barY = y + 18;
            using var barPaint = new SKPaint { IsAntialias = true, Color = accent };
            canvas.DrawRoundRect(new SKRect((Width / 2f) - 70, barY, (Width / 2f) + 70, barY + 7), 4, 4, barPaint);

            var sub = Subline(movies, series);
            if (sub.Length > 0)
            {
                using var subFont = new SKFont(typeface, 38) { Edging = SKFontEdging.SubpixelAntialias };
                using var subPaint = new SKPaint { IsAntialias = true, Color = new SKColor(235, 235, 240, 235) };
                var sw = subFont.MeasureText(sub);
                canvas.DrawText(sub, (Width - sw) / 2f, barY + 34 - subFont.Metrics.Ascent, SKTextAlign.Left, subFont, subPaint);
            }

            if (hasLogos)
            {
                DrawLogoRow(canvas, logos.Take(4).ToList(), Math.Min(Height - 150, barY + 34 + 38 + 150));
            }
        }

        return result;
    }

    /// <summary>Builds the "12 Filme · 3 Serien" line.</summary>
    /// <param name="movies">Movie count.</param>
    /// <param name="series">Series count.</param>
    /// <returns>The subline, empty if both are zero.</returns>
    public static string Subline(int movies, int series)
    {
        var parts = new List<string>();
        if (movies > 0)
        {
            parts.Add($"{movies} {(movies == 1 ? "Film" : "Filme")}");
        }

        if (series > 0)
        {
            parts.Add($"{series} {(series == 1 ? "Serie" : "Serien")}");
        }

        return string.Join("  ·  ", parts);
    }

    internal static SKTypeface LoadTypeface(string? fontPath)
    {
        if (!string.IsNullOrWhiteSpace(fontPath) && File.Exists(fontPath))
        {
            var custom = SKTypeface.FromFile(fontPath);
            if (custom != null)
            {
                return custom;
            }
        }

        foreach (var family in new[] { "Montserrat", "Inter", "DejaVu Sans", "Liberation Sans", "Noto Sans" })
        {
            var tf = SKTypeface.FromFamilyName(family, SKFontStyle.Bold);
            if (tf != null && string.Equals(tf.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                return tf;
            }

            tf?.Dispose();
        }

        return SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
    }

    internal static (SKFont Font, List<string> Lines) FitTitle(string text, SKTypeface typeface, int maxWidth, int maxSize = 150)
    {
        for (var size = maxSize; size >= 58; size -= 6)
        {
            var font = new SKFont(typeface, size) { Edging = SKFontEdging.SubpixelAntialias };
            var lines = Wrap(text, font, maxWidth);
            if (lines.Count <= 3 && lines.All(l => font.MeasureText(l) <= maxWidth))
            {
                return (font, lines);
            }

            font.Dispose();
        }

        var small = new SKFont(typeface, 56) { Edging = SKFontEdging.SubpixelAntialias };
        return (small, Wrap(text, small, maxWidth).Take(3).ToList());
    }

    private static List<string> Wrap(string text, SKFont font, int maxWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trial = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || font.MeasureText(trial) <= maxWidth)
            {
                current = trial;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines.Count > 0 ? lines : new List<string> { string.Empty };
    }

    /// <summary>Scales and crops to fill the canvas (centre x, 40% y – keeps faces in frame).</summary>
    private static SKBitmap CoverFit(SKBitmap src, int dw, int dh, float focusY = 0.4f)
    {
        var dst = new SKBitmap(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Black);
        var scale = Math.Max((float)dw / src.Width, (float)dh / src.Height);
        var w = src.Width * scale;
        var h = src.Height * scale;
        var x = (dw - w) * 0.5f;
        var y = (dh - h) * focusY;
        using var paint = new SKPaint { IsAntialias = true };
        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, new SKRect(x, y, x + w, y + h), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        return dst;
    }

    private static SKBitmap Background(IReadOnlyList<SKBitmap> shots, int dw, int dh)
    {
        if (shots.Count == 0)
        {
            var flat = new SKBitmap(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul));
            flat.Erase(new SKColor(24, 24, 30));
            return flat;
        }

        var layers = shots.Select(s => CoverFit(s, dw, dh)).ToList();
        try
        {
            if (layers.Count == 1)
            {
                return layers[0].Copy();
            }

            var n = layers.Count;
            var bytes = layers.Select(l => l.Bytes).ToList();
            var result = new SKBitmap(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul));
            var dst = new byte[dw * dh * 4];

            // Triangular weights centred on each panel give smooth cross-fades.
            var weights = new float[n][];
            for (var i = 0; i < n; i++)
            {
                weights[i] = new float[dw];
            }

            for (var x = 0; x < dw; x++)
            {
                var fx = x / (float)(dw - 1);
                var total = 0f;
                for (var i = 0; i < n; i++)
                {
                    var c = (i + 0.5f) / n;
                    var w = Math.Clamp(1f - (Math.Abs(fx - c) * n), 0f, 1f);
                    weights[i][x] = w;
                    total += w;
                }

                total = Math.Max(total, 1e-6f);
                for (var i = 0; i < n; i++)
                {
                    weights[i][x] /= total;
                }
            }

            for (var y = 0; y < dh; y++)
            {
                for (var x = 0; x < dw; x++)
                {
                    var o = ((y * dw) + x) * 4;
                    float b = 0, g = 0, r = 0;
                    for (var i = 0; i < n; i++)
                    {
                        var w = weights[i][x];
                        b += bytes[i][o] * w;
                        g += bytes[i][o + 1] * w;
                        r += bytes[i][o + 2] * w;
                    }

                    dst[o] = (byte)b;
                    dst[o + 1] = (byte)g;
                    dst[o + 2] = (byte)r;
                    dst[o + 3] = 255;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(dst, 0, result.GetPixels(), dst.Length);
            return result;
        }
        finally
        {
            foreach (var l in layers)
            {
                l.Dispose();
            }
        }
    }

    /// <summary>Darkens, tints with the accent colour, adds vignette and bottom gradient.</summary>
    private static SKBitmap Grade(SKBitmap background, SKColor accent, int dw, int dh, float blur = 3f)
    {
        // Slight blur so busy scenes don't fight with the text.
        var blurred = new SKBitmap(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(blurred))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(blur, blur, SKShaderTileMode.Clamp) })
        using (var image = SKImage.FromBitmap(background))
        {
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(), paint);
        }

        var px = blurred.Bytes;
        float tr = accent.Red / 255f, tg = accent.Green / 255f, tb = accent.Blue / 255f;

        for (var y = 0; y < dh; y++)
        {
            var ny = y / (float)dh;
            var tintAmount = 0.10f + (0.32f * MathF.Pow(ny, 1.6f));
            for (var x = 0; x < dw; x++)
            {
                var nx = (x - (dw / 2f)) / (dw / 2f);
                var centre = MathF.Exp(-(nx * nx) * 2.2f);
                var dim = 0.62f - (0.22f * centre);
                var vig = 1f - (0.45f * Math.Clamp((nx * nx) + ((ny - 0.5f) * (ny - 0.5f) * 2f), 0f, 1f));

                var o = ((y * dw) + x) * 4;
                var b = px[o] / 255f * dim;
                var g = px[o + 1] / 255f * dim;
                var r = px[o + 2] / 255f * dim;

                r = (r * (1 - tintAmount)) + (((r * 0.4f) + (tr * 0.55f)) * tintAmount);
                g = (g * (1 - tintAmount)) + (((g * 0.4f) + (tg * 0.55f)) * tintAmount);
                b = (b * (1 - tintAmount)) + (((b * 0.4f) + (tb * 0.55f)) * tintAmount);

                px[o] = (byte)(Math.Clamp(b * vig, 0f, 1f) * 255f);
                px[o + 1] = (byte)(Math.Clamp(g * vig, 0f, 1f) * 255f);
                px[o + 2] = (byte)(Math.Clamp(r * vig, 0f, 1f) * 255f);
                px[o + 3] = 255;
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(px, 0, blurred.GetPixels(), px.Length);
        return blurred;
    }

    /// <summary>Most vivid mid-tone hue across the images (ignores grey/black/white).</summary>
    internal static SKColor DominantColor(IReadOnlyList<SKBitmap> images)
    {
        var hueWeight = new double[12];
        var hueSum = new double[12];
        foreach (var img in images)
        {
            using var small = img.Resize(new SKImageInfo(48, 27, SKColorType.Bgra8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear));
            if (small == null)
            {
                continue;
            }

            var bytes = small.Bytes;
            for (var o = 0; o + 3 < bytes.Length; o += 4)
            {
                float b = bytes[o] / 255f, g = bytes[o + 1] / 255f, r = bytes[o + 2] / 255f;
                var mx = Math.Max(r, Math.Max(g, b));
                var mn = Math.Min(r, Math.Min(g, b));
                var sat = mx <= 1e-6f ? 0f : (mx - mn) / mx;
                var weight = (sat * (1f - Math.Abs(mx - 0.6f))) + 1e-4f;
                var hue = Hue(r, g, b, mx, mn);
                var bucket = (int)(hue * 12) % 12;
                hueWeight[bucket] += weight;
                hueSum[bucket] += hue * weight;
            }
        }

        var best = Array.IndexOf(hueWeight, hueWeight.Max());
        var h = hueWeight[best] > 0 ? (float)(hueSum[best] / hueWeight[best]) : 0.6f;
        return FromHsv(h, 0.62f, 0.95f);
    }

    private static float Hue(float r, float g, float b, float mx, float mn)
    {
        var d = mx - mn;
        if (d < 1e-6f)
        {
            return 0f;
        }

        float h;
        if (mx == r)
        {
            h = ((g - b) / d) % 6f;
        }
        else if (mx == g)
        {
            h = ((b - r) / d) + 2f;
        }
        else
        {
            h = ((r - g) / d) + 4f;
        }

        h /= 6f;
        return h < 0 ? h + 1f : h;
    }

    private static SKColor FromHsv(float h, float s, float v)
    {
        var i = (int)(h * 6f);
        var f = (h * 6f) - i;
        var p = v * (1 - s);
        var q = v * (1 - (f * s));
        var t = v * (1 - ((1 - f) * s));
        (float r, float g, float b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q)
        };
        return new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    internal static void DrawLogoRow(SKCanvas canvas, IReadOnlyList<SKBitmap> logos, float yCenter, int canvasWidth = Width, float limit = Safe - 160f, int boxW = 300, int boxH = 120)
    {
        float gap = 56;

        var fitted = new List<(SKBitmap Bmp, float W, float H)>();
        foreach (var logo in logos)
        {
            var crop = CropToAlpha(logo);
            var scale = Math.Min((float)boxW / crop.Width, (float)boxH / crop.Height);
            fitted.Add((crop, crop.Width * scale, crop.Height * scale));
        }

        try
        {
            var total = fitted.Sum(f => f.W) + (gap * (fitted.Count - 1));
            if (total > limit)
            {
                var k = limit / total;
                for (var i = 0; i < fitted.Count; i++)
                {
                    fitted[i] = (fitted[i].Bmp, fitted[i].W * k, fitted[i].H * k);
                }

                gap *= k;
                total = limit;
            }

            using var paint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateDropShadow(0, 6, 12, 12, new SKColor(0, 0, 0, 180))
            };

            var x = (canvasWidth - total) / 2f;
            foreach (var (bmp, w, h) in fitted)
            {
                using var image = SKImage.FromBitmap(bmp);
                canvas.DrawImage(image, new SKRect(x, yCenter - (h / 2), x + w, yCenter + (h / 2)), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
                x += w + gap;
            }
        }
        finally
        {
            foreach (var f in fitted)
            {
                f.Bmp.Dispose();
            }
        }
    }

    /// <summary>Crops transparent borders; always returns a new bitmap.</summary>
    private static SKBitmap CropToAlpha(SKBitmap src)
    {
        using var rgba = src.Copy(SKColorType.Bgra8888);
        var bytes = rgba.Bytes;
        int minX = rgba.Width, minY = rgba.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < rgba.Height; y++)
        {
            for (var x = 0; x < rgba.Width; x++)
            {
                if (bytes[(((y * rgba.Width) + x) * 4) + 3] > 8)
                {
                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }
                }
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return rgba.Copy();
        }

        var rect = new SKRectI(minX, minY, maxX + 1, maxY + 1);
        var cropped = new SKBitmap(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        rgba.ExtractSubset(cropped, rect);
        return cropped.Copy();
    }
}
