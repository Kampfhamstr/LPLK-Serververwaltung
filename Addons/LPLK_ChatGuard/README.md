# LPLK_ChatGuard – Chat-, Namens- und Werbeschutz

Quellstand **0.1.0**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

ChatGuard bündelt konfigurierbare Regeln gegen unerwünschte Chat-/Namensinhalte und Werbung. Es kann lokale Regel- und Moderationsdaten verwenden und mit der vorhandenen Verwaltung zusammenarbeiten. Welche Texte oder Namen betroffen sind, ergibt sich aus den eigenen Filtern; das Addon ist kein automatisch für jede Community passender Regelkatalog.

## Installation und Einrichtung

`LPLK_ChatGuard.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_ChatGuard/LPLK_ChatGuard.dll`.

Die Konfiguration liegt unter **`data/LPLK_ChatGuard/config.json`**. Regeln, Ausnahmen und Reaktionen gezielt einstellen. Persistierte Moderationsdaten im selben Datenbereich erhalten und nicht als Quellcode veröffentlichen. Bei einer Ablösung eines bestehenden Filters dessen Regeln und Zuständigkeit vorher abgleichen, damit nicht zwei Systeme dieselbe Nachricht bestrafen.

## Bedienung und Rechte

`!lplk_chatguard` ist der direkte Verwaltungsbefehl und verlangt `lplk.chatguard.admin`. Mit installiertem LPLK_Admin erscheint außerdem der passende Moduleinstieg. Änderungen durch die Website benötigen deren Rechte und die passende Bridge-Anbindung.

## Prüfen

Zulässige Nachrichten, gezielte Testtreffer und Ausnahmen prüfen, ebenso Namensänderungen und erneuten Beitritt. Vor scharfen Sanktionen Fehlalarme ausschließen. Testtexte und synthetische Konten verwenden; echte Chatverläufe, Sperrlisten und Spielerinformationen gehören nicht in das Repository.
