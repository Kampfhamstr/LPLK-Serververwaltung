<?php
$root=dirname(__DIR__);
$service=file_get_contents($root.'/src/Services/AdminActionLogService.php');
$controller=file_get_contents($root.'/src/Controllers/LiveController.php');
$template=file_get_contents($root.'/templates/server_live.php');
$js=file_get_contents($root.'/public/assets/app.js');

function ok159(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok159(str_contains($service,'admin_actions.log'),'Service liest admin_actions.log');
ok159(str_contains($service,'DEFAULT_LIMIT = 100'),'Standardlimit 100 vorhanden');
ok159(str_contains($service,'MAX_READ_BYTES = 1024 * 1024'),'1-MB-Leselimit vorhanden');
ok159(str_contains($service,'array_reverse($lines)'),'Neueste Einträge zuerst');
ok159(str_contains($service,"str_starts_with(\$part, 'Fehler=')"),'Fehler wird separat geparst');
ok159(str_contains($controller,'AdminActionLogService::latest($sid, 100)'),'LiveController liefert Admin-Log');
ok159(str_contains($template,'id="admin-log"'),'Admin-Log direkt in Liveverwaltung');
ok159(str_contains($template,'Admin-Log anzeigen'),'Admin-Log standardmäßig einklappbar');
ok159(str_contains($template,'data-admin-log-search'),'Textsuche vorhanden');
ok159(str_contains($template,'data-admin-log-status'),'Statusfilter vorhanden');
ok159(str_contains($template,'Fehler anzeigen'),'Fehlerdetails aufklappbar');
ok159(str_contains($js,'v1.5.9: Admin-Log Filter'),'Clientseitiger Filter vorhanden');

echo "\nv1.5.9 Admin-Log-Live-Test erfolgreich.\n";
