# Jellyfin Playlist Cover Generator

Erzeugt hochwertige Cover für Film-/Serien-Playlists in Jellyfin (Streaming-Look).

- 16:9-Cover aus bis zu 3 Backdrops (weich überblendet), abgedunkelt und mit Akzentfarbe aus den Bildern getönt
- Playlist-Name groß, darunter „12 Filme · 3 Serien" und die Titel-Logos der Einträge
- Name und Logos liegen in der **quadratischen Mitte**: auf dem Handy (1:1-Zuschnitt) bleibt alles sichtbar, am TV nutzt das Cover die volle Breite

## Benutzung

```bash
pip install -r requirements.txt

# Vorschau ohne Jellyfin (Beispielbilder landen in ./demo_out)
python -m playlist_covers --demo

# Gegen den echten Server (zuerst ohne Upload)
export JELLYFIN_URL=http://jellyfin:8096
export JELLYFIN_API_KEY=...     # Dashboard → API-Schlüssel
export JELLYFIN_USER_ID=...     # Benutzer-ID aus der Benutzerverwaltung
python -m playlist_covers --dry-run --out preview
python -m playlist_covers --exclude "Handgemacht*"
```

Unveränderte Playlists werden übersprungen (`--force` erzwingt Neuaufbau).
Mit `--exclude` bleiben von Hand gestaltete Cover unangetastet.

## Schrift
Ohne eigene Schrift wird eine Systemschrift genutzt. Für den besten Look eine
`.ttf` (z. B. `Montserrat-Bold.ttf`) in `fonts/` legen oder `--font` angeben.
