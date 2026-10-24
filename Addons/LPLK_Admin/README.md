# LPLK_Admin – Spieler- und Adminmenüs

**Ausgabe 24.10.2026: 0.10.5.** [LPLK_Admin.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Admin.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Spielermenü 0.9.1 vom 24.10.2026:** Kopfgeld → Konto → Wetten → Bankraub → Lotterie → Titel → Mapmenü → Sounds → Statistik → Waffen → Kamera → Demo sichern → Hilfe → Adminmenü. Sichtbarkeit und Rechte gelten unverändert. Aktueller Auslieferungsstatus im [Prüfmanifest](Build/build-manifest.json); die folgenden Versionsabschnitte beschreiben frühere Stände.

**Menüstand 23.10.2026: 0.9.0, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Admin.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Aktueller lokaler Teststand:** [LPLK_Admin.zip](Build/LPLK_Admin.zip) · [Prüfmanifest](Build/build-manifest.json). 0.7.0 ist nur auf Testserver89450e53 installiert und noch nicht auf GitHub veröffentlicht. Die veröffentlichte Menüausgabe vom14.10. bleibt0.5.0.

Quellstand **0.7.0**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

LPLK_Admin bietet unter `!admin` vier kurze Einstiege: **Spieler**, **Maps**, **Sounds** und **Erweitert**. Spieler öffnet direkt die Spielerliste; Maps und Sounds öffnen ihr Fachmodul ohne Zwischenkategorie. Seltene Aktionen und übrige Module stehen in einer flachen, blätterbaren Liste unter Erweitert. Das öffentliche `!menu` bleibt nach Spielerfunktionen gruppiert. Spieler sehen Funktionen nur, wenn das entsprechende Addon geladen und die Aktion für sie verfügbar ist. Berechtigte Administratoren erhalten außerdem Zugang zur Verwaltung und zu Moduleinstellungen.

## Installieren und einrichten

Das vollständige Paket `LPLK_Admin.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ergebnis: `game/csgo/addons/swiftlys2/plugins/LPLK_Admin/LPLK_Admin.dll`.

Die gewünschten Fachmodule zusätzlich installieren. Das Adminaddon ersetzt weder Economy noch Maps, Sounds oder andere Module. Rechte in SwiftlyS2 passend zu den Aufgaben vergeben. Modulverwaltung und einzelne Spieleraktionen prüfen ihre eigenen Rechte und Zielimmunitäten; ein sichtbares Adminmenü ist keine pauschale Erlaubnis für jede Aktion. Für die Websiteanbindung außerdem deren Rechte und die Bridge einrichten.

## Bedienung

| Befehl | Zweck |
|---|---|
| `!menu` | Gruppiertes Spielermenü; mit ausreichenden Rechten auch Einstieg ins Adminmenü |
| `!admin` | Direkter Einstieg in die Administration |
| `!adminmenu` | Weiterer Einstieg in dieselbe Administration |

Nach der Spielerauswahl stehen alle erlaubten Spieleraktionen in derselben Liste: **Kicken, Bannen, Chat, Team, Töten, Respawn, Hinteleportieren, Herholen, Einfrieren, Freigeben, Heilen, Rüsten, Noclip an, Noclip aus und Nachricht**. Bei geladenen, aktiven und freigegebenen Fachmodulen kommen **Verliererteam, Fluch, Granateneffekt, Konto und Sound** hinzu. Lange Listen scrollen mit höchstens fünf sichtbaren Zeilen. Alte Befehle und Binds bleiben erhalten.

`!menu` bietet **Maps, Sounds, Waffen, Kamera, Wirtschaft, Info** sowie für berechtigte Nutzer **Adminmenü**. Deaktivierte Funktionen und leere Funktionsgruppen werden ausgeblendet; Info enthält die allgemeine Bedienhilfe. Sounds fragen nach Auswahl **„An alle?“ → Ja/Nein**. Nein öffnet die Spielerwahl, berechtigte öffentliche Adminstarts fragen anschließend **„Override?“ → Ja/Nein**. Globale Soundkonfiguration liegt unter Einstellungen. Maps bietet **RTV, Nominieren, Mapwechsel, Maptest und Einstellungen**; im reinen Prüfmodus entfallen RTV/Nominieren.

Das Recht `lplk.admin` wird für die übergreifende Modulverwaltung berücksichtigt. Die Fachmodule nennen zusätzliche eigene Rechte in ihren Anleitungen. Vorhandene direkte Befehle bleiben neben den Menüs nutzbar.

## Nach der Installation prüfen

Mit einem normalen Spieler und einem Administrator testen: richtige Gruppen, keine leeren Kategorien, kurze Seiten, funktionierender Zurückweg und erneute Rechteprüfung nach Änderungen. Eine deaktivierte Spielerfunktion muss verschwinden; berechtigte Administratoren sollen die Einstellungen des weiterhin geladenen Moduls erreichen können. Version 0.7.0 wird zunächst ausschließlich auf dem Testserver 89450e53 geprüft. Eine erfolgreiche automatische Prüfung ersetzt die Bedienabnahme im Spiel nicht.

## Gründe und Dauern

Unter `!admin → Erweitert → Einstellungen` öffnen **Gründe** und **Dauern** die vorhandenen Listen. **Hinzufügen**, **Ändern** und **Entfernen** bearbeiten einen Eintrag; Entfernen muss bestätigt werden. Reihenfolge und bestehende Werte bleiben ansonsten erhalten. Gründe:1–30 Einträge mit1–150 Zeichen; Dauern:1–20 Einträge mit0–525600 Minuten.0 bedeutet dauerhaft. Der letzte Eintrag kann nicht entfernt werden.

Diese Einstellungen benötigen vorhandenes `lplk.admin` oder `admin.root`; dadurch werden keine neuen Rollen vergeben. Einzelne Spieleraktionen und ihre Zielprüfung behalten die bisherigen Rechte und das Bridge-Backend. Zusätzliche T3-Befehlsnamen sind ab 0.7.0 konfigurierbar; die tatsächlichen alten Serverprofile und Sanktionsdaten sind weiterhin gesondert zu übernehmen. Vorhandene Rechte und Immunitäten werden dabei nicht durch neue Namen erweitert.

Datei: `addons/swiftlys2/data/LPLK_Admin/settings.json`. Änderungen werden inhaltlich geprüft, etwa alle zwei Sekunden sowie beim Menüaufruf. Bei beschädigten oder ungültigen Dateien bleiben die letzten gültigen Listen aktiv; Menüänderungen überschreiben die ungültige Datei nicht. Zusätzliche Felder bleiben beim Speichern erhalten. Ändern sich Gründe oder Dauern, werden offene eigene Adminmenüs geschlossen und ihre bisherigen Eingaben/Bestätigungen ungültig. Menü neu öffnen. Fremde Fachmenüs sowie reine Formatänderungen werden davon nicht betroffen.

Technischer Prüfstand19.10.:422 neue lokale Fachprüfungen,1287 bestehende Menüprüfungen,93 Grundprüfungen, beide APIs und native Lade-/Status-/Webprüfung bestanden. Fünf vorhandene Gründe und sieben Dauern hashgleich; keine echten Sanktionen als Probe ausgeführt. Rückfälle erhalten. Bedienabnahme im Spiel auf Benutzerwunsch später.

## Zusätzliche Befehlsnamen und alte Binds ab 0.7.0

Unter `!admin → Erweitert → Einstellungen → Befehle` die Aktion auswählen. **Hinzufügen** ergänzt einen eigenen Namen, **Entfernen** löscht ihn nach Bestätigung. Die festen bisherigen Namen bleiben erhalten. Ein Name wie `sperren` für **Ban** ist als `sperren`, `sw_sperren` und über die konfigurierten Chatpräfixe verwendbar. Bestehende fremde Befehlsnamen werden nicht ersetzt; ein Konflikt wird in der Diagnose gemeldet. Bei einer Änderung werden entfernte Namen und noch offene alte Aufrufe gesperrt.

Die bisherigen direkten Konsolennamen sind zusätzlich registriert: `admin`, `adminmenu`, `asay`, `say`, `psay`, `csay`, `hsay`, `ban`, `addban`, `unban`, `kick`, `gag`, `ungag`, `mute`, `unmute`, `silence`, `unsilence`, `slay`, `respawn`, `1up`, `team`, `noclip`, `goto`, `bring`, `freeze`, `unfreeze`, `rr`, `restart`, `rcon`, `cvar`, `players`, `list`, `who`, `health`, `armor`. Die vorhandenen `sw_`-Befehle bleiben erreichbar. Für Spieler gelten weiterhin die jeweiligen Aktionsrechte und der Zielschutz; das Recht zum Bearbeiten der Befehlsnamen erteilt keine zusätzlichen Aktionsrechte.

Ziele akzeptieren zusätzlich Steam2 (`STEAM_0:0:19867136` oder `STEAM_1:0:19867136`), Steam3 (`[U:1:39734272]`), numerische Spieler-Slots und die Langformen `@terrorists` / `@counterterrorists`. Bestehende Steam64-, Namens- und Selektormöglichkeiten bleiben erhalten. Der Rundenneustart akzeptiert z. B. `rr 10` für zehn Sekunden; gültig sind 0–60 Sekunden. Ohne Angabe bleiben die bisherigen fünf Sekunden bestehen.

Optionales zusätzliches Feld in der vorhandenen `settings.json` – vorhandene Gründe, Dauern und andere Felder erhalten:

```json
"Commands": {
  "AdminMenu": ["verwaltung"],
  "Ban": ["sperren"],
  "RestartGame": ["neuerunde"]
}
```

Namen werden kleingeschrieben, ein optionales `sw_`-Präfix wird einmal entfernt. Erlaubt sind 1–40 ASCII-Buchstaben/Ziffern sowie `_`, `-`, `.`; höchstens 16 zusätzliche Namen je Gruppe. Mehrdeutige Aktionszuordnungen, reservierte Namen und beschädigte Dateien werden abgewiesen. Die letzte gültige Einstellung bleibt aktiv. Die Diagnose `sw_lplk_admin_commands` zeigt die registrierten Namen und Fehler; als Spieler erfordert sie bestehendes Verwaltungsrecht.

Die alten Konfigurationsgruppen `AddAdmin`, `RemoveAdmin`, `ListAdmins`, `ChangeMap`, `ChangeWSMap` werden bei vorhandenem Eintrag erhalten, jedoch nicht von diesem Addon aktiviert. Rollen bleiben bei der zentralen Verwaltung, Maps beim Mapaddon. Botziele, die genaue frühere Noclip-Umschaltung, Cvar-Leseabfragen, Players-JSON-Ausgaben sowie abweichende alte Rechteprofile und Sanktionsdaten sind noch offen.

Technischer Prüfstand 20.10.: 276 Befehlsprüfungen jeweils mit API 1.4.14 und 1.5.0-beta.10, 422 Konfigurations-, 1287 Menü- und 93 Grundprüfungen bestanden. Auf Testserver89450e53 sind 20 Plugins geladen, alle 35 direkten Namen registriert und 39 Webbefehle bestätigt. Der zunächst gefundene Präfix-Konflikt wurde korrigiert. 24 andere DLLs, Einstellungen und Sanktionsdaten sind unverändert; 0.6.0 bleibt als geprüfter Rückfall erhalten. Es wurden keine echten Kicks/Banns als Probe ausgeführt. Die Tests im Spiel folgen auf Benutzerwunsch später.
