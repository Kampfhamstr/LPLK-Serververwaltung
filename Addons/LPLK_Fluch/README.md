# LPLK_Fluch – Zeitlich begrenzter Hühnerfluch

**Download:** [LPLK_Fluch.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Fluch.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.1**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Administratoren können einen zeitlich begrenzten Effekt aus umkreisenden Hühnern und einer abschließenden Explosion auslösen. Dauer, Umfang und Schaden sind konfigurierbar. Begrenzungen sollen verhindern, dass beliebig viele gleichzeitige Effekte entstehen oder eine alte Aktion einen neu verbundenen Spieler auf demselben Slot trifft.

## Installation und Einrichtung

`LPLK_Fluch.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Fluch/LPLK_Fluch.dll`.

Einstellungen unter `data/LPLK_Fluch/settings.json` bzw. im Modulmenü anpassen. Schaden und Anzahl zuerst zurückhaltend in einer Testumgebung wählen; das ist eine administrative Spielaktion mit Auswirkung auf das Ziel.

## Bedienung und Rechte

`!fluch` startet den vorgesehenen Auswahleinstieg, `!fluchstop` beendet Effekte, `!fluchconfig` und `!lplk_fluch` führen zur Einrichtung. Modulrecht `lplk.fluch.admin`, zusätzlich der konfigurierte historische `AdminPermission`-Zugang; zentrale Verwaltung auch `lplk.admin`. Zielschutz bleibt zu beachten.

## Prüfen

Darstellung, Ablauf, Explosion, Abbruch, Tod und erneuten Beitritt testen. Nach Kartenwechsel oder Entladen dürfen keine verwaisten Effekte zurückbleiben. Die tatsächliche Darstellung und Schadenswirkung im Client sind noch gesondert abzunehmen.
