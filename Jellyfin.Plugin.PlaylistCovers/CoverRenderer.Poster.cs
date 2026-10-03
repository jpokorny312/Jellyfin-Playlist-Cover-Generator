using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.PlaylistCovers;

/// <summary>
/// Portrait 2:3 layout: the playlist's real posters as a calm, receding stack,
/// with the title set left-aligned underneath.
/// </summary>
public static partial class CoverRenderer
{
    /// <summary>Poster layout width.</summary>
    public const int PosterWidth = 1200;

    /// <summary>Poster layout height (2:3).</summary>
    public const int PosterHeight = 1800;

    private const int TextMargin = 90;
    private const float TextBottom = 1700f;

    private static readonly float[] StackScale = { 1.00f, 0.94f, 0.88f, 0.82f };
    private static readonly ConcurrentDictionary<string, SKTypeface?> EmbeddedFonts = new();

    /// <summary>Renders the portrait poster cover as JPEG.</summary>
    /// <param name="title">Playlist name.</param>
    /// <param name="posters">Poster images of the titles (the first four are shown, the first one in front).</param>
    /// <param name="movies">Number of movies.</param>
    /// <param name="series">Number of series.</param>
    /// <param name="fontPath">Optional font file for the title.</param>
    /// <param name="upperCase">Render title in upper case.</param>
    /// <returns>JPEG bytes.</returns>
    public static byte[] RenderPosterJpeg(
        string title,
        IReadOnlyList<SKBitmap> posters,
        int movies,
        int series,
        string? fontPath = null,
        bool upperCase = false)
    {
        using var bitmap = RenderPoster(title, posters, movies, series, fontPath, upperCase);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        return data.ToArray();
    }

    /// <summary>Renders the portrait poster cover as bitmap.</summary>
    /// <param name="title">Playlist name.</param>
    /// <param name="posters">Poster images of the titles.</param>
    /// <param name="movies">Number of movies.</param>
    /// <param name="series">Number of series.</param>
    /// <param name="fontPath">Optional font file for the title.</param>
    /// <param name="upperCase">Render title in upper case.</param>
    /// <returns>The rendered bitmap (caller disposes).</returns>
    public static SKBitmap RenderPoster(
        string title,
        IReadOnlyList<SKBitmap> posters,
        int movies,
        int series,
        string? fontPath = null,
        bool upperCase = false)
    {
        var stack = posters.Take(4).ToList();
        var accent = DominantColor(stack.Take(3).ToList());

        using var hero = stack.Count > 0
            ? CoverFit(stack[0], PosterWidth, PosterHeight, 0.5f)
            : SolidBitmap(new SKColor(30, 30, 36));
        var result = GradePoster(hero, accent);
        using var canvas = new SKCanvas(result);

        // The title block is anchored to the bottom; the stack is centred in the space above it.
        var textTop = DrawTitleBlock(canvas, upperCase ? title.ToUpperInvariant() : title, Subline(movies, series), accent, fontPath);
        DrawStack(canvas, stack, textTop);
        return result;
    }

    private static SKBitmap SolidBitmap(SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(PosterWidth, PosterHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(color);
        return bmp;
    }

    /// <summary>Blurred, darkened hero with a hint of the accent colour, a dark floor and a touch of film grain.</summary>
    private static SKBitmap GradePoster(SKBitmap hero, SKColor accent)
    {
        var bmp = new SKBitmap(new SKImageInfo(PosterWidth, PosterHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(48, 48, SKShaderTileMode.Clamp) })
        using (var image = SKImage.FromBitmap(hero))
        {
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(), paint);
        }

        var px = bmp.Bytes;
        float tr = accent.Red / 255f, tg = accent.Green / 255f, tb = accent.Blue / 255f;
        var rnd = new Random(7);

        for (var y = 0; y < PosterHeight; y++)
        {
            var ny = y / (float)PosterHeight;
            var floor = MathF.Pow(Math.Clamp((ny - 0.5f) / 0.5f, 0f, 1f), 1.5f) * 0.85f;
            var dim = 0.46f - (0.10f * ny);
            for (var x = 0; x < PosterWidth; x++)
            {
                var nx = (x - (PosterWidth / 2f)) / (PosterWidth / 2f);
                var vig = 1f - (0.30f * Math.Clamp((nx * nx * 0.8f) + ((ny - 0.45f) * (ny - 0.45f) * 1.2f), 0f, 1f));
                var grain = ((float)rnd.NextDouble() - 0.5f) * 0.04f;

                var o = ((y * PosterWidth) + x) * 4;
                var b = px[o] / 255f * dim;
                var g = px[o + 1] / 255f * dim;
                var r = px[o + 2] / 255f * dim;

                // a whisper of the accent colour, then fade into a near-black floor
                r = (r * 0.86f) + (tr * 0.05f);
                g = (g * 0.86f) + (tg * 0.05f);
                b = (b * 0.86f) + (tb * 0.05f);
                r = (r * (1 - floor)) + (0.035f * floor);
                g = (g * (1 - floor)) + (0.035f * floor);
                b = (b * (1 - floor)) + (0.045f * floor);

                px[o] = ToByte((b * vig) + grain);
                px[o + 1] = ToByte((g * vig) + grain);
                px[o + 2] = ToByte((r * vig) + grain);
                px[o + 3] = 255;
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
        return bmp;
    }

    private static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f);

    /// <summary>Posters overlap left to right, each one a little smaller; the first title sits in front.</summary>
    private static void DrawStack(SKCanvas canvas, List<SKBitmap> stack, float textTop)
    {
        var n = stack.Count;
        if (n == 0)
        {
            return;
        }

        var w0 = n switch { 1 => 680f, 2 => 560f, 3 => 520f, _ => 470f };

        // free zone above the text; shrink the stack if a long title leaves too little room
        var zoneTop = 90f;
        var zoneBottom = textTop - 90f;
        w0 = Math.Min(w0, (zoneBottom - zoneTop) / 1.5f);
        var stackBottom = ((zoneTop + zoneBottom) / 2f) + (w0 * 1.5f / 2f);
        var step = n == 1 ? 0f : w0 * 0.5f;
        var total = (step * (n - 1)) + (w0 * StackScale[n - 1]);
        var x0 = (PosterWidth - total) / 2f;

        for (var i = n - 1; i >= 0; i--)
        {
            var w = w0 * StackScale[i];
            var h = w * 1.5f;
            var x = x0 + (step * i);
            var rect = new SKRect(x, stackBottom - h, x + w, stackBottom);

            using var fitted = CoverFit(stack[i], (int)w, (int)h, 0.5f);
            using var image = SKImage.FromBitmap(fitted);
            using var rrect = new SKRoundRect(rect, 12);

            using (var shadow = new SKPaint { IsAntialias = true, ImageFilter = SKImageFilter.CreateDropShadowOnly(0, 20, 26, 26, new SKColor(0, 0, 0, 150)) })
            {
                canvas.DrawRoundRect(rrect, shadow);
            }

            canvas.Save();
            canvas.ClipRoundRect(rrect, SKClipOperation.Intersect, true);
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKCubicResampler.Mitchell));
            canvas.Restore();

            using var edge = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f, Color = new SKColor(255, 255, 255, 28) };
            canvas.DrawRoundRect(rrect, edge);
        }
    }

    /// <summary>Accent hairline, title and a small tracked line – left-aligned, anchored to the bottom.</summary>
    private static float DrawTitleBlock(SKCanvas canvas, string title, string sub, SKColor accent, string? fontPath)
    {
        var ownsTitleFace = false;
        SKTypeface? titleFace = null;
        if (string.IsNullOrWhiteSpace(fontPath) || !File.Exists(fontPath))
        {
            titleFace = Embedded("Inter-SemiBold.ttf");
        }

        if (titleFace == null)
        {
            titleFace = LoadTypeface(fontPath);
            ownsTitleFace = true;
        }

        var subFace = Embedded("Inter-Medium.ttf") ?? titleFace;

        try
        {
            var (font, lines) = FitTitle(title, titleFace, PosterWidth - (2 * TextMargin), 96);
            using (font)
            using (var subFont = new SKFont(subFace, 27) { Edging = SKFontEdging.SubpixelAntialias })
            {
                const float spacing = 4f;
                var lineHeight = font.Size * 1.12f;
                var hasSub = sub.Length > 0;

                var subTop = TextBottom - 27f;
                var titleBottom = hasSub ? subTop - 30f : TextBottom;
                var titleTop = titleBottom - (lineHeight * lines.Count);
                var hairlineY = titleTop - 38f;

                using (var bar = new SKPaint { IsAntialias = true, Color = accent })
                {
                    canvas.DrawRoundRect(new SKRect(TextMargin, hairlineY, TextMargin + 64, hairlineY + 4), 2, 2, bar);
                }

                using var titlePaint = new SKPaint
                {
                    IsAntialias = true,
                    Color = SKColors.White,
                    ImageFilter = SKImageFilter.CreateDropShadow(0, 3, 8, 8, new SKColor(0, 0, 0, 120))
                };
                var y = titleTop;
                foreach (var line in lines)
                {
                    canvas.DrawText(line, TextMargin, y - font.Metrics.Ascent, SKTextAlign.Left, font, titlePaint);
                    y += lineHeight;
                }

                if (hasSub)
                {
                    using var subPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 150) };
                    DrawTracked(canvas, sub.ToUpperInvariant(), TextMargin, subTop - subFont.Metrics.Ascent, subFont, subPaint, spacing);
                }

                return hairlineY;
            }
        }
        finally
        {
            if (ownsTitleFace)
            {
                titleFace.Dispose();
            }
        }
    }

    private static void DrawTracked(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKPaint paint, float spacing)
    {
        foreach (var ch in text)
        {
            var s = ch.ToString();
            canvas.DrawText(s, x, baseline, SKTextAlign.Left, font, paint);
            x += font.MeasureText(s) + spacing;
        }
    }

    /// <summary>Loads a font bundled in the plugin assembly (cached for the process lifetime).</summary>
    private static SKTypeface? Embedded(string fileName) =>
        EmbeddedFonts.GetOrAdd(fileName, name =>
        {
            using var stream = typeof(CoverRenderer).Assembly.GetManifestResourceStream($"Jellyfin.Plugin.PlaylistCovers.Fonts.{name}");
            if (stream == null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            using var data = SKData.CreateCopy(ms.ToArray());
            return SKTypeface.FromData(data);
        });
}
