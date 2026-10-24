# LPLK_Teams – Teamwechselregeln

**Ausgabe 24.10.2026: 0.2.2.** [LPLK_Teams.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Teams.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.2.1, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Teams.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_Teams.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Teams.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Dieser veröffentlichte Download enthält weiterhin den älteren Stand0.1.1. Das aktuelle, ausschließlich lokal/testserverseitig geprüfte Paket0.2.0 liegt unter `Build/LPLK_Teams.zip`; keine neue GitHub-Veröffentlichung.

Quellstand **0.2.0**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Modul begrenzt Teamwechsel nach konfigurierten Regeln und schützt beispielsweise das führende Team vor unerwünschtem Zulauf. Es berücksichtigt Spielstand und Seitenwechsel. Ausgewählte Spieler können für dafür vorgesehene Regeln gesondert geführt werden.

## Installation und Einrichtung

`LPLK_Teams.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Teams/LPLK_Teams.dll`.

Unter `data/LPLK_Teams/` Einstellungen, `selected-players.json` und Status getrennt erhalten. Teamregeln zum Spielmodus passend setzen. Automatische Teamverteilung anderer Plugins und administrative Wechsel müssen mit diesen Regeln zusammenpassen; bei Websiteaktionen auch die Bridge-Prüfung beachten.

## Bedienung und Rechte

`!lplk_teams` öffnet die Verwaltung. Die bisherigen Einstiege `!tsw` und `!tsbscore` bleiben vorhanden. Modulrecht `lplk.teams.admin`; zusätzlich wird das historische Recht `wl.admin` für den vorgesehenen Kompatibilitätszugriff berücksichtigt. Die zentrale Verwaltung kann auch `lplk.admin` verwenden.

## Prüfen

Teamwechsel bei Gleichstand und Führung, Halbzeit, Beobachterstatus sowie administrative Änderungen prüfen. Die Liste ausgewählter Spieler ist Nutzerdatenbestand und gehört nicht in den Quellcodeupload.

## Änderungen 0.1.2 (nur Testserver)

Der Einstieg führt zu Einstellungen, Spielstand und der bestehenden Verliererteam-Auswahl. Alle Seiten zeigen höchstens fünf Zeilen; Zurück bleibt weiß und ohne Nummer. Die Auswahl einschließlich zuordnendem Administrator bleibt unverändert in `selected-players.json`.

`settings.json` wird inhaltlich alle zwei Sekunden geprüft, auch bei unverändertem Dateidatum. Ungültige/fehlende/zu große/verknüpfte Dateien und unbekannte Schlüssel behalten die letzten gültigen Regeln; Menüs überschreiben solche Dateien nicht. Neue gültige externe Werte werden vor einer Menüänderung berücksichtigt. Geänderte Regeln verwerfen ältere aufgeschobene Teamwechsel und einmalige Freigaben. Die ursprünglichen Regeln, Aliasse und Rechte bleiben erhalten. Zahlen lassen sich weiterhin im gesamten bisherigen Wertebereich eingeben.

Der Testserver behält `Enabled=false`; es wurden keine Teamregeln aktiviert.96 Fach-/Referenzprüfungen und114 gemeinsame Wartungsprüfungen bestehen. Beide APIs und native Status-/Webantworten geprüft. Tatsächliche Teamwechsel mit mehreren Spielern bleiben auf Benutzerwunsch später. Zusätzliche alte Profiloptionen (unter anderem konfigurierbare BlockedCommands und alternative Menürechte/Resetoptionen) sind kein fertig migrierter Funktionsumfang.

## Profilergänzungen 0.2.0 (nur Testserver)

`!tsw` öffnet direkt die Verliererteam-Auswahl mit „Ausgewählte“, „Nicht ausgewählte“ und „Alle Spieler“. Bestehende Offline-Zuordnungen bleiben sichtbar und erhalten ihren verantwortlichen Administrator. Die Auswahl ist weiterhin auch über `!admin` beziehungsweise `!lplk_teams` erreichbar. Aktuelle Rechte und Zielschutz gelten bei jedem Klick erneut.

Unter Einstellungen sind nun zusätzlich erreichbar:

- Matchende: `ResetScoreOnNewMap` und `ResetScoreOnNewMatch`; Standard jeweils `true`. Auch bei ausgeschalteter Spielstandrücksetzung werden alte Halbzeit-/Matchendefenster und aufgeschobene Teamwechsel verworfen.
- Meldungen: `EnableLosingTeamMenu` (Standard `true`) und `Debug` (Standard `false`). Ein ausgeschaltetes Auswahlmenü verhindert neue Menüänderungen; bestehende Zuordnungen werden dadurch nicht gelöscht. Der Teamschutz selbst wird über `Enabled` gesteuert.
- Texte/Befehle: `ChatPrefix`, `BlockedMessage`, `CenterMessage` sowie `BlockedCommands`. Befehle werden mit Komma getrennt; leer bedeutet keine abgefangenen Teambefehle. Die bisherige Liste `jointeam, teammenu` bleibt ohne Änderung der Konfiguration aktiv. Custom-Befehle werden nur ausgewertet, nicht neu als Engine-Kommandos registriert. Meldungen dürfen leer sein; Steuerzeichen und überlange Texte sind ungültig.

Das Laden alter Dateien ergänzt fehlende Standardwerte nur im Speicher. Die Testauslieferung hat keine Einstellungen neu gespeichert und keine Personendaten angelegt. Der Teamschutz bleibt `Enabled=false`. 148 neue lokale Prüfungen, 119 Team-/Referenzprüfungen sowie die bisherigen Menü-/Administrationsprüfungen bestehen. Build und ZIP auf beiden APIs geprüft; 20 Komponenten geladen, 36 Webbefehle und native Versionsantwort bestätigt. Client-Teamwechsel bleiben später.

Offen bleibt die vollständige Übernahme fremder Rechteprofile (`RestrictLosingTeamMenuToAdmins`, `LosingTeamMenuAdminPermissions`) und vorhandener persönlicher Altzuordnungen. Der neue Stand öffnet kein frei zugängliches Administrationsmenü und ändert keine Rollen. Für einen Rückfall liegt zusätzlich zur vorherigen DLL eine geprüfte Sicherung der vorherigen Einstellungen und gegebenenfalls vorhandener Auswahl unter `.lplk-rollback/LPLK_Teams.data-before-maintenance.zip` auf dem Testserver. Daten und DLL müssen bei inzwischen geänderter Konfiguration zusammen geprüft werden.
