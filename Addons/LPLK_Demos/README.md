# LPLK_Demos – Lokale SourceTV-Aufnahmen

**Ausgabe 24.10.2026: 0.1.3.** [LPLK_Demos.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Demos.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.3, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Demos.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_Demos.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Demos.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.2**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Addon steuert lokale SourceTV-Aufnahmen und führt einen Aufnahmeindex. Je nach Konfiguration können automatische Aufnahmen und Spieleranfragen genutzt werden. Es lädt Aufnahmen nicht automatisch zu FTP, Mega oder Discord hoch.

## Installation und Einrichtung

`LPLK_Demos.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Demos/LPLK_Demos.dll`.

**SourceTV muss vor dem Mapstart passend eingerichtet und aktiviert sein.** Das Plugin aktiviert es nicht selbst. Ports, Serverprofil und Speicherplatz passend zum Host einrichten. Einstellungen und Index liegen unter `data/LPLK_Demos/`, der Index in `index.json`; die eigentlichen Aufnahmen unter **`game/csgo/demos/LPLK_Demos/`**.

Automatische Aufnahme, Spieleranfragen und Aufräumen bewusst einschalten. Die Testreferenz lässt diese Funktionen zunächst deaktiviert. Eine Löschregel darf nur dafür vorgesehene, abgeschlossene Aufnahmen erfassen; bestehende fremde Demos erhalten.

## Bedienung und Rechte

`!demo` markiert die aktuell laufende Aufnahme zur Aufbewahrung, wenn Spieleranfragen aktiv und eine Aufnahme verfügbar sind. Der Befehl startet nicht selbst eine neue Aufnahme. `!lplk_demos` öffnet Verwaltung/Status. In der Serverkonsole stehen `sw_lplk_demos start` und `sw_lplk_demos stop` zur Verfügung. Modulrecht `lplk.demos.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Aufnahme starten, kurze Testsequenz spielen, regulär stoppen, Index und abgeschlossene Datei prüfen. Anschließend die Demo tatsächlich im Client wiedergeben. Ein gültiger Dateiheader und erfolgreicher Stopp sind noch keine Abnahme einer vollständig abspielbaren Partie. Speicherverbrauch und Aufbewahrung zum Serverbetrieb passend festlegen.


## Teststand 19.10.2026

Die Verwaltung bietet Aufnahme, Einstellungen und Aufbewahrung. Alle bestehenden Aufnahmeregeln einschließlich Anfragehinweis, Mindestdauer, Leerlauf und Aufbewahrungsfrist sind erreichbar. Reine Formatänderungen und ungültige Dateien beenden keine Aufnahme. Eine gültige Regeländerung beendet wie bisher die laufende Aufnahme; ihre beim Start gespeicherten Mindestdauer-/Aufbewahrungsregeln bleiben maßgeblich.

Konfiguration wird anhand des Inhalts geprüft, auch bei unverändertem Dateizeitstempel. Menüänderungen lesen vorher die aktuelle gültige Datei und überschreiben keine ungültige externe Änderung. Letzte gültige Regeln bleiben bei Lesefehlern erhalten.

Nur Testserver 89450e53: 20 Komponenten geladen, 36 Webbefehle, aktuelle native Statusantworten und Daten-/Einstellungshashes geprüft. 168 zusätzliche Wartungsprüfungen und 213 bisherige Statistik/Demoprüfungen; beide SwiftlyS2-APIs 1.4.14 und 1.5.0-beta.10. Clienttests ausdrücklich später. Beleg `state/recording-maintenance-verification.json`.

Das aktuelle lokale Paket liegt in `Build/`. Der oben gegebenenfalls verlinkte GitHub-Download ist die frühere veröffentlichte Ausgabe und enthält diese Änderung noch nicht. Rückfall: vorige DLL und `.lplk-rollback/LPLK_Demos.data-before-maintenance.zip` erhalten. Vor Wiederherstellung spätere Nutzerdaten berücksichtigen; aktuelle Daten nicht blind durch Sicherung überschreiben.
