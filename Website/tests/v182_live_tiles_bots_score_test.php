<?php
$root=dirname(__DIR__);
$version=trim(file_get_contents($root.'/VERSION'));
$auth=file_get_contents($root.'/src/Core/Auth.php');
$controller=file_get_contents($root.'/src/Controllers/LiveController.php');
$service=file_get_contents($root.'/src/Services/LiveServerService.php');
$layoutService=file_get_contents($root.'/src/Services/LiveLayoutService.php');
$live=file_get_contents($root.'/templates/server_live.php');
$js=file_get_contents($root.'/public/assets/app.js');
$css=file_get_contents($root.'/public/assets/app.css');
$schema=file_get_contents($root.'/migrations/schema.sql');
$routes=file_get_contents($root.'/public/index.php');
$bridgePath=$root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs';
$bridgeProjectPath=$root.'/bridge/SwiftlyS2/SerververwaltungBridge/SerververwaltungBridge.csproj';
$bridge=is_file($bridgePath)?file_get_contents($bridgePath):'';
$bridgeProject=is_file($bridgeProjectPath)?file_get_contents($bridgeProjectPath):'';
function ok182(bool $ok,string $msg):void{if(!$ok){fwrite(STDERR,"FEHLER: $msg\n");exit(1);}echo "OK  $msg\n";}
ok182(version_compare($version,'1.8.2','>='),'VERSION ist 1.8.2 oder neuer');
ok182(str_contains($schema,'user_live_layouts')&&str_contains($layoutService,'scope_key'),'persönliche Live-Layouts werden serverseitig gespeichert');
ok182(str_contains($routes,"/live/layout")&&str_contains($controller,'LiveLayoutService::save'),'Layout-Speichern besitzt Controller und Route');
ok182(str_contains($js,'Modulares Live-Dashboard')&&str_contains($js,'dragstart')&&str_contains($js,'data-live-module-toggle'),'Kacheln unterstützen Aufklappen und Drag & Drop');
ok182(str_contains($css,'.live-module-collapsed')&&str_contains($css,'.is-layout-editing'),'Kachelzustände sind gestaltet');
ok182(str_contains($live,'<th>Kills</th>')&&str_contains($live,'<th>Punkte</th>'),'Online-Spielerliste zeigt Kills und Punkte');
ok182(str_contains($live,'data-live-team-score')&&str_contains($js,'data.ct_score')&&str_contains($js,'data.t_score'),'CT-/T-Teamstand wird live angezeigt');
ok182(str_contains($live,'game-role-bot')&&str_contains($js,'Boolean(player.is_bot)'),'Bots werden als eigene Spielerzeilen dargestellt');
ok182(str_contains($auth,"'live.bot.kick'")&&str_contains($auth,"'live.bot.slay'")&&str_contains($auth,"'live.bot.team'"),'Einzel-Bot-Aktionen besitzen granulare Rechte');
ok182(str_contains($service,"'bot_player_kick'")&&str_contains($service,"'bot_player_slay'")&&str_contains($service,"'bot_player_team'"),'Bridge-Queue erlaubt Einzel-Bot-Kommandos');
ok182(str_contains($controller,'public function botPlayerAction')&&str_contains($controller,"'user_id'=>\$userId")&&str_contains($controller,"'player_id'=>\$playerId"),'Bot wird ohne SteamID über aktuelle User-/Player-ID adressiert');
ok182(str_contains($service,"preg_match('/^7656119\\d{10}$/', \$steamId)"),'Bot-Leer-IDs gelangen nicht in Steam-Spielerabfragen');
if(version_compare($version,'1.8.4','>=')){
 ok182($bridge===''&&$bridgeProject==='','Bridge wird ab v1.8.4 bewusst als separates ZIP ausgeliefert');
}else{
 ok182(str_contains($bridgeProject,'<Version>1.5.5</Version>')&&str_contains($bridge,'Version = "1.5.5"'),'Komplettpaket enthält Bridge v1.5.5');
 ok182(str_contains($bridge,'Kills = player.Controller.KillCount')&&str_contains($bridge,'CtScore = ctScore')&&str_contains($bridge,'case "bot_player_kick"'),'eingebettete Bridge liefert Kills/Teamstand und Bot-Einzelaktionen');
}
echo "v1.8.2 Live Tiles/Bots/Score Test erfolgreich.\n";
