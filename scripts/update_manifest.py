#!/usr/bin/env python3
"""Adds a plugin version to a Jellyfin plugin-repository manifest.

usage: update_manifest.py <previous.json> <out.json> <version> <zip> <source_url> [changelog]

The previous manifest (may be empty/missing) keeps older versions installable.
"""
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

TARGET_ABI = "12.1.0.0"
PLUGIN = {
    "guid": "6f1c3a52-8d3e-4b7a-9a41-2c5e7d90b1f4",
    "name": "Playlist Covers",
    "description": "Erzeugt hochwertige Cover für Film- und Serien-Playlists.",
    "overview": "Poster-Cover im Streaming-Look für Playlists",
    "owner": "jpokorny312",
    "category": "General",
}


def main() -> None:
    prev, out, version, zip_path, url = sys.argv[1:6]
    changelog = sys.argv[6] if len(sys.argv) > 6 else ""

    try:
        manifest = json.loads(Path(prev).read_text())
    except (OSError, ValueError):
        manifest = []
    entry = next((p for p in manifest if p.get("guid") == PLUGIN["guid"]), None)
    if entry is None:
        entry = {**PLUGIN, "versions": []}
        manifest.append(entry)
    entry.update(PLUGIN)

    md5 = hashlib.md5(Path(zip_path).read_bytes()).hexdigest()
    new = {
        "version": version,
        "changelog": changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": url,
        "checksum": md5,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    entry["versions"] = [new] + [v for v in entry.get("versions", []) if v.get("version") != version]
    Path(out).write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
    print(f"manifest: {version} md5={md5}")


if __name__ == "__main__":
    main()
