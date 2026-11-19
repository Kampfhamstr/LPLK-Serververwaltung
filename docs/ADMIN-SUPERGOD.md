# Supergodmodus und Admin-Hinweise in LPLK_Admin 0.11.13

Unter `!admin → Spielerverwaltung → Supergodmodus` gibt es einen Schalter mit AN und AUS. Mit `!sgm` oder `!supergod` lässt sich der Modus direkt umschalten. Das geht als lebender Spieler im T- oder CT-Team mit `admin.cheats`, `lplk.admin` oder `admin.root`. Der Modus gilt für den Admin, der ihn einschaltet.

- Der Schadensschutz macht den Admin unverwundbar. Die Bewegung nutzt den Faktor 3. Magazine von Schusswaffen füllen sich nach einem Schuss wieder auf.
- **Waffen holen** und `!superguns` öffnen die kostenlose Waffenauswahl. Sie enthält Waffen beider Teams sowie Granaten und den Zeus. Kaufzone, Kaufzeit und Kontostand spielen für diese Auswahl keine Rolle. Das Menü ist eine eigene Auswahl neben dem CS2-Kaufmenü auf **B**. Das Spiel kann bei einzelnen Gegenständen weiterhin technische Inventargrenzen durchsetzen.
- Der Modus endet beim Ausschalten, Rundenende, Tod, Respawn, Team- oder Mapwechsel, Verbindungsabbruch, Rechteverlust und Entladen des Plugins.

Andere verbundene und berechtigte Admins sehen bei erfolgreicher Aktion einen Hinweis im Spielchat. Kennzeichnung und Name des handelnden Admins sind rot, die Aktionsbeschreibung ist weiß. Die Hinweise erfassen das manuelle Umschalten des Supergodmodus, bestätigte Waffenausgaben, zentrale Spieler- und Serveraktionen, gespeicherte Admin-Einstellungen und den älteren direkten LPLK_Admin-Mapwechsel. Freitext, Sperrgründe und rohe Befehls- oder Einstellungswerte bleiben privat.

Die Regel gilt auch für künftige Adminfunktionen. Bestehende eigenständige Fachmodule und die Webverwaltung senden ihre Aktionen erst dann auf diesem Weg, wenn sie an die Hinweise angeschlossen sind.

Die Serverprüfung bestätigt das Laden der neuen Version auf dem dedizierten Testserver nach Pluginreload im selben Prozess. Die Vorabversion enthält das installierbare Plugin und den vollständigen zugehörigen Quellstand. Die Bedienung und das Verhalten von Geschwindigkeit, Schaden, Munition und Waffenausgabe im echten CS2-Client bleiben Gegenstand der Spielabnahme.

Die gezielten Menütests, der API-Abgleich und die Befehlsprüfung sind bestanden. In den ergänzenden Tests zu Aliasen, Einstellungswechsel und dem älteren Menüumbau traten dieselben Fehler bereits mit Version 0.11.11 auf.

Für die Installation den Ordner `LPLK_Admin` aus dem Plugin-ZIP nach `game/csgo/addons/swiftlys2/plugins/` übernehmen und das Plugin geordnet neu laden. Konfigurationen sowie Betriebsdaten liegen getrennt vom Pluginpaket.
