# LPLK_Economy – Bank, Wetten, Lotto und weitere Wirtschaftsfunktionen

Quellstand **0.9.0**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

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
