"""CLI: python -m playlist_covers [--demo | --dry-run] ..."""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

from .render import render_cover, to_jpeg_bytes


def _demo(out_dir: Path, font: str | None) -> None:
    """Render sample covers from synthetic images (no Jellyfin needed)."""
    import numpy as np

    out_dir.mkdir(parents=True, exist_ok=True)

    def fake_backdrop(c1, c2, seed):
        rng = np.random.default_rng(seed)
        w, h = 960, 540
        xs = np.linspace(0, 1, w)[None, :, None]
        ys = np.linspace(0, 1, h)[:, None, None]
        a, b = np.array(c1, float), np.array(c2, float)
        arr = a * (1 - xs * 0.7 - ys * 0.3) + b * (xs * 0.7 + ys * 0.3)
        arr += rng.normal(0, 8, arr.shape)
        im = Image.fromarray(np.clip(arr, 0, 255).astype("uint8"))
        d = ImageDraw.Draw(im)
        for _ in range(6):  # blobs so the fit/blur has structure
            x, y, r = rng.integers(0, w), rng.integers(0, h), rng.integers(60, 200)
            d.ellipse((x - r, y - r, x + r, y + r), fill=tuple(int(v) for v in rng.integers(30, 220, 3)))
        return im.filter(ImageFilter.GaussianBlur(12))

    def fake_logo(text):
        im = Image.new("RGBA", (600, 220), (0, 0, 0, 0))
        from .render import find_font
        from PIL import ImageFont

        f = ImageFont.truetype(find_font(font), 90)
        ImageDraw.Draw(im).text((20, 50), text, font=f, fill=(255, 255, 255, 255))
        return im

    samples = {
        "Marvel Filmreihe": dict(
            bd=[fake_backdrop((200, 30, 30), (20, 20, 80), 1), fake_backdrop((230, 160, 30), (120, 20, 20), 2), fake_backdrop((20, 40, 120), (200, 40, 60), 3)],
            logos=["IRON MAN", "THOR", "AVENGERS"], movies=12, series=0),
        "Weihnachtsklassiker": dict(
            bd=[fake_backdrop((20, 110, 60), (180, 30, 30), 4)],
            logos=["GRINCH", "HOME ALONE"], movies=7, series=0),
        "Krimi Serien Abend": dict(
            bd=[fake_backdrop((20, 40, 70), (10, 10, 20), 5), fake_backdrop((60, 70, 90), (15, 25, 40), 6)],
            logos=["BROADCHURCH", "TRUE DETECTIVE", "LUTHER"], movies=1, series=5),
        "Sonntag Familienfilme mit der ganzen Bande": dict(
            bd=[fake_backdrop((240, 170, 40), (200, 90, 40), 7), fake_backdrop((40, 150, 220), (20, 60, 140), 8)],
            logos=[], movies=9, series=1),
    }
    for name, s in samples.items():
        cover = render_cover(name, s["bd"], [fake_logo(t) for t in s["logos"]], s["movies"], s["series"], font)
        p = out_dir / (name.replace(" ", "_") + ".jpg")
        p.write_bytes(to_jpeg_bytes(cover))
        # square crop preview (what a 1:1 client would show)
        sq = cover.crop(((cover.width - cover.height) // 2, 0, (cover.width + cover.height) // 2, cover.height))
        sq.resize((540, 540)).save(out_dir / (name.replace(" ", "_") + "_square.jpg"), quality=90)
        print("wrote", p)


def _signature(items: list[dict], name: str) -> str:
    raw = name + "|" + ",".join(i["Id"] for i in items[:60])
    return hashlib.sha1(raw.encode()).hexdigest()


def _run(args) -> int:
    from .jellyfin import Jellyfin

    url = args.url or os.environ.get("JELLYFIN_URL")
    key = args.api_key or os.environ.get("JELLYFIN_API_KEY")
    uid = args.user_id or os.environ.get("JELLYFIN_USER_ID")
    if not (url and key and uid):
        print("JELLYFIN_URL, JELLYFIN_API_KEY und JELLYFIN_USER_ID müssen gesetzt sein.", file=sys.stderr)
        return 2

    jf = Jellyfin(url, key, uid)
    state_path = Path(args.state)
    state = json.loads(state_path.read_text()) if state_path.exists() else {}
    out_dir = Path(args.out) if args.out else None
    if out_dir:
        out_dir.mkdir(parents=True, exist_ok=True)

    for pl in jf.playlists():
        name = pl["Name"]
        if any(fnmatch.fnmatch(name, pat) for pat in args.exclude):
            print(f"skip (excluded): {name}")
            continue
        items = jf.playlist_items(pl["Id"])
        if not items:
            print(f"skip (empty): {name}")
            continue
        sig = _signature(items, name)
        if state.get(pl["Id"]) == sig and not args.force:
            print(f"unchanged: {name}")
            continue

        backdrops, logos = [], []
        # prefer titles that actually have the artwork; spread across the list
        step = max(1, len(items) // 3)
        for it in items[::step]:
            if len(backdrops) < 3:
                bd = jf.image(it["Id"], "Backdrop", 1280)
                if bd:
                    backdrops.append(bd)
        for it in items:
            if len(logos) >= 3:
                break
            lg = jf.image(it["Id"], "Logo", 600)
            if lg:
                logos.append(lg)

        movies = sum(1 for i in items if i.get("Type") == "Movie")
        series = sum(1 for i in items if i.get("Type") in ("Series", "Episode", "Season"))
        cover = render_cover(name, backdrops, logos, movies, series, args.font)
        jpeg = to_jpeg_bytes(cover)

        if out_dir:
            (out_dir / f"{pl['Id']}.jpg").write_bytes(jpeg)
        if args.dry_run:
            print(f"dry-run: {name}")
            continue
        jf.upload_primary(pl["Id"], jpeg)
        state[pl["Id"]] = sig
        state_path.write_text(json.dumps(state, indent=2))
        print(f"uploaded: {name}")
    return 0


def main() -> int:
    p = argparse.ArgumentParser(prog="playlist_covers")
    p.add_argument("--demo", action="store_true", help="Beispiel-Cover ohne Jellyfin rendern")
    p.add_argument("--out", help="Ausgabeordner (Demo: Pflicht-Default ./demo_out)")
    p.add_argument("--url"), p.add_argument("--api-key"), p.add_argument("--user-id")
    p.add_argument("--font", help="Pfad zu einer .ttf")
    p.add_argument("--exclude", action="append", default=[], help="Playlist-Name (Glob) überspringen, mehrfach möglich")
    p.add_argument("--dry-run", action="store_true", help="Rendern, aber nicht hochladen")
    p.add_argument("--force", action="store_true", help="Auch unveränderte Playlists neu erzeugen")
    p.add_argument("--state", default=".playlist_covers_state.json")
    args = p.parse_args()
    if args.demo:
        _demo(Path(args.out or "demo_out"), args.font)
        return 0
    return _run(args)


if __name__ == "__main__":
    sys.exit(main())
