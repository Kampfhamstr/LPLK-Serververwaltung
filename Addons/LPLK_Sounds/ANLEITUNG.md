# LPLK_Sounds – Sounds und persönliche Hörfilter

**Download:** [LPLK_Sounds.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Sounds.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.7**, SwiftlyS2-API **1.4.13**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

Spieler können freigegebene Sounds auswählen, an Empfänger senden und ihre eigene Wiedergabe einstellen. Für Administratoren gibt es einen getrennten Ansagebereich. Das Modul enthält keine Deathcards; der bestehende Spielerbefehl `!sound` bleibt erhalten.

## Installation und Einrichtung

`LPLK_Sounds.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Sounds/LPLK_Sounds.dll`.

Die eigentlichen Audiodateien müssen als passende Workshop-Inhalte vorhanden und gemountet sein. Dafür [AddonsManager](../AddonsManager/ANLEITUNG.md) einrichten, die vorgesehenen Workshop-IDs zuordnen und den Clientdownload prüfen. Katalogmetadaten ersetzen keine Audiodateien. Bestehende Soundnummern bei Katalogänderungen berücksichtigen, damit Binds weiterhin denselben Sound treffen.

Konfiguration: `configs/plugins/LPLK_Sounds/config.json`, relativ zum SwiftlyS2-Ordner. Persönliche Einstellungen und Cache liegen unter `data/LPLK_Sounds/`, unter anderem `sound-listening.json` und `workshop-catalog-cache.json`. Bestehende Hörpräferenzen erhalten.

## Bedienung und Rechte

| Befehl | Zweck |
|---|---|
| `!sound` bzw. `!sound <Nummer>` | Menü öffnen bzw. Sound auswählen |
| `!sound s<Seite>`, `!sounds [Seite]` | Im Katalog blättern / Liste lesen |
| `!soundsend [Soundnummer]` | Sound an einen Spieler senden |
| `!soundvolume <0-100>` | Eigene Lautstärke einstellen |
| `!lplk_sound_listen <on\|off>` | Eigenes Hören ein-/ausschalten |
| `!adminsounds`, `!adminsound` | Geschützter Adminbereich |
| `!soundall` | Geschützte Ansage mit abweichender Behandlung persönlicher Hörfilter |
| `!lplk_sounds` | Moduleinstellungen/Status |

Modulrecht `lplk.sounds.admin`; besondere Ansagen unterliegen zusätzlichen konfigurierten Prüfungen. Die Sounds müssen nach den aktiven Empfänger-, Rundengrenz- und Cooldownregeln verfügbar sein.

## Prüfen

Mit mindestens zwei Clients normalen Sound, privaten Versand, Lautstärke, Hörfilter und eine berechtigte Adminansage hörbar testen. „Im Katalog“ und „precache erfolgreich“ sind noch kein Beweis für Clientwiedergabe. Die aktuelle Quellversion ist neuer als ein Teil der produktiv eingesetzten Pakete; dieser Upload aktualisiert keinen Server.
