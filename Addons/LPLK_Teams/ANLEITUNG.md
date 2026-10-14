# LPLK_Teams – Teamwechselregeln

**Download:** [LPLK_Teams.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Teams.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.1**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Modul begrenzt Teamwechsel nach konfigurierten Regeln und schützt beispielsweise das führende Team vor unerwünschtem Zulauf. Es berücksichtigt Spielstand und Seitenwechsel. Ausgewählte Spieler können für dafür vorgesehene Regeln gesondert geführt werden.

## Installation und Einrichtung

`LPLK_Teams.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Teams/LPLK_Teams.dll`.

Unter `data/LPLK_Teams/` Einstellungen, `selected-players.json` und Status getrennt erhalten. Teamregeln zum Spielmodus passend setzen. Automatische Teamverteilung anderer Plugins und administrative Wechsel müssen mit diesen Regeln zusammenpassen; bei Websiteaktionen auch die Bridge-Prüfung beachten.

## Bedienung und Rechte

`!lplk_teams` öffnet die Verwaltung. Die bisherigen Einstiege `!tsw` und `!tsbscore` bleiben vorhanden. Modulrecht `lplk.teams.admin`; zusätzlich wird das historische Recht `wl.admin` für den vorgesehenen Kompatibilitätszugriff berücksichtigt. Die zentrale Verwaltung kann auch `lplk.admin` verwenden.

## Prüfen

Teamwechsel bei Gleichstand und Führung, Halbzeit, Beobachterstatus sowie administrative Änderungen prüfen. Die Liste ausgewählter Spieler ist Nutzerdatenbestand und gehört nicht in den Quellcodeupload.
