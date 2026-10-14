<?php
$root=dirname(__DIR__);
function ok173(bool $condition,string $label):void{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}
$version=trim((string)file_get_contents($root.'/VERSION'));
$routes=(string)file_get_contents($root.'/public/index.php');
$schema=(string)file_get_contents($root.'/migrations/schema.sql');
$service=(string)file_get_contents($root.'/src/Services/GlobalBanService.php');
$fast=(string)file_get_contents($root.'/src/Services/FastLiveService.php');
$actions=(string)file_get_contents($root.'/src/Controllers/PlayerActionsController.php');
$layout=(string)file_get_contents($root.'/templates/layout.php');
$chat=(string)file_get_contents($root.'/templates/server_chat.php');
$live=(string)file_get_contents($root.'/templates/server_live.php');
$players=(string)file_get_contents($root.'/templates/players.php');
$dashboard=(string)file_get_contents($root.'/templates/dashboard.php');
$login=(string)file_get_contents($root.'/templates/login.php');
$bridge=(string)file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
$proj=(string)file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/SerververwaltungBridge.csproj');

ok173(version_compare($version,'1.7.3','>='),'Webpanel-Version ist 1.7.3 oder neuer');
ok173(str_contains($schema,'CREATE TABLE IF NOT EXISTS global_bans'),'Globale Ban-Tabelle ist in der Migration enthalten');
foreach(['/bans','/bans/new','/player-actions/context','/player-actions/live'] as $route)
    ok173(str_contains($routes,$route),"Route {$route} vorhanden");
foreach(['advertising','cheating','insult','griefing','server_rules','admin_instruction','ban_evasion'] as $reason)
    ok173(str_contains($service,"'{$reason}'"),"Bangrund {$reason} vorhanden");
ok173(str_contains($service,"'evidence_chat_message_id'") || str_contains($service,'evidence_chat_message_id'),'Chat-Beleg wird beim Ban archiviert');
ok173(str_contains($fast,"'global_bans'=>GlobalBanService::activePayload()"),'Fast Live liefert globale Banliste an jede Bridge');
foreach(['kick','mute','unmute','gag','ungag','private_message'] as $action)
    ok173(str_contains($actions,"'{$action}'"),"Globale Spieleraktion {$action} vorhanden");
ok173(str_contains($layout,'data-player-action-modal'),'Einheitliche Spieleraktions-Kachel vorhanden');
ok173(str_contains($layout,'Nachricht an Spieler') && str_contains($layout,'Global bannen'),'Nachricht und globaler Ban in Spieleraktions-Kachel vorhanden');
foreach([$chat,$live,$players,$dashboard] as $i=>$template)
    ok173(str_contains($template,'data-player-actions'),'Spieleraktionen in zentraler Anzeige '.($i+1).' eingebunden');
ok173(str_contains($login,'autocomplete="off"'),'Login deaktiviert Formular-Autocomplete für den Browser');

ok173(str_contains($proj,'<Version>1.5.5</Version>'),'Bridge-Projektversion ist aktueller Stand 1.5.5');
ok173(str_contains($bridge,'Version = "1.5.5"'),'Bridge-Metadatenversion ist aktueller Stand 1.5.5');
ok173(str_contains($bridge,'global_bans.json'),'Bridge besitzt lokalen globalen Ban-Cache');
ok173(str_contains($bridge,'ApplyGlobalBanSnapshot'),'Bridge übernimmt globale Ban-Snapshots per Fast Live');
ok173(str_contains($bridge,'Global gebannt | Grund:'),'Banmeldung enthält Grund');
ok173(str_contains($bridge,'Ban-ID #'),'Banmeldung enthält Ban-ID');
ok173(str_contains($bridge,'NETWORK_DISCONNECT_KICKBANADDED'),'Globaler Ban wird beim Gameserver durchgesetzt');
ok173(str_contains($bridge,'Core.GameHooks.Items.CanAcquire.Pre'),'Moderner Waffen-GameHook bleibt erhalten');
ok173(!str_contains($bridge,'IOnItemServicesCanAcquireHookEvent'),'Veralteter Waffen-Hook ist nicht zurückgekehrt');

echo "\nv1.7.3 Global-Moderation-Test erfolgreich.\n";
