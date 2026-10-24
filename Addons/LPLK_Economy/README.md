# LPLK_Economy – Bank, Wetten, Lotto und weitere Wirtschaftsfunktionen

**Ausgabe 24.10.2026: 0.11.0.** [LPLK_Economy.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_Economy.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.10.0, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_Economy.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Aktueller lokaler Teststand:** [LPLK_Economy0.9.2](Build/LPLK_Economy.zip) · [Manifest](Build/build-manifest.json). Nur Testserver89450e53; noch keine Veröffentlichung.

**Ältere veröffentlichte Ausgabe:** [LPLK_Economy.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_Economy.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **0.9.2**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Was macht es?

Ein gemeinsames Addon verwaltet Bankkonten, Überweisungen, Wetten, Lotto, Bankraub, Titel, Kopfgeld und Kill-Belohnungen. Die Teilfunktionen sind getrennt schaltbar. Buchungen, offene Ansprüche und Auszahlungen werden nachvollziehbar gespeichert; Wiederholungen eines bereits bekannten Auftrags sollen nicht erneut Geld buchen.

## Installation und Besonderheiten

`LPLK_Economy.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_Economy/LPLK_Economy.dll`.

**Das aktuelle Paket ist für Linux x64 vorgesehen.** Alle mitgelieferten Datenbankbibliotheken einschließlich `libe_sqlite3.so` übertragen. Die native Bibliothek wird für eine glibc-2.17-Basis gebaut; das ist keine Freigabe jedes beliebigen Linuxsystems oder Windowsservers.

Unter `data/LPLK_Economy/` liegen `settings.json`, `economy.sqlite3` und der weitere Betriebsbestand. Eine vorhandene Bank vor Änderungen bei gestopptem Server konsistent sichern, einschließlich gegebenenfalls vorhandener SQLite-Begleitdateien. Bestehende BetBank-Daten benötigen einen ausdrücklich geprüften Übernahmeweg; kein automatischer Import und kein paralleler Betrieb beider Systeme für dieselben Buchungen.

Für einen Quellbuild im Projekt-Hauptordner:

```powershell
. .\scripts\Use-Environment.ps1
.\scripts\Build-EconomyNative.ps1
.\scripts\Build-Plugin.ps1 -Project .\Addons\LPLK_Economy\Rohskripte\LPLK_Economy.csproj
```

Der native Builder verwendet festgelegte und per Hash geprüfte SQLite-/Zig-Downloads. Das erzeugte native Paket ist Teil des vollständigen Pluginbuilds. Ein .NET-Build ohne die native Bibliothek reicht nicht für den Gameserver.

## Spielerbefehle

| Einstieg | Funktion / bisherige Aliase |
|---|---|
| `!bank`, `!konto` | Eigenes Konto und Wirtschaftmenüs |
| `!topbank` | Bankrangliste; auch `!banktop`, `!rich`, `!ranking` |
| `!bbhelp` | Funktionsabhängige Hilfe; auch `!betbankhelp` |
| `!deposit`, `!einzahlen` | Einzahlen |
| `!withdraw`, `!abheben` | Abheben |
| `!transfer`, `!ueberweisen`, `!überweisen` | Überweisen |
| `!bet`, `!bt`, `!wette`, `!wetten` | Wetten |
| `!bets`, `!pool`, `!wettenstatus`, `!wettinfo` | Wettübersicht |
| `!lotto`, `!lotterie`, `!los` | Lotto |
| `!bankraub`, `!robbery` | Bankraub |
| `!spionlog`, `!spylog`, `!bankraublog` | Zugehöriges Spionprotokoll |
| `!titel`, `!title`, `!titles` | Titel und Fortschritt |
| `!kopfgeld`, `!bounty` | Kopfgeld |

Diese Tabelle zeigt die Einstiege; erforderliche Beträge, Ziele und Unteraktionen stehen in der jeweiligen Befehlshilfe bzw. im Menü. Verfügbarkeit hängt von aktiven Teilfunktionen und der Spielsituation ab. Bestehende Binds erhalten; nicht willkürlich zwischen Chat-, rohen Konsolen- und `sw_`-Namen wechseln.

Administration: `!bbadmin`, `!bankadmin`, `!betbankadmin`, außerdem `!lplk_economy` und die geschützten `!bankset`-/`!bankadd`-/`!bankremove`- bzw. Kontoreset-Befehle. Rechte: `lplk.economy.admin`, historisch `betbank.admin`, übergreifend `lplk.admin`; Zielimmunität und Betragsgrenzen gelten zusätzlich.

## Website, Betrieb und Grenzen

Webexport und Web-Adminänderungen müssen ausdrücklich eingerichtet sein. Die aktuelle Websiteanbindung ist auf das freigegebene Testprofil begrenzt. Die Kontoauszugsansicht zeigt nur den begrenzten Export der jüngsten Buchungen.

Eine gemeinsame Bank aller Produktionsserver ist **noch nicht aktiviert**. Planung und ein Übernahmeplaner sind kein fertiger zentraler Buchungsdienst. Keine Datenbanken zwischen laufenden Servern teilen, Bestände zusammenkopieren oder Pending-Einträge löschen.

Nach Installation Ein-/Auszahlung, Überweisung, eingeschaltete Spiele, Rechte und Neustartwiederaufnahme mit isolierten Testkonten prüfen. Trotz umfangreicher lokaler und nativer Buchungsprüfungen bleiben die reale Mehrspielerabnahme und die Migration produktiver Bestände eigene Aufgaben.


## Einstellungen sicher ändern – Teststand 0.9.2

Die bestehenden Einstellungen bleiben unter `data/LPLK_Economy/settings.json`. Über `!admin` ist die Wirtschaftsverwaltung mit dem bereits eingestellten Recht `betbank.admin` erreichbar. Wetten, Lotto, Bankraub, Boni, Titel, Kopfgeld und Kill-Belohnungen bleiben Bestandteil derselben Kontoführung. Alte Befehle und Binds sind erhalten.

Externe gültige Änderungen werden inhaltlich erkannt, auch bei unverändertem Dateizeitstempel. Eine Menüaktion liest die aktuellen Regeln und Rechte erneut, ändert nur die gewünschte Eigenschaft und erhält unbekannte Zusatzfelder. Ungültige oder fehlende Dateien werden beim Betrieb nicht überschrieben; der letzte gültige Stand bleibt aktiv und `reloadError` erklärt das Problem. Fehlende bekannte Erweiterungsfelder werden beim Start weiterhin ergänzt. Die bisher impliziten Felder `Betting` und `BankEnabled` lösen allein kein Umschreiben aus.

Ein abgewiesener Speichervorgang verändert weder aktive Einstellungen noch das Banklimit. Bei offenen Cashvorgängen bleibt das Senken des Limits gesperrt. Neue Einstellungen ändern keine bereits gespeicherten Regeln einer laufenden Wett- oder Lottorunde. Ein Fehler beim Schreiben der Statusdatei sperrt keine gültige Kontoführung.

Der Inhaltsvergleich vor dem atomaren Dateiersatz erkennt zwischenzeitliche Änderungen; er ist keine systemweite Schreibsperre gegen beliebige externe Editoren. Einstellungen daher nicht gleichzeitig in mehreren Programmen speichern.

63 neue Datei-/Menü-/SQLiteprüfungen und die bestehenden Wirtschaftssuiten bestanden; beide APIs und native Status-/36 Webbefehle geprüft. Alle14 Datenbanktabellen samt Schema, vorhandene Einstellungen und24 andere DLLs sind vor/nach der Wartung identisch. Eine während der Erstprüfung erkannte Ergänzung impliziter Defaults wurde korrigiert, die ursprüngliche Einstellungsdatei exakt wiederhergestellt. Nachweis: `../../state/economy-config-verification.json`.

Auf dem Testserver liegen der vorherige Pluginstand0.9.1 in `.lplk-rollback/LPLK_Economy.previous.zip` und die erforderliche Daten-/Konfigurationskopie in `LPLK_Economy.data-before-maintenance.zip`. Eine Datenbankrückfallkopie niemals über inzwischen erfolgte echte Buchungen schreiben. Sichtbare Client-/Cash-/Mehrspielerprüfung folgt später; Produktion und produktive Nutzerdaten wurden nicht migriert.
