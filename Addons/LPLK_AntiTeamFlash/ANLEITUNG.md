# LPLK_AntiTeamFlash – Schutz vor Teamblenden

**Ausgabe 24.10.2026: 0.1.3.** [LPLK_AntiTeamFlash.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_AntiTeamFlash.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.3, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_AntiTeamFlash.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_AntiTeamFlash.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_AntiTeamFlash.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Dieser veröffentlichte Download bleibt auf dem älteren Stand. Aktuelles lokales Paket0.1.2: `Build/LPLK_AntiTeamFlash.zip`, nur Testserver.

Quellstand **0.1.2**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Modul schützt Spieler nach den eingestellten Regeln vor Blendwirkung durch Teammitglieder. Gegnerische und eigene Blendungen werden getrennt behandelt. Überlappende Blendereignisse sind ein wichtiger Sonderfall: Der Teamschutz soll eine berechtigte gegnerische Blendung nicht einfach aufheben.

## Installation und Einrichtung

`LPLK_AntiTeamFlash.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_AntiTeamFlash/LPLK_AntiTeamFlash.dll`.

Einstellungen unter `data/LPLK_AntiTeamFlash/settings.json` bzw. im Modulmenü anpassen. `AccessPermission` kann die geschützte Spielergruppe einschränken. Eine parallel laufende zweite Teamflash-Korrektur deaktivieren bzw. vor der Ablösung gezielt abgleichen, um widersprüchliche Eingriffe zu vermeiden.

## Bedienung und Rechte

Spieler müssen keinen Befehl ausführen. `!lplk_antiteamflash` dient der Verwaltung; Modulrecht `lplk.antiteamflash.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Mit realen Clients Teamflash, Selbstflash, Gegnerflash und deren zeitliche Überlagerung prüfen. Die lokale Prüfung und das erfolgreiche Laden ersetzen diese visuelle Spielabnahme nicht; sie steht noch aus.

## Erweiterung0.1.2 vom19.10.2026

Unter `!admin` stehen Einstellungen und Status getrennt bereit. Höchstens fünf sichtbare Einträge; Nummern, Farben und W/S/E/Tab bleiben erhalten. Gültige externe Dateiänderungen werden regelmäßig übernommen. Ungültige Dateien behalten die letzten gültigen Regeln und werden nicht durch Menüaktionen überschrieben. Eigene Zwischenwerte bleiben bei Auswahlmenüs erhalten. `status.json` zeigt Konfigurationsfehler über `reloadError`.

Eigenblendung, Nutzungsrecht und Team-/FFA-Filter bleiben erhalten. Menü- und Dateiänderungen löschen keine laufende erlaubte Gegner-/Eigenblendung aus der Historie. Dadurch kann eine spätere Teamflash diese erlaubte Blendung nicht versehentlich aufheben.

154 neue gemeinsame Prüfungen sowie bestehende Fach-/Menütests bestanden; beide APIs und tatsächliche native Statusantworten geprüft. Echte Client-/Mehrspielerabnahme auf Benutzerwunsch später.
