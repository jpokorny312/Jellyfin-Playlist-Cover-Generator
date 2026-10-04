#!/usr/bin/env bash
# Builds dist/playlist-covers_<version>.zip (plugin DLL + meta.json) for Jellyfin.
# Usage: ./build.sh [version]   (4-part, default 0.3.0.0)
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-0.3.0.0}"
TARGET_ABI="12.1.0.0"
PROJECT=Jellyfin.Plugin.PlaylistCovers
OUT=dist/stage

rm -rf dist && mkdir -p "$OUT"
dotnet publish "$PROJECT" -c Release -o "$OUT" -p:AssemblyVersion="$VERSION" -p:FileVersion="$VERSION"

# The server provides these; shipping them would shadow the host's copies.
find "$OUT" -maxdepth 1 -type f \( -name "Jellyfin.*.dll" -o -name "MediaBrowser.*.dll" -o -name "Emby.*.dll" \
  -o -name "SkiaSharp*.dll" -o -name "*.pdb" -o -name "*.deps.json" \) ! -name "$PROJECT.dll" -delete

cat > "$OUT/meta.json" <<JSON
{
  "guid": "6f1c3a52-8d3e-4b7a-9a41-2c5e7d90b1f4",
  "name": "Playlist Covers",
  "description": "Erzeugt Poster-Cover im Streaming-Look für Film- und Serien-Playlists.",
  "overview": "Poster-Cover im Streaming-Look für Playlists",
  "owner": "jpokorny312",
  "category": "General",
  "version": "$VERSION",
  "targetAbi": "$TARGET_ABI",
  "framework": "net10.0",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "status": "Active",
  "autoUpdate": true
}
JSON

ZIP="dist/playlist-covers_${VERSION}.zip"
(cd "$OUT" && zip -qr "../$(basename "$ZIP")" .)
echo "Built $ZIP (md5 $(md5sum "$ZIP" | cut -d' ' -f1))"
