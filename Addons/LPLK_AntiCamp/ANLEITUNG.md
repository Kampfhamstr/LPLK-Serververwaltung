# LPLK_AntiCamp – Camping erkennen und begrenzen

**Ausgabe 24.10.2026: 0.1.6.** [LPLK_AntiCamp.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_AntiCamp.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.5, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_AntiCamp.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_AntiCamp.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_AntiCamp.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Dieser veröffentlichte Download bleibt auf dem älteren Stand. Aktuelles lokales Paket0.1.4: `Build/LPLK_AntiCamp.zip`, nur Testserver.

Quellstand **0.1.4**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Addon beobachtet Aufenthaltsdauer und Bewegung innerhalb eines eingestellten Bereichs. Nach der vorgesehenen Zeit warnt es und führt die konfigurierte Reaktion aus. Die Auswertung berücksichtigt verstrichene Zeit; sie zählt nicht einfach Frames. Einsatz und Grenzen müssen zum Spielmodus passen.

## Installation und Einrichtung

`LPLK_AntiCamp.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel-DLL: `game/csgo/addons/swiftlys2/plugins/LPLK_AntiCamp/LPLK_AntiCamp.dll`.

Den erzeugten Daten-/Konfigurationsbestand unter `data/LPLK_AntiCamp/` erhalten. Campdauer, Warnzeit, Bewegungsradius, Bestrafung und Gesundheitsreserve im Modulmenü passend einstellen. Auf Surf-, Zombie- oder AFK-Situationen abgestimmte Regeln separat testen.

## Bedienung und Rechte

`!lplk_anticamp` öffnet für Berechtigte die Verwaltung. Der direkte Befehl verlangt `lplk.anticamp.admin`. Über LPLK_Admin ist das Modul ebenfalls erreichbar, sofern die zentrale Rechteprüfung den Zugriff erlaubt. Für Spieler ist kein Aktivierungsbefehl erforderlich.

## Prüfen

Stillstehen bis zur Warnung, rechtzeitiges Wegbewegen, Strafe, Tod und Rundenwechsel kontrollieren. Besonders wichtig: Eine Warnung darf nicht unbemerkt in einer anderen Runde oder für einen neuen Spieler auf demselben Slot weiterlaufen. Erst mit zurückhaltenden Testwerten prüfen, bevor die Regel für ein vollständiges Spielprofil übernommen wird.

## Erweiterung0.1.4 vom19.10.2026

Unter `!admin` stehen Einstellungen und Status getrennt bereit. Höchstens fünf sichtbare Einträge; Nummern, Farben und W/S/E/Tab bleiben erhalten. Gültige externe Dateiänderungen werden regelmäßig übernommen. Ungültige Dateien behalten die letzten gültigen Regeln und werden nicht durch Menüaktionen überschrieben. Eigene Zwischenwerte bleiben bei Auswahlmenüs erhalten. `status.json` zeigt Konfigurationsfehler über `reloadError`.

Erkennung, Warnung, Strafen und Ausnahmen haben kurze Unterseiten. Strafzeit, Schaden, HP-Reserve, Spielzielausnahmen und AFK-Behandlung sind unverändert. Vor einer beobachteten Runde wird kein nativer Spielregelzugriff benötigt.

154 neue gemeinsame Prüfungen sowie bestehende Fach-/Menütests bestanden; beide APIs und tatsächliche native Statusantworten geprüft. Echte Client-/Mehrspielerabnahme auf Benutzerwunsch später.
