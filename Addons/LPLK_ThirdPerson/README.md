# LPLK_ThirdPerson – Außenkamera

Quellstand **0.1.0**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Addon ermöglicht berechtigten Spielern eine Außenkamera. Abstand, Höhe und weitere Kameraeigenschaften sind konfigurierbar. Spieler-, Lebens- und Teamwechsel müssen die Kamera zuverlässig zurücksetzen, damit kein alter Kamerazustand übernommen wird.

## Installation und Einrichtung

`LPLK_ThirdPerson.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_ThirdPerson/LPLK_ThirdPerson.dll`.

Einstellungen: `data/LPLK_ThirdPerson/settings.json`. `UseTpPermission` steht im Quellstandard auf **`thirdperson.use`**. Das Recht gezielt vergeben oder die dafür vorgesehene Freigabe im Adminmenü wählen. Kameraabstand, Höhe und Schadensverhalten mit dem Servermodus abstimmen.

## Bedienung und Rechte

`!thirdperson` und `!tp` schalten die Kamera für berechtigte Spieler. `!lplk_thirdperson` dient der Verwaltung; Modulrecht `lplk.thirdperson.admin`, zentrale Verwaltung auch `lplk.admin`.

## Prüfen

Sicht, Bewegung, Kollisionen, Tod, Teamwechsel und Wiederbeitritt mit einem realen Client prüfen. Der erfolgreiche Pluginstart bestätigt noch keine fehlerfreie Kameradarstellung; die visuelle Clientabnahme steht noch aus.
