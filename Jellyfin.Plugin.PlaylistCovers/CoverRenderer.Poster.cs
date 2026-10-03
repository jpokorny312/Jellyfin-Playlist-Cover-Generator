using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.PlaylistCovers;

/// <summary>Portrait 1:2 layout: a fan of the real posters from the playlist, title and logos below.</summary>
public static partial class CoverRenderer
{
    /// <summary>Poster layout width.</summary>
    public const int PosterWidth = 1080;

    /// <summary>Poster layout height (1:2).</summary>
    public const int PosterHeight = 2160;

    private const int PosterW = 660;
    private const int PosterH = 990;

    // Back-to-front (dx from centre, dy lift, scale, rotation in degrees) per number of posters.
    // Outer posters sit higher so they peek out above the ones in front, like a hand of cards.
    private static readonly (float Dx, float Dy, float Scale, float Rot)[][] FanSlots =
    {
        Array.Empty<(float, float, float, float)>(),
        new[] { (0f, 0f, 1.0f, 0f) },
        new[] { (-215f, 0f, 0.92f, -5f), (215f, 0f, 0.92f, 5f) },
        new[] { (-330f, 0f, 0.82f, -9f), (330f, 0f, 0.82f, 9f), (0f, 0f, 1.0f, 0f) },
        new[] { (-420f, -130f, 0.72f, -14f), (420f, -130f, 0.72f, 14f), (-205f, 0f, 0.9f, -5f), (205f, 0f, 0.9f, 5f) },
        new[] { (-450f, -150f, 0.70f, -15f), (450f, -150f, 0.70f, 15f), (-300f, 0f, 0.84f, -8f), (300f, 0f, 0.84f, 8f), (0f, 0f, 1.0f, 0f) },
    };

    /// <summary>Renders the portrait poster cover as JPEG.</summary>
    /// <param name="title">Playlist name.</param>
    /// <param name="posters">Poster images of the titles (first five are used).</param>
    /// <param name="backdrops">Optional backdrops for the blurred background; posters are used if empty.</param>
    /// <param name="logos">Title logos.</param>
    /// <param name="movies">Number of movies.</param>
    /// <param name="series">Number of series.</param>
    /// <param name="fontPath">Optional font file.</param>
    /// <param name="upperCase">Render title in upper case.</param>
    /// <returns>JPEG bytes.</returns>
    public static byte[] RenderPosterJpeg(
        string title,
        IReadOnlyList<SKBitmap> posters,
        IReadOnlyList<SKBitmap> backdrops,
        IReadOnlyList<SKBitmap> logos,
        int movies,
        int series,
        string? fontPath = null,
        bool upperCase = true)
    {
        using var bitmap = RenderPoster(title, posters, backdrops, logos, movies, series, fontPath, upperCase);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        return data.ToArray();
    }

    /// <summary>Renders the portrait poster cover as bitmap.</summary>
    /// <param name="title">Playlist name.</param>
    /// <param name="posters">Poster images of the titles.</param>
    /// <param name="backdrops">Optional backdrops for the background.</param>
    /// <param name="logos">Title logos.</param>
    /// <param name="movies">Number of movies.</param>
    /// <param name="series">Number of series.</param>
    /// <param name="fontPath">Optional font file.</param>
    /// <param name="upperCase">Render title in upper case.</param>
    /// <returns>The rendered bitmap (caller disposes).</returns>
    public static SKBitmap RenderPoster(
        string title,
        IReadOnlyList<SKBitmap> posters,
        IReadOnlyList<SKBitmap> backdrops,
        IReadOnlyList<SKBitmap> logos,
        int movies,
        int series,
        string? fontPath = null,
        bool upperCase = true)
    {
        var fan = posters.Take(5).ToList();
        var bgSources = (backdrops.Count > 0 ? backdrops : fan).Take(3).ToList();
        var accent = DominantColor(bgSources.Count > 0 ? bgSources : fan);

        using var background = Background(bgSources, PosterWidth, PosterHeight);
        var result = Grade(background, accent, PosterWidth, PosterHeight, 40f);
        using var canvas = new SKCanvas(result);

        DrawFan(canvas, fan);

        using var typeface = LoadTypeface(fontPath);
        var text = upperCase ? title.ToUpperInvariant() : title;
        var (font, lines) = FitTitle(text, typeface, PosterWidth - 140, 112);
        using (font)
        {
            var hasLogos = logos.Count > 0;
            var lineHeight = font.Size * 1.12f;
            var y = hasLogos ? 1390f : 1440f;

            using var titlePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                ImageFilter = SKImageFilter.CreateDropShadow(0, 5, 10, 10, new SKColor(0, 0, 0, 170))
            };
            foreach (var line in lines)
            {
                var w = font.MeasureText(line);
                canvas.DrawText(line, (PosterWidth - w) / 2f, y - font.Metrics.Ascent, SKTextAlign.Left, font, titlePaint);
                y += lineHeight;
            }

            var barY = y + 18;
            using var barPaint = new SKPaint { IsAntialias = true, Color = accent };
            canvas.DrawRoundRect(new SKRect((PosterWidth / 2f) - 70, barY, (PosterWidth / 2f) + 70, barY + 7), 4, 4, barPaint);

            var sub = Subline(movies, series);
            var subBottom = barY + 7;
            if (sub.Length > 0)
            {
                using var subFont = new SKFont(typeface, 38) { Edging = SKFontEdging.SubpixelAntialias };
                using var subPaint = new SKPaint { IsAntialias = true, Color = new SKColor(235, 235, 240, 235) };
                var sw = subFont.MeasureText(sub);
                canvas.DrawText(sub, (PosterWidth - sw) / 2f, barY + 34 - subFont.Metrics.Ascent, SKTextAlign.Left, subFont, subPaint);
                subBottom = barY + 34 + 38;
            }

            if (hasLogos)
            {
                DrawLogoRow(canvas, logos.Take(3).ToList(), Math.Min(2000f, subBottom + 80f), PosterWidth, PosterWidth - 140f, 280, 110);
            }
        }

        return result;
    }

    private static void DrawFan(SKCanvas canvas, List<SKBitmap> fan)
    {
        var slots = FanSlots[Math.Min(fan.Count, 5)];
        for (var i = 0; i < slots.Length; i++)
        {
            var (dx, dy, scale, rot) = slots[i];
            var w = PosterW * scale;
            var h = PosterH * scale;
            var cx = (PosterWidth / 2f) + (dx * 1.1f);
            var cy = 850f + ((1f - scale) * 80f) + dy;

            using var poster = CoverFit(fan[slots.Length - 1 - i], (int)w, (int)h, 0.5f);
            using var image = SKImage.FromBitmap(poster);
            var rect = new SKRect(-w / 2, -h / 2, w / 2, h / 2);
            using var rrect = new SKRoundRect(rect, 20);

            canvas.Save();
            canvas.Translate(cx, cy);
            canvas.RotateDegrees(rot);

            using (var shadow = new SKPaint { IsAntialias = true, ImageFilter = SKImageFilter.CreateDropShadowOnly(0, 22, 26, 26, new SKColor(0, 0, 0, 200)) })
            {
                canvas.DrawRoundRect(rrect, shadow);
            }

            canvas.Save();
            canvas.ClipRoundRect(rrect, SKClipOperation.Intersect, true);
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKCubicResampler.Mitchell));
            canvas.Restore();

            using (var edge = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 2, Color = new SKColor(255, 255, 255, 40) })
            {
                canvas.DrawRoundRect(rrect, edge);
            }

            canvas.Restore();
        }
    }
}
