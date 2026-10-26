# LPLK_Maps 0.4.17

Mapauswahl, RTV/Nominate, Votes, Workshopverwaltung, sichere Start- und Leerlaufrotation.

[Fertiges Pluginpaket](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.26-current/LPLK_Maps.zip) · [Vollständige Quellen](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.26-current/LPLK-Plugins-Source.zip) · [Installation und Build](../../docs/INSTALLATION-ADDONS.md) · [Änderungen und Prüfstand](../../docs/RELEASE-20261026.md)

Aktuelle Ausgabe vom 26.10.2026. Einstellungen und Daten sind nicht im ZIP enthalten; vorhandene Daten bei Updates erhalten. Das Paket enthält die für dieses Plugin vorgesehenen Ressourcen, Abhängigkeiten und Lizenzhinweise. Laufende Addondaten werden im RAM verarbeitet; reguläre Persistenz und Exporte erfolgen verzögert am Rundenende. Initialisierung/Entladen und native Enginezugriffe bleiben gesondert.

Das Plugin entsprechend dem eigenen Serverprofil aktivieren. Die Anwesenheit in dieser Sammlung bedeutet nicht, dass es auf jedem Server installiert werden soll. Funktionen und Prüfgrenzen dieser Veröffentlichung stehen in den oben verlinkten Releasehinweisen. Lizenz-/Autorenhinweise unter `Rohskripte/` beziehungsweise im Pluginverzeichnis beachten.

IdleRotationMinutes=40 und RotateStartupMap=true aktivieren das Standardverhalten. Die normale +map-Startzeile beibehalten; kein +exec lplk_startmap.cfg verwenden. Der Kaltstartwechsel erfolgt erst nach geladener Basiskarte und entfällt bei menschlichem Beitritt; Hotreload startet keinen Kaltstartwechsel.
