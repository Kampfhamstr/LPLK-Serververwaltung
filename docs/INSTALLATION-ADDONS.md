# CS2-Addons installieren und aktualisieren

Diese Anleitung gilt für die Pakete unter `Addons/<Name>/Build/`. Welche Funktion ein Addon hat, welche Befehle es mitbringt und welche Sonderfälle gelten, steht in seiner Anleitung im [Addon-Verzeichnis](ADDONS.md). Stand: 14.10.2026.

## 1. Passendes Paket wählen

- Zielumgebung ist ein CS2-Server mit SwiftlyS2. Die aktuellen Projekte verwenden .NET 10 und die jeweils festgeschriebene SwiftlyS2-API. Die Versionen stehen im [Verzeichnis](ADDONS.md).
- Die Sammlung wird auf Linux/Debian unter Pterodactyl entwickelt und getestet. Insbesondere **LPLK_Economy enthält eine native Bibliothek für Linux x64**. Ein erfolgreicher Windows-Build ist keine Windows-Serverfreigabe.
- `LPLK_Admin` ergänzt das gemeinsame Spieler- und Adminmenü. Viele Module lassen sich auch über ihre direkten Befehle benutzen. `Addons/_Gemeinsam` ist gemeinsam kompilierter Quellcode, kein zusätzlich zu installierendes Plugin.
- AddonsManager wird für eingebundene Workshop-Inhalte benötigt, beispielsweise Soundpakete. Ein Eintrag in einem Sound- oder Mapkatalog lädt die Inhalte noch nicht automatisch auf jeden Client.

Ein vollständiges fertiges Paket heißt beispielsweise `LPLK_Parachute.zip`. **GitHubs „Download ZIP“ lädt dagegen den Quellcode des Repositorys.** Diesen muss man erst bauen. Der Quellcodeupload erzeugt keine fertigen GitHub-Releases; Paketdownloads nur verwenden, wenn sie tatsächlich als Release-Anhang angeboten werden.

## 2. SwiftlyS2 vorbereiten

Bei einer Erstinstallation die zum Serverbetriebssystem passende SwiftlyS2-Ausgabe **mit Runtimes** verwenden und deren `addons/`-Verzeichnis nach `game/csgo/` kopieren. Den Loader in `game/csgo/gameinfo.gi` im Suchpfad eintragen:

```text
Game csgo/addons/swiftlys2
```

Die Zeile gehört unter `Game_LowViolence csgo_lv`; bei vorhandenem Metamod hinter dessen Eintrag. Nach einem regulären Neustart prüft `sw` in der Serverkonsole, ob das Framework verfügbar ist. Die genaue Einrichtung beschreibt die [offizielle SwiftlyS2-Installation](https://swiftlys2.net/docs/installation). Für diese Sammlung ist die bisher verwendete stabile Linie 1.4.14 maßgeblich; eine neuere Beta nicht ungeprüft einsetzen.

## 3. Addon einsetzen

1. Zunächst einen getrennten Testserver bzw. eine getrennte Testkonfiguration verwenden. Bei einer bestehenden Installation den Server regulär stoppen und die bisherige Pluginversion sowie ihre Konfiguration und Daten sichern.
2. Das ZIP lokal öffnen. Es enthält einen Ordner mit dem Addonnamen. Diesen **vollständig**, einschließlich benötigter Bibliotheken, Ressourcen und Lizenzdateien, nach `game/csgo/addons/swiftlys2/plugins/` übertragen.
3. Die DLL muss danach beispielsweise unter `game/csgo/addons/swiftlys2/plugins/LPLK_Parachute/LPLK_Parachute.dll` liegen. Ein zusätzlich verschachtelter Ordner `LPLK_Parachute/LPLK_Parachute/` ist falsch.
4. Beim ersten Start erzeugt das jeweilige Addon seine vorgesehenen Standarddateien. In der Einzelanleitung steht, ob Einstellungen unter `data/` oder `configs/plugins/` liegen. Den Server für manuelle Dateiänderungen wieder stoppen; JSON/JSONC-Syntax erhalten und keine laufend geschriebenen Dateien überschreiben.
5. Funktionsschalter und Rechte gezielt einrichten. Danach starten, Konsole/Pluginstatus prüfen und die Funktion mit einem Spieler testen. „Loaded“ bestätigt das Laden, nicht die erfolgreiche Bedienung, Tonwiedergabe oder Spielregel.

Unter Pterodactyl beziehen sich diese Pfade auf das Dateisystem **des jeweiligen Gameservers**. Ein Upload in das Verzeichnis des Pterodactyl-Webpanels installiert kein CS2-Plugin.

## 4. Menüs, Rechte und bestehende Binds

Mit LPLK_Admin öffnet `!menu` das nach Funktionen gegliederte Spielermenü. Berechtigte Administratoren erreichen dort auch das Adminmenü; `!admin` und `!adminmenu` bleiben direkte Einstiege. Spielereinträge erscheinen nur, wenn das benötigte Addon geladen und die betreffende Funktion für diesen Spieler aktiv ist. Administratoren können Einstellungen eines geladenen, deaktivierten Moduls weiterhin erreichen, sofern ihre Rechte es erlauben.

Die Einzelanleitungen nennen die jeweiligen Berechtigungen. Rechte über die SwiftlyS2-Rechteverwaltung der tatsächlich eingesetzten Version vergeben; Namen, SteamID64 und Zielimmunität korrekt zuordnen. Website-Rollen sind eine zusätzliche Berechtigungsebene und werden durch eine native Pluginberechtigung nicht automatisch vergeben. Manche direkte Befehle besitzen eigene Framework-Rechteprüfungen, auch wenn ein zentraler Menüeintrag erreichbar ist.

Bestehende Chatbefehle und Aliasnamen bleiben vorgesehen. Die aufgeführten Chatbeispiele verwenden `!`; abweichende Framework-Präfixe hängen von der Serverkonfiguration ab. **Vorhandene Konsolenbinds nicht pauschal umschreiben:** je nach Registrierung gibt es `sw_…` und zusätzliche rohe Befehlsnamen. Die Economy-Kompatibilität ist besonders ausführlich geprüft; ein vollständiger realer Bind-Test aller übrigen Addons steht noch aus.

## 5. Updates ohne Datenverlust

Konfigurationen und Nutzerdaten nicht durch Dateien eines frischen Pakets ersetzen. Typische Orte sind:

| Bereich | Beispiel | Beim Update |
|---|---|---|
| Plugin | `plugins/LPLK_Economy/` | Programmbestand vollständig und passend ersetzen |
| Einstellungen und Daten | `data/LPLK_Economy/` | Sichern und erhalten; Änderungen gezielt übernehmen |
| Andere Konfigurationsablage | `configs/plugins/LPLK_Sounds/` | Bestehende Einstellungen erhalten |
| Sonderfall Whitelist | `plugins/LPLK_Whitelist/whitelist.txt` | **Liegt im Pluginordner: vor dessen Austausch sichern und erhalten** |
| Bridge | `data/serververwaltung_bridge/` | Schreibende Aufträge und Einstellungen nicht ungeprüft ersetzen |
| Demos | `game/csgo/demos/LPLK_Demos/` | Aufnahmen separat erhalten |

Banken und andere SQLite-Datenbanken nur konsistent sichern, etwa nach regulärem Serverstopp. Eine einzelne während des Betriebs kopierte `.sqlite3`-Datei kann zugehörige WAL-Daten verpassen. Offene Buchungen oder Auszahlungen nicht durch Löschen vermeintlicher temporärer Dateien „reparieren“.

Nach einem Update Version, Konsole, Einstellungen, Datenbestand und die betroffene Spielerfunktion prüfen. Ein Downgrade kann mit einem neueren Datenschema unvereinbar sein. Es ist keine sichere Rückkehr, nur die alte DLL über eine inzwischen weiterbenutzte Bank zu kopieren. Alte ersetzte Pakete erst entfernen, wenn der neue Stand geprüft und eine noch erforderliche Rückfallmöglichkeit gesichert ist.

## 6. Aus dem Quellcode bauen

Der vollständige Checkout wird benötigt, insbesondere `Addons/_Gemeinsam`, die Buildskripte und die Dateien im Repository-Hauptordner. Nicht nur einen einzelnen `Rohskripte`-Ordner herunterladen. Die [Einrichtung der Windows-Werkzeuge](INSTALLATION-PORTAL.md) beschreibt SDK und lokale Ausgabeorte.

In PowerShell 7 im Hauptordner:

```powershell
. .\scripts\Use-Environment.ps1
.\scripts\Build-Plugin.ps1 -Project .\Addons\LPLK_Parachute\Rohskripte\LPLK_Parachute.csproj
```

Der Build verwendet festgeschriebene NuGet-Abhängigkeiten und liefert `Addons/LPLK_Parachute/Build/LPLK_Parachute.zip` plus `build-manifest.json`. Die Paketprüfung kontrolliert Struktur und SHA-256. Den Projektnamen für andere Addons entsprechend ersetzen. Bei Economy zuvor dessen [native Build-Anleitung](../Addons/LPLK_Economy/ANLEITUNG.md) beachten.

## Häufige Probleme

| Beobachtung | Als Erstes prüfen |
|---|---|
| `sw` unbekannt | Framework, Runtime und Loader-Eintrag in `gameinfo.gi` |
| Plugin fehlt | ZIP-Struktur, vollständiger Paketinhalt, API-Kompatibilität und Framework-Ladekonfiguration |
| Modul geladen, Menüpunkt fehlt | Funktion aktiviert, passendes Spielprofil, Spielerrecht; bei Maps auch MeasurementOnly |
| Zugriff verweigert | Recht des direkten Befehls, Modulrecht, Zielimmunität und gegebenenfalls Website-Rechte |
| Sound gelistet, aber stumm | Workshop-Mount, Precache, Clientdownload, Hörfilter und Lautstärke |
| Daten scheinbar verschwunden | Richtiger Server, Plugin-ID, Datenpfad und Dateirechte; nicht mit leeren Vorlagen überschreiben |

Fehlermeldungen ohne Passwörter, Tokens oder private Spielerlisten weitergeben. Ein unbekannter oder veralteter Status ist keine bestätigte Null und kein Beweis für eine fehlende Installation.
