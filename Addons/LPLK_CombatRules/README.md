# LPLK_CombatRules – Treffer- und Kampfregeln

**Ausgabe 24.10.2026: 0.1.4.** [LPLK_CombatRules.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_CombatRules.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.4, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_CombatRules.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_CombatRules.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_CombatRules.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Lokaler Teststand **0.1.3**, SwiftlyS2-API **1.4.14**, zusätzlich gegen **1.5.0-beta.10** geprüft. Der GitHub-Link oben enthält weiterhin die ältere Ausgabe 0.1.1. Aktuelles Paket: [Build/LPLK_CombatRules.zip](Build/LPLK_CombatRules.zip). [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Addon bündelt Headshot-Regeln, die konfigurierbare Golden Rule und waffen-/teambezogene Schadensfilter. Regeln können von Waffen, Spielphase und Rundensituation abhängen. Maßgeblich ist die konkrete Konfiguration des Spielprofils.

## Installation und Einrichtung

`LPLK_CombatRules.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_CombatRules/LPLK_CombatRules.dll`.

Unter `data/LPLK_CombatRules/settings.json` insbesondere `HeadshotOnly`, `FirearmsOnly`, `GoldenRule` und die individuellen Regeln prüfen. Nicht gleichzeitig ein älteres HeadshotOnly-Addon für dieselben Ereignisse aktiv lassen, ohne die Wechselwirkung ausdrücklich getestet zu haben. Bestehende Regeln beim Umstieg einzeln abgleichen; Quellstandardwerte sind nicht automatisch die gewünschte Serverkonfiguration.

## Bedienung und Rechte

Zentraler Einstieg: `!admin → Erweitert → Kampfregeln`. **Einstellungen** enthält die globalen Regeln. **Sonderregeln** bietet je Waffe Kopftrefferpflicht, Schaden (Festwert, Zuschlag/Abzug oder Faktor), Status und bestätigtes Zurücksetzen. „Unverändert“ hebt nur die Schadensformel auf, nicht die Kopftrefferregel. Die bestehenden Spielregeln bleiben beim Update erhalten.

`!lplk_combatrules` öffnet die Verwaltung. Modulrecht **`lplk.combat.admin`**, zentrale Verwaltung auch `lplk.admin`. Spieler benötigen keinen eigenen Befehl, um den aktiven Kampfregeln zu unterliegen.

## Prüfen

Kopf-/Körpertreffer, erlaubte und ausgeschlossene Waffen, Gegner-/Teamschaden sowie Warmup und Rundenvorsprung prüfen. Bei Regelausnahmen muss auch der ausgeschaltete Zustand getestet werden. Lokale Logikprüfungen ersetzen keine vollständige Mehrspielerabnahme.


## Sichere Einstellungsänderungen – 0.1.3

Über `!admin` bleiben die vorhandenen Kampfregelmenüs mit `lplk.combat.admin`, `lplk.admin` oder dem vorhandenen Rootrecht erreichbar. Gültige externe Dateiänderungen werden auch bei unverändertem Zeitstempel erkannt. Ein Menü verändert den aktuellen Datensatz; dabei bleiben neuere Werte anderer Felder und Waffen erhalten.

Eine fehlende, ungültige oder mit unbekannten Feldern versehene Datei wird beim Betrieb nicht überschrieben. Der letzte gültige Stand bleibt aktiv, `reloadError` zeigt den Fehler. Unbekannte Felder werden weiterhin ausdrücklich abgewiesen. Bei fehlgeschlagenem Speichern bleiben aktive Regeln unverändert. Statusdateifehler unterbrechen die Trefferregeln nicht.

`Sonderregeln > Waffe > Zurücksetzen` bindet die Bestätigung an den angezeigten Regelstand. Wird diese Waffenregel zwischenzeitlich geändert, ist eine neue Bestätigung erforderlich. Andere Waffenregeln und Zähler bleiben erhalten.

Trefferberechnung, Goldene Regel, Scopeprüfung zum Trefferzeitpunkt und Headshotmechanik sind gegenüber0.1.2 im Quellcode identisch.58 neue lokale Prüfungen und bestehende Waffen-/Kampf-/Munitions-/Menütests bestanden. Native Lade-/Status-/Webprüfung ist keine Bestätigung tatsächlicher Clienttreffer. Nachweis: `../../state/combat-maintenance-verification.json`.

Der Inhaltsvergleich vor atomarem Speichern ist keine systemweite Schreibsperre für externe Editoren. Auf dem Testserver wurden alle bestehenden Einstellungen exakt erhalten. Rückfall: `.lplk-rollback/LPLK_CombatRules.previous.zip` enthält0.1.2; `LPLK_CombatRules.data-before-maintenance.zip` enthält die damaligen Einstellungen. Produktion und GitHub-Ausgabe bleiben unverändert.
