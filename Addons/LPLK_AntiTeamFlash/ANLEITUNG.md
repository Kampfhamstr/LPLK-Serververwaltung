# LPLK_AntiTeamFlash – Schutz vor Teamblenden

**Download:** [LPLK_AntiTeamFlash.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_AntiTeamFlash.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.1**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Modul schützt Spieler nach den eingestellten Regeln vor Blendwirkung durch Teammitglieder. Gegnerische und eigene Blendungen werden getrennt behandelt. Überlappende Blendereignisse sind ein wichtiger Sonderfall: Der Teamschutz soll eine berechtigte gegnerische Blendung nicht einfach aufheben.

## Installation und Einrichtung

`LPLK_AntiTeamFlash.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_AntiTeamFlash/LPLK_AntiTeamFlash.dll`.

Einstellungen unter `data/LPLK_AntiTeamFlash/settings.json` bzw. im Modulmenü anpassen. `AccessPermission` kann die geschützte Spielergruppe einschränken. Eine parallel laufende zweite Teamflash-Korrektur deaktivieren bzw. vor der Ablösung gezielt abgleichen, um widersprüchliche Eingriffe zu vermeiden.

## Bedienung und Rechte

Spieler müssen keinen Befehl ausführen. `!lplk_antiteamflash` dient der Verwaltung; Modulrecht `lplk.antiteamflash.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Mit realen Clients Teamflash, Selbstflash, Gegnerflash und deren zeitliche Überlagerung prüfen. Die lokale Prüfung und das erfolgreiche Laden ersetzen diese visuelle Spielabnahme nicht; sie steht noch aus.
