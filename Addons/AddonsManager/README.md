# AddonsManager – Workshop-Inhalte laden

**Download:** [AddonsManager.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/AddonsManager.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **2.0.3-lplk.3**, SwiftlyS2-API **1.4.13**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

AddonsManager kümmert sich um die Einbindung benötigter Workshop-Addons und deren Bereitstellung für Spieler. Das ist beispielsweise für die Audiodateien von LPLK_Sounds erforderlich. Es ist weder das zentrale LPLK-Adminmenü noch ein Installer für das SwiftlyS2-Framework.

## Installation und Einrichtung

`AddonsManager.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/AddonsManager/AddonsManager.dll`. Mitgelieferte Ressourcen und Lizenzdatei vollständig übernehmen.

Konfiguration: `game/csgo/addons/swiftlys2/configs/plugins/AddonsManager/config.jsonc`. Unter **`Main.Addons`** die benötigten Workshop-IDs gemäß erzeugter Konfigurationsstruktur eintragen. Bereits vorhandene servereigene Inhalte erhalten; nicht pauschal durch eine fremde Liste ersetzen. Ein Workshop-Eintrag muss zum benötigten Inhalt passen und verfügbar sein.

## Kompatibilität und Prüfung

Diese LPLK-Ausgabe enthält eine Gamedata-Korrektur für den damals geprüften **CS2-Build 2000930**. Das ist keine Zusage für jede spätere CS2-/SwiftlyS2-Version. Nach einem Spielupdate Kompatibilität und tatsächliche Ladefunktion erneut prüfen.

Serverstart, Workshop-Mount und Clientdownload kontrollieren. Bei Sounds zusätzlich hörbar testen; ein vorhandener Katalogeintrag reicht nicht. Es gibt hier keinen allgemeinen Spielerbefehl zum Installieren beliebiger Addons. Drittanbieter-Herkunft und `LICENSE-AddonsManager.txt` bei der Weitergabe erhalten.
