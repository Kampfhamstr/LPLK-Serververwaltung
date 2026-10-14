# LPLK_NadeEffects – Granateneffekte für ausgewählte Spieler

**Download:** [LPLK_NadeEffects.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_NadeEffects.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.1**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Modul erlaubt Administratoren, ausgewählte Spieler mit der vorgesehenen Granatenwarnung und einem weißen Fade-Effekt zu versehen sowie betroffene Granaten nach den Regeln zu entfernen. Es ist eine gezielte Adminfunktion, keine automatische allgemeine Granatenstatistik.

## Installation und Einrichtung

`LPLK_NadeEffects.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_NadeEffects/LPLK_NadeEffects.dll`.

Einstellungen unter `data/LPLK_NadeEffects/settings.json` und über das Modulmenü einrichten. Spielerziel und Rechte vor dem Auslösen prüfen; gespeicherte oder temporäre Zielauswahlen gehören nicht in den Quellcodeupload.

## Bedienung und Rechte

`!nadeboom` bzw. `!nb` bleiben die direkten Einstiege. `!lplk_nadeeffects` dient der Modulverwaltung. Modulrecht `lplk.nadeeffects.admin`, zusätzlich der konfigurierte historische `AdminPermission`-Zugang; zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Mit einem Testspieler Granatenbestand, Warnung, Fade, Entfernung und Abbruch prüfen. Die Aktion darf weder unbeteiligte Spieler noch einen späteren Benutzer desselben Slots treffen. Die echte Inventar- und Darstellungsabnahme steht noch aus.
