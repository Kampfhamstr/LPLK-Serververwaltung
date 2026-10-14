# Website: Funktionen, Installation und Updates

Die Webverwaltung verwaltet Server, Spieler, Rechte und unterstützte Addonfunktionen im Browser. Dazu gehören serverbezogene Spielerprofile mit Steam-Verknüpfung und Avatar, Whitelistansichten sowie die angebundenen Bank- und Statistikansichten. Änderungen benötigen die jeweiligen Website-Rechte und zusätzlich eine passende, erreichbare Gameserver-Anbindung.

**Dokumentierter freigegebener Stand: 1.15.2.8.** Daneben liegt 1.15.2.9 als vorbereitete Erweiterung der Live-Serverübersicht vor; vorbereitet bedeutet noch nicht installiert. Beim späteren GitHub-Import den gewählten Quellstand ausdrücklich kennzeichnen. Diese Anleitung führt keine Installation aus.

## Was schon vorhanden ist – und was noch nicht

- Die Navigation bietet getrennte Bereiche, Suche, Serverfavoriten und Spieleransichten. Rollen entscheiden über Sichtbarkeit und Änderungsrechte.
- Bankkonten können als Übersicht oder Kontoauszug/Bilanz angezeigt werden. Der Export enthält derzeit höchstens die **20 jüngsten Buchungen**, keine vollständige Monats- oder Jahresbuchhaltung.
- Statistikansichten unterscheiden unter anderem PvP, Bottraining, Waffenwertungen, gelegte und entschärfte Bomben. Highlights werden nur aus vorhandenen Daten abgeleitet.
- Die neue LPLK-Bank-/Statistikanbindung ist bislang auf das freigegebene Testprofil begrenzt. Eine frische Installation macht diese Funktionen nicht automatisch für beliebige Server verfügbar.
- Eine frei erreichbare öffentliche Statistikwebsite mit historischen Kurven ist noch nicht umgesetzt. Der gemeinsame Bankverbund für Produktionsserver ist vorbereitet, aber noch nicht aktiviert; Guthaben werden nicht automatisch zusammengeführt.

## Voraussetzungen und Ablage

Vorgesehen ist ein eigener Linux-Webserverbereich mit Nginx, PHP-FPM/CLI und PHP 8.3 als bisheriger Installationsbasis. Erforderliche PHP-Erweiterungen: `pdo_sqlite`, `curl`, `sodium`, `openssl`. `ssh2` und `ftp` werden nur für die entsprechenden optionalen Dateiconnectoren benötigt. Paketnamen und PHP-Socket müssen zur Distribution passen.

Den vollständigen **ausgewählten Websitequellstand** beispielsweise nach `/var/www/serververwaltung` übertragen. Das ist eine eigenständige PHP-Anwendung, kein SwiftlyS2-Plugin und kein Ordner innerhalb eines CS2-Containers. Pterodactyl und Wings bleiben eigene Dienste.

Zu einer vollständigen Quelle gehören insbesondere `src/`, `templates/`, `public/`, `config/app.example.php`, `migrations/`, `bin/` und `deploy/`. Die genaue Struktur des ausgewählten Pakets erhalten. Produktive `config/app.php`, Datenbanken, Backups, Logs und Spielerdaten gehören nicht in den Quellcodeupload.

## Neue Installation

1. Eine eigene HTTPS-Domain bzw. Subdomain auf den Webserver zeigen lassen und einen **neuen, leeren** Anwendungsordner vorbereiten.
2. Den gewählten Websitequellstand vollständig übertragen. Die mitgelieferte `config/app.example.php` dient als Vorlage; keine fremde produktive `app.php` übernehmen.
3. Im Websiteordner den interaktiven Installer starten:

   ```bash
   cd /var/www/serververwaltung
   php bin/install.php
   ```

   Er fragt öffentliche HTTPS-URL, Founder-Anzeigename, E-Mail und Passwort ab. Er erzeugt `config/app.php`, einen eigenen Verschlüsselungsschlüssel und `storage/database/app.sqlite`.
4. Dem PHP-Webprozess Leserechte auf die Anwendung und die nötigen Schreibrechte auf `storage/` geben. Code und Konfiguration sollen nicht allgemein schreibbar sein. Datenbank und Anwendungsschlüssel zusammen sichern; ohne den passenden Schlüssel sind gespeicherte verschlüsselte Zugänge nicht wiederherstellbar.
5. Die mitgelieferte `deploy/nginx.conf` als Ausgangspunkt verwenden. Domain, Anwendungsweg und PHP-FPM-Socket anpassen. Der DocumentRoot muss auf **`/var/www/serververwaltung/public`** zeigen; niemals den gesamten Quellordner veröffentlichen. Die mitgelieferten Schutzregeln erhalten.
6. Nginx-Konfiguration mit `nginx -t` prüfen, aktivieren und HTTPS einrichten. Danach im Anwendungsordner prüfen:

   ```bash
   php bin/migrate.php
   php bin/self-test.php
   php bin/health-check.php
   ```

7. Per HTTPS anmelden, ein Konto mit beschränkten Rechten prüfen und die gewünschten Serververbindungen einzeln einrichten. Ein leeres Profil darf fehlende Daten anzeigen; keine Beispieldaten als echte Spielerstatistik importieren.

Die genaue distributionsabhängige Einrichtung steht zusätzlich in der `INSTALLATION.md` und der Nginx-Vorlage des gewählten Websitepakets. Dort enthaltene ältere Versionsbeispiele sind keine Aufforderung, einen anderen Quellstand einzusetzen.

## Gameserver anbinden

Die Website braucht einen berechtigten Connector zum jeweiligen Server, beispielsweise die vorgesehenen Panel-/Dateizugänge, und passende Gameserver-Komponenten. [SerververwaltungBridge](../Addons/SerververwaltungBridge/ANLEITUNG.md) stellt Status und die bestehende Auftragsanbindung bereit. Bank, Whitelist und Statistik benötigen außerdem das jeweilige Addon und seine aktivierten Exporte bzw. Funktionen.

Eine installierte Bridge allein verknüpft keine Website automatisch. Zielserver, Verbindung, Pfade und Rechte müssen übereinstimmen. Einen Adminauftrag erst als erfolgreich betrachten, wenn dessen bestätigter Ergebnisstatus und der anschließend gelesene Bestand passen.

## Bestehende Installation aktualisieren

**`bin/install.php` nicht als Updateverfahren verwenden.** Vor einem Update den tatsächlich laufenden Stand, Änderungen vor Ort und den vorgesehenen Zielstand vergleichen. Dateien, `config/app.php` einschließlich Anwendungsschlüssel und eine konsistente SQLite-Datenbanksicherung erhalten. Während der Sicherung/Übernahme konkurrierende schreibende Zugriffe kontrollieren; eine offene SQLite-Datei nicht ohne WAL-Behandlung kopieren.

Nur die geprüften Programmdateien des Zielstands übernehmen. Bestehende Konfiguration und Nutzerdaten erhalten, notwendige neue Einstellungen gezielt ergänzen. Anschließend Migration, Selbsttest, Health-Check, HTTPS sowie relevante Seiten und Rechte prüfen. Update-/Patchskripte im Paket sind teils für konkrete Vorversionen und freigegebene Zielprofile bestimmt; sie sind kein universeller Einzeiler für beliebige Installationen.

Ein Rückfall muss Code, Schema und Daten zusammen berücksichtigen. Neu entstandene Spieler- oder Bankdaten dürfen dabei nicht durch einen alten Snapshot verloren gehen. Keine produktiven Gameserver automatisch neu starten, nur weil die Website aktualisiert wird.
