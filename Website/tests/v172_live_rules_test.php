<?php
$root=dirname(__DIR__);
function ok172(bool $condition,string $label):void{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}
$version=trim((string)file_get_contents($root.'/VERSION'));
$routes=file_get_contents($root.'/public/index.php');
$auth=file_get_contents($root.'/src/Core/Auth.php');
$controller=file_get_contents($root.'/src/Controllers/LiveController.php');
$template=file_get_contents($root.'/templates/server_live.php');
$js=file_get_contents($root.'/public/assets/app.js');
$css=file_get_contents($root.'/public/assets/app.css');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
$proj=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/SerververwaltungBridge.csproj');
$fast=file_get_contents($root.'/src/Services/FastLiveService.php');
$ptero=file_get_contents($root.'/src/Services/PterodactylConnector.php');

ok172(version_compare($version,'1.7.2','>='),'Webpanel-Version ist 1.7.2 oder neuer');
foreach(['/live/bots','/live/rules','/live/weapon-mode','/live/scope','/live/rules-reset','/live/preset'] as $route)
    ok172(str_contains($routes,$route),"Route {$route} vorhanden");
foreach(['live.bots','live.rules','live.speed','live.gravity','live.godmode','live.weapon_mode','live.presets'] as $permission)
    ok172(str_contains($auth,"'{$permission}'"),"Recht {$permission} vorhanden");

ok172(str_contains($template,'Live-Regelstatus'),'Live-Regelstatus wird sichtbar angezeigt');
ok172(str_contains($template,'Bots & Spielregeln'),'Bots/Spielregeln besitzen eigenen Kachelbereich');
ok172(str_contains($template,'Godmode'),'Godmode ist in der Live-Verwaltung verfügbar');
ok172(str_contains($template,'Waffenmodus & Teamlimits'),'Waffenmodus und Teamlimits sind gemeinsam verwaltbar');
ok172(str_contains($template,'AWP Only') && str_contains($template,'Pistols Only') && str_contains($template,'Knife Only'),'Waffen-Presets vorhanden');
ok172(str_contains($template,'team_limits[') && str_contains($template,'data-weapon-limit'),'Pro-Waffe-Teamlimit-Eingaben vorhanden');
ok172(str_contains($template,'team_limits_include_bots'),'Bots können optional in Waffenlimits einbezogen werden');
ok172(str_contains($js,"awp-one") && str_contains($js,"snipers-one"),'Schnellpresets für 1 AWP/Sniper pro Team vorhanden');
ok172(str_contains($css,'.weapon-limit-grid'),'Teamlimit-Kacheln sind gestylt');

ok172(str_contains($controller,"'team_limits'=>\$teamLimits"),'Controller übermittelt Waffen-Teamlimits');
ok172(str_contains($controller,"'team_limits_include_bots'=>!empty"),'Controller übermittelt Bot-Zähloption');
ok172(str_contains($controller,"'weapon_team_limits'=>[]"),'Built-in-Presets definieren Waffenlimit-Zustand explizit');

ok172(str_contains($proj,'<Version>1.5.5</Version>'),'Bridge-Projektversion ist aktueller Stand 1.5.5');
ok172(str_contains($bridge,'Version = "1.5.5"'),'Bridge-Metadatenversion ist aktueller Stand 1.5.5');
foreach(['case "bot_kick"','case "bot_add"','case "server_rules"','case "headshot_only"','case "weapon_mode"','case "player_speed"','case "player_gravity"'] as $needle)
    ok172(str_contains($bridge,$needle),"Bridge-Aktion {$needle} vorhanden");
ok172(str_contains($bridge,'_weaponTeamLimits'),'Bridge speichert pro-Waffe-Teamlimits');
ok172(str_contains($bridge,'CountTeamWeaponHolders'),'Bridge zählt vorhandene Waffenträger pro Team');
ok172(str_contains($bridge,'NotAllowedByProhibition'),'Kauf/Pickup kann bei erreichtem Teamlimit blockiert werden');
ok172(str_contains($bridge,'team_limits_include_bots'),'Bridge verarbeitet Bot-Zähloption');
ok172(str_contains($bridge,'weaponServices.MyValidWeapons'),'Teamlimit basiert auf tatsächlich getragenen Waffen');
ok172(str_contains($bridge,'EventPlayerSpawn') && str_contains($bridge,'ReapplyPlayerOverrides'),'Spieler-Sondermodi werden nach Spawn erneut angewendet');
ok172(str_contains($bridge,'public LiveRuleStatus Rules'),'Bridge-Status enthält echten Live-Regelstatus');
ok172(str_contains($bridge,'WeaponTeamLimits = new Dictionary'),'Live-Regelstatus meldet aktive Waffen-Teamlimits');
ok172(str_contains($bridge,'_weaponTeamLimits.Clear()'),'Regel-Reset entfernt Waffen-Teamlimits');
ok172(str_contains($bridge,'IPlayer acquiringPlayer = ctx.Params.Player'),'Moderner CanAcquire-GameHook ordnet den kaufenden Spieler direkt über den Context zu');
ok172(str_contains($bridge,'500,503,505,506,507,508'),'Knife-Skins bleiben als Messerklasse berücksichtigt');

ok172(str_contains($fast,"game/csgo/addons/swiftlys2/data/serververwaltung_bridge/fastlive.json"),'Fast-Live-Pfad bleibt korrekt');
ok172(str_contains($ptero,'Serververwaltung/1.8.3'),'Pterodactyl User-Agent ist auf aktuellen Stand 1.8.3 aktualisiert');

echo "\nv1.7.2 Live-Regeln/Weapon-Teamlimits-Test erfolgreich.\n";
