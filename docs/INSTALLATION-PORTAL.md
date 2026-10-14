# Windows-Programm: Zweck und Einrichtung

Das Desktopprogramm verbindet die CS2-Serverübersicht mit Addonbestand, Versionsprüfung und Verbindungsverwaltung. Im Dashboard sieht man mehrere Server und deren Plugins gleichzeitig. Beobachteter Laufzeitstatus, geplanter Ersatz und Entwicklungsstand werden getrennt geführt. Es richtet außerdem den verschlüsselten Zugang zu Pterodactyl, Debian/SSH und GitHub ein.

Die Windows-Oberfläche und die feste Diagnose lesen zunächst Informationen. Im Quellprojekt vorhandene Wartungs- und Auslieferungswerkzeuge können dagegen Änderungen ausführen; sie gehören nicht zum normalen Einstieg und sind kein automatischer Updateknopf.

## Voraussetzungen

- Windows x64 mit PowerShell 7.
- Vollständiger Projektordner mit `portal/`, `scripts/`, `global.json`, `NuGet.Config` und den zentralen Builddateien.
- .NET SDK passend zu `global.json`: derzeit **10.0.401**, mit `latestPatch` als Roll-forward-Regel. Für SwiftlyS2-Plugins wird ebenfalls .NET 10 verwendet.
- Für den Build Internetzugang zu den offiziellen Download- und NuGet-Quellen. Für die Nutzung jeweils ein berechtigter Zugang zum gewünschten Dienst.

## Installieren und starten

PowerShell 7 im Hauptordner des vollständigen Projekts öffnen:

```powershell
.\scripts\Install-Toolchain.ps1
. .\scripts\Use-Environment.ps1
dotnet --version
.\scripts\Build-Portal.ps1
.\scripts\Open-Portal.ps1
```

Das Setup legt SDK, Git, Cache und Programmausgabe unter `%LOCALAPPDATA%\LPLK-Serververwaltung\` an. `toolchain.json` dort enthält die lokalen Pfade. Projektquellen und Ausgabepakete dürfen auf einem NAS liegen; ausführbare Programme und Compiler-Zwischenstände bleiben lokal. Keine Rechtebeschränkung der NAS-Freigabe lockern, um dort Programme auszuführen.

**SDK-Hinweis:** Der aktuelle Werkzeuginstaller lädt das neueste .NET-10-SDK aus den Microsoft-Metadaten. Falls dieses künftig nicht mehr zum in `global.json` festgelegten Feature-Band passt, meldet `dotnet --version` im Projekt einen Fehler. Dann das passende SDK aus den offiziellen [.NET-Downloads](https://dotnet.microsoft.com/download/dotnet/10.0) installieren und `dotnetRoot` in der lokalen `toolchain.json` auf dessen Ordner setzen. Die Projektvorgabe nicht ungeprüft ändern.

`Build-Portal.ps1` stellt Abhängigkeiten im Locked-Modus wieder her, veröffentlicht an den festen lokalen Ausgabeort und führt den Programm-Selbsttest aus. Bei einer Aktualisierung vorher das laufende Portal normal schließen. Danach genügt für den täglichen Start `Portal.cmd` im Projektordner.

## Verbindungen einrichten

| Verbindung | Eingabe | Prüfung und Speicherung |
|---|---|---|
| Pterodactyl | HTTPS-Paneladresse und **Client-API-Schlüssel** aus `/account/api` | „Prüfen und speichern“, anschließend Serverübersicht prüfen; ein Application-API-Schlüssel ist hierfür ungeeignet |
| Debian / SSH | Host, Port, Benutzer und Passwort | Beim Erstkontakt die angezeigte SHA-256-Serverkennung mit einer vertrauenswürdigen Quelle vergleichen, dann speichern |
| GitHub | Classic- oder Fine-grained Personal Access Token | Konto und erreichbare Repositories lesen, danach verschlüsselt speichern; [ausführliche GitHub-Anleitung](GITHUB.md) |

Die Verbindungsdateien unter `private/` sind mit Windows-DPAPI an das Windows-Konto und den Computer gebunden. Auf einem anderen PC die Zugänge erneut in der Oberfläche einrichten. `private/` gehört auch verschlüsselt nicht in GitHub. Gespeicherte Passwort- und Tokenfelder bleiben leer; das ist beabsichtigt.

GitHub lässt sich direkt mit `scripts/Open-Portal.ps1 -GitHub` öffnen, SSH mit `-Ssh`. Der GitHub-Zugang ist kontoweit angelegt; die sichtbare Repositoryliste hängt zusätzlich von Tokenrechten und Organisationsregeln ab. Das Laden dieser Liste ist noch kein Schreibtest und lädt keine Projektdateien hoch.

## Erste Kontrolle

1. Panelverbindung prüfen, dann **Server prüfen** ausführen.
2. Einen bekannten Server und dessen Addonbestand mit der tatsächlichen Installation vergleichen. Veraltete oder nicht verfügbare Daten nicht als entfernte Plugins deuten.
3. Falls benötigt die feste SSH-Diagnose lesen; sie soll keine Dienste verändern.
4. GitHub-Repositories laden und Kontonamen kontrollieren.

## Aktualisieren und sichern

Quellen aktualisieren, Portal normal schließen und erneut `Build-Portal.ps1` ausführen. Aktuelle Ausgabeorte bleiben gleich. `private/` und eigene Daten unter `state/` erhalten; insbesondere der editierbare Addonplan und dessen Verlauf sind Nutzerdaten. Nach dem erfolgreichen Start entbehrliche Compiler-Zwischenstände nur entfernen, wenn sie nicht mehr referenziert oder benötigt werden.

Bei Buildfehlern zuerst SDK-Version, lokale Laufzeitzuordnung, NuGet-Erreichbarkeit und ein noch laufendes Portal prüfen. Bei Verbindungsfehlern Rechte, HTTPS und gegebenenfalls IP-Freigaben des Dienstes prüfen; Geheimnisse nicht in Fehlermeldungen, Screenshots oder GitHub-Issues übernehmen.
