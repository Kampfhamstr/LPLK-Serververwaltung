# LPLK_Admin – Spieler- und Adminmenüs

Quellstand **0.3.7**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

LPLK_Admin bündelt Spielerfunktionen und Serveradministration in kurzen Untermenüs. Statt einer langen Liste gibt es thematische Gruppen, etwa für Wirtschaft, Maps, Sounds und Statistik. Spieler sehen Funktionen nur, wenn das entsprechende Addon geladen und die Aktion für sie verfügbar ist. Berechtigte Administratoren erhalten außerdem Zugang zur Verwaltung und zu Moduleinstellungen.

## Installieren und einrichten

Das vollständige Paket `LPLK_Admin.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ergebnis: `game/csgo/addons/swiftlys2/plugins/LPLK_Admin/LPLK_Admin.dll`.

Die gewünschten Fachmodule zusätzlich installieren. Das Adminaddon ersetzt weder Economy noch Maps, Sounds oder andere Module. Rechte in SwiftlyS2 passend zu den Aufgaben vergeben. Modulverwaltung und einzelne Spieleraktionen prüfen ihre eigenen Rechte und Zielimmunitäten; ein sichtbares Adminmenü ist keine pauschale Erlaubnis für jede Aktion. Für die Websiteanbindung außerdem deren Rechte und die Bridge einrichten.

## Bedienung

| Befehl | Zweck |
|---|---|
| `!menu` | Gruppiertes Spielermenü; mit ausreichenden Rechten auch Einstieg ins Adminmenü |
| `!admin` | Direkter Einstieg in die Administration |
| `!adminmenu` | Weiterer Einstieg in dieselbe Administration |

Das Recht `lplk.admin` wird für die übergreifende Modulverwaltung berücksichtigt. Die Fachmodule nennen zusätzliche eigene Rechte in ihren Anleitungen. Vorhandene direkte Befehle bleiben neben den Menüs nutzbar.

## Nach der Installation prüfen

Mit einem normalen Spieler und einem Administrator testen: richtige Gruppen, keine leeren Kategorien, kurze Seiten, funktionierender Zurückweg und erneute Rechteprüfung nach Änderungen. Eine deaktivierte Spielerfunktion muss verschwinden; berechtigte Administratoren sollen die Einstellungen des weiterhin geladenen Moduls erreichen können. Die vollständige Bedienabnahme mit realen Clients steht noch aus.
