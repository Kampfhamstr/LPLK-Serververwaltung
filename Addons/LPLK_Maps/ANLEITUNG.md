# LPLK_Maps – Mapwahl, RTV und Nominierungen

**Ausgabe 24.10.2026: 0.4.2.** [LPLK_Maps.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Maps.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.4.0, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Maps.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Bisher veröffentlichtes Paket 0.2.7:** [LPLK_Maps.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/compat-2026.10.16-bridge-maps/LPLK_Maps.zip) · [Prüfstand und Grenzen](../../docs/COMPATIBILITY-20261016.md). Veröffentlichung und Download-Prüfsummen bestätigt.

Aktueller, ausschließlich auf dem Testserver installierter Stand **0.3.1**, gebaut mit SwiftlyS2-API **1.4.14**, mit **1.4.14 und 1.5.0-beta.10** geprüft. Aktuelles Paket: [Build/LPLK_Maps.zip](Build/LPLK_Maps.zip). [Alle Addons](../../docs/ADDONS.md).

## Einstellungen sicher nachladen – 0.3.1

Die Konfigurationsdatei wird alle fünf Sekunden nach Inhalt geprüft, auch wenn ihr Änderungszeitpunkt gleich bleibt. Gültige Formatänderungen, eine andere Schlüsselreihenfolge und Speichern ohne Wertänderung erhalten laufende Abstimmungen. Eine tatsächliche Regeländerung setzt die betroffenen Abstimmungszustände wie bisher zurück; Nominierungen bleiben erhalten.

Adminänderungen lesen zuerst die aktuellen Werte und prüfen die aktuellen vorhandenen Rechte erneut. So überschreibt ein älteres Menü keine inzwischen hinzugefügten Workshop-IDs oder anderen Einstellungen. Ungültige, fehlende oder gesperrte Dateien werden nicht durch Menüänderungen ersetzt; die letzte gültige Konfiguration bleibt aktiv und `reloadError` benennt das Problem. Statusdateifehler unterbrechen die Maplogik nicht.

JSONC mit Kommentaren und abschließendem Komma sowie die bisherige flache Datei oder die Sektion `LPLK_Maps` werden gelesen. Zusätzliche Felder und andere Sektionen bleiben beim Speichern erhalten. Ein tatsächlicher Einstellungswechsel schreibt formatiertes JSON; Kommentare werden dabei nicht erhalten. Inhaltsvergleich und atomarer Dateiaustausch sind kein betriebssystemweiter Vergleich-und-Schreib-Lock.

66 neue lokale Datei-/Menüprüfungen, 59 Mapregelprüfungen und die finale Paket-DLL mit jeweils 325 Prüfungen auf beiden API-Versionen bestanden. Auf dem Testserver sind 0.3.1, die nativen Statusantworten und die Webpanel-Befehle bestätigt. Mapgrenzen, manuelle Sperren, Freigaben und Messbetrieb blieben unverändert. Es wurden keine neuen Grenzen freigegeben. [Nachweis](../../state/maps-weapons-maintenance-verification.json). Mehrspieler-, RTV- und Clienttests folgen auf Benutzerwunsch später.

## Funktionsausbau 0.3.0

Verlängerung, Surf/KZ-Kategorien, Kategorieausgleich, zeitgesteuerter Endvote, direkte Votemap-Stimmen, eigene Admin-Kandidatenliste und zusätzliche Altbefehle sind ergänzt. Bestehende Regeln bleiben erhalten; neue Funktionen werden nicht ungefragt auf Produktion aktiviert. `ExtendInitializeRoundLimits` bietet die explizite alte MapChooser-Nullgrenzen-Semantik. Reale Spieler-/Profilabnahme bleibt offen. [Vollständiger Abgleich](../../docs/ADDON-FUNKTIONSABGLEICH.md) und `state/addon-parity-verification.json` nennen Prüfungen und Grenzen. Der GitHub-Download oben ist noch 0.2.7; der aktuelle lokale Stand 0.3.1 enthält diesen Funktionsausbau.

## Historische Kompatibilitätsprüfung der Version 0.2.7

Zwei `ExecuteCommandWithBuffer`-Aufrufe beim Mapwechsel wurden durch die gemeinsame `ExecuteCommand`-API ersetzt. Die entfernte Puffer-API wird nicht mehr referenziert. Ausführung weiterhin im Spielthread, Bestätigung weiterhin anhand der tatsächlich geladenen Map; Reihenfolge, Wiederholungsgrenze und Mapregeln bleiben erhalten.

Dieselbe DLL bestand lokal jeweils 310 Prüfungen gegen 1.4.14 und 1.5.0-beta.10, einschließlich aller 157 Framework-Verweise. Zusätzlich 93 Addon-Regeltests. Auf dem isolierten Testserver `89450e53` wurden unter beiden Frameworks die eigenen Befehle `sw_lplk_map 3070192462` und `sw_lplk_map de_dust2` ausgeführt: `de_drawbridge` und anschließend `de_dust2` bestätigt, 20 Plugins geladen. Die Bridge transportierte nur den Konsolenbefehl; der Mapwechsel lief durch LPLK_Maps.

Beim damaligen Abschluss blieb der Testserver auf beta.10, LPLK_Maps 0.2.7 und Bridge 1.6.5. Überwachung: 0 API-Befunde und 0 Ladefehler. Andere Plugin-DLLs, Konfigurationen und bestehende Mapfreigaben unverändert. `MeasurementOnly=true` bleibt aktiv. Es waren keine menschlichen Spieler verbunden; Mehrspieler-/RTV-Menüabnahme ist hierdurch nicht abgedeckt. Keine Übernahme auf Produktionsserver ohne gesonderte Freigabe.

Nachweis: [maps-compatibility-verification.json](../../state/maps-compatibility-verification.json). Lokale Wiederholung: `Use-Environment.ps1` einbinden, danach `dotnet run --project tests/MapsCompatibilityChecks/MapsCompatibilityChecks.csproj -c Release` und das Projekt `tests/MapsCompatibilityChecks/Beta/MapsBetaChecks.csproj` ausführen. Die vorhandenen Testserver-Rollbacks für Maps und Framework bleiben erhalten. Die vorherige Maps-Version 0.2.6 nur gemeinsam mit SwiftlyS2 1.4.14 verwenden; Nutzerdaten bei einem Rückwechsel nicht überschreiben.

## Was macht es?

Das Addon bündelt MapChooser, Rock-the-Vote und Nominierungen. Es kann Karten vermessen und daraus Vorschläge für Spielergrenzen ableiten. Nominierungen werden mit den jeweils geltenden Regeln und der aktuellen Spielerzahl geprüft. Unbekannte Messwerte gelten nicht automatisch als bestätigte Kapazität.

## Installation und Einrichtung

`LPLK_Maps.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Maps/LPLK_Maps.dll`.

Regeln liegen in `configs/plugins/LPLK_Maps/config.jsonc`, Sektion `LPLK_Maps`. Unter `data/LPLK_Maps/` liegen Katalog, Verlauf und Status. Einen passenden Mapkatalog einrichten und sicherstellen, dass der Server die tatsächlichen Karten/Workshop-Inhalte laden kann. Ein Katalogeintrag allein installiert keine Karte.

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

