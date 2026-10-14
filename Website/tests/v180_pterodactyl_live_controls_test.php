<?php
$root=dirname(__DIR__);
$version=trim(file_get_contents($root.'/VERSION'));
$auth=file_get_contents($root.'/src/Core/Auth.php');
$migrate=file_get_contents($root.'/bin/migrate.php');
$connector=file_get_contents($root.'/src/Services/PterodactylConnector.php');
$service=file_get_contents($root.'/src/Services/PterodactylLiveService.php');
$controller=file_get_contents($root.'/src/Controllers/LiveController.php');
$routes=file_get_contents($root.'/public/index.php');
$live=file_get_contents($root.'/templates/server_live.php');
$chat=file_get_contents($root.'/templates/server_chat.php');
$users=file_get_contents($root.'/templates/users.php');
$js=file_get_contents($root.'/public/assets/app.js');
$css=file_get_contents($root.'/public/assets/app.css');
$digest=file_get_contents($root.'/src/Services/ActivityDigestService.php');
function ok180(bool $ok,string $msg):void{if(!$ok){fwrite(STDERR,"FEHLER: $msg\n");exit(1);}echo "OK  $msg\n";}

ok180(version_compare($version,'1.8.0','>='),'VERSION ist 1.8.0 oder neuer');
foreach(['server.resources.view','server.power.start','server.power.restart','server.power.stop','server.power.kill','server.console.view','server.console.execute'] as $permission){
    ok180(str_contains($auth,"'{$permission}'"),"Permission {$permission} registriert");
}
ok180(str_contains($auth,"'admin' => [")&&str_contains($auth,"'server.power.start'")&&str_contains($auth,"'server.console.execute'"),'Admin-Standardrechte enthalten Pterodactyl Power/Ressourcen/Konsole');
ok180(str_contains($auth,"'server.power.kill'")&&str_contains($auth,"'server.console.execute'")&&str_contains($auth,'isDangerousPermission'),'Kill und Konsolenausführung sind kritische Rechte');
ok180(str_contains($connector,'function power(')&&str_contains($connector,"['start','stop','restart','kill']")&&str_contains($connector,'function resources(')&&str_contains($connector,'function serverDetails('),'Pterodactyl-Connector unterstützt Power, Ressourcen und Limits');
ok180(str_contains($service,'CACHE_TTL_SECONDS = 4.0')&&str_contains($service,'STALE_TTL_SECONDS = 30.0')&&str_contains($service,'LOCK_EX | LOCK_NB'),'Ressourcen-Polling ist gecacht und gegen parallele Worker gelockt');
ok180(str_contains($service,'floor($uptimeMs / 1000)')&&str_contains($service,"'uptime_human'"),'Pterodactyl-Uptime wird korrekt von ms in Sekunden/Laufzeit umgerechnet');
ok180(str_contains($service,'$limitsAge > 300'),'Serverlimits werden höchstens im 5-Minuten-Takt aktualisiert');
ok180(str_contains($routes,"/servers/{id}/live/pterodactyl")&&str_contains($routes,"/servers/{id}/live/power"),'Ressourcen- und Power-Routen vorhanden');
ok180(str_contains($controller,"'start' => 'server.power.start'")&&str_contains($controller,"'restart' => 'server.power.restart'")&&str_contains($controller,"'stop' => 'server.power.stop'")&&str_contains($controller,"'kill' => 'server.power.kill'"),'Jede Poweraktion besitzt ein eigenes serverseitiges Recht');
ok180(str_contains($controller,"!== 'KILL'")&&str_contains($controller,'Audit::log(\'server.power.\' . $signal'),'Hard Kill verlangt KILL und Poweraktionen werden auditiert');
ok180(str_contains($controller,'$this->requireLive($sid, \'server.console.execute\')')&&str_contains($controller,'Audit::log(\'server.console.execute\''),'Konsolenbefehle besitzen eigenes Recht und Audit-Log');
ok180(str_contains($live,'data-ptero-uptime')&&str_contains($live,'data-ptero-cpu')&&str_contains($live,'data-ptero-memory')&&str_contains($live,'data-ptero-disk')&&str_contains($live,'data-ptero-network'),'Live-UI zeigt Laufzeit, CPU, RAM, Festplatte und Netzwerk');
foreach(['server.power.start','server.power.restart','server.power.stop','server.power.kill'] as $permission){
    ok180(str_contains($live,"Auth::can('{$permission}')"),"Live-UI prüft {$permission}");
}
ok180(str_contains($live,"Auth::can('server.console.view')")&&str_contains($live,"Auth::can('server.console.execute')")&&str_contains($live,'NUR LESERECHT'),'Konsole trennt Ansehen und Ausführen verständlich');
ok180(str_contains($users,"'Serververwaltung'=>['servers.','server.']"),'Neue server.* Rechte erscheinen im Rechte-Center');
ok180(str_contains($migrate,"v1.8.0-Rechte ergaenzt")&&str_contains($migrate,'$add180(\'server.power.start\')')&&str_contains($migrate,'$add180(\'server.console.execute\')'),'Migration erweitert vorhandene Rollenvorlagen konservativ');
ok180(!str_contains($migrate,'DELETE FROM user_server_access'),'Migration löscht keine vorhandenen Serverfreigaben');
ok180(str_contains($live,'server-chat-day-separator')&&str_contains($chat,'server-chat-day-separator')&&str_contains($js,'server-chat-day-separator'),'Tagestrenner existieren in Livechat, Archiv und AJAX-Refresh');
ok180(str_contains($js,'refreshPterodactyl')&&str_contains($js,"/live/pterodactyl"),'JavaScript aktualisiert Pterodactyl-Ressourcen live');
ok180(str_contains($js,'updatePteroUptimeClock')&&str_contains($js,'setInterval(updatePteroUptimeClock,1000)'),'Serverlaufzeit zählt zwischen API-Refreshs jede Sekunde sichtbar weiter');
ok180(str_contains($css,'.ptero-resource-grid')&&(str_contains($css,'.ptero-power-grid')||str_contains($css,'.ptero-power-toolbar'))&&str_contains($css,'.server-chat-day-separator'),'Responsive Ressourcen-/Power-/Tagestrenner-Styles vorhanden');
ok180(str_contains($digest,"'server.power.start'=>'Server starten'")&&str_contains($digest,"'server.power.kill'=>'Server Hard Kill'")&&str_contains($digest,"'server.console.execute'=>'Serverkonsole'"),'Aktivitätsübersicht kennt neue Audit-Aktionen');
ok180(!str_contains($controller,'/websocket')&&!str_contains($live,'/websocket'),'Pterodactyl-WebSocket-Token wird nicht über Livecontroller/template exponiert');
echo "v1.8.0 Pterodactyl Live Controls Test erfolgreich.\n";
