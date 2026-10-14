# LPLK_Weapons – Waffenregeln und Waffenmenü

**Download:** [LPLK_Weapons.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Weapons.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.1**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Modul verwaltet erlaubte Waffen, voreingestellte Waffengruppen und ein begrenztes Menü für kostenlose Waffen. Regeln und verfügbare Spieleraktionen lassen sich getrennt schalten. Ein geladenes Addon bedeutet daher nicht, dass jeder Spieler jederzeit Waffen erhalten kann.

## Installation und Einrichtung

`LPLK_Weapons.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Weapons/LPLK_Weapons.dll`.

Einstellungen liegen unter `data/LPLK_Weapons/settings.json`. Gewünschte Waffen, Begrenzungen, Menüaktivierung und gegebenenfalls `MenuPermission` einstellen. Bereits vorhandene Waffenplugins vor einer Ablösung auf Überschneidungen prüfen. Die aktuellen Funktionen sind kein vollständiger Ersatz sämtlicher fortgeschrittener AWS-Magazin-, Munitions-, Nachlade- oder Granatenregeln.

## Bedienung und Rechte

`!guns` öffnet das verfügbare Waffenmenü; `!primary` und `!secondary` führen zu den entsprechenden Bereichen. Deaktivierte oder nicht erlaubte Funktionen werden nicht freigegeben. `!lplk_weapons` ist der Verwaltungseinstieg, Modulrecht `lplk.weapons.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Erlaubte und gesperrte Waffen, Kontingente und ausgeschaltetes Menü testen. Beim Ersetzen einer Slotwaffe muss die neue Waffe erfolgreich bereitstehen, bevor die bisherige entfernt wird. Surf-/Zombieprofile mit besonderen Waffenregeln separat abgleichen; die vollständige Migration dieser Profile steht noch aus.
