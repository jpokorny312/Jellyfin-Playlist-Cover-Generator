using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.PlaylistCovers;

/// <summary>The cover styles. Clients overlay a count badge (top right) and a progress bar (bottom), so keep those corners calm.</summary>
public static partial class CoverRenderer
{
    /// <summary>Names accepted by <see cref="Render"/>.</summary>
    public static readonly string[] Styles = { "Auto", "Wall", "Mosaic", "Hero" };

    /// <summary>Renders a cover and returns it as JPEG.</summary>
    /// <param name="posters">Poster images, in playlist order (the first one is the most important).</param>
    /// <param name="style">"Auto", "Wall", "Mosaic" or "Hero". Styles that need more posters than available fall back to "Hero".</param>
    /// <returns>JPEG bytes.</returns>
    public static byte[] RenderJpeg(IReadOnlyList<SKBitmap> posters, string style)
    {
        using var bitmap = Render(posters, style);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        return data.ToArray();
    }

    /// <summary>Renders a cover.</summary>
    /// <param name="posters">Poster images, in playlist order.</param>
    /// <param name="style">Style name, see <see cref="Styles"/>.</param>
    /// <returns>The rendered bitmap (caller disposes).</returns>
    public static SKBitmap Render(IReadOnlyList<SKBitmap> posters, string style)
    {
        var n = posters.Count;
        var accent = DominantColor(posters.Take(4).ToList());
        var chosen = ResolveStyle(n, style);
        return chosen switch
        {
            "Wall" => RenderWall(posters, accent),
            "Mosaic" => RenderMosaic(posters, accent),
            _ => RenderHero(posters, accent),
        };
    }

    /// <summary>Picks the style that actually gets rendered for <paramref name="count"/> posters.</summary>
    /// <param name="count">Number of posters available.</param>
    /// <param name="style">Requested style.</param>
    /// <returns>"Wall", "Mosaic" or "Hero".</returns>
    public static string ResolveStyle(int count, string? style)
    {
        var s = Styles.FirstOrDefault(x => string.Equals(x, style, StringComparison.OrdinalIgnoreCase)) ?? "Auto";
        if (s == "Auto")
        {
            s = count >= 6 ? "Wall" : count >= 4 ? "Mosaic" : "Hero";
        }

        return (s == "Wall" || s == "Mosaic") && count < 4 ? "Hero" : s;
    }

    private static SKBitmap NewCanvas(SKColor fill)
    {
        var bmp = new SKBitmap(new SKImageInfo(PosterWidth, PosterHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(fill);
        return bmp;
    }

    /// <summary>Colour matrix that scales saturation and brightness so mismatched posters sit together.</summary>
    private static SKColorFilter Harmonise(float saturation, float brightness)
    {
        const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
        var s = saturation;
        float[] m =
        {
            (lr * (1 - s)) + s, lg * (1 - s), lb * (1 - s), 0, 0,
            lr * (1 - s), (lg * (1 - s)) + s, lb * (1 - s), 0, 0,
            lr * (1 - s), lg * (1 - s), (lb * (1 - s)) + s, 0, 0,
            0, 0, 0, 1, 0,
        };
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                m[(row * 5) + col] *= brightness;
            }
        }

        return SKColorFilter.CreateColorMatrix(m);
    }

    /// <summary>Draws one image scaled to fill <paramref name="rect"/> with optional rounded corners.</summary>
    private static void DrawTile(SKCanvas canvas, SKImage image, SKRect rect, float radius, SKColorFilter? filter)
    {
        using var paint = new SKPaint { IsAntialias = true, ColorFilter = filter };
        canvas.Save();
        if (radius > 0)
        {
            canvas.ClipRoundRect(new SKRoundRect(rect, radius), SKClipOperation.Intersect, true);
        }
        else
        {
            canvas.ClipRect(rect);
        }

        canvas.DrawImage(image, rect, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        canvas.Restore();
    }

    /// <summary>Vignette, a touch of the accent colour, a darker floor and film grain, applied in place.</summary>
    private static void Finish(SKBitmap bmp, SKColor accent, float vignette, float tint, float grainAmount, float floorDark)
    {
        var px = bmp.Bytes;
        float tr = accent.Red / 255f, tg = accent.Green / 255f, tb = accent.Blue / 255f;
        var rnd = new Random(11);
        for (var y = 0; y < PosterHeight; y++)
        {
            var ny = y / (float)PosterHeight;
            var floor = MathF.Pow(Math.Clamp((ny - 0.55f) / 0.45f, 0f, 1f), 1.6f) * floorDark;
            for (var x = 0; x < PosterWidth; x++)
            {
                var nx = (x - (PosterWidth / 2f)) / (PosterWidth / 2f);
                var vig = 1f - (vignette * Math.Clamp((nx * nx * 0.7f) + ((ny - 0.5f) * (ny - 0.5f) * 1.6f), 0f, 1f));
                var grain = ((float)rnd.NextDouble() - 0.5f) * grainAmount;
                var o = ((y * PosterWidth) + x) * 4;
                var b = (px[o] / 255f * (1 - tint)) + (tb * tint * 0.6f);
                var g = (px[o + 1] / 255f * (1 - tint)) + (tg * tint * 0.6f);
                var r = (px[o + 2] / 255f * (1 - tint)) + (tr * tint * 0.6f);
                var keep = vig * (1 - floor);
                px[o] = ToByte((b * keep) + grain);
                px[o + 1] = ToByte((g * keep) + grain);
                px[o + 2] = ToByte((r * keep) + grain);
                px[o + 3] = 255;
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
    }

    /// <summary>
    /// "Wall": a slightly tilted, staggered wall of posters that bleeds off every edge
    /// (the look of streaming sign-up screens). Best with 6+ titles.
    /// </summary>
    private static SKBitmap RenderWall(IReadOnlyList<SKBitmap> posters, SKColor accent)
    {
        const int pw = 300, ph = 450, gap = 22;
        var tiles = posters.Take(12).Select(p =>
        {
            using var fitted = CoverFit(p, pw, ph, 0.5f);
            return SKImage.FromBitmap(fitted);
        }).ToList();

        var result = NewCanvas(new SKColor(10, 10, 12));
        try
        {
            using var canvas = new SKCanvas(result);
            using var filter = Harmonise(0.78f, 0.80f);
            canvas.Translate(PosterWidth / 2f, PosterHeight / 2f);
            canvas.RotateDegrees(-9);

            var n = tiles.Count;
            const int cols = 4, rows = 3; // counted from the centre in both directions
            for (var c = -cols; c <= cols; c++)
            {
                var stagger = (((c % 3) + 3) % 3) * ((ph + gap) / 3f);
                for (var r = -rows; r <= rows; r++)
                {
                    var idx = ((((c * 3) + (r * 5)) % n) + n) % n;
                    var x = (c * (pw + gap)) - (pw / 2f);
                    var y = (r * (ph + gap)) - (ph / 2f) + stagger;
                    DrawTile(canvas, tiles[idx], new SKRect(x, y, x + pw, y + ph), 10, filter);
                }
            }
        }
        finally
        {
            tiles.ForEach(t => t.Dispose());
        }

        Finish(result, accent, vignette: 0.50f, tint: 0.10f, grainAmount: 0.035f, floorDark: 0.45f);
        return result;
    }

    /// <summary>
    /// "Mosaic": the familiar 2×2 / 3×3 grid of Spotify and Apple Music, with hairline gutters,
    /// one shared colour grade and posters ordered by colour so the grid reads as a gradient.
    /// </summary>
    private static SKBitmap RenderMosaic(IReadOnlyList<SKBitmap> posters, SKColor accent)
    {
        var grid = posters.Count >= 9 ? 3 : 2;
        var chosen = SortByColor(posters.Take(grid * grid).ToList());
        const int gutter = 6;
        var tw = (PosterWidth - (gutter * (grid - 1))) / (float)grid;
        var th = (PosterHeight - (gutter * (grid - 1))) / (float)grid;

        var result = NewCanvas(new SKColor(12, 12, 14));
        using (var canvas = new SKCanvas(result))
        using (var filter = Harmonise(0.86f, 0.92f))
        {
            for (var i = 0; i < chosen.Count; i++)
            {
                var col = i % grid;
                var row = i / grid;
                var rect = new SKRect(col * (tw + gutter), row * (th + gutter), (col * (tw + gutter)) + tw, (row * (th + gutter)) + th);
                using var fitted = CoverFit(chosen[i], (int)Math.Ceiling(tw), (int)Math.Ceiling(th), 0.5f);
                using var image = SKImage.FromBitmap(fitted);
                DrawTile(canvas, image, rect, 0, filter);
            }
        }

        Finish(result, accent, vignette: 0.18f, tint: 0.06f, grainAmount: 0.025f, floorDark: 0.0f);
        return result;
    }

    /// <summary>
    /// "Hero": the first title large and complete, with up to two more posters peeking out above it
    /// like a stack of cards, on a blurred, colour-boosted copy of the hero poster. Works for any number of titles.
    /// </summary>
    private static SKBitmap RenderHero(IReadOnlyList<SKBitmap> posters, SKColor accent)
    {
        var stack = posters.Take(3).ToList();
        var result = NewCanvas(new SKColor(24, 24, 28));
        if (stack.Count == 0)
        {
            Finish(result, accent, 0.4f, 0f, 0.03f, 0f);
            return result;
        }

        // background: the hero poster, blurred, more saturated and darkened
        using (var bgCanvas = new SKCanvas(result))
        using (var fitted = CoverFit(stack[0], PosterWidth, PosterHeight, 0.5f))
        using (var image = SKImage.FromBitmap(fitted))
        using (var bg = new SKPaint { ColorFilter = Harmonise(1.5f, 0.68f), ImageFilter = SKImageFilter.CreateBlur(50, 50, SKShaderTileMode.Clamp) })
        {
            bgCanvas.DrawImage(image, 0, 0, new SKSamplingOptions(), bg);
        }

        // vignette, darker floor and grain only on the background, never on the posters
        Finish(result, accent, vignette: 0.30f, tint: 0f, grainAmount: 0.03f, floorDark: 0.30f);
        using var canvas = new SKCanvas(result);

        // (width, top) per card, back to front; the front card is last. Keeps ~60px clear at the bottom (progress bar).
        var cards = stack.Count switch
        {
            1 => new[] { (960f, 180f) },
            2 => new[] { (780f, 150f), (940f, 310f) },
            _ => new[] { (640f, 100f), (790f, 215f), (940f, 330f) },
        };

        for (var i = 0; i < cards.Length; i++)
        {
            var (w, top) = cards[i];
            var h = w * 1.5f;
            var x = (PosterWidth - w) / 2f;
            var rect = new SKRect(x, top, x + w, top + h);
            using var fitted = CoverFit(stack[cards.Length - 1 - i], (int)w, (int)h, 0.5f);
            using var image = SKImage.FromBitmap(fitted);
            using var rrect = new SKRoundRect(rect, 18);
            var isFront = i == cards.Length - 1;

            using (var shadow = new SKPaint { IsAntialias = true, ImageFilter = SKImageFilter.CreateDropShadowOnly(0, isFront ? 26 : 14, isFront ? 34 : 20, isFront ? 34 : 20, new SKColor(0, 0, 0, (byte)(isFront ? 190 : 150))) })
            {
                canvas.DrawRoundRect(rrect, shadow);
            }

            // cards further back are dimmed so the front one leads
            using var dim = isFront ? null : Harmonise(0.95f, 0.70f);
            DrawTile(canvas, image, rect, 18, dim);

            using var edge = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f, Color = new SKColor(255, 255, 255, 30) };
            canvas.DrawRoundRect(rrect, edge);
        }

        return result;
    }
}
