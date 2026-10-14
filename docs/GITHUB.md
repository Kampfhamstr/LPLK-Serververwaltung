# Zentraler GitHub-Zugang

Stand: 14.10.2026. Der Benutzer hat die zunächst repositorygebundene Einrichtung ausdrücklich erweitert: **Ein allgemeiner Token soll künftig für die Verwaltung aller seiner Repositories verwendet werden.** Der Zugang ist deshalb an das GitHub-Konto gebunden und benötigt keine Repository-Adresse. Private, öffentliche, eigene und über Mitarbeit/Organisationen erreichbare Repositories können gemeinsam geladen werden.

Die gemeinsame private Ablage für Addons, Verwaltungsprogramm und Website begrenzt den allgemeinen Zugang nicht. Der echte gespeicherte Zugang wurde am 14.10.2026 erfolgreich geprüft; die erreichbaren Repositories wurden gelesen. Der Benutzer hat `Kampfhamstr/LPLK-Serververwaltung` als neues privates Importziel freigegeben. Vorhandene andere Repositories werden dabei nicht verändert. Der konkrete Abschluss wird im lokalen Importnachweis festgehalten.

Die [Anleitungsübersicht](README.md), [21 Addon-Anleitungen](ADDONS.md) sowie Installation und Updates für Addons, Windows-Programm und Website begleiten den Import. Interne Entwicklungsverläufe und Serverbestände werden nicht pauschal als Anwenderdokumentation exportiert.

## Einrichtung

1. Das lokale Serververwaltungsprogramm öffnen: **Verbindung & Versionen → GitHub**. Direkter Einstieg: `scripts/Open-Portal.ps1 -GitHub`.
2. Den allgemeinen Personal Access Token direkt im maskierten Feld eingeben. Unterstützt werden **Classic** (`ghp_…`) und **Fine-grained** (`github_pat_…`). Ein normales GitHub-Passwort ist nicht geeignet.
3. **Prüfen und speichern** liest das zugehörige Konto und die erreichbaren Repositories, bindet die gespeicherte Verbindung an die stabile GitHub-Konto-ID und speichert sie verschlüsselt. Nach erfolgreichem Speichern bleibt das Tokenfeld leer.
4. **Repositories laden** aktualisiert die Übersicht mit dem gespeicherten Token. Nach Besitzer oder Name filtern; Doppelklick oder Enter öffnet ein Repository im Browser. **Abbrechen** beendet eine laufende Abfrage.

### Berechtigungen

Für einen klassischen Token ermöglicht `repo` den allgemeinen Zugriff auf private und öffentliche Repositories, soweit das Konto entsprechende Rechte besitzt. `workflow` wird zusätzlich für Änderungen an GitHub-Actions-Workflowdateien benötigt. `read:org` ist relevant, wenn Organisations- und Teammitgliedschaften ausgelesen werden sollen. Nicht pauschal sämtliche Kontoverwaltungsrechte aktivieren; weitere Rechte richten sich nach der tatsächlichen Aufgabe. Organisationen können Classic-Tokens beschränken oder eine zusätzliche SSO-Freigabe verlangen.

Ein Fine-grained Token kann unter **All repositories** für sämtliche Repositories seines gewählten Resource owners eingerichtet werden. Dieser Tokentyp bleibt an einen Besitzer gebunden und ist kein universeller Zugang über mehrere Organisationen. Die Repository-Rechte passend zu den gewünschten Aufgaben vergeben, beispielsweise Contents, Pull requests, Issues, Actions oder Workflows. Das Programm verändert diese Rechte nicht.

Die Übersicht zeigt die von GitHub gemeldeten **Kontoberechtigungen** und bei Classic-Tokens bekannte gemeldete Scopes. Diese Angaben sind kein erfolgreicher Schreibtest. Tokenrechte, Organisationsrichtlinien und Branchschutz können einzelne Aktionen weiter einschränken. Bei Fine-grained Tokens ist die vollständige Berechtigungsliste nicht über den Verbindungstest auslesbar. „Liste geladen“ bedeutet die für diesen Token erreichbare Liste, keine Garantie, dass jede Repositoryfreigabe des Kontos im Token enthalten ist.

## Speicherung und Diagnose

Token, Konto-ID und Kontoname werden gemeinsam per Windows-DPAPI für das aktuelle Windows-Konto unter `private/github-connection.json` verschlüsselt. Die Verbindung bleibt von Panel und SSH getrennt. Schema 2 liest frühere Schema-1-Zugänge weiter; Lesen verändert die gespeicherte Datei nicht. Erst erneutes erfolgreiches Speichern übernimmt die neue Kontozuordnung. Ein anderer Windows-Benutzer oder Computer kann die DPAPI-Datei nicht einfach wiederverwenden.

Den Token niemals in Chat, Shellargumente, Logs oder Git-Dateien einfügen. Auch der verschlüsselte Speicher bleibt außerhalb der Repositories. Zugangsdaten ausschließlich über die vorgesehenen Portal-Funktionen verwenden.

```powershell
. .\scripts\Use-Environment.ps1
dotnet (Join-Path $env:LPLK_PORTAL_DIR 'CS2ServerPortal.dll') --workspace $PWD.Path --github-status
dotnet (Join-Path $env:LPLK_PORTAL_DIR 'CS2ServerPortal.dll') --workspace $PWD.Path --github-repositories
```

`--github-test` entspricht der Konto- und Repositoryprüfung. Die CLI gibt nur ausgewählte Metadaten aus, niemals den Token. HTTPS verwendet reguläre Zertifikatsprüfung; automatische Weiterleitungen und Cookies sind abgeschaltet. Fehlerantworten werden nicht ausgegeben. Der Client lädt Ergebnisseiten zu je 100 Repositories über selbst konstruierte GitHub-Adressen. Die Grenze von 100 Seiten wird ausdrücklich als unvollständige Liste ausgewiesen. Die gesamte Abfrage ist auf 90 Sekunden begrenzt und in der Oberfläche abbrechbar.

## Weitere Repositoryarbeit

Der allgemeine Zugang steht für künftige vom Benutzer gewünschte Repositoryarbeit zur Verfügung. Das Eintragen löst keine unbeaufsichtigte Wartung, keinen Upload und keine Änderungen an allen Repositories aus. Die aktuelle Oberfläche richtet den Zugang ein und zeigt die erreichbaren Projekte; Bearbeitungs- und Uploadaktionen erfolgen auftragsbezogen.

Vor dem ersten LPLK-Import eine explizite Quellcodeauswahl zusammenstellen, tatsächliche Zielinhalte lesen und erhalten. Vorgesehene Gliederung: Addons, Verwaltungsprogramm, Website und Dokumentation. Abhängigkeiten und Drittanbieter-Lizenzen berücksichtigen.

Der Portal-CLI-Einstieg `--github-import <Manifest> --manifest-sha256 <SHA256>` führt ausschließlich einen ausdrücklich vorbereiteten ersten Import aus. Er liest den gespeicherten Zugang intern, kontrolliert Konto und privaten Zielstatus, überprüft jeden Dateihash und verweigert vorhandene Ziele ohne passenden eigenen Anlegenachweis. Der Branch wird ohne Force-Push gesetzt; zwischenzeitliche Änderungen führen zum Abbruch. Anschließend werden alle hochgeladenen Git-Blob-Hashes und die Sichtbarkeit erneut gelesen. Diese Funktion ist kein automatischer Abgleich beliebiger Verzeichnisse und kein allgemeines Überschreiben vorhandener Repositories. Manifest, Anlegenachweis und Prüfergebnis bleiben lokale Betriebsdaten.

Kein pauschales `git add .` über den NAS-Arbeitsbereich: `private/`, Laufzeitdaten aus `state/`, Spieler- und Bankdatenbanken, produktive Konfigurationen, Server-/Website-Sicherungen, Logs, alte Pakete und SDK-/Build-Caches ausschließen. Benötigte Konfigurationsvorlagen erhalten Platzhalter. Die endgültige Auswahl zusätzlich auf eingebettete Geheimnisse und Daten prüfen; `.gitignore` allein reicht dafür nicht.

Offizielle Dokumentation:
- https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens
- https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/scopes-for-oauth-apps
- https://docs.github.com/en/rest/repos/repos#list-repositories-for-the-authenticated-user
