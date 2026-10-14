# LPLK_Admin – Spieler- und Adminmenüs

**Download:** [LPLK_Admin.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Admin.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.5.0**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

LPLK_Admin bietet unter `!admin` vier kurze Einstiege: **Spieler**, **Maps**, **Sounds** und **Erweitert**. Spieler öffnet direkt die Spielerliste; Maps und Sounds öffnen ihr Fachmodul ohne Zwischenkategorie. Seltene Aktionen und übrige Module stehen in einer flachen, blätterbaren Liste unter Erweitert. Das öffentliche `!menu` bleibt nach Spielerfunktionen gruppiert. Spieler sehen Funktionen nur, wenn das entsprechende Addon geladen und die Aktion für sie verfügbar ist. Berechtigte Administratoren erhalten außerdem Zugang zur Verwaltung und zu Moduleinstellungen.

## Installieren und einrichten

Das vollständige Paket `LPLK_Admin.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ergebnis: `game/csgo/addons/swiftlys2/plugins/LPLK_Admin/LPLK_Admin.dll`.

Die gewünschten Fachmodule zusätzlich installieren. Das Adminaddon ersetzt weder Economy noch Maps, Sounds oder andere Module. Rechte in SwiftlyS2 passend zu den Aufgaben vergeben. Modulverwaltung und einzelne Spieleraktionen prüfen ihre eigenen Rechte und Zielimmunitäten; ein sichtbares Adminmenü ist keine pauschale Erlaubnis für jede Aktion. Für die Websiteanbindung außerdem deren Rechte und die Bridge einrichten.

## Bedienung

| Befehl | Zweck |
|---|---|
| `!menu` | Gruppiertes Spielermenü; mit ausreichenden Rechten auch Einstieg ins Adminmenü |
| `!admin` | Direkter Einstieg in die Administration |
| `!adminmenu` | Weiterer Einstieg in dieselbe Administration |

Nach der Spielerauswahl stehen alle erlaubten Spieleraktionen in derselben Liste: **Kicken, Bannen, Chat, Team, Töten, Respawn, Hinteleportieren, Herholen, Einfrieren, Freigeben, Heilen, Rüsten, Noclip an, Noclip aus und Nachricht**. Bei geladenen, aktiven und freigegebenen Fachmodulen kommen **Verliererteam, Fluch, Granateneffekt, Konto und Sound** hinzu. Lange Listen scrollen mit höchstens fünf sichtbaren Zeilen. Alte Befehle und Binds bleiben erhalten.

`!menu` bietet **Maps, Sounds, Waffen, Kamera, Wirtschaft, Info** sowie für berechtigte Nutzer **Adminmenü**. Deaktivierte Funktionen und leere Funktionsgruppen werden ausgeblendet; Info enthält die allgemeine Bedienhilfe. Sounds fragen nach Auswahl **„An alle?“ → Ja/Nein**. Nein öffnet die Spielerwahl, berechtigte öffentliche Adminstarts fragen anschließend **„Override?“ → Ja/Nein**. Globale Soundkonfiguration liegt unter Einstellungen. Maps bietet **RTV, Nominieren, Mapwechsel, Maptest und Einstellungen**; im reinen Prüfmodus entfallen RTV/Nominieren.

Das Recht `lplk.admin` wird für die übergreifende Modulverwaltung berücksichtigt. Die Fachmodule nennen zusätzliche eigene Rechte in ihren Anleitungen. Vorhandene direkte Befehle bleiben neben den Menüs nutzbar.

## Nach der Installation prüfen

Mit einem normalen Spieler und einem Administrator testen: richtige Gruppen, keine leeren Kategorien, kurze Seiten, funktionierender Zurückweg und erneute Rechteprüfung nach Änderungen. Eine deaktivierte Spielerfunktion muss verschwinden; berechtigte Administratoren sollen die Einstellungen des weiterhin geladenen Moduls erreichen können. Version 0.5.0 wird zunächst ausschließlich auf dem Testserver 11110004 geprüft. Eine erfolgreiche automatische Prüfung ersetzt die Bedienabnahme im Spiel nicht.
