# Sicherheitsmodell

## Zugriff

- Keine öffentliche Registrierung
- Erster Founder ausschließlich über CLI
- Weitere Benutzer ausschließlich über einmalige E-Mail-Einladung
- Einladungs-Token wird nur gehasht gespeichert
- Einladungen verfallen nach 48 Stunden
- Founder-Rechte können nur von Foundern vergeben werden
- Server-Verbindungsdaten und SMTP-Einstellungen sind Founder-Funktionen

## Authentifizierung

- Argon2id-Passwort-Hashing
- Mindestlänge bei neu gesetzten Passwörtern: 12 Zeichen
- TOTP-2FA mit lokalem QR-Code-Setup; das TOTP-Secret wird nicht an externe QR-Dienste übertragen
- Recovery-Codes werden nur als Argon2id-Hashes gespeichert und nach einmaliger Verwendung sofort ungültig
- Login-Rate-Limit pro E-Mail/IP
- Session-ID-Regeneration
- `HttpOnly`, `Secure`, standardmäßig `SameSite=Strict`
- Für den ausdrücklich gestarteten Steam-OpenID-Login bleibt das eigentliche Panel-Session-Cookie unverändert `SameSite=Strict`. Stattdessen wird ein separates, `HttpOnly`/`Secure`, signiertes und auf `/profile/steam/callback` begrenztes Hilfscookie mit `SameSite=Lax` ausgegeben. Es enthält nur die gebundene Sessionkennung, den zufälligen State und eine maximale Lebensdauer von zehn Minuten, wird nach dem Callback gelöscht und ist außerhalb dieses Pfads nicht verwendbar. Der Callback verlangt zusätzlich den sitzungsgebundenen State und die serverseitig geprüfte OpenID-Antwort.


## Founder-2FA-Sicherheitsmodus ab v1.8.1

- Die Datenbankrolle eines Founders wird ohne 2FA nicht verändert.
- Solange 2FA fehlt, ist die effektive Panelrolle jedoch `Admin`; die editierbare Admin-Rollenvorlage bildet die harte Berechtigungsobergrenze.
- Individuelle oder temporäre Deny-Regeln bleiben wirksam. Individuelle Allows können den Admin-Cap nicht überschreiten.
- Rollen-/Einladungs-/Vorlagenverwaltung und Serverzugriffe verwenden ebenfalls die effektive Rolle.
- Nach bestätigter 2FA-Aktivierung wird Founder-Vollzugriff sofort wirksam.
- 2FA-Aktivierung, Deaktivierung, Recovery-Nutzung und Recovery-Neugenerierung werden im Audit-Log protokolliert.

## Websicherheit

- CSRF-Token für alle schreibenden Aktionen
- HTML-Escaping für Ausgaben
- Content Security Policy
- `X-Frame-Options: DENY`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer`
- HSTS bei HTTPS
- Nur `public/` ist Document-Root

## Geheimnisse

Folgende Werte werden mit libsodium Secretbox verschlüsselt gespeichert:

- Pterodactyl Client API-Key
- SFTP-Passwort
- SMTP-Passwort
- TOTP-Secret

Der Verschlüsselungsschlüssel liegt in:

```text
config/app.php
```

Diese Datei darf niemals öffentlich erreichbar oder in ein öffentliches Repository eingecheckt werden.

## Gameserver-Dateien

Vor Änderungen wird die aktuelle Remote-Datei gesichert. Anschließend wird die neue Datei geschrieben, erneut gelesen und per SHA-256 geprüft.

Zwischen Scan/Sync extern veränderte verwaltete Dateien führen zu einem Konflikt. Die Anwendung schreibt dann nicht weiter, bis ein erneuter Scan erfolgt.

## Root-Rechte

Die Webanwendung benötigt keine Root-Rechte, keinen Docker-Socket und keinen direkten Wings-Zugriff.

Empfohlen:

```text
Webanwendung -> HTTPS -> Pterodactyl Client API -> Gameserverdateien
```

SFTP und FTPS sind die bevorzugten direkten Alternativen. Unverschlüsseltes FTP ist nur als Kompatibilitäts-Fallback vorgesehen und wird im Panel als unsicher gekennzeichnet.

## Delegation und Einladungen

- Nicht-Founder können keine höhere Webrolle als ihre eigene vergeben.
- Nicht-Founder können nur Panelrechte weitergeben, die sie selbst besitzen.
- Serverrechte können nur innerhalb der eigenen Serverfreigaben delegiert werden.
- Die maximale vergebbare CS2-Gruppe wird pro Server begrenzt.
- Founder, SMTP und Server-Zugangsdaten sind nicht delegierbar.
- Wird ein Server zwischen Einladung und Annahme gelöscht, wird die veraltete Serverfreigabe beim Annehmen sicher übersprungen.

## Personen- und Steam-Daten ab 1.1.0

Die Personenakte kann personenbezogene Kontaktdaten enthalten. Deshalb existieren getrennte Rechte für das Anzeigen und Bearbeiten dieser Felder. Nutzer ohne `players.contacts.view` erhalten diese Felder weder in der Detailansicht noch als Suchkriterien.

Der Steam Web API Key wird wie andere Secrets mit dem Anwendungsschlüssel verschlüsselt gespeichert und ausschließlich serverseitig über HTTPS verwendet. Steam-Avatare werden im nicht öffentlich erreichbaren Verzeichnis `storage/steam-avatars/` gespeichert und nur nach erfolgreicher Panel-Authentifizierung ausgeliefert.

Die SteamID64 bleibt der technische Primärbezug. Ab v1.1.2 wird der aktuelle Steam-Profilname automatisch als Anzeigename verwendet und bei jedem Steam-Sync aktualisiert. Frühere Namen bleiben in der Namenshistorie erhalten.

## Haupt-/Zweitaccounts (seit 1.1.4)

Zweitaccounts besitzen keine eigenständige Community- oder Serverrollen-Autorität. Rechteänderungen werden auf den Hauptaccount normalisiert und anschließend auf die gesamte verknüpfte Accountfamilie übertragen. Damit kann ein Zweitaccount nicht durch eine abweichende Rollenänderung höhere oder niedrigere Rechte als der Hauptaccount behalten.

Die Zuordnung eines Accounts zu einem Hauptaccount ist eine privilegierte Spieler-Verwaltungsaktion, CSRF-geschützt, bestätigungspflichtig in der Oberfläche und im Audit-Log protokolliert.

## Live-Verwaltung 1.2.0

Die `SerververwaltungBridge` öffnet keinen eigenen HTTP-/TCP-Port. Befehle werden über einen kontrollierten Dateiqueue im SwiftlyS2-Datenbereich übermittelt. Das Webpanel stellt keine frei formulierbare RCON-/Console-Schnittstelle bereit. Waffen, Teams, Mapnamen und numerische Werte werden serverseitig validiert. Jede Live-Aktion benötigt sowohl ein globales `live.*`-Recht als auch die serverbezogene Freigabe `Live-Administration` und wird im Audit-Log protokolliert.

Der vollständige Gameserver-Restart verwendet ausschließlich das Pterodactyl-Powersignal `restart`; beliebige Pterodactyl-Console-Befehle werden hierfür nicht aus Benutzereingaben erzeugt.


## Live-Verwaltung v1.2.0

- Die SwiftlyS2-Bridge öffnet keinen TCP-/HTTP-Port.
- Befehle werden nur aus einem festen, serverseitigen Allowlist-Satz verarbeitet.
- Waffen werden zusätzlich gegen eine feste Allowlist geprüft.
- Mapnamen/Workshop-IDs werden im Webpanel und in der Bridge validiert.
- Live-Aktionen benötigen CSRF-Schutz, ein passendes `live.*`-Recht und serverbezogene Live-Freigabe.
- Kritische Aktionen wie Kick, Ban, Mapwechsel, Neustart und Nickname-Änderung verlangen in der Oberfläche eine Bestätigung.
- Alle Aktionen werden im Audit-Log protokolliert.
- Die Bridge verändert keine BetBank-/Lotto-Daten.

## Passwort-Reset

- Kein Account-Enumeration-Leak: die öffentliche Antwort ist für existierende und nicht existierende Adressen identisch.
- Reset-Token: 32 zufällige Bytes, als 64-stelliger Hex-Token übertragen.
- Persistiert wird nur SHA-256 des Tokens.
- Token-Ablauf: 60 Minuten.
- Einmalige Verwendung; ein neuer Token invalidiert ältere offene Tokens.
- Rate-Limits pro IP und gehashter E-Mail-Adresse.
- Nach erfolgreichem Reset wird `auth_version` erhöht und jede vorherige Sitzung ungültig.
- TOTP/2FA wird nicht deaktiviert oder zurückgesetzt.
- Passwortspeicherung: Argon2id.
