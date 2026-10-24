# LPLK_Statistics – Spieler-, Waffen- und Bombenstatistik

**Ausgabe 24.10.2026: 0.2.5.** [LPLK_Statistics.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Statistics.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.2.4, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Statistics.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_Statistics.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Statistics.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.2.3**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was wird erfasst?

Das Modul führt unter anderem Kills, Tode, K/D, Headshots, Assists und Schaden. PvP und Bottraining bleiben getrennt. Waffenwertungen umfassen 44 Waffen-/Granatentypen mit getrennten Kategorien für Messer und Zeus/Taser. Messer-Varianten werden zu einer Wertung zusammengefasst.

Bomben **gelegt** und **entschärft** sind eigene Zähler. Weitere Spezialwerte berücksichtigen etwa besondere Kill-Situationen und bestätigte Waffenherkunft. Ein Kill mit einer aufgehobenen Waffe kann nur dann entsprechend zählen, wenn die Herkunft tatsächlich erfasst wurde. Neue Zähler rekonstruieren keine früheren Ereignisse.

## Installation und Einrichtung

`LPLK_Statistics.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Statistics/LPLK_Statistics.dll`.

Unter `data/LPLK_Statistics/` liegen `settings.json`, `statistics.json`, Status und der Webexport in `web/`. Spielerdateien heißen dort beispielsweise `<SteamID64>.json`; außerdem existieren Ranglistenexporte. Diese Dateien sind Nutzerdaten und gehören nicht ins Repository.

PvP-, Bot-, Waffen-, Warmup- und Teamkillregeln gezielt prüfen. Historische Fremddaten nur mit einem passenden, ausdrücklich vorbereiteten Offline-Import übernehmen. Beim Update aktuelle Daten und den Beginn neuer Zähler erhalten.

## Bedienung und Rechte

| Befehl | Zweck |
|---|---|
| `!stats` | Eigene Statistikmenüs |
| `!hs`, `!top` | Einstieg in den PvP-Bereich |
| `!weaponmenu`, `!weaponkills`, `!weaponstats` | Waffenwerte |
| `!weapontop` | Waffenrangliste |
| `!nademenu`, `!nadekills`, `!nadestats` | Granatenwerte |
| `!nadetop` | Granatenrangliste |
| `!stattrak` | Einstieg in die Waffenanzeige |
| `!lplk_statistics` | Modulverwaltung/Status |

Weitere bestehende Aliase bleiben vorhanden, etwa `!wkills`, `!wmenu`, `!wtop`, `!wstats`, `!nades`, `!ntop`, `!nstats` und `!nstat`. Modulrecht `lplk.statistics.admin`, zentrale Verwaltung auch `lplk.admin`.

## Website und Prüfung

Die Webverwaltung liest vorbereitete Exporte statt für jede Seitenansicht sämtliche Ereignisse im Spiel neu auszuwerten. Die aktuelle Anbindung ist weiterhin auf das freigegebene Testprofil begrenzt. Öffentliche Statistikseiten und historische Fortschrittskurven sind noch nicht implementiert. Der vorhandene Zählerbestand allein liefert keine rückwirkenden Tagesverläufe.

Mit bekannten Testereignissen PvP/Bots, Messer, Taser, aufgehobene Waffen, Bombenlegen und Entschärfen getrennt prüfen. Nicht alle Änderungen werden sofort dauerhaft geschrieben; ein Absturz kann noch nicht gespeicherte Werte verlieren. Lokale Lasttests mit künstlichen Daten sind keine Performancegarantie für einen gefüllten Gameserver.


## Teststand 19.10.2026

Ranglisten, vollständige gültige Zahlenbereiche, zusätzliche Schadensoptionen und Waffen-HUD-Einstellungen sind im Adminmenü erreichbar. Änderungen erhalten vorhandene Zähler und persönliche HUD-Auswahl. Reine Formatänderungen lassen aktuelle Schuss-/Schadenszuordnungen erhalten. Bei echten Regeländerungen werden neue Ranglistenexporte eingeplant; eine schon laufende alte Ausgabe kann noch vor der unmittelbar anschließenden Neuausgabe fertig werden.

Konfiguration wird anhand des Inhalts geprüft, auch bei unverändertem Dateizeitstempel. Menüänderungen lesen vorher die aktuelle gültige Datei und überschreiben keine ungültige externe Änderung. Letzte gültige Regeln bleiben bei Lesefehlern erhalten.

Nur Testserver 89450e53: 20 Komponenten geladen, 36 Webbefehle, aktuelle native Statusantworten und Daten-/Einstellungshashes geprüft. 168 zusätzliche Wartungsprüfungen und 213 bisherige Statistik/Demoprüfungen; beide SwiftlyS2-APIs 1.4.14 und 1.5.0-beta.10. Clienttests ausdrücklich später. Beleg `state/recording-maintenance-verification.json`.

Das aktuelle lokale Paket liegt in `Build/`. Der oben gegebenenfalls verlinkte GitHub-Download ist die frühere veröffentlichte Ausgabe und enthält diese Änderung noch nicht. Rückfall: vorige DLL und `.lplk-rollback/LPLK_Statistics.data-before-maintenance.zip` erhalten. Vor Wiederherstellung spätere Nutzerdaten berücksichtigen; aktuelle Daten nicht blind durch Sicherung überschreiben.
