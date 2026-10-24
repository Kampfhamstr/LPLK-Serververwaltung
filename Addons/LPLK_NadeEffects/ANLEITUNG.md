# LPLK_NadeEffects – Granateneffekte für ausgewählte Spieler

**Ausgabe 24.10.2026: 0.1.3.** [LPLK_NadeEffects.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_NadeEffects.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.3, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_NadeEffects.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_NadeEffects.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_NadeEffects.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Der veröffentlichte Download enthält noch0.1.1. Die aktuelle ausschließlich lokal/testserverseitig geprüfte0.1.2 liegt in `Build/LPLK_NadeEffects.zip`; keine neue GitHub-Veröffentlichung.

Quellstand **0.1.2**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Modul erlaubt Administratoren, ausgewählte Spieler mit der vorgesehenen Granatenwarnung und einem weißen Fade-Effekt zu versehen sowie betroffene Granaten nach den Regeln zu entfernen. Es ist eine gezielte Adminfunktion, keine automatische allgemeine Granatenstatistik.

## Installation und Einrichtung

`LPLK_NadeEffects.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_NadeEffects/LPLK_NadeEffects.dll`.

Einstellungen unter `data/LPLK_NadeEffects/settings.json` und über das Modulmenü einrichten. Spielerziel und Rechte vor dem Auslösen prüfen; gespeicherte oder temporäre Zielauswahlen gehören nicht in den Quellcodeupload.

## Bedienung und Rechte

`!nadeboom` bzw. `!nb` bleiben die direkten Einstiege. `!lplk_nadeeffects` dient der Modulverwaltung. Modulrecht `lplk.nadeeffects.admin`, zusätzlich der konfigurierte historische `AdminPermission`-Zugang; zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Mit einem Testspieler Granatenbestand, Warnung, Fade, Entfernung und Abbruch prüfen. Die Aktion darf weder unbeteiligte Spieler noch einen späteren Benutzer desselben Slots treffen. Die echte Inventar- und Darstellungsabnahme steht noch aus.

## Änderungen 0.1.2 – nur Testserver

Direkte Eingaben ersetzen die eingeschränkten zyklischen Werte für Auslösesperre0,1–60s, Fade-Dauer/Haltezeit0–10s, Deckkraft0–255, Lebensdauer0,1–30s und Hinweis100–10000ms. Freie RGB-Farbe und Namenslänge5–64 sind erreichbar. Die bisherigen vier eigenen Menütasten gelten auch auf allen Eingabeseiten; Belegung wird angezeigt und weiterhin in settings.json geändert. Warntext, alle sieben Granatentypen, Zielauswahl und Rechte bleiben erhalten. Gültige Regeländerungen brechen wartende Effekte ab, lassen die Spielerauswahl bestehen; ungültige Änderungen verändern beides nicht.

`settings.json` wird inhaltlich alle zwei Sekunden geprüft, auch bei gleichem Dateidatum. Ungültige/fehlende/verknüpfte Dateien, unbekannte Felder oder mehr als64KiB behalten die letzten gültigen Regeln. Menüs überschreiben solche Dateien nicht und berücksichtigen neuere gültige externe Werte. Schreibfehler im Statusbericht unterbrechen den Effektablauf nicht.

165 gemeinsame neue Prüfungen mit tatsächlichen Menü-/Eingabeaufrufen und ersetzten Engine-Grenzen bestanden; zusätzlich die bisherigen Fachtests. Build und Paket gegen Swiftly1.4.14 und1.5.0-beta.10 geprüft, native Lade-/Status-/Webantworten auf dem Testserver bestätigt. Keine neue synthetische Entityprobe. Sichtbare Effekte, tatsächlicher Schaden beziehungsweise Inventarentzug und die Bedienung mit Clients bleiben auf Benutzerwunsch später.
