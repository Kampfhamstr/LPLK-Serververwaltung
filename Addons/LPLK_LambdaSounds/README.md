# LPLK_LambdaSounds 0.1.0

[Plugin herunterladen](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/lambda-sounds-0.1.0/LPLK_LambdaSounds.zip) · [Quellpaket](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/lambda-sounds-0.1.0/LPLK-Plugins-Source.zip)

Auf **CS: Lambda – Dust2** (Workshop-ID `3773100753`) registriert das Plugin die auf der Map enthaltene Lambda-Soundbank und die zugehörigen Schussressourcen beim Mapstart. So sind die Lambda-Ereignisse auch dann verfügbar, wenn ein Modellpaket die generische Soundbank gleichen Pfads verdeckt. Das Paket enthält eigenen Plugin-Code. Die Sounddateien stammen aus der bereits eingebundenen Workshop-Map.

Den Ordner aus dem Plugin-ZIP nach `game/csgo/addons/swiftlys2/plugins/` übernehmen und das Plugin laden. Die Registrierungen greifen beim passenden Mapstart.

Auf Test, Atlas und Inner Circle geprüft. Der Vergleich auf Test weist sieben wieder verfügbare Lambda-Ereignisse und ausgehende Soundmeldungen nach. Wie eigene und fremde Schüsse im CS2-Client klingen, bleibt separat zu prüfen.

Zum Nachbauen mit .NET 10 zuerst `dotnet restore Addons/LPLK_LambdaSounds/Rohskripte/LPLK_LambdaSounds.csproj --locked-mode` und dann `dotnet build Addons/LPLK_LambdaSounds/Rohskripte/LPLK_LambdaSounds.csproj -c Release --no-restore` ausführen. Das ZIP enthält den Ordner `LPLK_LambdaSounds/` mit `LPLK_LambdaSounds.dll`, `LPLK_LambdaSounds.pdb` und `LPLK_LambdaSounds.deps.json` aus der Release-Ausgabe.
