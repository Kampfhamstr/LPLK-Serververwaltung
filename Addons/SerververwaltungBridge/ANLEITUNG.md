# SerververwaltungBridge – Verbindung zur Webverwaltung

Quellstand **1.6.4**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

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
