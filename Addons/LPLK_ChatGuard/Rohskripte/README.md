# LPLK_ChatGuard 0.1.0

Chat-, Namen- und Werbeschutz für SwiftlyS2 1.4.14. Erster Einsatz ausschließlich auf dem isolierten Testserver 89450e53. Administration über `!admin → Chat / Werbeschutz`, direkter Einstieg `!lplk_chatguard`.

## Übernommene Mechanik

Referenz: LPLK SerververwaltungAntiAdvertisement 1.0.14, NAS-Quelle `sERVERVERWALTUNG/work/antiad/SerververwaltungAntiAdvertisement_v1.0.14_Source/src/Plugin.cs` einschließlich Änderungs- und Prüfberichte 1.0.7–1.0.14. Der Livebestand meldet 1.0.14; eine Gleichheit der alten Binärdatei mit dem NAS-Quelltext ist nicht nachgewiesen. Die tatsächlichen nicht geheimen Inner-Circle-Einstellungen und zentralen Textregeln wurden am 11.10.2026 um 19:40 Uhr gelesen; Nachweis `state/reference-chatguard-rules.json`. Produktive Werbe-SteamIDs wurden nicht kopiert.

- Chatwerbung blockieren: erster Treffer Warnung, zweiter Treffer 30 Minuten lokaler Gag, ab drittem Treffer Kick; Trefferfenster zehn Minuten. Während eines aktiven Gags wird Chat verworfen und nicht als weiterer Treffer gezählt. Das Verhalten entspricht der bisherigen Quelle.
- Bekannte Werbenamen maskieren und im folgenden Spielupdate entfernen. Unbekannte Domains allein sind kein Werbenachweis. Feste Sperrdomains haben Vorrang vor Freigaben.
- VIP, Member, Admin und Founder bei aktivem Rollenschutz nur privat warnen: keine automatischen Strikes, Gags, Kicks oder globalen Werbeidentitäten. Manuelle Bridge-Sanktionen bleiben eigenständig wirksam.
- Nach bestätigter Werbung öffentliche Disconnectmeldung für diese Verbindung unterdrücken, auch nach Namenswechsel. System-/Mapwerbung weiterhin über die bisherigen fünf Ereignis-/15 Nachrichtenpfade filtern.
- Der gesonderte Konfigurationsschalter für einzelne Meldungskanäle bleibt erhalten. Unterdrückte Ereignisse werden weiterhin intern verarbeitet; die private Enginekonsole wird nicht gelöscht.

## Struktur und Korrekturen

`ChatGuardPolicy.cs` / `Validation.cs`: Erkennung, Rollenreihenfolge, Trefferfenster und Validierung ohne Spielabhängigkeit. `PlayerProtection.cs`: Chat, Namensprüfung und Sanktionen. `ServerMessages.cs`: öffentliche Nachrichten. `Roles.cs`: Rollenquelle. `Persistence.cs` / `BridgeFiles.cs`: Dateien und bisheriges Austauschformat. `Administration.cs`: Menüs und bestätigte Einstellungen. `Runtime.cs`: Lebenszyklus, Status und gemeinsamer Filtervertrag.

Bridge 1.6.3 fragt `LPLK.ChatGuard.Filter.v1` vor der Chatarchivierung ab. Wenn sie vorhanden ist, führt der eigene Chat-Hook keine zweite Prüfung durch. Ohne ChatGuard bleibt die alte Bridge-Prüfung als Rückfall erhalten; bei vorhandenem, aber nicht erreichbarem ChatGuard wird eine Nachricht weder ausgespielt noch archiviert. Der Status `bridgeFilterReady` prüft den echten Vertrag über die Bridge mit einem ungültigen Absender, ohne Spieler oder Sanktionen zu erzeugen. Die Bestätigung erscheint auch in `sw_lplk_chatguard`; ein zusätzlicher Diagnosebefehl wird nicht benötigt.

Die Erkennung erhält Wortgrenzen: `promo code lplk.de` wird nicht mehr zu einer anderen Domain zusammengezogen. Freigegebene Subdomains sind zulässig, `lplk.de.attacker.com` nicht. Ein zusätzlich genannter gesperrter Host umgeht den Filter nicht. Vorgemerkte Kicks prüfen dieselbe Verbindung, Pluginaktivität und aktuelle Rolle erneut. Geänderte Regeln/Rollen leeren den Namensprüfcache. Ungültige Konfigurationsänderungen lassen den letzten gültigen Stand aktiv.

## Testdaten und Bedienung

Eigene Dateien unter `data/LPLK_ChatGuard/`: `config.json`, `global_protection.json`, `status.json`, `events.log`. Seeds werden nur bei erstmaliger Anlage gelesen. Vorhandene Konfigurationen werden nicht überschrieben. Das zentrale Schema Version 2 bleibt lesbar; der aktuelle Testbestand enthält nur Textregeln, keine produktiven Sperridentitäten. `export_bridge_events=false`; der neue Test-Ausgang wird von der Bridge nicht an das produktive Werbeschutzsystem übermittelt. Vollständiger produktiver Regelsync ist noch kein abgenommener Bestandteil dieses Moduls.

Alle Konfigurationsfelder für Erkennung, Warnungen, Sanktionen und Meldungen sind über das Adminmenü erreichbar, lokale Domain- und Begriffsliste einschließlich Hinzufügen/Entfernen. Entfernen verlangt die konkrete Menübestätigung. Änderungen sind sofort wirksam und werden erst nach validierter, atomarer Speicherung aktiv. Domains erscheinen in Listen mit `·` als Punkttrenner, damit die Verwaltungsansicht selbst keine gesperrten Werbeadressen ausgibt. Eingabe erfolgt mit normalen Punkten.

Die gemeinsame nummerierte/farbige Menüdarstellung wird verwendet. Der Benutzer hat die Sichtprüfung am 11.10.2026 vorläufig verschoben; seine frühere Meldung zu falschen Farben bezog sich auf den produktiven Inner Circle und belegt keinen Fehler am neuen Teststand.

## Prüfung und offene Abnahme

80 automatische fachliche Prüfungen in `tests/ChatGuardChecks`, Releasebuild gegen festgelegte API 1.4.14. Runtime-/Webpanelnachweise unter `state/test-chatguard-*.json`. Diese Prüfungen ersetzen keine reale Prüfung mit Spielern: Chat/Teamchat, Menüeingaben, Rollenwechsel, Warn-/Gag-/Kickfolge und öffentliche Join-/Leave-Unterdrückung sind im Spiel noch abzunehmen. Keine Produktionsinstallation.
