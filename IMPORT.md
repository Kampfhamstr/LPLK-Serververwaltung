# Erster Quellenimport

Stand: 14.10.2026. Ziel: privates gemeinsames Repository LPLK-Serververwaltung.

Enthalten sind die aktuellen 21 Addonprojekte, gemeinsam benötigte Quellen und Lizenzen,
das Windows-Portal, ausgewählte Build-/Startskripte, Prüfprojekte, Anwenderanleitungen
und die PHP-Website auf Basis 1.15.2.8. Der Websiteordner heißt `Website/`.

Nicht enthalten sind Zugangsspeicher, tatsächliche Konfigurationen und Verbindungsdaten,
Spieler-/Bankdaten, Datenbanken, Statusberichte, Serverbackups, Chatverläufe, alte Pakete,
SDKs, Caches oder erzeugte ausführbare Dateien. Das Economy-Nativmodul wird mit
`scripts/Build-EconomyNative.ps1` aus den dort festgelegten Quellen gebaut.

Für den Import wurden ausschließlich in dieser Quellkopie fest eingetragene
Server-UUIDs/Kurzkennungen durch konsistente Beispielkennungen ersetzt. Die tatsächliche
Serveradresse und die LPLK-Webziele wurden durch Dokumentations-/Beispieladressen ersetzt;
der Name des Referenz-Testservers lautet hier Beispielserver. Die lokalen aktiven Quellen
und produktiven Installationen bleiben unverändert. Die wenigen SteamID-Literale sind
synthetische Prüffälle oder Grenzen des gültigen ID-Bereichs, keine exportierte Spielerliste.

Die anonymisierten Diagnose- und Wartungspfade sind weiterhin an genau definierte
Testbedingungen gebunden. Vor Nutzung auf einer eigenen Umgebung müssen Ziel, Rechte und
Konfiguration bewusst eingerichtet werden. Der GitHub-Import ist keine Serverinstallation.

Die vorbereitete Website 1.15.2.9 ist nicht enthalten. Die Bank-/Statistik-Webanbindung
ist weiterhin ein begrenztes Testprofil. Gemeinsame Produktionsbank und öffentliche
Statistikzeitreihen sind noch kein allgemein freigegebener Bestandteil.

Die Paketstruktur und Dependency-Lockdateien bleiben erhalten. Jede Komponente behält
ihre Lizenz-/NOTICE-Dateien. Keine pauschale Neulizenzierung des Gesamtprojekts.
