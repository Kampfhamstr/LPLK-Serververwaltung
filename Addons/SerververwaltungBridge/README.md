# SerververwaltungBridge – Verbindung zur Webverwaltung

**Aktuelles Paket 1.6.5:** [SerververwaltungBridge.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/compat-2026.10.16-bridge-maps/SerververwaltungBridge.zip) · [Prüfstand und Grenzen](../../docs/COMPATIBILITY-20261016.md).

Quellstand **1.6.5**, gebaut mit SwiftlyS2-API **1.4.14**, dieselbe DLL lokal gegen **1.4.14 und 1.5.0-beta.10** geprüft. [Alle Addons](../../docs/ADDONS.md).

## Kompatibilitätskorrektur 16.10.2026

Die in beta.10 entfernte `ExecuteCommandWithBuffer`-Methode wird nicht mehr verwendet. Alle sieben betroffenen Befehls-, ConVar- und Mapwechselstellen nutzen die gemeinsame `ExecuteCommand`-API. Die zweistufige Ausführung über zwei World-Updates und die bestehenden Lebenszyklus-, Ziel- und Wiederholungssperren bleiben erhalten. Konsolenausgaben werden weiterhin regulär erfasst; die zusätzliche Pufferkopie entfällt.

Je 391 lokale Prüfungen mit beiden API-Paketen, darunter die Auflösung aller 207 verwendeten Framework-Verweise, bestanden. Auf Testserver `Beispiel-Testserver` mit SwiftlyS2 1.4.14 wurden der tatsächliche Konsolenbefehl, der ConVar-Fallback, ein Workshopwechsel auf de_drawbridge und der Rückwechsel auf de_dust2 verifiziert. Alle 20 Plugins laden, andere DLLs und Konfigurationen blieben hashgleich. Die vorige Bridge ist im Testserver-Rückfallarchiv gesichert. Nachweis: [Kompatibilitätsnachweis](../../docs/COMPATIBILITY-20261016.md).

Zusätzlicher nativer Test unter **1.5.0-beta.10** am 16.10.2026 ebenfalls bestanden: tatsächliche Engine-Ausgabe, ConVar-Fallback, Workshop de_drawbridge und Rückkehr auf de_dust2; 20 Plugins Loaded, alle Plugin-DLLs und Konfigurationen unverändert. Nachweis [Kompatibilitätsnachweis](../../docs/COMPATIBILITY-20261016.md). Eine Mehrspielerabnahme bleibt offen.

Der separate API-Befund in LPLK_Maps wurde inzwischen mit **0.2.7** behoben und anhand eigener Mapwechsel unter beiden Frameworks geprüft. Der Testserver bleibt jetzt auf **1.5.0-beta.10**. Keine globale beta.10-Freigabe für alle Addons. Geprüftes Framework-Rollback `.lplk-rollback/swiftly-before-beta10.zip` erhalten. Die produktive Bridge-Übernahme ist vom Benutzer am 16.10.2026 freigegeben; sie erfolgt mit Konfigurationsvergleich, Rückfallkopie und tatsächlicher Befehlsprüfung je Server. [Gemeinsamer Prüfstand und Migrationsgrenzen](../../docs/COMPATIBILITY-20261016.md).

## Funktion

Die Bridge stellt Gameserverstatus und die bestehende Auftragsanbindung für die Serververwaltung bereit. Dazu gehören je nach eingerichtetem Funktionsumfang Server-/Spielerinformationen und administrative Aktionen. Sie vermittelt den vorhandenen Vertrag zwischen Website und Gameserver; sie ersetzt nicht die einzelnen Fachmodule.

## Installation und Pfade

`SerververwaltungBridge.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Die DLL liegt unter `game/csgo/addons/swiftlys2/plugins/SerververwaltungBridge/SerververwaltungBridge.dll`.

**Die Namen unterscheiden sich bewusst:** Paket-/Ordnername `SerververwaltungBridge`, interne Plugin-ID `lplk.serververwaltung.bridge`, Datenablage **`game/csgo/addons/swiftlys2/data/serververwaltung_bridge/`**. Groß-/Kleinschreibung unter Linux beachten. Bestehende Auftrags-, Status- und Konfigurationsstrukturen nicht ungeprüft umbenennen.

## Verbindung einrichten

In der [Website](../../docs/INSTALLATION-WEBSITE.md) den richtigen Server mit dem passenden Panel-/Dateiconnector und den vorgesehenen Pfaden verknüpfen. Zugangsdaten ausschließlich in den dafür vorgesehenen geschützten Verbindungsdialogen hinterlegen. Das Kopieren der DLL allein registriert keine Website und überträgt keine Zugangsdaten.

Zunächst nur Status lesen und Server-/Map-/Spielerdaten vergleichen. Erst danach eine konkrete berechtigte Testaktion prüfen. Ein eingereichter Auftrag ist nicht automatisch erledigt: bestätigtes Ergebnis und anschließend neu gelesenen Serverzustand kontrollieren. Bank, Statistik und Whitelist benötigen zusätzlich ihre eigenen Addons und Freigaben.

## Betrieb und Grenzen

Website-Rollen, native Spielrechte und Zielschutz bleiben getrennte Prüfungen. Ältere Verbraucher der Bridge dürfen bei Änderungen am Datenformat nicht stillschweigend brechen. Betriebsdaten, Spielerlisten, Aufträge und produktive Verbindungsinformationen nicht nach GitHub übertragen.
