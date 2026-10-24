# LPLK_ChatGuard – Chat-, Namens- und Werbeschutz

**Ausgabe 24.10.2026: 0.1.4.** [LPLK_ChatGuard.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/fleet-2026.10.24-gameplay/LPLK_ChatGuard.zip) · [Serverprofile, Migration und Prüfgrenzen](../../docs/FLEET-20261024.md). Die folgenden älteren Versionsnachweise sind historisch. Die echte Client- und Mehrspielerabnahme steht aus.

**Menüstand 23.10.2026: 0.1.3, auf Testserver 89450e53 installiert.** [Aktuelles Paket](Build/LPLK_ChatGuard.zip) · [Aktuelle Bedienung und Prüfgrenzen](../../docs/MENUE-UEBERARBEITUNG-2026-10-23.md). Frühere Versions- und Prüfabschnitte unten sind historische Nachweise; bestehende GitHub-Downloads sind ältere Pakete.

**Download:** [LPLK_ChatGuard.zip](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/download/test-2026.10.14-menus/LPLK_ChatGuard.zip) · [Ausgabe, Prüfsummen und Teststatus](https://github.com/Kampfhamstr/LPLK-Serververwaltung/releases/tag/test-2026.10.14-menus). Dieser veröffentlichte Download enthält weiterhin den älteren Stand0.1.1. Das aktuelle, ausschließlich lokal/testserverseitig geprüfte Paket0.1.2 liegt unter `Build/LPLK_ChatGuard.zip`; keine neue GitHub-Veröffentlichung.

Quellstand **0.1.2**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

ChatGuard bündelt konfigurierbare Regeln gegen unerwünschte Chat-/Namensinhalte und Werbung. Es kann lokale Regel- und Moderationsdaten verwenden und mit der vorhandenen Verwaltung zusammenarbeiten. Welche Texte oder Namen betroffen sind, ergibt sich aus den eigenen Filtern; das Addon ist kein automatisch für jede Community passender Regelkatalog.

## Installation und Einrichtung

`LPLK_ChatGuard.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_ChatGuard/LPLK_ChatGuard.dll`.

Die Konfiguration liegt unter **`data/LPLK_ChatGuard/config.json`**. Regeln, Ausnahmen und Reaktionen gezielt einstellen. Persistierte Moderationsdaten im selben Datenbereich erhalten und nicht als Quellcode veröffentlichen. Bei einer Ablösung eines bestehenden Filters dessen Regeln und Zuständigkeit vorher abgleichen, damit nicht zwei Systeme dieselbe Nachricht bestrafen.

## Bedienung und Rechte

`!lplk_chatguard` ist der direkte Verwaltungsbefehl und verlangt `lplk.chatguard.admin`. Mit installiertem LPLK_Admin erscheint außerdem der passende Moduleinstieg. Änderungen durch die Website benötigen deren Rechte und die passende Bridge-Anbindung.

## Prüfen

Zulässige Nachrichten, gezielte Testtreffer und Ausnahmen prüfen, ebenso Namensänderungen und erneuten Beitritt. Vor scharfen Sanktionen Fehlalarme ausschließen. Testtexte und synthetische Konten verwenden; echte Chatverläufe, Sperrlisten und Spielerinformationen gehören nicht in das Repository.

## Änderungen 0.1.2 (nur Testserver)

Der Einstieg trennt Einstellungen und Status. Schutz, Sanktionen, Meldungen und Filterregeln haben kompakte Seiten; lange Filterlisten bleiben vollständig über Seiten erreichbar, Entfernen verlangt weiterhin die vorhandene Bestätigung. Werteingaben umfassen die bisherigen gültigen Bereiche: Gagschwelle2–100, Kickschwelle3–100, Zeitfenster/Gagdauer1–1440Minuten und Warnabstand1–300Sekunden. Eigene Werte werden nicht durch zyklische Kurzvorgaben ersetzt.

Das bisherige `snake_case`-Format in `config.json` bleibt bestehen. Inhaltliches Nachladen alle drei Sekunden erkennt Änderungen auch ohne neues Dateidatum. Ungültige/fehlende/verknüpfte Dateien, unbekannte Schlüssel oder mehr als2MiB behalten die letzten gültigen Regeln; Menüs überschreiben solche Dateien nicht. Die2MiB erlauben weiterhin alle drei gültigen Listen mit je256Einträgen zu200Zeichen. Listen werden vor dem Bearbeiten kopiert; eine abgelehnte Änderung verändert die aktiven Filter nicht. Gleichzeitige externe Änderungen während der Bearbeitung werden geprüft; das ist keine betriebssystemweite Transaktionssperre für fremde Editoren.

Menü-/Konfigurationsänderungen löschen keine vorhandenen Gags oder Verstoßzähler. `global_protection.json`, Rollen, Sanktionen und bestehende Testfilter wurden nicht geändert. Status meldet die tatsächlich geladene0.1.2.80 Filter-/Moderationsprüfungen und114 gemeinsame Wartungsprüfungen, beide APIs und native Status-/Webantworten bestanden. Reale Chat-/Namensmeldungen und Mehrspielerverhalten werden später abgenommen.
