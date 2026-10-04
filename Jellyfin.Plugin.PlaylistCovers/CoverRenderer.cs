using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.PlaylistCovers;

/// <summary>
/// Renders text-free 2:3 playlist covers from the posters of the playlist's titles.
/// The playlist name is shown by every Jellyfin client next to the card, so none is drawn.
/// Styles live in <c>CoverRenderer.Styles.cs</c>; this file holds the shared helpers.
/// </summary>
public static partial class CoverRenderer
{
    /// <summary>Cover width in pixels.</summary>
    public const int PosterWidth = 1200;

    /// <summary>Cover height in pixels (2:3).</summary>
    public const int PosterHeight = 1800;

    /// <summary>Scales and crops <paramref name="src"/> to fill dw×dh (centred horizontally, <paramref name="focusY"/> vertically).</summary>
    internal static SKBitmap CoverFit(SKBitmap src, int dw, int dh, float focusY = 0.4f)
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

    internal static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f);

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

    /// <summary>Orders posters by hue so a grid reads as a gradient; greys go last, darkest first.</summary>
    internal static List<SKBitmap> SortByColor(IReadOnlyList<SKBitmap> images)
    {
        float Key(SKBitmap img)
        {
            using var small = img.Resize(new SKImageInfo(8, 12, SKColorType.Bgra8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear));
            if (small == null)
            {
                return 3f;
            }

            double r = 0, g = 0, b = 0;
            var bytes = small.Bytes;
            var count = bytes.Length / 4;
            for (var o = 0; o + 3 < bytes.Length; o += 4)
            {
                b += bytes[o];
                g += bytes[o + 1];
                r += bytes[o + 2];
            }

            float rf = (float)(r / count / 255), gf = (float)(g / count / 255), bf = (float)(b / count / 255);
            var mx = Math.Max(rf, Math.Max(gf, bf));
            var mn = Math.Min(rf, Math.Min(gf, bf));
            var sat = mx <= 1e-6f ? 0f : (mx - mn) / mx;
            return sat < 0.12f ? 2f + mx : Hue(rf, gf, bf, mx, mn);
        }

        return images.Select(i => (Image: i, Key: Key(i))).OrderBy(t => t.Key).Select(t => t.Image).ToList();
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
}
