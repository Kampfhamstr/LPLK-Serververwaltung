# LPLK_AntiCamp – Camping erkennen und begrenzen

**Download:** [LPLK_AntiCamp.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_AntiCamp.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.3**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Addon beobachtet Aufenthaltsdauer und Bewegung innerhalb eines eingestellten Bereichs. Nach der vorgesehenen Zeit warnt es und führt die konfigurierte Reaktion aus. Die Auswertung berücksichtigt verstrichene Zeit; sie zählt nicht einfach Frames. Einsatz und Grenzen müssen zum Spielmodus passen.

## Installation und Einrichtung

`LPLK_AntiCamp.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel-DLL: `game/csgo/addons/swiftlys2/plugins/LPLK_AntiCamp/LPLK_AntiCamp.dll`.

Den erzeugten Daten-/Konfigurationsbestand unter `data/LPLK_AntiCamp/` erhalten. Campdauer, Warnzeit, Bewegungsradius, Bestrafung und Gesundheitsreserve im Modulmenü passend einstellen. Auf Surf-, Zombie- oder AFK-Situationen abgestimmte Regeln separat testen.

## Bedienung und Rechte

`!lplk_anticamp` öffnet für Berechtigte die Verwaltung. Der direkte Befehl verlangt `lplk.anticamp.admin`. Über LPLK_Admin ist das Modul ebenfalls erreichbar, sofern die zentrale Rechteprüfung den Zugriff erlaubt. Für Spieler ist kein Aktivierungsbefehl erforderlich.

## Prüfen

Stillstehen bis zur Warnung, rechtzeitiges Wegbewegen, Strafe, Tod und Rundenwechsel kontrollieren. Besonders wichtig: Eine Warnung darf nicht unbemerkt in einer anderen Runde oder für einen neuen Spieler auf demselben Slot weiterlaufen. Erst mit zurückhaltenden Testwerten prüfen, bevor die Regel für ein vollständiges Spielprofil übernommen wird.
