# LPLK_Maps – Mapwahl, RTV und Nominierungen

**Download:** [LPLK_Maps.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Maps.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.2.6**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

Das Addon bündelt MapChooser, Rock-the-Vote und Nominierungen. Es kann Karten vermessen und daraus Vorschläge für Spielergrenzen ableiten. Nominierungen werden mit den jeweils geltenden Regeln und der aktuellen Spielerzahl geprüft. Unbekannte Messwerte gelten nicht automatisch als bestätigte Kapazität.

## Installation und Einrichtung

`LPLK_Maps.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Maps/LPLK_Maps.dll`.

Unter `data/LPLK_Maps/` liegen unter anderem `settings.json`, `maps.json`, `history.json` und `status.json`. Einen passenden Mapkatalog einrichten und sicherstellen, dass der Server die tatsächlichen Karten/Workshop-Inhalte laden kann. Ein Katalogeintrag allein installiert keine Karte.

**MeasurementOnly** ist ein eigener Messbetrieb: Abstimmungen und normale Spieleraktionen sind dann nicht einfach automatisch freigeschaltet. Neue Messergebnisse und vorgeschlagene Grenzen vor der Übernahme prüfen. Manuelle Ausschlüsse erhalten; fehlgeschlagene Messungen nicht in Nullwerte umwandeln. Automatische Messläufe nur in einer dafür vorgesehenen leeren Testumgebung durchführen.

## Bedienung

| Befehl | Zweck |
|---|---|
| `!rtv` | Stimme für eine vorzeitige Mapabstimmung, sofern verfügbar |
| `!nominate` | Passende Karte nominieren |
| `!mapchooser`, `!votemap` | Einstieg in die Mapwahl nach den aktiven Regeln |
| `!lplk_maps` | Modulverwaltung/Status |
| `!lplk_map` | Administrativer Mapwechsel; Argumente und Ziel prüfen |

Modulrecht: `lplk.maps.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Nominierungen mit zulässiger und unzulässiger Spielerzahl, ausgeschlossene Karten, Voteabbruch und tatsächlichen Mapwechsel prüfen. Messungen sind keine Freigabe, den produktiven Mappool oder seine Grenzen zu verändern.
