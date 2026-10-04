# Jellyfin Playlist Covers

Plugin für den Jellyfin-Server, das für **Film- und Serien-Playlists** hochwertige Cover erzeugt – statt der
zusammengewürfelten Standard-Collage. Es läuft direkt im Server, ohne externes Tool.

<p>
  <img src="docs/preview-hero.jpg" width="200" alt="Held + Stapel (3 Titel)">
  <img src="docs/preview-mosaic.jpg" width="200" alt="Mosaik (5 Titel)">
  <img src="docs/preview-wall.jpg" width="200" alt="Poster-Wand (12 Titel)">
</p>

> Die Beispiele sind mit erfundenen Platzhalter-Postern gerendert. Im Betrieb verwendet das Plugin die echten Poster
> der Titel aus deiner Bibliothek.

## Funktionen

- **Nur Poster, kein Text:** Der Playlist-Name steht in jedem Jellyfin-Client ohnehin neben der Karte, deshalb zeigt das Cover
  ausschließlich die Poster der Titel und füllt die ganze Karte (2:3, 1200×1800).
- **Drei Stile, automatisch nach Titelzahl gewählt:**
  - **Poster-Wand** (ab 6 Titeln): leicht schräge, gestaffelte Posterwand über die ganze Fläche
  - **Mosaik** (4–5 Titel): 2×2- bzw. 3×3-Raster mit hauchdünnen Fugen, nach Farbe sortiert
  - **Held + Stapel** (1–3 Titel): das erste Poster groß, dahinter schauen weitere hervor, auf unscharfem Hintergrund
- Einheitliche Farbkorrektur, Vignette und feines Filmkorn, damit auch bunt gemischte Poster zusammenpassen.
- Bei Serien, Staffeln und Episoden wird das Poster der **Serie** verwendet.
- Läuft als **geplante Aufgabe** (beim Serverstart und täglich um 04:00) und lässt sich jederzeit von Hand starten.
  Es werden nur Playlists neu gerendert, deren Inhalt sich geändert hat – oder deren Cover von etwas anderem überschrieben wurde.
- **Ausschlussmuster** für Playlists, die ihr eigenes Cover behalten sollen.

## Voraussetzungen

- Jellyfin **12.1** oder neuer
- Playlists mit Filmen und/oder Serien, die Poster haben (Metadaten-Anbieter wie TMDb)

## Installation (Plugin-Katalog)

1. Jellyfin-Dashboard → **Plugins** → **Repositories** → **+**
2. Eintragen:
   - **Name:** `Playlist Covers`
   - **URL:** `https://github.com/jpokorny312/Jellyfin-Playlist-Cover-Generator/releases/latest/download/manifest.json`
3. Speichern, dann unter **Plugins → Katalog** nach **Playlist Covers** suchen und installieren.
4. Jellyfin neu starten.
5. Dashboard → **Geplante Aufgaben** → **Playlist-Cover erzeugen** → ▶ starten.

Updates erscheinen danach ganz normal im Katalog.

### Manuelle Installation

ZIP aus dem [neuesten Release](https://github.com/jpokorny312/Jellyfin-Playlist-Cover-Generator/releases/latest)
nach `<Jellyfin-Datenordner>/plugins/PlaylistCovers/` entpacken und Jellyfin neu starten.

## Einstellungen

Dashboard → **Plugins** → **Playlist Covers**

| Einstellung | Bedeutung |
|---|---|
| Stil | **Automatisch** (Standard) oder fest Poster-Wand, Mosaik bzw. Held + Stapel. Nach einem Wechsel werden alle Cover beim nächsten Lauf neu erzeugt. |
| Playlists ausschließen | Eine pro Zeile, `*` und `?` sind erlaubt (z. B. `Handgemacht*`). Diese Playlists behalten ihr Cover. |

## Hinweise

- Das Plugin ersetzt das **Primärbild** der Playlist. Cover, die du von Hand gesetzt hast, werden überschrieben –
  nimm solche Playlists in die Ausschlussliste auf.
- Jellyfin erzeugt für Playlists selbst eine Collage. Sollte sie das Plugin-Cover nach einer Metadaten-Aktualisierung
  ersetzen, erkennt das Plugin das beim nächsten Lauf und rendert neu.
- Clients legen einen Zähler (oben rechts) und eine Fortschrittsleiste (unten) über die Karte; die Stile halten diese Bereiche ruhig.
- Playlists ohne Poster-Bilder werden übersprungen.

## Entwicklung

Voraussetzung: .NET-10-SDK.

```bash
./build.sh [version]                                # baut dist/playlist-covers_<version>.zip
dotnet run --project Demo -c Release -- demo_out    # rendert Beispiel-Cover (alle Stile, Kartenvorschau) ohne Server
```

Projektaufbau:

- `Jellyfin.Plugin.PlaylistCovers/` – das Plugin (`CoverRenderer*.cs` = Rendering mit SkiaSharp, `CoverRenderer.Styles.cs` = die drei Stile, `PlaylistCoverService.cs` = Anbindung an Jellyfin)
- `Demo/` – Konsolenprogramm, das Beispiel-Cover aus erfundenen Bildern rendert
- `scripts/update_manifest.py` – pflegt das Plugin-Repository-Manifest

### Release veröffentlichen

Entweder per Tag:

```bash
git tag v0.3.0 && git push origin v0.3.0
```

oder auf GitHub: **Releases → Draft a new release**, Tag `v0.3.0` neu anlegen, veröffentlichen.

In beiden Fällen läuft `.github/workflows/release.yml`: Er baut das Plugin, hängt ZIP und `manifest.json` an das Release
und ergänzt das bisherige Manifest um die neue Version, sodass ältere Versionen installierbar bleiben.
Das Repository-Manifest ist immer unter `releases/latest/download/manifest.json` erreichbar.
Die Versionsnummer kommt aus dem Tag (`v0.3.0` → `0.3.0.0`).
