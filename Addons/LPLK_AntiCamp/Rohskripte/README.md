# LPLK_AntiCamp 0.1.4

Aktuelle Erweiterung19.10.2026: kompakte Adminseiten Einstellungen/Status, direkte Werteauswahl mit Erhaltung eigener Zwischenwerte, validiertes Nachladen ohne Überschreiben ungültiger Dateien. Kein Zugriff auf native Spielregeln vor beobachtetem Rundenstart. Campingzeit, Warnung, Strafen und Ausnahmen bleiben fachlich unverändert. Ausschließlich für den isolierten Testserver; Spielabnahme später. Aktuelle Installationsnachweise stehen im Projekt unter state/protection-maintenance-verification.json, sobald abgeschlossen.

Die folgenden Angaben dokumentieren den bisherigen Stand und seine Referenzmechanik.

# LPLK_AntiCamp 0.1.2

Neufassung der Camping-Erkennung für das Standard-Testprofil The inner Circle. Seit 11.10.2026, 19:13 Uhr ausschließlich auf Testserver 89450e53 / 195.201.170.223:27020 geladen. Aktuelles Paket: `../Build/LPLK_AntiCamp.zip`. Build: `scripts/Build-Plugin.ps1 -Project Addons/LPLK_AntiCamp/Rohskripte/LPLK_AntiCamp.csproj` aus dem Hauptprojekt.

## Übernommene Regeln

Die tatsächlich gelesene Referenzkonfiguration steht in `state/reference-gameplay-rules.json`. 30 Sekunden Camping, Radius 50 um einen festen Ausgangspunkt, erster Eingriff nach weiteren zwei Sekunden, danach alle zwei Sekunden, fünf Schaden und mindestens 15 HP. Team-/Spielziel-Ausnahmen entsprechen der Referenz: T darf Geiseln beziehungsweise die gelegte Bombe bewachen; CT darf Bombenplätze beziehungsweise die fallengelassene Bombe bewachen. Tatsächliche Map-Entitäten bestimmen den Spielzieltyp. Konfigurierbar sind Hinweis, Slap und Slay sowie alle bisherigen Regelwerte.

Zeitmessung verwendet reale monotone Sekunden. Reines AFK startet den Camping-Timer nicht. Der Vorlauf war in der gelesenen Serverdatei nicht enthalten; fünf Sekunden stammen aus der historischen 2.1.0-Quelle und dem Benutzerchat. Die Warnung erfolgt fünf Sekunden vor dem ersten Eingriff: bei Standardwerten nach 27 Sekunden, Bestrafung frühestens nach 32. Ein verspäteter Timer erhält den gesamten Warnvorlauf. Kein Nachholen mehrerer Bestrafungen in einem Tick. Die HP-Reserve wird beim Slap nicht unterschritten.

Menü: `!admin` → AntiCamp, Recht `lplk.anticamp.admin`. Konfiguration und Status liegen in `data/LPLK_AntiCamp/`. Ungültige Konfigurationsänderungen ersetzen nicht den letzten gültigen Stand. Verbindungs-/Pawn-/Teamwechsel setzen die Erkennung zurück. Warmup, Freezezeit, Rundenende und fehlende Gegnerteams sind ausgenommen.

## Nachweise und offene Abnahme

Release-Build gegen SwiftlyS2 1.4.14 erfolgreich. `tests/AntiCampChecks` prüft 42 fachliche Fälle, darunter zwei Serien mit je 1000 Aufrufen für AFK und wiederholte Timeraufrufe ohne Zeitfortschritt. Hinzu kommen Menügrenzen bei extern gesetzten Zwischenwerten, abgeschaltete Warnung, neue Spielerzustände und volle Warnzeit bei sehr kurzer Campzeit. Historie: Remote-Chat „Als SwiftlyS2 Plugin umsetzen“ (01a05e68-a70a-73f2-90d0-d36da135d6b8), Warnung/Center/Alert/Chat, echte Zeit und fester Radius. Serverreferenz meldet nominell SLAYER_AntiCamp 2.0.0; NAS-Quelle 2.1.0 ist kein Beleg für gleiche Produktionsbinärdateien. Deren Markierung besteht bereits aus Center-Hinweis und Ton; es wurde kein vorhandener dreidimensionaler Beacon entfernt.

Installation mit ZIP- und Datei-Prüfsummen bestätigt. Alle neun Komponenten geladen, bestehende acht Versionen unverändert, weiterhin 88/88 Sounds, sieben Whitelisteinträge und 90 freigegebene Mapvorschläge im Messbetrieb. `sw_admin` listet LPLK_AntiCamp über die gemeinsame Schnittstelle auf; `sw_lplk_anticamp` gibt die aktiven Regeln über den realen Webpanel-/Bridge-Weg aus. Nachweise: `state/test-anticamp-{before,deployment,runtime,web-check}.json`. Keine Produktionsinstallation.

Offene Ingame-Abnahme: Menü W/S/E/Tab; in einer echten Runde mit beiden Teams als T auf de_dust2 kurz umsehen, dann im Radius bleiben (Warnung nach etwa 27 Sekunden, erster Slap nach 32); Bewegung über den Radius setzt zurück, reines AFK startet keinen Timer. Danach Bombenlegen, CT-Ausnahme, Tod/Respawn und erneute Runde prüfen. Warmup und Freezezeit sind absichtlich ausgenommen. Automatisierte Regelprüfungen ersetzen diese Client-/Spieltests nicht.

Menüstand 0.1.2: nummerierte Aktionen; Überschriften und Zurück weiß, normale Aktionen gelb, aktive Schalter grün, inaktive rot. Die Gameplayregeln bleiben identisch zu 0.1.1.
