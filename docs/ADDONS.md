# Addons: Funktionen und Anleitungen

**Fertige ZIPs:** [Paketdownloads und Prüfsummen](DOWNLOADS.md) · [Ausgabe 24.10.2026](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/fleet-2026.10.24-gameplay). Prerelease mit ausstehender Client- und Mehrspielerabnahme.

Aktuelle LPLK-Module und Bridge: Ausgabe 24.10.2026. Die Version bezeichnet das angebotene Paket; tatsächlich installierte Profile und Prüfgrenzen stehen in [FLEET-20261024.md](FLEET-20261024.md). Die beiden älteren Infrastruktur-/Beispielpakete bleiben gesondert gekennzeichnet.

Zum Einstieg: [Addons installieren und aktualisieren](INSTALLATION-ADDONS.md). Jede Einzelanleitung erklärt Zweck, Einrichtung, Befehle, Rechte und die notwendige Funktionsprüfung.

## Menüs, Zugang und Verwaltung

| Addon | Version / SwiftlyS2-API | Aufgabe | Anleitung |
|---|---|---|---|
| LPLK_Admin | 0.10.5 / siehe Einzelanleitung | Einfaches Adminmenü: Spieler, Maps, Sounds und Erweitert | [Öffnen](../Addons/LPLK_Admin/ANLEITUNG.md) |
| LPLK_Whitelist | 0.2.6 / siehe Einzelanleitung | Zutrittslisten, Verwaltung und optionaler Adminbypass | [Öffnen](../Addons/LPLK_Whitelist/ANLEITUNG.md) |
| LPLK_ChatGuard | 0.1.4 / siehe Einzelanleitung | Chat-, Namens- und Werbefilter | [Öffnen](../Addons/LPLK_ChatGuard/ANLEITUNG.md) |
| SerververwaltungBridge | 1.7.0 / siehe Einzelanleitung | Status und Auftragsanbindung der Webverwaltung | [Öffnen](../Addons/SerververwaltungBridge/ANLEITUNG.md) |

## Wirtschaft, Statistik und Aufnahmen

| Addon | Version / SwiftlyS2-API | Aufgabe | Anleitung |
|---|---|---|---|
| LPLK_Economy | 0.11.0 / siehe Einzelanleitung | Bank, Wetten, Lotto, Raub, Titel, Kopfgeld und Kill-Belohnungen | [Öffnen](../Addons/LPLK_Economy/ANLEITUNG.md) |
| LPLK_Statistics | 0.2.5 / siehe Einzelanleitung | Spieler-/Waffenwerte, Bombenzähler und Ranglistenexporte | [Öffnen](../Addons/LPLK_Statistics/ANLEITUNG.md) |
| LPLK_Demos | 0.1.3 / siehe Einzelanleitung | Lokale SourceTV-Aufnahmen und Aufnahmeindex | [Öffnen](../Addons/LPLK_Demos/ANLEITUNG.md) |

## Maps und Spielregeln

| Addon | Version / SwiftlyS2-API | Aufgabe | Anleitung |
|---|---|---|---|
| LPLK_Maps | 0.4.2 / siehe Einzelanleitung | MapChooser, RTV, Nominierungen und Mapvermessung | [Öffnen](../Addons/LPLK_Maps/ANLEITUNG.md) |
| LPLK_Weapons | 0.2.4 / siehe Einzelanleitung | Waffenregeln und begrenztes Waffenmenü | [Öffnen](../Addons/LPLK_Weapons/ANLEITUNG.md) |
| LPLK_CombatRules | 0.1.4 / siehe Einzelanleitung | Headshot-Regeln, Golden Rule und Schadensfilter | [Öffnen](../Addons/LPLK_CombatRules/ANLEITUNG.md) |
| LPLK_Teams | 0.2.2 / siehe Einzelanleitung | Teamwechsel und Schutz des führenden Teams | [Öffnen](../Addons/LPLK_Teams/ANLEITUNG.md) |
| LPLK_AntiCamp | 0.1.6 / siehe Einzelanleitung | Zeitbasierte Camping-Erkennung mit Warnung und Reaktion | [Öffnen](../Addons/LPLK_AntiCamp/ANLEITUNG.md) |
| LPLK_AntiTeamFlash | 0.1.3 / siehe Einzelanleitung | Schutz vor Teamblenden | [Öffnen](../Addons/LPLK_AntiTeamFlash/ANLEITUNG.md) |

## Bewegung, Sounds und Effekte

| Addon | Version / SwiftlyS2-API | Aufgabe | Anleitung |
|---|---|---|---|
| LPLK_Parachute | 0.1.6 / siehe Einzelanleitung | Fallschirm über die gebundene Benutzen-Aktion | [Öffnen](../Addons/LPLK_Parachute/ANLEITUNG.md) |
| LPLK_BombWires | 0.1.6 / siehe Einzelanleitung | Drahtwahl beim Bombenlegen und Entschärfen | [Öffnen](../Addons/LPLK_BombWires/ANLEITUNG.md) |
| LPLK_Sounds | 0.1.9 / siehe Einzelanleitung | Soundkatalog, Versand, Hörfilter und Adminansagen | [Öffnen](../Addons/LPLK_Sounds/ANLEITUNG.md) |
| LPLK_ThirdPerson | 0.1.4 / siehe Einzelanleitung | Berechtigungsgesteuerte Außenkamera | [Öffnen](../Addons/LPLK_ThirdPerson/ANLEITUNG.md) |
| LPLK_Fluch | 0.1.3 / siehe Einzelanleitung | Zeitlich begrenzter Hühnerfluch | [Öffnen](../Addons/LPLK_Fluch/ANLEITUNG.md) |
| LPLK_NadeEffects | 0.1.3 / siehe Einzelanleitung | Administrative Granatenhinweise und Effekte | [Öffnen](../Addons/LPLK_NadeEffects/ANLEITUNG.md) |

## Infrastruktur und Beispiel

| Addon | Version / SwiftlyS2-API | Aufgabe | Anleitung |
|---|---|---|---|
| AddonsManager | 2.0.3-lplk.3 / 1.4.13 | Workshop-Inhalte einbinden; Kompatibilität nach CS2-Updates prüfen | [Öffnen](../Addons/AddonsManager/ANLEITUNG.md) |
| EnvironmentCheck | 1.0.0 / 1.4.13 | Leeres Buildbeispiel, keine eigentliche Umgebungsdiagnose | [Öffnen](../Addons/EnvironmentCheck/ANLEITUNG.md) |

`Addons/_Gemeinsam` enthält mehrfach benötigten Quellcode und Lizenzmaterial. Es wird beim Bauen in die Module eingebunden und ist kein eigenständiges Plugin.

## Wie die Komponenten zusammenarbeiten

LPLK_Admin bietet die gemeinsame Bedienung. Die Fachmodule liefern ihre Funktionen und prüfen deren Verfügbarkeit. AddonsManager stellt benötigte Workshop-Inhalte bereit. Die Bridge verbindet den Gameserver mit der Website. Das Windows-Programm verwaltet Verbindungen und die Bestandsübersicht; es muss nicht auf dem Gameserver installiert werden.

Nicht jedes Modul ist für jeden Server sinnvoll. Beispielsweise braucht ein reiner Standardserver keine Außenkamera, und ein Surfprofil benötigt andere Bewegungs-/Waffenregeln. Nur die tatsächlich benötigten Komponenten installieren und Überschneidungen mit bestehenden Plugins prüfen.

## Prüfstand und Lizenzen

Die funktionalen Komponenten wurden lokal und/oder im vorgesehenen Testbetrieb geprüft. Die vollständige reale Client-, Mehrspieler- und Profilabnahme steht in Teilen noch aus. „Baut erfolgreich“, „Loaded“, „Funktion im Spiel geprüft“ und „Produktiv freigegeben“ sind verschiedene Aussagen. Aktuelle Quellpakete dürfen nicht mit den teilweise älteren produktiv eingesetzten Ständen verwechselt werden.

Die Sammlung enthält eigene und abgeleitete Komponenten mit unterschiedlichen Lizenzen. Zugehörige `LICENSE*`, `NOTICE*` und erforderliche Quellen erhalten. Keine pauschale MIT-Lizenz für die gesamte Sammlung annehmen; die Herkunftshinweise des jeweiligen Moduls sind maßgeblich.

## Surf und Zombie

| Addon | Version | Aufgabe | Anleitung |
|---|---|---|---|
| LPLK_Surf | 0.1.0 | Surf/KZ mit Timer, Bestzeiten, Replays, Stages, Training und Zonen | [Öffnen](../Addons/LPLK_Surf/ANLEITUNG.md) |
| LPLK_Zombie | 0.1.0 | Zombie-Wellen, Klassen und Integration der Kampfregeln | [Öffnen](../Addons/LPLK_Zombie/ANLEITUNG.md) |
