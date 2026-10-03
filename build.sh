#!/usr/bin/env bash
# Builds dist/playlist-covers_<version>.zip (+ manifest.json entry) for installation in Jellyfin.
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-0.1.0.0}"
TARGET_ABI="12.1.0.0"
PROJECT=Jellyfin.Plugin.PlaylistCovers
OUT=dist/stage

rm -rf dist && mkdir -p "$OUT"
dotnet publish "$PROJECT" -c Release -o "$OUT" -p:AssemblyVersion="$VERSION" -p:FileVersion="$VERSION"

# Server provides these; shipping them would shadow the host's copies.
find "$OUT" -maxdepth 1 -type f \( -name "Jellyfin.*.dll" -o -name "MediaBrowser.*.dll" -o -name "Emby.*.dll" -o -name "SkiaSharp*.dll" -o -name "*.pdb" -o -name "*.deps.json" \) ! -name "$PROJECT.dll" -delete

TS="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$OUT/meta.json" <<JSON
{
  "guid": "6f1c3a52-8d3e-4b7a-9a41-2c5e7d90b1f4",
  "name": "Playlist Covers",
  "description": "Erzeugt hochwertige Cover für Film- und Serien-Playlists.",
  "overview": "Streaming-Look für Playlist-Cover",
  "owner": "jpokorny312",
  "category": "General",
  "version": "$VERSION",
  "targetAbi": "$TARGET_ABI",
  "framework": "net10.0",
  "timestamp": "$TS",
  "status": "Active",
  "autoUpdate": true
}
JSON

ZIP="dist/playlist-covers_${VERSION}.zip"
(cd "$OUT" && zip -qr "../$(basename "$ZIP")" .)
MD5="$(md5sum "$ZIP" | cut -d' ' -f1)"

cat > dist/manifest.json <<JSON
[
  {
    "guid": "6f1c3a52-8d3e-4b7a-9a41-2c5e7d90b1f4",
    "name": "Playlist Covers",
    "description": "Erzeugt hochwertige Cover für Film- und Serien-Playlists.",
    "overview": "Streaming-Look für Playlist-Cover",
    "owner": "jpokorny312",
    "category": "General",
    "versions": [
      {
        "version": "$VERSION",
        "changelog": "Erste Version",
        "targetAbi": "$TARGET_ABI",
        "sourceUrl": "REPLACE_WITH_DOWNLOAD_URL/$(basename "$ZIP")",
        "checksum": "$MD5",
        "timestamp": "$TS"
      }
    ]
  }
]
JSON
echo "Built $ZIP (md5 $MD5)"
