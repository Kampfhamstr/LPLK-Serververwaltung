# LPLK_Parachute – Fallschirm

**Download:** [LPLK_Parachute.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Parachute.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.4**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

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
