<?php
$root=dirname(__DIR__);
$auth=file_get_contents($root.'/src/Core/Auth.php');
$service=file_get_contents($root.'/src/Services/ActivityDigestService.php');
$controller=file_get_contents($root.'/src/Controllers/ActivityDigestController.php');
$routes=file_get_contents($root.'/public/index.php');
$layout=file_get_contents($root.'/templates/layout.php');
$js=file_get_contents($root.'/public/assets/app.js');

function ok164(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok164(str_contains($auth,"_activity_digest_since"),'Vorheriger Login wird in Session übernommen');
ok164(str_contains($auth,"_activity_digest_pending"),'Digest wird nach Login als ausstehend markiert');
ok164(str_contains($auth,'$previousLogin !== \'\''),'Vorheriges last_login_at wird bevorzugt');

ok164(str_contains($service,'forumActivity'),'Forumaktivität enthalten');
ok164(str_contains($service,'directMessages'),'Persönliche Nachrichten enthalten');
ok164(str_contains($service,'adminActions'),'Adminaktionen enthalten');
ok164(str_contains($service,'announcements'),'Mitteilungen enthalten');
ok164(str_contains($service,'chatActivity'),'Webpanel-Chat enthalten');
ok164(str_contains($service,"a.action LIKE 'live.%'"),'Adminaktionen kommen aus lokalem Audit-Log');
ok164(!str_contains($service,'Pterodactyl'), 'Digest enthält keine Pterodactyl-Abfrage');
ok164(!str_contains($service,'ConnectorFactory'), 'Digest enthält keinen Remote-Connector');
ok164(str_contains($service,"Auth::canServer(\$serverId,'live')"),'Adminaktionen respektieren Server-Liveberechtigung');
ok164(str_contains($service,"'#post-'"),'Forumlinks führen direkt zum neuen Beitrag');

ok164(str_contains($controller,'ActivityDigestService::markConsumed'),'Digest wird einmal pro Login verbraucht');
ok164(str_contains($routes,"'/activity-digest'"),'Digest-Route vorhanden');
ok164(str_contains($layout,'data-activity-digest-pending'),'Layout startet Popup nur bei neuem Login');
ok164(str_contains($layout,'Seit deinem letzten Besuch'),'Popup vorhanden');
ok164(str_contains($js,'v1.6.4: Since-last-login activity digest'),'Digest-JavaScript vorhanden');
ok164(str_contains($js,"panel-users:open-user"),'Nachrichtenlink öffnet rechte Nutzerleiste');
ok164(str_contains($js,"fetch('/activity-digest'"),'Digest wird erst nach Seitenaufbau geladen');

echo "\nv1.6.4 Activity-Digest-Test erfolgreich.\n";
