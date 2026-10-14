# LPLK_Whitelist – Zutrittsliste

**Download:** [LPLK_Whitelist.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Whitelist.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.2.3**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

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
