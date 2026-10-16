# LPLK_Maps – Mapwahl, RTV und Nominierungen

**Aktuelles Paket 0.2.7:** [LPLK_Maps.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/compat-2026.10.16-bridge-maps/LPLK_Maps.zip) · [Prüfstand und Grenzen](../../docs/COMPATIBILITY-20261016.md).

Aktueller Quellstand **0.2.7**, gebaut mit SwiftlyS2-API **1.4.14**, mit **1.4.14 und 1.5.0-beta.10** geprüft. Aktuelles Paket: [Build/LPLK_Maps.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/compat-2026.10.16-bridge-maps/LPLK_Maps.zip). [Alle Addons](../../docs/ADDONS.md).

## Kompatibilitätsprüfung vom 16.10.2026

Zwei `ExecuteCommandWithBuffer`-Aufrufe beim Mapwechsel wurden durch die gemeinsame `ExecuteCommand`-API ersetzt. Die entfernte Puffer-API wird nicht mehr referenziert. Ausführung weiterhin im Spielthread, Bestätigung weiterhin anhand der tatsächlich geladenen Map; Reihenfolge, Wiederholungsgrenze und Mapregeln bleiben erhalten.

Dieselbe DLL bestand lokal jeweils 310 Prüfungen gegen 1.4.14 und 1.5.0-beta.10, einschließlich aller 157 Framework-Verweise. Zusätzlich 93 Addon-Regeltests. Auf dem isolierten Testserver `Beispiel-Testserver` wurden unter beiden Frameworks die eigenen Befehle `sw_lplk_map 3070192462` und `sw_lplk_map de_dust2` ausgeführt: `de_drawbridge` und anschließend `de_dust2` bestätigt, 20 Plugins geladen. Die Bridge transportierte nur den Konsolenbefehl; der Mapwechsel lief durch LPLK_Maps.

Testserver bleibt auf beta.10, LPLK_Maps 0.2.7 und Bridge 1.6.5. Überwachung: 0 API-Befunde und 0 Ladefehler. Andere Plugin-DLLs, Konfigurationen und bestehende Mapfreigaben unverändert. `MeasurementOnly=true` bleibt aktiv. Es waren keine menschlichen Spieler verbunden; Mehrspieler-/RTV-Menüabnahme ist hierdurch nicht abgedeckt. Keine Übernahme auf Produktionsserver ohne gesonderte Freigabe.

Nachweis: [Kompatibilitätsnachweis](../../docs/COMPATIBILITY-20261016.md). Lokale Wiederholung: `Use-Environment.ps1` einbinden, danach `dotnet run --project tests/MapsCompatibilityChecks/MapsCompatibilityChecks.csproj -c Release` und das Projekt `tests/MapsCompatibilityChecks/Beta/MapsBetaChecks.csproj` ausführen. Die vorhandenen Testserver-Rollbacks für Maps und Framework bleiben erhalten. Die vorherige Maps-Version 0.2.6 nur gemeinsam mit SwiftlyS2 1.4.14 verwenden; Nutzerdaten bei einem Rückwechsel nicht überschreiben.

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

