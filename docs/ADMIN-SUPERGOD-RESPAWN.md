# Supergodmodus nach Respawn in LPLK_Admin 0.11.14

Berechtigte Admins können `!sgm` oder `!supergod` auch benutzen, wenn sie in T oder CT tot sind. Der Befehl fordert einen Respawn an. Der Modus schaltet sich ein, wenn der Spawn für dieselbe Verbindung und dasselbe Team innerhalb von drei Sekunden bestätigt wird und die Berechtigung weiterhin besteht. Andere berechtigte Admins sehen dann den rot-weißen Hinweis **Respawn · Supergodmodus AN**. Während der Wartezeit kann ein erneutes `!sgm` oder **Start abbrechen** die Aktivierung zurücknehmen. Bei einem späteren Tod endet der Modus wieder.

Lebende Admins schalten den Modus weiterhin mit demselben Befehl um. Im Menü führt der Weg über **!admin → Spielerverwaltung → Supergodmodus**. `!superguns` öffnet bei aktivem Modus die kostenlose Waffenauswahl. [Supergodmodus und Admin-Hinweise](ADMIN-SUPERGOD.md)

Der Testservernachweis prüft das Pluginladen und die Befehlsregistrierung nach Pluginreload im selben Serverprozess. Tests zur Respawn-Logik prüfen die Aktivierung und die Abbruchbedingungen. Der praktische Ablauf im menschlich bedienten CS2-Client bleibt offen.

Zur Installation den Ordner `LPLK_Admin` aus dem Plugin-ZIP nach `game/csgo/addons/swiftlys2/plugins/` übertragen und das Plugin geordnet neu laden.
