# LPLK_Sounds 0.1.18 – !sound nach Pluginstart

LPLK_Sounds liest die AddonsManager-Konfiguration bereits beim eigenen Laden. Die spätere Freigabe von `!sound` kann so auf den vorbereiteten Stand zugreifen. Der benötigte Workshop-Eintrag und das vorgeladene Soundpaket werden weiterhin geprüft.

Auf dem Testserver wurde die Version geladen und nach einem echten Map-Ladevorgang geprüft. Alle 88 Sounds waren serverseitig vorbereitet, das Plugin meldete bereit und die Dateiprüfung zeigte null Lesezugriffe im Spielthread. 186 Sound- und 34 Abhängigkeitsprüfungen sowie der Befehlsabgleich über sieben Profile waren erfolgreich.

Diese Vorabversion ist auf dem Testserver eingespielt. Atlas und Inner Circle laufen zum Veröffentlichungszeitpunkt mit 0.1.17. Nach einer Plugin-Neuladung werden die Soundressourcen beim nächsten Mapstart erneut vorbereitet. Menübedienung und hörbare Wiedergabe im CS2-Client bleiben separat zu prüfen.

Für die Installation den Ordner `LPLK_Sounds` aus dem Plugin-ZIP nach `game/csgo/addons/swiftlys2/plugins/` übernehmen und das Plugin geordnet neu laden. Konfigurationen und Daten liegen getrennt vom Pluginpaket.
