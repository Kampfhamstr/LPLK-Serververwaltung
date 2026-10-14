<?php
$root=dirname(__DIR__);
$auth=file_get_contents($root.'/src/Core/Auth.php');
$players=file_get_contents($root.'/src/Controllers/PlayersController.php');
$users=file_get_contents($root.'/src/Controllers/UsersController.php');
$invites=file_get_contents($root.'/src/Controllers/InvitationsController.php');
$migrate=file_get_contents($root.'/bin/migrate.php');
function ok132(bool $ok,string $msg):void{if(!$ok)throw new RuntimeException($msg);echo "OK  $msg\n";}
ok132(str_contains($auth,"'founder' => ['*']"),'Founder besitzt Vollzugriff');
ok132(str_contains($auth,"'spieler' => 'Spieler'")&&str_contains($auth,"'vip' => 'VIP'")&&str_contains($auth,"'member' => 'Member'")&&str_contains($auth,"'admin' => 'Admin'")&&str_contains($auth,"'founder' => 'Founder'"),'Fünf Panelrollen vorhanden');
ok132(str_contains($auth,"'admin' => [")&&str_contains($auth,"'live.console'")&&str_contains($auth,"'live.godmode'")&&str_contains($auth,"'plugins.manage'"),'Admin-Standardvorlage besitzt umfangreiche Schreib-/Live-Rechte');
ok132(str_contains($auth,'webRoleLevel(self::effectiveWebRoleForUser($user)) > self::webRoleLevel($role)'),'Rollen dürfen nur nach unten vergeben werden – einschließlich Founder-2FA-Sicherheitsmodus');
ok132(str_contains($users,"Auth::requirePermission('users.manage')"),'Nutzerverwaltung verwendet Permission statt Founder-only');
ok132(str_contains($invites,"Auth::requirePermission('users.invite')")&&str_contains($invites,'Auth::canInviteWebRole($role)'),'Einladungen sind granular und hierarchisch geschützt');
ok132(str_contains($auth,'permissionOverrides(')&&str_contains($auth,'permissions_json'),'Individuelle Benutzerrechte werden ausgewertet');
ok132(str_contains($auth,'FROM user_server_access WHERE user_id=? AND server_id=?'),'Serverbezogene Nutzerrechte werden ausgewertet');
ok132(!str_contains($migrate,'DELETE FROM user_server_access'),'Migration löscht vorhandene Serverrechte nicht mehr');
ok132(!str_contains($migrate,"UPDATE users SET permissions_json=NULL WHERE"),'Migration löscht individuelle Rechte nicht mehr');
ok132(str_contains($players,'assertCommunityRoleTargetManageable'),'Community-Rollenänderung besitzt Zielschutz');
ok132(str_contains($players,'assertServerRoleTargetManageable'),'Serverrollenänderung besitzt Zielschutz');
echo "\nWebpanel-Rollen-Test v1.7.9 erfolgreich.\n";
