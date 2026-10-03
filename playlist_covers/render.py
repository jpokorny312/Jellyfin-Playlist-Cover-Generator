"""Renders a streaming-style cover for a Jellyfin playlist.

The canvas is 16:9. Everything important (title, logos) sits inside the centred
square, so clients that crop playlists to 1:1 (phones, some TV apps) still show
a complete cover.
"""
from __future__ import annotations

import colorsys
from pathlib import Path
from typing import Sequence

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps

WIDTH, HEIGHT = 1920, 1080
SAFE = HEIGHT  # side of the centred square that survives a 1:1 crop
SAFE_X0 = (WIDTH - SAFE) // 2

FONT_DIR = Path(__file__).resolve().parent.parent / "fonts"
_FONT_CANDIDATES = [
    "Montserrat-Bold.ttf",
    "Inter-Bold.ttf",
    "BebasNeue-Regular.ttf",
]
_SYSTEM_FALLBACKS = [
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    "/Library/Fonts/Arial Bold.ttf",
    "C:/Windows/Fonts/arialbd.ttf",
]


def find_font(custom: str | None = None) -> str:
    candidates = [custom] if custom else []
    candidates += [str(FONT_DIR / n) for n in _FONT_CANDIDATES]
    candidates += sorted(str(p) for p in FONT_DIR.glob("*.ttf"))
    candidates += _SYSTEM_FALLBACKS
    for c in candidates:
        if c and Path(c).is_file():
            return c
    raise FileNotFoundError("Keine Schriftart gefunden – lege eine .ttf in fonts/ ab.")


def _cover_fit(img: Image.Image, size: tuple[int, int]) -> Image.Image:
    return ImageOps.fit(img.convert("RGB"), size, Image.LANCZOS, centering=(0.5, 0.4))


def dominant_color(images: Sequence[Image.Image]) -> tuple[int, int, int]:
    """Most vivid mid-tone colour across the images (ignores grey/black/white)."""
    pixels = []
    for im in images:
        small = im.convert("RGB").resize((48, 27))
        pixels.append(np.asarray(small).reshape(-1, 3))
    px = np.concatenate(pixels).astype(np.float32) / 255.0
    mx, mn = px.max(1), px.min(1)
    sat = (mx - mn) / np.maximum(mx, 1e-6)
    weight = sat * (1 - np.abs(mx - 0.6)) + 1e-4
    # bucket by hue, pick the heaviest bucket
    hsv = np.array([colorsys.rgb_to_hsv(*p) for p in px])
    buckets = (hsv[:, 0] * 12).astype(int) % 12
    best = np.bincount(buckets, weights=weight, minlength=12).argmax()
    sel = buckets == best
    h = float(np.average(hsv[sel, 0], weights=weight[sel]))
    # normalise to a rich but not neon accent
    r, g, b = colorsys.hsv_to_rgb(h, 0.62, 0.95)
    return int(r * 255), int(g * 255), int(b * 255)


def _background(backdrops: Sequence[Image.Image]) -> Image.Image:
    """One backdrop full-bleed, or up to three cross-faded left→right."""
    shots = list(backdrops[:3]) or [Image.new("RGB", (WIDTH, HEIGHT), (24, 24, 30))]
    layers = [_cover_fit(b, (WIDTH, HEIGHT)) for b in shots]
    if len(layers) == 1:
        return layers[0]
    n = len(layers)
    xs = np.linspace(0, 1, WIDTH, dtype=np.float32)
    canvas = np.zeros((HEIGHT, WIDTH, 3), dtype=np.float32)
    # triangular weights centred on each panel → smooth cross-fades
    centres = [(i + 0.5) / n for i in range(n)]
    weights = [np.clip(1 - np.abs(xs - c) * n, 0, 1) for c in centres]
    total = np.maximum(sum(weights), 1e-6)
    for layer, w in zip(layers, weights):
        canvas += np.asarray(layer, dtype=np.float32) * (w / total)[None, :, None]
    return Image.fromarray(canvas.astype(np.uint8))


def _grade(bg: Image.Image, accent: tuple[int, int, int]) -> Image.Image:
    """Darken, tint with the accent colour, add vignette + bottom gradient."""
    bg = bg.filter(ImageFilter.GaussianBlur(3))
    arr = np.asarray(bg, dtype=np.float32) / 255.0

    ys, xs = np.mgrid[0:HEIGHT, 0:WIDTH].astype(np.float32)
    ny, nx = ys / HEIGHT, (xs - WIDTH / 2) / (WIDTH / 2)

    # global dim, stronger in the centre where the text sits
    centre = np.exp(-(nx**2) * 2.2)
    dim = 0.62 - 0.22 * centre
    arr *= dim[..., None]

    # accent tint: stronger toward the bottom
    tint = np.array(accent, dtype=np.float32) / 255.0
    a = (0.10 + 0.32 * ny**1.6)[..., None]
    arr = arr * (1 - a) + (arr * 0.4 + tint * 0.55) * a

    # vignette
    vig = 1 - 0.45 * np.clip((nx**2 + (ny - 0.5) ** 2 * 2), 0, 1)
    arr *= vig[..., None]
    return Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8))


def _wrap(draw: ImageDraw.ImageDraw, text: str, font, max_w: int) -> list[str]:
    words, lines, cur = text.split(), [], ""
    for w in words:
        trial = f"{cur} {w}".strip()
        if draw.textlength(trial, font=font) <= max_w or not cur:
            cur = trial
        else:
            lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return lines


def _fit_title(draw, text: str, font_path: str, max_w: int, max_lines: int = 3):
    for size in range(150, 56, -6):
        font = ImageFont.truetype(font_path, size)
        lines = _wrap(draw, text, font, max_w)
        if len(lines) <= max_lines and all(
            draw.textlength(l, font=font) <= max_w for l in lines
        ):
            return font, lines
    font = ImageFont.truetype(font_path, 56)
    return font, _wrap(draw, text, font, max_w)[:max_lines]


def _shadowed(canvas: Image.Image, layer: Image.Image, xy, blur=14, opacity=0.7):
    """Paste an RGBA layer with a soft drop shadow."""
    alpha = layer.getchannel("A")
    shadow = Image.new("RGBA", layer.size, (0, 0, 0, 255))
    shadow.putalpha(alpha.point(lambda v: int(v * opacity)))
    pad = blur * 3
    holder = Image.new("RGBA", (layer.width + pad * 2, layer.height + pad * 2), (0, 0, 0, 0))
    holder.paste(shadow, (pad, pad))
    holder = holder.filter(ImageFilter.GaussianBlur(blur))
    canvas.alpha_composite(holder, (xy[0] - pad, xy[1] - pad + 6))
    canvas.alpha_composite(layer, xy)


def _logo_row(canvas: Image.Image, logos: Sequence[Image.Image], y_center: int):
    logos = list(logos[:4])
    if not logos:
        return
    box_w, box_h, gap = 300, 120, 56
    fitted = []
    for lg in logos:
        lg = lg.convert("RGBA")
        bbox = lg.getchannel("A").getbbox()
        if bbox:
            lg = lg.crop(bbox)
        scale = min(box_w / lg.width, box_h / lg.height)
        fitted.append(lg.resize((max(1, int(lg.width * scale)), max(1, int(lg.height * scale))), Image.LANCZOS))
    total = sum(f.width for f in fitted) + gap * (len(fitted) - 1)
    # never leave the safe square
    if total > SAFE - 160:
        k = (SAFE - 160) / total
        fitted = [f.resize((max(1, int(f.width * k)), max(1, int(f.height * k))), Image.LANCZOS) for f in fitted]
        total = sum(f.width for f in fitted) + int(gap * k) * (len(fitted) - 1)
        gap = int(gap * k)
    x = (WIDTH - total) // 2
    for f in fitted:
        _shadowed(canvas, f, (x, y_center - f.height // 2))
        x += f.width + gap


def subline(movies: int, series: int) -> str:
    parts = []
    if movies:
        parts.append(f"{movies} {'Film' if movies == 1 else 'Filme'}")
    if series:
        parts.append(f"{series} {'Serie' if series == 1 else 'Serien'}")
    return "  ·  ".join(parts)


def render_cover(
    name: str,
    backdrops: Sequence[Image.Image],
    logos: Sequence[Image.Image] = (),
    movies: int = 0,
    series: int = 0,
    font_path: str | None = None,
) -> Image.Image:
    font_file = find_font(font_path)
    source = list(backdrops) or [Image.new("RGB", (64, 36), (40, 40, 48))]
    accent = dominant_color(source)

    bg = _grade(_background(source), accent)
    canvas = bg.convert("RGBA")
    draw = ImageDraw.Draw(canvas)

    max_w = SAFE - 220
    font, lines = _fit_title(draw, name.upper(), font_file, max_w)
    line_h = int(font.size * 1.12)
    block_h = line_h * len(lines)

    sub = subline(movies, series)
    sub_font = ImageFont.truetype(font_file, 38)
    has_logos = bool(logos)

    # vertical layout inside the safe square
    title_top = (HEIGHT - block_h) // 2 - (70 if has_logos else 30)
    y = title_top
    for line in lines:
        w = draw.textlength(line, font=font)
        x = (WIDTH - w) / 2
        # soft shadow under the headline
        sh = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        ImageDraw.Draw(sh).text((x, y + 5), line, font=font, fill=(0, 0, 0, 170))
        canvas.alpha_composite(sh.filter(ImageFilter.GaussianBlur(10)))
        draw = ImageDraw.Draw(canvas)
        draw.text((x, y), line, font=font, fill=(255, 255, 255, 255))
        y += line_h

    # accent bar
    bar_y = y + 18
    draw.rounded_rectangle(
        (WIDTH // 2 - 70, bar_y, WIDTH // 2 + 70, bar_y + 7), radius=4, fill=accent + (255,)
    )

    if sub:
        sw = draw.textlength(sub, font=sub_font)
        draw.text(((WIDTH - sw) / 2, bar_y + 34), sub, font=sub_font, fill=(235, 235, 240, 235))

    if has_logos:
        _logo_row(canvas, logos, y_center=min(HEIGHT - 150, bar_y + 34 + 38 + 150))

    return canvas.convert("RGB")


def to_jpeg_bytes(img: Image.Image, quality: int = 92) -> bytes:
    import io

    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=quality, optimize=True, subsampling=0)
    return buf.getvalue()
