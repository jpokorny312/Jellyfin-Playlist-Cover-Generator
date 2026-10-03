# Jellyfin Playlist Cover Generator (Plugin)

Jellyfin-Server-Plugin für Jellyfin **12.1** (.NET 10), das für Film-/Serien-Playlists
hochwertige Cover im Streaming-Look erzeugt. Es läuft direkt im Server, ohne externes Tool.

- 16:9-Cover aus bis zu 3 Backdrops (weich überblendet), abgedunkelt und mit einer aus den Bildern berechneten Akzentfarbe getönt
- Playlist-Name groß, darunter „12 Filme · 3 Serien“ und bis zu 3 Titel-Logos
- Name und Logos liegen in der **quadratischen Mitte**: Clients mit 1:1-Zuschnitt (Handy) zeigen alles, am TV nutzt das Cover die volle Breite
- Bei Serien/Episoden werden Backdrop und Logo der Serie verwendet

## Installation

```bash
./build.sh            # benötigt das .NET-10-SDK → dist/playlist-covers_0.1.0.0.zip
```

ZIP-Inhalt (`Jellyfin.Plugin.PlaylistCovers.dll` + `meta.json`) in einen neuen Ordner
`<Jellyfin-Datenordner>/plugins/PlaylistCovers_0.1.0.0/` entpacken und Jellyfin neu starten.
Alternativ `dist/manifest.json` als eigenes Plugin-Repository hosten (`sourceUrl` anpassen)
und unter Dashboard → Plugins → Repositories eintragen.

## Benutzung

- Dashboard → Geplante Aufgaben → **Playlist-Cover erzeugen**: läuft beim Start und täglich um 04:00, manuell startbar.
  Nur Playlists, deren Inhalt/Bilder sich geändert haben, werden neu gerendert.
- Dashboard → Plugins → Playlist Covers: Ausschlussmuster (Playlists mit eigenem Cover), optionale Schrift, Groß-/Kleinschreibung.

## Schrift
Ohne Angabe nimmt das Plugin Montserrat/Inter/DejaVu Sans/Liberation Sans, was auf dem Server vorhanden ist.
Eigene Schrift: `.ttf` auf den Server legen und den Pfad in den Plugin-Einstellungen eintragen.

## Entwicklung

```bash
dotnet run --project Demo -c Release -- demo_out   # rendert Beispiel-Cover ohne Server
```
