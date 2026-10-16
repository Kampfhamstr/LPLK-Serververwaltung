# Fertige Addonpakete

[Release öffnen](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus)

[Bridge-/Maps-Kompatibilitätsupdate vom 16.10.2026](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/compat-2026.10.16-bridge-maps) · [Prüfstand und Grenzen](COMPATIBILITY-20261016.md). Die Tabelle verlinkt pro Komponente den passenden Stand.

Vollständige Pluginpakete für Linux/SwiftlyS2, jeweils mit Ordnerstruktur und benötigten Bibliotheken. Testausgabe, keine allgemeine Produktionsfreigabe.

Die überarbeiteten 18 LPLK-Addons laufen auf dem getrennten Testserver; zusammen mit dessen Bridge und Workshopmanager sind 20 Komponenten Loaded. 6.842 lokale Prüfungen bestanden. Sichtprüfung der Menüs im CS2-Client bleibt offen. Die zusätzlich enthaltenen Infrastrukturpakete behalten ihren dokumentierten Prüfstand; EnvironmentCheck ist nur ein leeres Buildbeispiel.

`!admin`: alle Spieleraktionen beim gewählten Spieler, kurze Zeilen, Maps mit direkten Aktionen, Soundkonfiguration unter Einstellungen. `!menu`: passende aktive Spielerfunktionen, Adminmenü nur für Berechtigte. Sounds: „An alle?“ und „Override?“ mit Ja/Nein. Alte Befehle/Binds bleiben erhalten.

Installation: Das benötigte ZIP unter Assets herunterladen, vollständig nach game/csgo/addons/swiftlys2/plugins/ übertragen; vorhandene Konfigurationen und Daten erhalten. Whitelist.txt liegt im Pluginordner und muss bei Updates erhalten bleiben. Detaillierte Anleitung im Repository unter docs/INSTALLATION-ADDONS.md. GitHubs automatisch angebotene Source-code-Archive enthalten Quellen und sind keine Pluginpakete.

| Paket | Version | Bytes | SHA-256 |
|---|---|---:|---|
| [AddonsManager](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/AddonsManager.zip) | 2.0.3-lplk.3 | 57,655 | `a14a55e9235e6f728dbfc82e4fac149283d3065c87d53374b920366af999d6ee` |
| [EnvironmentCheck](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/EnvironmentCheck.zip) | 1.0.0 | 12,420 | `b5179882f224e4d1244e3520495b606e3a3d9802d49bfcf72b93c5f8679bbfd8` |
| [LPLK_Admin](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Admin.zip) | 0.5.0 | 47,588 | `5be5a2cd21f1d8b2075aa9c94eb63170fd5192778fa2c7d58541eb3c43d7ddf9` |
| [LPLK_AntiCamp](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_AntiCamp.zip) | 0.1.3 | 32,746 | `cf4a01c512acb74e07018ad9c05b4b08c27c403be54228535f831d526daa9c9d` |
| [LPLK_AntiTeamFlash](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_AntiTeamFlash.zip) | 0.1.1 | 29,294 | `e1c944452bd0bd8db2352f3296b883951bbe51231934714133d648402a271733` |
| [LPLK_BombWires](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_BombWires.zip) | 0.1.4 | 61,666 | `615f96b80e5d0b2cd98a3f3e6214094fcf63b7a7838a2909a8805e1e06338f92` |
| [LPLK_ChatGuard](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_ChatGuard.zip) | 0.1.1 | 52,848 | `8b2f86ba6646c6f863ecf0f1276c304664de6c0e3e11607a206924e57986e195` |
| [LPLK_CombatRules](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_CombatRules.zip) | 0.1.1 | 50,112 | `f0b815decc71cef00b030b8ac91e6e46037053589a94173a064cf6d7b55ab2ba` |
| [LPLK_Demos](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Demos.zip) | 0.1.1 | 48,230 | `1df9f1a3d2cd8ca8ae3a95c76f856ed439cfd5388bb07302db83da71f8ddbbc7` |
| [LPLK_Economy](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Economy.zip) | 0.9.1 | 3,060,040 | `bd7e0bb868068c0db42feaedbcd1a591cc2263201338fbec7b64b9148832e82a` |
| [LPLK_Fluch](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Fluch.zip) | 0.1.1 | 40,747 | `7d553cee52ff17aee06efc43cc0955967930277f2ce5f1edcaf06a6930552eb0` |
| [LPLK_Maps](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/compat-2026.10.16-bridge-maps/LPLK_Maps.zip) | 0.2.7 | 78,349 | `83f021b4cca5fb1836144f8d0b5c9c3b30761294ac7b309b7b1267250b5145a7` |
| [LPLK_NadeEffects](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_NadeEffects.zip) | 0.1.1 | 50,928 | `3061d9debb99cc4afad9d2413a2c1812d0740688133240594701129f38bac76a` |
| [LPLK_Parachute](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Parachute.zip) | 0.1.4 | 60,558 | `892a8382df026d1160ff7dd7aa494191eabb2e383406cc2004e6b895bda4567e` |
| [LPLK_Sounds](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Sounds.zip) | 0.1.7 | 86,035 | `3bd019758c657dc7489ea4af718b372f0a4981f805d8658022ef4d465c646744` |
| [LPLK_Statistics](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Statistics.zip) | 0.2.2 | 88,836 | `de5159901ed9b1d1f57b678602fc71811c23739af3d39311eb7e84ad5c03cacb` |
| [LPLK_Teams](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Teams.zip) | 0.1.1 | 46,772 | `60eaf40a4854a322064b81faedd248abc9277fd81349a14ed0f269d7bca11e74` |
| [LPLK_ThirdPerson](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_ThirdPerson.zip) | 0.1.1 | 43,144 | `1055f5f42fa535b1f9e6944161b34b7f7d8177d6739be54d3613cbf0d5bfc2d7` |
| [LPLK_Weapons](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Weapons.zip) | 0.1.1 | 54,135 | `545d7b6c4c324027d5c56a4adc2e05c9bca38cf52657e8b928cf268a9b12be42` |
| [LPLK_Whitelist](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Whitelist.zip) | 0.2.3 | 29,227 | `a1bb645f4ccb133c0c14bb64236f85ac61ea13ce22db7281881f30ebbfcbef0b` |
| [SerververwaltungBridge](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/compat-2026.10.16-bridge-maps/SerververwaltungBridge.zip) | 1.6.5 | 1,207,709 | `c445712dd542a61d4f6a64ad44808cd73ae4ac0ed7f245525b13d80abb6c7b16` |
