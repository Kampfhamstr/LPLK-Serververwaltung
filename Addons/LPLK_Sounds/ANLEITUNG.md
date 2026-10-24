# LPLK_Sounds – Sounds und persönliche Hörfilter

**Ausgabe 24.10.2026: 0.1.9.** [LPLK_Sounds.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Sounds.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.9, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Sounds.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_Sounds.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Sounds.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.1.8**, SwiftlyS2-API **1.4.13**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

Spieler können freigegebene Sounds auswählen, an Empfänger senden und ihre eigene Wiedergabe einstellen. Für Administratoren gibt es einen getrennten Ansagebereich. Das Modul enthält keine Deathcards; der bestehende Spielerbefehl `!sound` bleibt erhalten.

## Installation und Einrichtung

`LPLK_Sounds.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Sounds/LPLK_Sounds.dll`.

Die eigentlichen Audiodateien müssen als passende Workshop-Inhalte vorhanden und gemountet sein. Dafür [AddonsManager](../AddonsManager/ANLEITUNG.md) einrichten, die vorgesehenen Workshop-IDs zuordnen und den Clientdownload prüfen. Katalogmetadaten ersetzen keine Audiodateien. Bestehende Soundnummern bei Katalogänderungen berücksichtigen, damit Binds weiterhin denselben Sound treffen.

Konfiguration: `configs/plugins/LPLK_Sounds/config.json`, relativ zum SwiftlyS2-Ordner. Persönliche Einstellungen und Cache liegen unter `data/LPLK_Sounds/`, unter anderem `sound-listening.json` und `workshop-catalog-cache.json`. Bestehende Hörpräferenzen erhalten.

## Einstellungen ab 0.1.8

Nur auf dem isolierten Testserver installiert; der oben verlinkte GitHub-Download ist ein älterer veröffentlichter Stand. Das aktuelle lokale Paket liegt unter `Build/LPLK_Sounds.zip` mit Prüfmanifest.

Die Konfiguration wird alle fünf Sekunden anhand ihres Inhalts geprüft. Ungültige, fehlende oder mehrdeutige Dateien ersetzen die letzten gültigen Regeln nicht. Menüänderungen und der DeadOnly-Konsolenbefehl lesen den aktuellen Datensatz vor dem Speichern; unbekannte zusätzliche Felder bleiben erhalten. Bei erkennbarer Zwischenänderung wird das Speichern abgewiesen.

Unter Einstellungen kann die Soundpause frei zwischen 1 und 60 Sekunden, auch mit Nachkommastellen, eingegeben werden. Gültige Regeländerungen beenden laufende Wiedergabe; Rundennutzung, private Sendelimits und bereits laufende Cooldown-Zeiten bleiben erhalten. Reine Formatänderungen beenden keinen Sound.

Änderungen an `AutoCatalogFromAddon` oder `WorkshopIds` werden erst beim nächsten tatsächlichen Map-Precache aktiviert. Bis dahin ist Wiedergabe gesperrt; im laufenden Spiel werden keine neuen Ressourcen nachgeladen. Für einen Rückfall müssen die passende Pluginversion und bei inzwischen geänderten Einstellungen auch die gesicherte Konfiguration/Daten zusammen geprüft werden. Die Testauslieferung hat die vorhandenen Werte, Soundnummern und den Katalogcache erhalten.

## Bedienung und Rechte

| Befehl | Zweck |
|---|---|
| `!sound` bzw. `!sound <Nummer>` | Menü öffnen bzw. Sound auswählen |
| `!sound s<Seite>`, `!sounds [Seite]` | Im Katalog blättern / Liste lesen |
| `!soundsend [Soundnummer]` | Sound an einen Spieler senden |
| `!soundvolume <0-100>` | Eigene Lautstärke einstellen |
| `!lplk_sound_listen <on\|off>` | Eigenes Hören ein-/ausschalten |
| `!adminsounds`, `!adminsound` | Geschützter Adminbereich |
| `!soundall` | Geschützte Ansage mit abweichender Behandlung persönlicher Hörfilter |
| `!lplk_sounds` | Moduleinstellungen/Status |

Modulrecht `lplk.sounds.admin`; besondere Ansagen unterliegen zusätzlichen konfigurierten Prüfungen. Die Sounds müssen nach den aktiven Empfänger-, Rundengrenz- und Cooldownregeln verfügbar sein.

## Prüfen

Mit mindestens zwei Clients normalen Sound, privaten Versand, Lautstärke, Hörfilter und eine berechtigte Adminansage hörbar testen. „Im Katalog“ und „precache erfolgreich“ sind noch kein Beweis für Clientwiedergabe. Die aktuelle Quellversion ist neuer als ein Teil der produktiv eingesetzten Pakete; dieser Upload aktualisiert keinen Server.
