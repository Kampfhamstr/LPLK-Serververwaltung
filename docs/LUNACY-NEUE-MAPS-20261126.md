# Lunacy und neue Maps – 26.11.2026

Ab 0.4.33 ist die besondere Hervorhebung und Bevorzugung neuer Maps auf drei Tage
begrenzt; NEW bleibt insgesamt 14 Tage. Die aktuelle Regel steht unter
[Drei Tage Vorrang, 14 Tage NEW](NEW-MAPS-DREI-TAGE.md). Die folgenden Angaben
beschreiben den ursprünglichen Stand 0.4.31 und den unveränderten Lunacy-Ablauf.

LPLK_Maps 0.4.31 fragt nach einem gewonnenen Lunacy-Mapvote:
**„Lunacy mit Low Gravity spielen?“ – Ja / Nein.** Die Nachfrage läuft 30 Sekunden;
`!mapchooser` öffnet sie erneut. Gezählt werden die abgegebenen Stimmen der beim
Start stimmberechtigten und beim Abschluss weiterhin stimmberechtigten Menschen.
Bots stimmen nicht mit. Jede Person hat eine Stimme. Ein Gleichstand oder keine
Stimme bedeutet normale Schwerkraft.

Die aktuelle Map behält ihre Schwerkraft. Bei einer Ja-Mehrheit wird erst nach dem
anschließenden Start von Lunacy die Schwerkraft auf 400 gesetzt. Der Mapwechsel
wartet auf die Nachfrage; ein zwischenzeitliches Matchende überspringt sie nicht.
Ein direkt gewählter Mapwechsel oder ein zufälliger Rotationswechsel eröffnet
keine neue Umfrage. Nach dem Verlassen wird der vorherige Wert wiederhergestellt,
sofern kein Administrator ihn zwischenzeitlich geändert hat. Eine Plugin-Neuladung
auf derselben Lunacy-Map erhält den angenommenen Beschluss.

Neue Karten werden 14 Tage ab ihrer ersten tatsächlichen Wählbarkeit mit
**hellgrünem, fettem NEW** gekennzeichnet. In Nominate erscheinen passende neue
Karten zuerst auf Seite 1. Im Mapvote bleiben angenommene Nominierungen erhalten;
freie Kandidatenplätze werden zuerst mit passenden NEW-Karten gefüllt. Die
angezeigte Abstimmung stellt NEW-Karten vor die übrigen Karten. Die vom Benutzer
gewünschte „Popularität“ meint diese auffällige Hervorhebung, keine Nutzungsrangliste.
Spielergrenzen, aktuelle Map, Wiederholungssperren und manuelle Sperren gelten weiter.

Am 26.11.2026 ausdrücklich für Atlas und Inner Circle freigegeben:

| Map | Workshop-ID | Menschliche Spieler | Besonderheit |
| --- | --- | --- | --- |
| Greece | 3841655039 | 6–8 | Bot-Verhalten ungeklärt, vom Benutzer trotzdem freigegeben |
| Inferno Winter | 3840612221 | 6–30 | Vier Spielbots im vorherigen Test erschienen |
| Hankagai | 3840105395 | 6–62 | Vier Spielbots im vorherigen Test erschienen |
| Karachi | 3825652304 | 6–54 | Vier Spielbots im vorherigen Test erschienen |

Die Grenzen erhöhen keine Serverslotzahl. Bei weniger als sechs Menschen werden
diese vier Karten von der normalen Nominierung ausgeschlossen, auch während NEW.

Umgesetzt und nativ bestätigt mit LPLK_Maps 0.4.31: Inner Circle am 26.11.2026
um 19:25 Uhr, Atlas um 19:33 Uhr (Berlin). Die vier NEW-Markierungen laufen jeweils
bis zum 10.12.2026 zur gleichen Uhrzeit. Auf Atlas waren bei der Abschlussprüfung
neun Menschen verbunden: die drei größeren neuen Karten standen in Nominate vorn,
Greece war wegen seiner Obergrenze korrekt nicht nominierbar. Alle 18 Plugins auf
Atlas und alle 22 auf Inner Circle waren geladen. Beide Serverprozesse blieben
unverändert; es gab keinen erzwungenen Produktionsmapwechsel.

Mit der identischen finalen DLL auf dem leeren Testserver wurde eine angenommene
Nachfrage über die begrenzte Testprobe eingespeist: echter Lunacy-Start mit 400,
Pluginreload auf Lunacy weiterhin 400, echter Wechsel zurück zu Dust II wieder
ursprüngliche 800. Alle 22 Plugins geladen; Mapregeln und Prozessstart erhalten,
keine gemeldeten IO-Fehler und keine Spielthread-Disklesevorgänge.

Prüfung: 224 Map-/Menü-/Abstimmungsprüfungen sowie 380 statische API-Prüfungen
(231 Framework-Verweise) bestanden. Native Testbelege und Aktivierungszeiten liegen
in `state/lunacy-*.json` und `state/newmaps26-*.json`. Die echte Ansicht und Bedienung
mit einem menschlichen CS2-Client stehen noch aus. Die vollständigen älteren
MapsCompatibilityChecks und MapsMaintenanceChecks scheiterten in ihren simulierten
Engine-Zugriffen; sie werden nicht als bestanden ausgewiesen. Ein ergänzender
DLL-Baselinevergleich wurde vor Ausführung durch die automatische Freigabeprüfung
mit „blocked by policy“ blockiert und nicht wiederholt.

Benötigte Serverrückfälle und der ausdrücklich aufbewahrte lokale Maps-Rückfall
bleiben erhalten. Produktionsarbeiten erfolgen ohne Prozessneustart und ohne
erzwungenen Mapwechsel, erst bei freier Abstimmung und sicherem Rundenzeitpunkt.

API-Referenzen: [SwiftlyS2-Core-Ereignisse](https://swiftlys2.net/docs/development/core-events),
[ConVars](https://swiftlys2.net/docs/development/convars). Die verwendete SDK-Abhängigkeit
bleibt für reproduzierbare Builds auf 1.4.14 gepinnt; native Prüfungen sind separat belegt.
