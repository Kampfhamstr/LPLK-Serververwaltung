# LPLK_CombatRules – Treffer- und Kampfregeln

Quellstand **0.1.0**, SwiftlyS2-API **1.4.14**. [Alle Addons](../../docs/ADDONS.md).

## Funktion

Das Addon bündelt Headshot-Regeln, die konfigurierbare Golden Rule und waffen-/teambezogene Schadensfilter. Regeln können von Waffen, Spielphase und Rundensituation abhängen. Maßgeblich ist die konkrete Konfiguration des Spielprofils.

## Installation und Einrichtung

`LPLK_CombatRules.zip` nach der [gemeinsamen Anleitung](../../docs/INSTALLATION-ADDONS.md) installieren. Ziel: `game/csgo/addons/swiftlys2/plugins/LPLK_CombatRules/LPLK_CombatRules.dll`.

Unter `data/LPLK_CombatRules/settings.json` insbesondere `HeadshotOnly`, `FirearmsOnly`, `GoldenRule` und die individuellen Regeln prüfen. Nicht gleichzeitig ein älteres HeadshotOnly-Addon für dieselben Ereignisse aktiv lassen, ohne die Wechselwirkung ausdrücklich getestet zu haben. Bestehende Regeln beim Umstieg einzeln abgleichen; Quellstandardwerte sind nicht automatisch die gewünschte Serverkonfiguration.

## Bedienung und Rechte

`!lplk_combatrules` öffnet die Verwaltung. Modulrecht **`lplk.combat.admin`**, zentrale Verwaltung auch `lplk.admin`. Spieler benötigen keinen eigenen Befehl, um den aktiven Kampfregeln zu unterliegen.

## Prüfen

Kopf-/Körpertreffer, erlaubte und ausgeschlossene Waffen, Gegner-/Teamschaden sowie Warmup und Rundenvorsprung prüfen. Bei Regelausnahmen muss auch der ausgeschaltete Zustand getestet werden. Lokale Logikprüfungen ersetzen keine vollständige Mehrspielerabnahme.
