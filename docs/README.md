# LPLK-Serververwaltung: Anleitungen

Die Sammlung besteht aus CS2-/SwiftlyS2-Addons, einem Windows-Verwaltungsprogramm und einer PHP-Website. Hier beginnt die Dokumentation für Einrichtung und Bedienung.

| Ich möchte … | Hier beginnen |
|---|---|
| verstehen, welches Addon wofür da ist | [Addon-Verzeichnis mit 21 Einzelanleitungen](ADDONS.md) |
| ein fertiges Addon installieren oder aktualisieren | [Gemeinsame Addon-Installation](INSTALLATION-ADDONS.md) |
| das Windows-Programm einrichten oder selbst bauen | [Desktopprogramm installieren](INSTALLATION-PORTAL.md) |
| die Webverwaltung aufsetzen oder aktualisieren | [Website installieren](INSTALLATION-WEBSITE.md) |
| GitHub mit dem Verwaltungsprogramm verbinden | [GitHub-Zugang einrichten](GITHUB.md) |

Für jede Komponente werden Zweck, Voraussetzungen, Einrichtung und nötige Prüfungen erklärt. Die Einzelanleitungen nennen außerdem die vorhandenen Chatbefehle und Berechtigungen. Sie unterscheiden zwischen implementierten Funktionen, Testständen und noch offenen Erweiterungen.

**Schnellstart für Serverbetreiber:** zuerst die passende Funktion im Verzeichnis auswählen, dann die gemeinsame Installation und die Besonderheiten der Einzelanleitung lesen. Ein Quellcodearchiv von GitHub ist kein fertig gebautes Pluginpaket.

**Schnellstart für Entwickler:** den vollständigen Quellbaum verwenden. `portal/`, `Addons/`, `scripts/`, `tests/` und die zentralen Builddateien behalten ihre Struktur. Gemeinsamen Code und Dependency-Lockdateien nicht weglassen. Produktive Konfigurationen und Nutzerdaten sind kein Bestandteil der Quellen.
