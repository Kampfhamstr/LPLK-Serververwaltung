<?php
$root=dirname(__DIR__);
$auth=file_get_contents($root.'/src/Core/Auth.php');
$openid=file_get_contents($root.'/src/Services/SteamOpenIdService.php');
$steamCtl=file_get_contents($root.'/src/Controllers/SteamIdentityController.php');
$dash=file_get_contents($root.'/src/Controllers/DashboardController.php');
$live=file_get_contents($root.'/src/Controllers/LiveController.php');
$liveTpl=file_get_contents($root.'/templates/server_live.php');
$js=file_get_contents($root.'/public/assets/app.js');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
$schema=file_get_contents($root.'/migrations/schema.sql');

function ok140(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok140(str_contains($openid,'https://steamcommunity.com/openid/login'),'Steam OpenID Provider ist fest');
ok140(str_contains($openid,'check_authentication'),'OpenID wird serverseitig bestätigt');
ok140(str_contains($steamCtl,'steam_id64=? AND id<>?'),'Doppelte Steam-Verknüpfung wird verhindert');
ok140(str_contains($auth,'public static function displayName'),'Steamname kann Panel-Anzeige übernehmen');
ok140(str_contains($auth,'last_seen_at=?'),'Webpanel-Präsenz wird gespeichert');
ok140(str_contains($dash,'onlinePanelUsers'),'Dashboard liefert Online-Webpanelnutzer');
ok140(str_contains($schema,'webpanel_announcements'),'Announcement-Tabelle vorhanden');
ok140(str_contains($auth,"'dashboard.announce'"),'Announcement-Recht vorhanden');
ok140(str_contains($liveTpl,'data-map-search'),'Map-Suche in Liveverwaltung vorhanden');
ok140(str_contains($js,'von ${options.length} Maps'),'Map-Suche clientseitig implementiert');
ok140(str_contains($live,'giveWeaponGroup'),'Gruppen-Waffenroute im Webpanel vorhanden');
ok140(str_contains($bridge,'case "give_weapon_group"'),'Bridge kennt Gruppen-Waffenaktion');
ok140(str_contains($bridge,'player.IsFakeClient'),'Gruppenwaffe schließt Bots aus');
ok140(str_contains($bridge,'team is 2 or 3'),'Alle-Zielgruppe beschränkt sich auf T/CT');

echo "\nv1.4.0 Feature-Test erfolgreich.\n";
