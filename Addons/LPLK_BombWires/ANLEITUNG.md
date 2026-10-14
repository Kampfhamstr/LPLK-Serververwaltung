# LPLK_BombWires – Bombendrähte

**Download:** [LPLK_BombWires.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_BombWires.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.4**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

Das Addon erweitert Bombenlegen und Entschärfen um eine Auswahl aus vier Drahtfarben. Beim Legen wird der sichere Draht gewählt; ohne ausdrückliche Wahl greift die vorgesehene Zufallsauswahl. Beim Entschärfen kann die konfigurierte Drahtaktion verwendet werden. Ob die normale Entschärfung daneben möglich ist, hängt von den Einstellungen ab.

## Installation und Einrichtung

`LPLK_BombWires.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_BombWires/LPLK_BombWires.dll`.

`data/LPLK_BombWires/settings.json` bzw. das Modulmenü verwenden, um Aktivierung und Bombenregeln einzustellen. Für die gemeinsame Menüführung LPLK_Admin installieren. Keine zweite Implementierung derselben Drahtmechanik parallel aktivieren.

## Bedienung und Rechte

Die Drahtaktion reagiert auf die **Spielaktion `+jump`**, nicht auf eine fest verdrahtete physische Leertaste. Standardmäßig ist das die Leertaste; eine andere Sprungtaste soll ebenso funktionieren. Bei einem Mausrad-Bind muss CS2 tatsächlich einen Sprungimpuls liefern. Die echte Clientabnahme verschiedener Mausradbelegungen steht noch aus.

Administration: `!lplk_bombwires`, Modulrecht `lplk.bombwires.admin`, zentrale Verwaltung auch über `lplk.admin`.

## Prüfen

Mit zwei Spielern Legen, Wahl und Zufallsfall, richtigen und falschen Draht sowie Abbruch/Tod/Rundenende prüfen. Sprungbindung mit Tastatur und Mausrad separat testen. Ein erfolgreich geladener Pluginstatus bestätigt noch nicht, dass jede Clientbindung zuverlässig ausgewertet wird.
