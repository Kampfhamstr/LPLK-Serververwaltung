<?php
$root=dirname(__DIR__);
$service=file_get_contents($root.'/src/Services/LiveServerService.php');
$controller=file_get_contents($root.'/src/Controllers/DashboardController.php');
$template=file_get_contents($root.'/templates/dashboard.php');
$js=file_get_contents($root.'/public/assets/app.js');

function ok143(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok143(!str_contains($service,'if (!in_array((string)$server[\'framework\'], [\'swiftlys2\', \'both\'], true))'),'Framework blockiert status() nicht mehr');
ok143(str_contains($service,'$connector->read(self::STATUS_PATH)') && !str_contains($service,'$connector->exists(self::STATUS_PATH)'),'Bridge status.json wird direkt und ohne zusätzlichen exists/list-Request geprüft');
ok143(str_contains($service,'frameworkWithSwiftly'),'Framework wird bei Bridge-Erkennung automatisch ergänzt');
ok143(str_contains($service,"'counterstrikesharp' => 'both'"),'CounterStrikeSharp + Bridge ergibt Both');
ok143(str_contains($service,"Audit::log('live.bridge_autodetected'"),'Automatische Erkennung wird auditiert');
ok143(!str_contains($service,'Live-Verwaltung v1.3.1 benötigt'),'Veralteter v1.3.1-Hinweis entfernt');
ok143(str_contains($controller,'framework_detected'),'Dashboard übernimmt automatisch erkanntes Framework');
ok143(str_contains($template,'data-dash-framework'),'Framework kann live aktualisiert werden');
ok143(str_contains($js,"'[data-dash-framework]'"),'JS aktualisiert Framework ohne Reload');

$livePos=strpos($template,'dashboard-live-panel');
$onlinePos=strpos($template,'id="panel-users-online"');
$chatPos=strpos($template,'id="dashboard-chat"');
$announcePos=strpos($template,'id="announcements"');
ok143($livePos!==false && $onlinePos!==false && $livePos<$onlinePos,'Livekacheln stehen vor Webpanel-Online');
ok143($livePos<$chatPos,'Livekacheln stehen vor Chat/Forum');
ok143($livePos<$announcePos,'Livekacheln stehen vor Announcements');

echo "\nv1.4.3 Bridge-Autodetect-/Dashboard-Test erfolgreich.\n";
