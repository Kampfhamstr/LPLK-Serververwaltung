# Installation und Build – Plugin-Ausgabe 26.10.2026

## Fertige Pakete

Die aktuellen Einzelpakete stehen unter [Downloads](DOWNLOADS.md). Das Paket enthält den vollständigen Pluginordner. Ziel ist `game/csgo/addons/swiftlys2/plugins/<Name>/`, einschließlich Ressourcen, Laufzeitabhängigkeiten und Lizenzdateien. Die DLL darf nicht doppelt verschachtelt liegen. Zielumgebung: Linux x64/CS2 mit SwiftlyS2 und .NET-10-Laufzeit. Die festgeschriebenen Build-API-Versionen stehen in den jeweiligen csproj-/Lockdateien; sie bestimmen keine automatische Framework-Aktualisierung.

Bestehende Einstellungen und Daten sichern. Bei einem Update RAM-Daten regulär sichern lassen und geordnet entladen oder stoppen, anschließend nur den Pluginordner ersetzen. Keine älteren Datenbanken über neuere Kontostände kopieren. Auf fremden Servern das Profil und die Rechte einrichten und die gewünschten Funktionen prüfen. Konfiguration wird beim Laden/Neuladen übernommen; externe Änderungen während des Spiels gelten nicht sofort.

Surf und Zombie bekommen keine Bank, Sounds oder AntiCamp. Maps und Teams können je Serverprofil unterschiedliche Funktionen aktivieren. BotFillSlots=4 in Teams gilt unabhängig vom Teamwechselschutz; nur Menschen in T/CT reduzieren die Spielbots. Zuschauer und SourceTV zählen nicht mit. SourceTV darf nicht durch allgemeine Kickbefehle entfernt werden.

## Demos und Webanbindung

SourceTV muss vor dem Mapstart eingerichtet sein. RAM-Aufnahmen benötigen ein begrenztes tmpfs unter `game/csgo/demos/LPLK_RAM`, passend in den Container eingebunden und für den Spielserverbenutzer schreibbar. Der laufende LPLK-Betrieb nutzt 2 GiB und 256 MiB Reserve. Dauerhafte Dateien liegen unter `game/csgo/demos/LPLK_Demos`. Ein Plugin-ZIP richtet weder Host-Mounts noch Swap-/Containerlimits ein. Aufbewahrung und automatische Aufnahme passend konfigurieren; die veröffentlichte Konfiguration ist kein Export der produktiven Einstellungen.

Mapaufnahmen enthalten Rundenmarken, Spieler und Killzeiten. Separate Rundendateien aus derselben Mapaufnahme sind noch offen. Für die Wiedergabe die heruntergeladene DEM in das lokale CS2-Verzeichnis `game/csgo` legen, danach `cl_demo_predict 0` und `playdemo <exakter Dateiname.dem>` in der CS2-Konsole. Die Webverwaltung ist eine separate Anwendung; die Bridge benötigt deren passende Einrichtung und eigene Zugangsdaten. Nichts davon ist im Download voreingetragen.

## Aus dem vollständigen Quellpaket bauen

Das Release-Asset `LPLK-Plugins-Source.zip` enthält alle aktuellen Pluginquellen, gemeinsam kompilierten Hilfen, csproj-/Lockdateien, Buildgrundlagen und die geprüfte native SQLite-Bibliothek. GitHubs automatisch erzeugte Repository-ZIPs enthalten die native Binärabhängigkeit nicht; dafür das vollständige Quellpaket verwenden. Benötigt wird das in global.json angegebene .NET SDK.

Im entpackten Quellverzeichnis, beispielsweise mit PowerShell:

```powershell
$env:LPLK_BUILD_ROOT = Join-Path $env:LOCALAPPDATA 'LPLK-PluginBuild'
dotnet restore Addons/LPLK_Teams/Rohskripte/LPLK_Teams.csproj --locked-mode
dotnet publish Addons/LPLK_Teams/Rohskripte/LPLK_Teams.csproj -c Release --no-restore
```

Den Namen für das gewünschte Plugin ersetzen. Nicht mehrere Projekte mit identischem Buildziel gleichzeitig ausführen. Economy und Surf verwenden `Addons/LPLK_Economy/Rohskripte/native/libe_sqlite3.so` für Linux x64. Herkunft, Compiler-/Quellarchiv-Prüfsummen und Ziel-ABI stehen in dessen `manifest.json`; die Bibliothek liegt im vollständigen Quellpaket. Für einen erneuten nativen Build beschreibt `docs/SQLITE-BUILD.md` die reproduzierbaren Eingaben und Flags.

`release-manifest.json` listet die Pluginversionen sowie Paket- und DLL-SHA-256 auf. `SOURCE-MANIFEST.json` im Quellpaket enthält die SHA-256 aller enthaltenen Quelldateien. Je Komponente die eigene Lizenz beachten; dieses Repository vergibt keine gemeinsame Ersatzlizenz für Fremdkomponenten.
