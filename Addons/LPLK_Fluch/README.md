# LPLK_Fluch – Zeitlich begrenzter Hühnerfluch

**Ausgabe 24.10.2026: 0.1.3.** [LPLK_Fluch.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Fluch.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.3, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Fluch.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_Fluch.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Fluch.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Der veröffentlichte Download enthält noch0.1.1. Die aktuelle ausschließlich lokal/testserverseitig geprüfte0.1.2 liegt in `Build/LPLK_Fluch.zip`; keine neue GitHub-Veröffentlichung.

Quellstand **0.1.2**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Administratoren können einen zeitlich begrenzten Effekt aus umkreisenden Hühnern und einer abschließenden Explosion auslösen. Dauer, Umfang und Schaden sind konfigurierbar. Begrenzungen sollen verhindern, dass beliebig viele gleichzeitige Effekte entstehen oder eine alte Aktion einen neu verbundenen Spieler auf demselben Slot trifft.

## Installation und Einrichtung

`LPLK_Fluch.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Fluch/LPLK_Fluch.dll`.

Einstellungen unter `data/LPLK_Fluch/settings.json` bzw. im Modulmenü anpassen. Schaden und Anzahl zuerst zurückhaltend in einer Testumgebung wählen; das ist eine administrative Spielaktion mit Auswirkung auf das Ziel.

## Bedienung und Rechte

`!fluch` startet den vorgesehenen Auswahleinstieg, `!fluchstop` beendet Effekte, `!fluchconfig` und `!lplk_fluch` führen zur Einrichtung. Modulrecht `lplk.fluch.admin`, zusätzlich der konfigurierte historische `AdminPermission`-Zugang; zentrale Verwaltung auch `lplk.admin`. Zielschutz bleibt zu beachten.

## Prüfen

Darstellung, Ablauf, Explosion, Abbruch, Tod und erneuten Beitritt testen. Nach Kartenwechsel oder Entladen dürfen keine verwaisten Effekte zurückbleiben. Die tatsächliche Darstellung und Schadenswirkung im Client sind noch gesondert abzunehmen.

## Änderungen 0.1.2 – nur Testserver

Zahlen lassen sich direkt innerhalb der bisherigen Konfigurationsgrenzen eingeben: Hühner1–64, Dauer1–60s, Schaden je Huhn0–100, gleichzeitige Flüche1–4; außerdem Höhe/Radius/Geschwindigkeit/Wellen und Aktualisierungsintervall. Unter Hinweise sind jetzt auch Start-/Endanzeigezeit, Fortschrittsanzeigezeit und Menüdauer erreichbar. Bestehende Zwischenwerte bleiben erhalten. Ein schon laufender Fluch behält seine beim Start festgelegten Werte; Ausschalten beendet ihn wie bisher.

`settings.json` wird inhaltlich alle zwei Sekunden geprüft, auch bei gleichem Dateidatum. Ungültige/fehlende/verknüpfte Dateien, unbekannte Felder oder mehr als64KiB behalten die letzten gültigen Regeln. Menüs überschreiben solche Dateien nicht und berücksichtigen neuere gültige externe Werte. Schreibfehler im Statusbericht unterbrechen den Effektablauf nicht.

165 gemeinsame neue Prüfungen mit tatsächlichen Menü-/Eingabeaufrufen und ersetzten Engine-Grenzen bestanden; zusätzlich die bisherigen Fachtests. Build und Paket gegen Swiftly1.4.14 und1.5.0-beta.10 geprüft, native Lade-/Status-/Webantworten auf dem Testserver bestätigt. Keine neue synthetische Entityprobe. Sichtbare Effekte, tatsächlicher Schaden beziehungsweise Inventarentzug und die Bedienung mit Clients bleiben auf Benutzerwunsch später.
