# LPLK-Serververwaltung

**Spielplugin-Ausgabe 24.10.2026:** [21 Pluginpakete und Zombie-Quellen](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/fleet-2026.10.24-gameplay) · [Produktivprofile und Prüfgrenzen](docs/FLEET-20261024.md). Surf/KZ und Zombie ohne Bank, Sounds und AntiCamp; Zombie nur Kopfschüsse mit erlaubtem Zielfernrohr.

**Fertige ZIPs:** [Paketdownloads und Prüfsummen](docs/DOWNLOADS.md) · [Aktuelles Prerelease](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/fleet-2026.10.24-gameplay). Die reale Client- und Mehrspielerabnahme steht aus.

**Historischer Prüfstand 16.10.2026:** Bridge **1.6.5** und LPLK_Maps **0.2.7** für SwiftlyS2 **1.4.14 und 1.5.0-beta.10**. [Neue Pakete](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/compat-2026.10.16-bridge-maps) · [Prüfstand und Migrationsgrenzen](docs/COMPATIBILITY-20261016.md). Der aktuelle Spielplugin-Stand ist im Bericht vom 24.10.2026 dokumentiert.

CS2-/SwiftlyS2-Addons, Windows-Verwaltungsprogramm und Webverwaltung in einem gemeinsamen Quellprojekt. Die Komponenten lassen sich gezielt einsetzen; nicht jeder Server benötigt jedes Addon.

Stand des ersten Imports: **14.10.2026**. Die Websitequelle unter `Website/` basiert auf dem freigegebenen Stand **1.15.2.8**. Die vorbereitete Erweiterung 1.15.2.9 ist nicht Bestandteil dieses ersten Imports.

## Was ist enthalten?

| Bereich | Was er macht | Anleitung |
|---|---|---|
| Addons | Spieler-/Adminmenüs, Bank und Spiele, Maps, Sounds, Statistik, Spielregeln und weitere Funktionen | [Alle Addons mit Einzelanleitungen](docs/ADDONS.md) |
| Windows-Programm | Server-/Addonübersicht, Verbindungsverwaltung und Versionsprüfung | [Einrichtung und Build](docs/INSTALLATION-PORTAL.md) |
| Website | Server-, Spieler- und Rechteverwaltung sowie angebundene Bank-/Statistikansichten | [Installation und Updates](docs/INSTALLATION-WEBSITE.md) |

## Einstieg

1. Im [Addon-Verzeichnis](docs/ADDONS.md) eine Funktion auswählen.
2. Die [gemeinsame Installation](docs/INSTALLATION-ADDONS.md) und die Besonderheiten der Einzelanleitung lesen.
3. Zunächst in einer getrennten Testumgebung einrichten und mit einem realen Spieler prüfen.

GitHubs **Download ZIP** liefert die Quellen. Fertige Pluginpakete müssen entweder selbst gebaut oder aus tatsächlich bereitgestellten Release-Anhängen bezogen werden. Der Build legt das aktuelle Paket unter `Addons/<Name>/Build/<Name>.zip` ab.

## Aufbau

```text
Addons/<Name>/Rohskripte/   Quellcode des jeweiligen Addons
Addons/<Name>/ANLEITUNG.md  Zweck, Einrichtung, Befehle und Prüfung
Addons/_Gemeinsam/         Gemeinsam eingebundener Code und Lizenzmaterial
portal/                   Windows-Programm
scripts/                  Build- und ausgewählte Verwaltungswerkzeuge
tests/                    Prüfprojekte
docs/                     Anleitungen
Website/                  PHP-Webverwaltung, Basis 1.15.2.8
```

SDK, Laufzeiten, private Verbindungsdateien, produktive Konfigurationen, Datenbanken, Spieler-/Bankdaten, Logs und Backups gehören nicht in diesen Quellbaum. Die Quellen enthalten generische Vorlagen und synthetische Testwerte. Einzelne Diagnose-/Wartungswege stammen aus einer konkreten Testumgebung; ihre Zielkennungen werden für diesen Import neutralisiert. Sie sind keine allgemeine Freigabe, fremde Server zu verändern. Details zur Auswahl stehen in [IMPORT.md](IMPORT.md).

## Entwicklungsstand

Die Einzelanleitungen nennen Quellversion und benötigte SwiftlyS2-API. Kompilierung und erfolgreicher Pluginstart ersetzen keine Client- oder Mehrspielerabnahme. Die gemeinsame Bank ist auf den vier Standardservern aktiv. Die sechs Spielplugin-Profile und verbleibenden Prüfgrenzen sind in [FLEET-20261024.md](docs/FLEET-20261024.md) dokumentiert. EnvironmentCheck ist ausdrücklich nur ein leeres Buildbeispiel.

Bestehende Chatbefehle und Aliasnamen bleiben bei der Weiterentwicklung wichtig, damit Spieler ihre Binds weiterverwenden können. Neue gemeinsame Menüs bieten zusätzliche Einstiege und zeigen nur passende aktive Funktionen.

Die Komponenten haben unterschiedliche Herkunft und Lizenzbedingungen. Die jeweiligen Lizenz- und NOTICE-Dateien einschließlich benötigter Quellen erhalten; für das Gesamtprojekt wird keine davon abweichende Pauschallizenz behauptet.
