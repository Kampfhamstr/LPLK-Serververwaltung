# LPLK_Demos – Lokale SourceTV-Aufnahmen

Quellstand **0.1.0**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Addon steuert lokale SourceTV-Aufnahmen und führt einen Aufnahmeindex. Je nach Konfiguration können automatische Aufnahmen und Spieleranfragen genutzt werden. Es lädt Aufnahmen nicht automatisch zu FTP, Mega oder Discord hoch.

## Installation und Einrichtung

`LPLK_Demos.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Demos/LPLK_Demos.dll`.

**SourceTV muss vor dem Mapstart passend eingerichtet und aktiviert sein.** Das Plugin aktiviert es nicht selbst. Ports, Serverprofil und Speicherplatz passend zum Host einrichten. Einstellungen und Index liegen unter `data/LPLK_Demos/`, der Index in `index.json`; die eigentlichen Aufnahmen unter **`game/csgo/demos/LPLK_Demos/`**.

Automatische Aufnahme, Spieleranfragen und Aufräumen bewusst einschalten. Die Testreferenz lässt diese Funktionen zunächst deaktiviert. Eine Löschregel darf nur dafür vorgesehene, abgeschlossene Aufnahmen erfassen; bestehende fremde Demos erhalten.

## Bedienung und Rechte

`!demo` markiert die aktuell laufende Aufnahme zur Aufbewahrung, wenn Spieleranfragen aktiv und eine Aufnahme verfügbar sind. Der Befehl startet nicht selbst eine neue Aufnahme. `!lplk_demos` öffnet Verwaltung/Status. In der Serverkonsole stehen `sw_lplk_demos start` und `sw_lplk_demos stop` zur Verfügung. Modulrecht `community.example.invalidmos.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Aufnahme starten, kurze Testsequenz spielen, regulär stoppen, Index und abgeschlossene Datei prüfen. Anschließend die Demo tatsächlich im Client wiedergeben. Ein gültiger Dateiheader und erfolgreicher Stopp sind noch keine Abnahme einer vollständig abspielbaren Partie. Speicherverbrauch und Aufbewahrung zum Serverbetrieb passend festlegen.
