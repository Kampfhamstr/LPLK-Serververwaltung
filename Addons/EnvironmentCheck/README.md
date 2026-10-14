# EnvironmentCheck – Leeres Buildbeispiel

**Download:** [EnvironmentCheck.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/EnvironmentCheck.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Diese Ausgabe ist als Teststand gekennzeichnet.

Quellstand **1.0.0**, SwiftlyS2-API **1.4.13**. [Alle Addons](../../docs/ADDONS.md).

## Was ist das?

EnvironmentCheck ist ein aus der SwiftlyS2-Vorlage erzeugtes **leeres Pluginprojekt**. Seine Lade- und Entladefunktionen enthalten keine Diagnoselogik. Trotz des Namens prüft es weder Servergesundheit noch Zugänge, Datenbanken oder die vollständige Laufzeitumgebung. Es dient als Beispiel für Kompilierung und Paketaufbau.

## Bauen und optional laden

Nach der [Werkzeugeinrichtung](../../docs/INSTALLATION-PORTAL.md) im Hauptordner:

```powershell
.\scripts\Build-Plugin.ps1 -Project .\Addons\EnvironmentCheck\Rohskripte\EnvironmentCheck.csproj
```

Das erzeugte `EnvironmentCheck.zip` kann nach der [gemeinsamen Installationsanleitung](../../docs/INSTALLATION-ADDONS.md) in einer getrennten Testumgebung eingesetzt werden. Ziel: `game/csgo/addons/swiftlys2/plugins/EnvironmentCheck/EnvironmentCheck.dll`.

## Status

Es gibt keine eigenen Spielerbefehle, Menüs oder fachlichen Einstellungen. Ein erfolgreicher Build ist belegt; eine Gameserver-/Clientabnahme dieses Beispiels ist nicht erfolgt. Für den normalen Betrieb der übrigen Addons wird EnvironmentCheck nicht benötigt.
