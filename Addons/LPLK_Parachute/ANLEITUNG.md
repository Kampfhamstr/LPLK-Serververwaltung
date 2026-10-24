# LPLK_Parachute – Fallschirm

**Ausgabe 24.10.2026: 0.1.6.** [LPLK_Parachute.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Parachute.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.6, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Parachute.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_Parachute.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Parachute.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Dieser GitHub-Download enthält weiterhin die ältere veröffentlichte Version. Aktuell geprüftes lokales Paket0.1.5: `Build/LPLK_Parachute.zip`, ausschließlich auf dem Testserver installiert.

Quellstand **0.1.5**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

Der Fallschirm begrenzt die Fallbewegung, solange ein berechtigter Spieler in der Luft die Benutzen-Aktion hält. Maßgeblich ist **`+use`**, standardmäßig die Taste E. Die persönliche Tastenbelegung zählt. Das Addon stellt veränderte Bewegungswerte beim Beenden der Aktion und bei passenden Spieler-/Lebenswechseln wieder her.

## Installation und Einrichtung

`LPLK_Parachute.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel-DLL: `game/csgo/addons/swiftlys2/plugins/LPLK_Parachute/LPLK_Parachute.dll`.

Einstellungen liegen im SwiftlyS2-Datenordner `data/LPLK_Parachute/settings.json`. Fallverhalten, Aktivierung und gegebenenfalls `UsePermission` einrichten. Abhängig von den Regeln kann die Nutzung beispielsweise beim Tragen einer Geisel ausgeschlossen sein. Bestehende Einstellungen beim Update erhalten.

## Bedienung und Rechte

- Spieler: beim Fallen die für `+use` gebundene Taste halten; ein Chatbefehl ist dafür nicht nötig.
- Administration: `!lplk_parachute` oder das passende Modulmenü unter `!admin`.
- Modulrecht: `lplk.parachute.admin`; für die zentrale Verwaltung wird auch `lplk.admin` berücksichtigt. Ein gesetztes `UsePermission` betrifft die Spielernutzung.

## Prüfen

Fall mit und ohne gedrücktes `+use`, Loslassen, Tod, Respawn und Kartenwechsel testen. Dabei prüfen, dass keine verringerte Schwerkraft zurückbleibt. Surf-/Zombieprofile benötigen eine eigene Abstimmung mit ihren Bewegungsregeln; der Standardtest ist keine pauschale Modusfreigabe.

## Erweiterung0.1.5 vom19.10.2026

Einstellungen und Status sind im Modulmenü getrennt. Höchstens fünf sichtbare Einträge, nummerierte Aktionen und unnummeriertes Zurück; bestehende Farben/Tasten erhalten. Änderungen an settings.json werden etwa alle zwei Sekunden geprüft. Bei ungültigen Dateien bleiben die letzten gültigen Regeln aktiv; Menüänderungen überschreiben die fehlerhafte Datei nicht. `data/LPLK_Parachute/status.json` meldet `reloadError`. Datei korrigieren, dann wird automatisch erneut geladen.

Falltempo und Bremsschritt lassen sich direkt auswählen; vorhandene eigene Zwischenwerte bleiben auswählbar. Bremsschritt0 bedeutet sofortige Geschwindigkeitsbegrenzung. Nutzungsrecht weiter gezielt über `UsePermission` konfigurieren. Fallphysik aus Parachutev5 unverändert. Das optionale sichtbare Originalmodell bleibt für andere Profile ein offener Abgleich.
