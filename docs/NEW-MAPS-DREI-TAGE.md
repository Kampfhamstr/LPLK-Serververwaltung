# Neue Maps: drei Tage Vorrang, 14 Tage NEW

LPLK_Maps 0.4.33 verwendet zwei Fristen ab derselben ersten Freischaltung:

| Alter der Freischaltung | Anzeige und Auswahl |
| --- | --- |
| Unter 3 Tagen | Fettes hellgrünes NEW; in Nominate und Mapvote zuerst angezeigt, freie Mapvoteplätze bevorzugt |
| Ab 3 Tagen bis unter 14 Tagen | Normales hellgrünes NEW; gewöhnliche Sortierung, Kategorien und Kandidatenauswahl |
| Ab 14 Tagen | Keine NEW-Markierung und keine Bevorzugung |

Die 14 Tage sind die Gesamtdauer ab Freischaltung. Es werden keine weiteren 14 Tage
an die ersten drei Tage angehängt. Beide Grenzen gelten exakt nach 72 bzw. 336 Stunden.
Bekannte Freischaltzeiten bleiben erhalten; Pluginreload, Neuinstallation und
Entfernen/Wiedereinfügen starten die Fristen nicht neu. Spielergrenzen,
Wiederholungssperren und angenommene Nominierungen gelten weiterhin.

Für die am 26.11.2026 freigeschalteten Greece, Inferno Winter, Hankagai und Karachi
endet der Vorrang am 29.11.2026 (Inner Circle 19:25 Uhr, Atlas 19:33 Uhr, Berlin).
NEW bleibt bis zum 10.12.2026 zur jeweiligen Uhrzeit. Der Lunacy-Abstimmungsablauf
aus 0.4.31 bleibt bestehen.

Zusätzlich sind 35 doppelt falsch codierte Zeichen in 25 Zeilen der Maptexte
korrigiert: unter anderem „Nächste Map“, „Zurück“, „Menü“, „verfügbar“ und der
Trennpunkt „·“. Umlaute bleiben echte Unicode-Zeichen. Die Prüfung liest auch
die Zeichenketten der fertig gebauten DLL und erzeugt Mapvote-/Nominate-Menüs.

Prüfungen: 250 Map-/Menütests einschließlich der exakten Zeitgrenzen und Menütitel,
72 Aktivierungs- und Listenprüfungen sowie die vollständigen 442 Kompatibilitäts-
und Textprüfungen (einschließlich 386 statischer API-/Textprüfungen mit
231 Framework-Verweisen). Native Rolloutbelege stehen in `state/promotion-*-final-check.json`; sie prüfen Version,
geladene Plugins, aktuelle Kennzeichnungen und Reihenfolge, erhaltene Mapregeln,
unveränderte Aktivierungszeiten und Serverprozesse sowie IO-Status.
Die menschliche CS2-Client-Sichtabnahme bleibt offen. MapsCompatibilityChecks
besteht mit 0.4.33 vollständig; die frühere MapsMaintenanceChecks-Problematik
im Engine-Mock wurde in dieser Änderung nicht erneut geprüft.
