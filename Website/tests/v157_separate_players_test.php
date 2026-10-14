<?php
$root=dirname(__DIR__);
$service=file_get_contents($root.'/src/Services/LiveServerService.php');
$controller=file_get_contents($root.'/src/Controllers/LiveController.php');
$template=file_get_contents($root.'/templates/server_live.php');
$routes=file_get_contents($root.'/public/index.php');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');

function ok157(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok157(str_contains($service,"'separate_players'"),'Webpanel erlaubt separate_players');
ok157(str_contains($controller,'function separatePlayers'),'Controller-Aktion vorhanden');
$queueNeedle="LiveServerService::queue(\$sid, 'separate_players')";
ok157(str_contains($controller,$queueNeedle),'Controller queued separate_players');
ok157(str_contains($routes,'/live/separate-players'),'Route vorhanden');
ok157(str_contains($template,'Überlappende Spieler trennen'),'Live-UI enthält Trenn-Button');
ok157(str_contains($template,"Auth::can('live.teleport')"),'Trenn-Button nutzt Teleport-Berechtigung');
ok157(str_contains($bridge,'case "separate_players"'),'Bridge kennt separate_players');
ok157(str_contains($bridge,'SeparateOverlappingPlayers()'),'Bridge führt Trennscan aus');
ok157(str_contains($bridge,'PlayersOverlap('),'Überlappungsprüfung vorhanden');
ok157(str_contains($bridge,'TrySeparatePlayer('),'Kollisionssichere Trennung vorhanden');
ok157(str_contains($bridge,'maxPasses = 8'),'Mehrfachdurchläufe vorhanden');
ok157(str_contains($bridge,'!player.IsFakeClient'),'Bots werden nicht automatisch versetzt');

echo "\nv1.5.7 Separate-Players-Test erfolgreich.\n";
