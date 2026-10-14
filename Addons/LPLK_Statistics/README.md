# LPLK_Statistics – Spieler-, Waffen- und Bombenstatistik

Quellstand **0.2.1**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

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
