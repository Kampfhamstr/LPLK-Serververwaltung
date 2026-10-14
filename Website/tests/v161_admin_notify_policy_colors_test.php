<?php
$root=dirname(__DIR__);
$settingsController=file_get_contents($root.'/src/Controllers/SettingsController.php');
$settingsTemplate=file_get_contents($root.'/templates/settings.php');
$live=file_get_contents($root.'/src/Services/LiveServerService.php');
$whitelist=file_get_contents($root.'/src/Services/WhitelistLiveService.php');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');

function ok161(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok161(str_contains($settingsController,"live.admin_action_notify.%"),'Settings lädt Admin-Notify-Werte');
ok161(str_contains($settingsController,"\$section==='admin_action_notify'"),'Settings speichert Admin-Notify-Bereich');
ok161(str_contains($settingsTemplate,'id="admin-action-notify"'),'Einstellungsblock vorhanden');
ok161(str_contains($settingsTemplate,'value="admins"'),'Nur Admin+Founder vorhanden');
ok161(str_contains($settingsTemplate,'value="all"'),'Alle Spieler vorhanden');
ok161(str_contains($settingsTemplate,'notify_always_target'),'Ziel immer vorhanden');

ok161(str_contains($live,'adminActionNotificationPolicy'),'Policy vorhanden');
ok161(str_contains($live,"'notify_scope' => \$notificationPolicy['scope']"),'Scope an Bridge');
ok161(str_contains($live,"'notify_target' => \$notificationPolicy['always_target']"),'Target-Flag an Bridge');
ok161(str_contains($whitelist,"'notify_scope'=>\$notificationPolicy['scope']"),'Whitelist übernimmt Policy');

ok161(str_contains($bridge,'NotifyScope { get; set; } = "admins"'),'Bridge NotifyScope');
ok161(str_contains($bridge,'NotifyTarget { get; set; } = true'),'Bridge NotifyTarget');
ok161(str_contains($bridge,'scope == "all"'),'Alle-Modus');
ok161(str_contains($bridge,'_adminNotifySteamIds.Contains(player.SteamID)'),'Admin-Founder-Modus');
ok161(str_contains($bridge,'NotifyDisconnectingTargetBeforeAction'),'Kick/Ban Zielvorabmeldung');

ok161(str_contains($bridge,'"founder" => "[darkred]"'),'Founder-Farbe');
ok161(str_contains($bridge,'"admin" => "[red]"'),'Admin-Farbe');
ok161(str_contains($bridge,'"member" => "[blue]"'),'Member-Farbe');
ok161(str_contains($bridge,'"vip" => "[green]"'),'VIP-Farbe');
ok161(str_contains($bridge,'"spieler" => "[yellow]"'),'Spieler-Farbe');
ok161(str_contains($bridge,'DescribeColoredAdminTarget'),'Ziel separat rollenfarbig');
ok161(str_contains($bridge,'$"[white][ADMIN] {timestamp} | "'),'Grundtext weiß');
ok161(str_contains($bridge,'$"{actorColor}{Limit(actor, 48)} ({actorRoleLabel})"'),'Auslöser separat farbig');
ok161(str_contains($bridge,'$"[white] | {action}[default]"'),'Aktion weiß + Reset');

echo "\nv1.6.1 Admin-Notify-Policy-/Color-Test erfolgreich.\n";
