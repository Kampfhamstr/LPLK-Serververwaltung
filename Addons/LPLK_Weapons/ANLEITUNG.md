# LPLK_Weapons – Waffenregeln und Waffenmenü

**Ausgabe 24.10.2026: 0.2.4.** [LPLK_Weapons.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Weapons.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.2.3, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Weapons.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_Weapons.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Weapons.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Ausschließlich auf Testserver 89450e53 installierter Stand **0.2.2**, gebaut mit SwiftlyS2-API **1.4.14**, zusätzlich gegen **1.5.0-beta.10** geprüft. Der GitHub-Link oben bleibt die ältere Veröffentlichung 0.1.1. Aktuell lokal: [Build/LPLK_Weapons.zip](Build/LPLK_Weapons.zip). [Funktionsabgleich](../../docs/ADDON-FUNKTIONSABGLEICH.md).

## Funktion

Das Modul verwaltet erlaubte Waffen, voreingestellte Waffengruppen und ein begrenztes Menü für kostenlose Waffen. Regeln und verfügbare Spieleraktionen lassen sich getrennt schalten. Ein geladenes Addon bedeutet daher nicht, dass jeder Spieler jederzeit Waffen erhalten kann.

## Installation und Einrichtung

`LPLK_Weapons.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Weapons/LPLK_Weapons.dll`.

Einstellungen liegen unter `data/LPLK_Weapons/settings.json`. Gewünschte Waffen, Begrenzungen, Menüaktivierung und gegebenenfalls `MenuPermission` einstellen. Bereits vorhandene Waffenplugins vor einer Ablösung auf Überschneidungen prüfen. 0.2.0 ergänzt Magazin-/Reservelimits, unendliche Munition, Nach-Schuss-Wechsel, Granatenstapelquoten und mehrteilige Bypassrechte. Echte Clientwirkung und Profilregeln sind gesondert abzunehmen; der lokale AWS-Konverter installiert nichts.

## Bedienung und Rechte

Im zentralen Menü: `!admin → Erweitert → Waffen`. Unter **Einstellungen** liegen Freigaben und Spielerauswahl; unter **Sonderregeln** die einzelne Waffe mit Munition, Zugriff, Grundquote und bestätigtem Zurücksetzen. Reservemagazine sind keine Reservepatronen. Mapquoten und benutzerdefinierte Rechte bleiben in der Konfiguration und werden erhalten. Die neuen Menüs aktivieren beim Öffnen keine Regeln. Nach-Schuss-Rückwechsel erfolgen nur nach bestätigtem Hinwechsel bei weiterhin erwarteter aktiver Waffe.

`!guns` öffnet das verfügbare Waffenmenü; `!primary` und `!secondary` führen zu den entsprechenden Bereichen. Deaktivierte oder nicht erlaubte Funktionen werden nicht freigegeben. `!lplk_weapons` ist der Verwaltungseinstieg, Modulrecht `lplk.weapons.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Erlaubte und gesperrte Waffen, Kontingente und ausgeschaltetes Menü testen. Beim Ersetzen einer Slotwaffe muss die neue Waffe erfolgreich bereitstehen, bevor die bisherige entfernt wird. Surf-/Zombieprofile mit besonderen Waffenregeln separat abgleichen; die vollständige Migration dieser Profile steht noch aus.


## Version 0.2.2: Einstellungen und laufende Waffenwechsel

Gültige Dateiänderungen werden inhaltlich erkannt. Admin- und Spielermenüs prüfen die aktuellen Regeln und vorhandenen Rechte erneut. Menüänderungen erhalten die übrigen aktuellen Werte einschließlich Mapquoten und mehrteiliger Rechte. Eine alte Löschbestätigung darf keine inzwischen geänderte Waffenregel entfernen.

Reine Format- oder Objektschlüsselreihenfolgeänderungen sowie Speichern ohne Wertänderung lassen eigene Munitionsdefinitionen und geplante Waffenwechsel bestehen. Tatsächliche Regeländerungen stellen weiterhin eigene Definitionswerte zurück und verwerfen alte Wechselaufträge. Spätere Definitionsänderungen anderer Komponenten werden erhalten. Fehlgeschlagenes Speichern oder ungültige Dateien lösen keine Rücksetzung aus. Unbekannte Felder bleiben ausdrücklich unzulässig und werden nicht stillschweigend entfernt. Statusdateifehler unterbrechen die Waffenregeln nicht.

62 neue lokale Prüfungen verwenden die echten Menü-/Datei- und Besitzstandsregeln mit ersetzten Enginegrenzen. Native Munition, Ausgabe, Schusswechsel und Trefferlogik sind im Quellcode unverändert. Lokale Suiten und beide APIs bestanden. Installation sowie native Lade-/Status-/Webprüfung am 19.10.2026 um 15:36 MESZ bestätigt: 20 Komponenten geladen, 36 Webbefehle, Einstellungen und 23 andere DLLs unverändert. Kein neuer Entity-/DispatchSpawn-Test. Die tatsächliche Clientwirkung wird später geprüft.

Nach erneuter ausdrücklicher Fortsetzung durch den Benutzer wurde der Testserver bei frisch bestätigtem Leerstand regulär gestoppt (Exit 0); nur Weapons und Maps wurden ersetzt. Das vorher installierte 0.2.1-Paket unter `../../state/weapons-maintenance-installed-baseline.zip` und die serverseitigen DLL-/Datenrückfälle bleiben für die noch ausstehende Spielabnahme erhalten. [Prüfbericht und Paketprüfsummen](../../state/maps-weapons-maintenance-verification.json). Die frühere Automation bis 14 Uhr bleibt pausiert.
