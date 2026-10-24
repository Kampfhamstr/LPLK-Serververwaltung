# LPLK_ThirdPerson – Außenkamera

**Ausgabe 24.10.2026: 0.1.4.** [LPLK_ThirdPerson.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_ThirdPerson.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.4, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_ThirdPerson.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_ThirdPerson.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_ThirdPerson.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Dieser unveränderte GitHub-Download enthält weiterhin0.1.1. Das aktuelle lokale Testpaket0.1.2 liegt unter [Build/LPLK_ThirdPerson.zip](Build/LPLK_ThirdPerson.zip), einschließlich passendem Manifest. Keine neue GitHub-Veröffentlichung.

Quellstand **0.1.2**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Addon ermöglicht berechtigten Spielern eine Außenkamera. Abstand, Höhe und weitere Kameraeigenschaften sind konfigurierbar. Spieler-, Lebens- und Teamwechsel müssen die Kamera zuverlässig zurücksetzen, damit kein alter Kamerazustand übernommen wird.

## Installation und Einrichtung

`LPLK_ThirdPerson.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_ThirdPerson/LPLK_ThirdPerson.dll`.

Einstellungen: `data/LPLK_ThirdPerson/settings.json`. `UseTpPermission` steht im Quellstandard auf **`thirdperson.use`**. Das Recht gezielt vergeben oder die dafür vorgesehene Freigabe im Adminmenü wählen. Kameraabstand, Höhe und Schadensverhalten mit dem Servermodus abstimmen.

## Bedienung und Rechte

`!thirdperson` und `!tp` schalten die Kamera für berechtigte Spieler. `!lplk_thirdperson` dient der Verwaltung; Modulrecht `lplk.thirdperson.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Sicht, Bewegung, Kollisionen, Tod, Teamwechsel und Wiederbeitritt mit einem realen Client prüfen. Der erfolgreiche Pluginstart bestätigt noch keine fehlerfreie Kameradarstellung; die visuelle Clientabnahme steht noch aus.

## Änderungen0.1.2 und Nachweise

Unter `!admin → Erweitert → Kamera` sind Abstand20–250, Höhe20–120 und Glättung0,01–1 direkt eingebbar; Komma oder Punkt zulässig. Rechte und Schadensmodi behalten ihre bisherigen Regeln. `!menu → Kamera`, `!tp`, `!thirdperson` sowie `sw_tp`/`sw_thirdperson` bleiben erhalten.

Dateiänderungen werden nach Inhalt erkannt, auch bei unverändertem Zeitstempel. Ungültige, unbekannte, fehlende oder übergroße Konfigurationen behalten die letzten gültigen Regeln. Menüs überschreiben solche Dateien nicht und erhalten inzwischen extern geänderte andere Werte. Normale Einstellungen verändern den bestehenden ConVar `thirdperson_enabled` nicht; der explizite Aktivschalter ändert ihn erst nach erfolgreichem Speichern. Ein nicht schreibbarer Statusbericht darf die Kamerabereinigung nicht aufhalten.

Am19.10. auf Testserver89450e53:119 neue lokale Menü-/Eingabe-/Konfigurations-/ConVarprüfungen,114 Kameraregeln, beide APIs mit Build UND ZIP,20 Loaded und36 Webbefehle samt echter nativer Statusantwort.24 andere DLLs und sämtliche Einstellungen/Konfigurationen/Framework unverändert; Rückfallarchiv bestätigt. Werte100/80/0,3/back bleiben erhalten. Der Containerabschluss nach regulärem Shutdown brauchte den bestehenden begrenzten finish-stop; Exit137 separat protokolliert, kein Gesundheitsreset. Nachweis: [Prüfbericht](../../state/camera-maintenance-verification.json).

Die sichtbare Kamerabewegung, Kollisionen und Trefferwirkung mit echten Clients bleiben wie vom Benutzer gewünscht für später. Keine neue synthetische native Entityprobe ausgeführt. Original-`CustomTPCommand=tp` ist durch den bestehenden Alias abgedeckt; andere benutzerdefinierte Aliasse anderer Profile sind noch gesondert zu übertragen.
