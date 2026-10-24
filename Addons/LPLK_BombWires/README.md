# LPLK_BombWires – Bombendrähte

**Ausgabe 24.10.2026: 0.1.6.** [LPLK_BombWires.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_BombWires.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.6, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_BombWires.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_BombWires.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_BombWires.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Dieser GitHub-Download enthält weiterhin die ältere veröffentlichte Version. Aktuell geprüftes lokales Paket0.1.5: `Build/LPLK_BombWires.zip`, ausschließlich auf dem Testserver installiert.

Quellstand **0.1.5**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

Das Addon erweitert Bombenlegen und Entschärfen um eine Auswahl aus vier Drahtfarben. Beim Legen wird der sichere Draht gewählt; ohne ausdrückliche Wahl greift die vorgesehene Zufallsauswahl. Beim Entschärfen kann die konfigurierte Drahtaktion verwendet werden. Normales Entschärfen bleibt möglich; Drahtziehen ist optional.

## Installation und Einrichtung

`LPLK_BombWires.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_BombWires/LPLK_BombWires.dll`.

`data/LPLK_BombWires/settings.json` bzw. das Modulmenü verwenden, um Aktivierung und Bombenregeln einzustellen. Für die gemeinsame Menüführung LPLK_Admin installieren. Keine zweite Implementierung derselben Drahtmechanik parallel aktivieren.

## Bedienung und Rechte

Die Drahtaktion reagiert auf die **Spielaktion `+jump`**, nicht auf eine fest verdrahtete physische Leertaste. Standardmäßig ist das die Leertaste; eine andere Sprungtaste soll ebenso funktionieren. Bei einem Mausrad-Bind muss CS2 tatsächlich einen Sprungimpuls liefern. Die echte Clientabnahme verschiedener Mausradbelegungen steht noch aus.

Administration: `!lplk_bombwires`, Modulrecht `lplk.bombwires.admin`, zentrale Verwaltung auch über `lplk.admin`.

## Prüfen

Mit zwei Spielern Legen, Wahl und Zufallsfall, richtigen und falschen Draht sowie Abbruch/Tod/Rundenende prüfen. Sprungbindung mit Tastatur und Mausrad separat testen. Ein erfolgreich geladener Pluginstatus bestätigt noch nicht, dass jede Clientbindung zuverlässig ausgewertet wird.

## Erweiterung0.1.5 vom19.10.2026

Einstellungen und Status sind im Modulmenü getrennt. Höchstens fünf sichtbare Einträge, nummerierte Aktionen und unnummeriertes Zurück; bestehende Farben/Tasten erhalten. Änderungen an settings.json werden etwa alle zwei Sekunden geprüft. Bei ungültigen Dateien bleiben die letzten gültigen Regeln aktiv; Menüänderungen überschreiben die fehlerhafte Datei nicht. `data/LPLK_BombWires/status.json` meldet `reloadError`. Datei korrigieren, dann wird automatisch erneut geladen.

Menüzeit1–10Sekunden und zufällige Reihenfolge separat einstellbar. Änderungen an Zeit/Reihenfolge erhalten den bereits gewählten Draht; Deaktivieren verwirft den aktuellen Drahtzustand. Beim Laden mitten in einer Pflanzaktion wird kein Draht nachträglich erfunden. Normales Entschärfen ist weiterhin möglich; Drahtziehen bleibt optional.
