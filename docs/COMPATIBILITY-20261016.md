# Bridge 1.6.5 und LPLK_Maps 0.2.7

Stand: 16.10.2026. Diese Ausgabe behebt die Verwendung der in SwiftlyS2 1.5.0-beta.10 entfernten `ExecuteCommandWithBuffer`-API. Beide Addons werden weiterhin gegen die festgeschriebene API 1.4.14 gebaut; jeweils dieselbe DLL wurde auch mit beta.10 geprüft.

| Komponente | Änderung | Lokale Prüfung je API | Native Prüfung auf dem Testserver, jeweils unter beiden APIs |
|---|---|---|---|
| SerververwaltungBridge 1.6.5 | Sieben Befehls-, ConVar- und Mapwechselstellen verwenden `ExecuteCommand`; Lebenszyklus-/Zielschutz und zweistufige Ausführung bleiben erhalten | 391 Prüfungen, alle 207 Framework-Verweise aufgelöst | Tatsächliche Konsolenausgabe, ConVar-Fallback, Workshop de_drawbridge und Rückkehr de_dust2 |
| LPLK_Maps 0.2.7 | Zwei Aufrufstellen in `IssuePending` verwenden `ExecuteCommand`; Mapbestätigung und begrenzte Wiederholungen bleiben erhalten | 310 Prüfungen, alle 157 Framework-Verweise aufgelöst, zusätzlich 93 Map-/Addon-Regeltests | Eigene Befehle `sw_lplk_map 3070192462` und `sw_lplk_map de_dust2`; beide tatsächlichen Mapwechsel bestätigt |

Der Testserver bleibt auf beta.10 mit beiden neuen Addons. 20 Plugins geladen, 0 API-Befunde und 0 Ladefehler beim Abschluss der Kompatibilitätsprüfung. Andere Plugin-DLLs, Konfigurationen und bestehende Mapfreigaben blieben unverändert. `MeasurementOnly=true` bleibt dort aktiv. Ein menschlicher Mehrspieler-/RTV-Menütest wurde nicht durchgeführt. Diese Prüfung ist keine allgemeine beta.10-Freigabe für sämtliche übrigen Addons.

## Produktionsmigration von LPLK_Maps

Die sechs untersuchten Produktionsserver verwenden bisher HeadshotOnly, MapChooser oder SwiftSurf für die Mapwahl. LPLK_Maps wäre dort eine Erstinstallation. Version 0.2.7 ersetzt deren serverabhängige Regeln nicht vollständig: insbesondere Mapverlängerung, Surf-Kategorien und zeitgesteuerte Surf-Abstimmungen benötigen eine gesonderte Migration. Vor der Ablösung muss entschieden werden, welche Funktionen erhalten bleiben. Bestehende Kartenkataloge, Spielergrenzen und manuelle Ausschlüsse dürfen nicht ungeprüft ersetzt werden. Testserverfreigaben für vermessene Karten gelten nicht automatisch produktiv.

Das Paket 0.2.7 ist deshalb verfügbar und auf beiden APIs geprüft; die Produktionsablösung der bisherigen Mapverwaltung ist noch offen. Parallel installierte konkurrierende RTV-/Nominate-Handler sind kein geprüfter Übergangsbetrieb.

## Quellen, Pakete und Wiederholung

Aktuelle Quellen: `Addons/SerververwaltungBridge/Rohskripte/` und `Addons/LPLK_Maps/Rohskripte/`. Zu jedem Addon gehören eine Anleitung, ein vollständiges ZIP und dessen SHA-256 in [DOWNLOADS.md](DOWNLOADS.md). Das Release enthält ausschließlich diese beiden aktualisierten Pakete; die übrigen Downloadzeilen behalten ihren vorherigen Prüfstand. Die Veröffentlichung bleibt als Prerelease gekennzeichnet.

Nach Einbindung von `scripts/Use-Environment.ps1`:

```powershell
dotnet run --project tests/BridgeCompatibilityChecks/BridgeCompatibilityChecks.csproj -c Release
dotnet run --project tests/BridgeCompatibilityChecks/Beta/BridgeBetaChecks.csproj -c Release
dotnet run --project tests/MapsCompatibilityChecks/MapsCompatibilityChecks.csproj -c Release
dotnet run --project tests/MapsCompatibilityChecks/Beta/MapsBetaChecks.csproj -c Release
```

Vor einem Serverupdate bisherige Pluginversion und Daten sichern, vorhandene Konfigurationen erhalten und nach dem regulären Neustart Ladestatus, Versionsnummer, frische Live-Daten und tatsächliche Befehlsausführung prüfen. Den alten Maps-Stand 0.2.6 nur zusammen mit SwiftlyS2 1.4.14 verwenden. Die geprüften Rückfallstände bleiben bis zum Abschluss der jeweiligen Übernahme erhalten.
