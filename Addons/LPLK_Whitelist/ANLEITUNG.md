# LPLK_Whitelist – Zutrittsliste

**Ausgabe 24.10.2026: 0.2.6.** [LPLK_Whitelist.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Whitelist.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.2.6, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Whitelist.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Aktueller lokaler Teststand:** [LPLK_Whitelist.zip](Build/LPLK_Whitelist.zip) · [Prüfmanifest](Build/build-manifest.json).0.2.5 ist noch nicht auf GitHub veröffentlicht.

Quellstand **0.2.5**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

LPLK_Whitelist steuert anhand einer gespeicherten Liste und des eingestellten Modus, welche Spieler den Server betreten dürfen. Administratoren können Einträge über Befehle und Menüs verwalten. Die angepasste Websiteansicht benötigt zusätzlich eine passende, freigegebene Webanbindung.

## Installation und wichtige Datenpfade

`LPLK_Whitelist.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Whitelist/LPLK_Whitelist.dll`.

| Datei relativ zu `game/csgo/addons/swiftlys2/` | Inhalt |
|---|---|
| `configs/plugins/LPLK_Whitelist/config.jsonc` | Aktivierung, Modus, Rechte und Zugangsregeln |
| `plugins/LPLK_Whitelist/whitelist.txt` | Gespeicherte Mitgliederliste |
| `data/LPLK_Whitelist/status.json` | Status für Diagnose und Anbindung |

**Die Mitgliederliste liegt im Pluginordner.** Vor dessen Austausch die Liste sichern und erhalten. Ein frischer leerer Paketordner darf sie nicht ersetzen. Vor Aktivierung einer beschränkenden Regel den eigenen berechtigten Zugang und eine Serverkonsolenmöglichkeit sicherstellen.

## Befehle und Rechte

`!wl <SteamID64>` und `!uwl <SteamID64>` dienen zum Hinzufügen bzw. Entfernen; die Aliasnamen `!lplk_wl_add` und `!lplk_wl_remove` bleiben verfügbar. `!lplk_whitelist` ist der administrative Moduleinstieg. Die Befehle erwarten genau eine gültige SteamID64, keinen beliebigen Anzeigenamen.

Das Modulrecht lautet `lplk.whitelist.admin`; auch `lplk.admin` wird berücksichtigt. Zusätzlich ist `PermissionForCommands` konfigurierbar und steht im Quellstandard auf `admin.ban`. `AdminBypass` muss ausdrücklich aktiviert sein, damit die vorgesehenen Bypassrechte greifen; relevant sind `lplk.whitelist.bypass` und `lplk.admin`. Website-Rollen werden unabhängig geprüft.

## Prüfen

Mit einem zugelassenen und einem nicht zugelassenen Testkonto den tatsächlichen Beitritt prüfen. Nach einer Änderung persistierte Liste und Status erneut lesen. Eine erfolgreich angenommene Webnachricht allein beweist noch nicht die Übernahme des Eintrags. Listen und SteamIDs gehören nicht in GitHub.


## Konfigurierbare Befehle ab 0.2.5

`AddCommand` und `RemoveCommand` in `config.jsonc` ergänzen eigene Befehlsnamen. Die alten Felder `WhitelistCommand` und `UnWhitelistCommand` aus Version1.0.0 werden ebenfalls erkannt. Sind alte und neue Felder gleichzeitig vorhanden, müssen sie denselben Namen bezeichnen. Beispiel: `AddCommand: "freischalten"` ergibt im Chat `!freischalten` und in der Konsole `sw_freischalten`. Ein bereits eingetragenes `sw_` wird genau einmal berücksichtigt; keine Chatzeichen `!` oder `/` in den Konfigurationsnamen schreiben.

Zulässig sind1–40 ASCII-Buchstaben/Ziffern sowie Punkt, Unterstrich und Bindestrich. Hinzufügen und Entfernen brauchen unterschiedliche Namen. Die festen Befehle `wl`, `uwl`, `lplk_wl_add`, `lplk_wl_remove` und der Moduleinstieg bleiben bestehen. Belegte Befehle anderer Komponenten werden nicht überschrieben. Bei widersprüchlichen/ungültigen Zusatznamen bleiben die zuletzt gültigen Befehle nutzbar; gültige Zutrittsregeln und die Liste werden trotzdem weiter übernommen. `commandError` im Status meldet das Problem.

Änderungen werden beim regulären Nachladen berücksichtigt. Veraltete eigene Handler werden gesperrt und anhand ihrer Registrierungskennung entfernt. Vor einer Listenänderung werden aktuelle Regeln, Rechte und die noch gültige Befehlszuordnung erneut geprüft. Dies vergibt keine neuen Rechte und ändert nicht die Mitgliederliste. Bei normalen Menü-Regeländerungen bleiben die konfigurierten Namen erhalten. Einträge mit mehr als17 Stellen werden im Menü jetzt abgewiesen, statt still gekürzt zu werden.

Quellenabgleich: [Original1.0.0](https://github.com/asapverneri/Whitelist-SwiftlyS2/blob/e7a207120ce25cd0254ac6b710a9af32457e51a0/src/Config.cs), [Original1.2.1](https://github.com/asapverneri/Whitelist-SwiftlyS2/blob/a1f923475271133eaf5c7ca1d69459839d26026a/src/Main.cs), [Swiftly-Befehlsregistrierung](https://swiftlys2.net/api-docs/stable/commands/icommandservice). Die neuere Originalversion wird nicht als tatsächlich installierte Altversion ausgegeben; beide Feldformen werden unterstützt. Details und Prüfsummen: `state/access-commands-source-review.json`.

Am19.10. nur Testserver89450e53:125 neue Befehlsprüfungen,101 Zugangsprüfungen,93 Grund-/1287 Menüprüfungen sowie beide APIs bestanden. Native Lade-/Status-/Webprüfung bestätigt20 Loaded/36Befehle. Sieben Einträge und Konfiguration hashgleich, Live-Namen weiter wl/uwl. Benutzerdefinierte Namen lokal mit den tatsächlichen Plugin-Callbacks und simuliertem Framework geprüft. Kein Live-Add/Remove-Test, Clientbeitritte später. Beleg `state/access-commands-verification.json`. Aktuelles lokales ZIP: `Build/LPLK_Whitelist.zip`; GitHub-Ausgabe bleibt unverändert.

## Historischer Teststand 0.2.4 vom 19.10.2026

Unter Zugang gibt es Regeln und Einträge. Die Listenansicht verteilt Einträge auf kurze Seiten, Zurück bleibt unnummeriert. Konfiguration und Mitgliederliste werden alle fünf Sekunden gemeinsam validiert. Änderungen werden auch bei gleich gebliebenem Dateizeitstempel erkannt. Fehlende oder ungültige Dateien behalten den letzten gültigen gemeinsamen Stand; sie werden im laufenden Betrieb nicht durch Standarddateien ersetzt.

Menü- und Befehlsänderungen lesen vorher den aktuellen Stand. Geänderte Verwaltungsrechte werden danach erneut geprüft. Unbekannte vorhandene Konfigurationsfelder, abweichende Schreibweise und flache alte JSON-Struktur bleiben beim Speichern erhalten; Kommentare können durch JSON-Neuschreiben entfallen. In0.2.4 wurden AddCommand/RemoveCommand nur erhalten. Seit0.2.5 sind die oben beschriebenen Zusatznamen implementiert; feste wl/uwl/LPLK-Aliasse bleiben erhalten. Zwischenzeitliche Änderungen werden unmittelbar vor dem Ersetzen geprüft; dies ist keine atomare Betriebssystemtransaktion über beide Dateien.

Nur Testserver 89450e53 aktualisiert. Sieben Einträge und sämtliche Zugangsregeln bytegleich erhalten, 20 Komponenten Loaded, 36 Webbefehle und native Statusantwort bestätigt. 99 lokale Wartungschecks, 93 Grundchecks und1287 Menüchecks; Paket/Build auf APIs1.4.14 und1.5.0-beta.10 geprüft. Tatsächliche erlaubte/gesperrte Clientbeitritte bleiben später zu prüfen. Nachweis: state/access-maintenance-verification.json.

Aktuelles lokales ZIP unter Build; der bisherige GitHub-Download enthält diese unveröffentlichte Änderung noch nicht. Rückfall auf dem Testserver: .lplk-rollback/LPLK_Whitelist.previous.zip enthält vorherige DLL und Mitgliederliste; LPLK_Whitelist.config-before-maintenance.json sichert die Konfiguration. Spätere Listen-/Regeländerungen vor Rückfall abgleichen und erhalten.
