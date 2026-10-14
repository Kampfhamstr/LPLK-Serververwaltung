<?php
$root=dirname(__DIR__);
$c=file_get_contents($root.'/src/Controllers/LiveController.php');
$t=file_get_contents($root.'/templates/server_live_player.php');
$b=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
function ok131($v,$m){if(!$v)throw new RuntimeException($m);echo "OK  $m\n";}
ok131(str_contains($c,'teleportPlayers'),'Teleport bietet alle online menschlichen Spieler als Quelle/Ziel an');
ok131(str_contains($c,"source_steamid") && str_contains($c,"destination_steamid"),'Quelle und Ziel werden gesendet');
ok131(str_contains($t,'source_steamid') && str_contains($t,'destination_steamid'),'Freie Quelle/Ziel-Semantik eindeutig');
ok131(str_contains($t,'ownSteamId'),'Eigenes verifiziertes Steamkonto kann vorausgewählt werden');
ok131(str_contains($t,'Gespeicherte Namen'),'Nickname-Historie als Dropdown');
ok131(str_contains($t,'Servername auf Steam-Profilnamen setzen'),'Restore-Richtung eindeutig');
ok131(str_contains($b,'public List<string> History'),'Bridge speichert Namenshistorie');
ok131(str_contains($b,'TeleportToPlayer(source, destination)'),'Bridge teleportiert beliebige Quelle zum Ziel');
ok131(str_contains($b,'case "teleport_player"'),'Bridge kennt freien Spieler-Teleport');
echo "\nTeleport-/Nickname-History-Test erfolgreich.\n";
