<?php
$root=dirname(__DIR__);
function ok(bool $cond,string $label):void{if(!$cond)throw new RuntimeException('FAIL: '.$label);echo "OK  $label\n";}
$routes=file_get_contents($root.'/public/index.php');
$servers=file_get_contents($root.'/templates/servers.php');
$players=file_get_contents($root.'/templates/players.php');
$js=file_get_contents($root.'/public/assets/app.js');
$css=file_get_contents($root.'/public/assets/app.css');
$ctl=file_get_contents($root.'/src/Controllers/QuickManageController.php');
ok(str_contains($routes,"/servers/{id}/quick-manage"),'Quick-Manage GET/POST Route vorhanden');
ok(str_contains($servers,'data-server-quick'),'Serverzeile ist für Schnellverwaltung markiert');
ok(str_contains($servers,'data-server-quick-open'),'Expliziter Schnellverwaltungs-Button vorhanden');
ok(str_contains($js,"data-server-quick-open"),'Schnellverwaltungs-Button ist per Linksklick gebunden');
ok(str_contains($js,"contextmenu"),'Rechtsklick bleibt zusätzlich verfügbar');
ok(str_contains($js,"slice((mapPage-1)*10,mapPage*10)"),'Mapliste zeigt exakt 10 Maps pro Seite');
ok(str_contains($js,'data-qm-map-search'),'Mapsuche vorhanden');
foreach(['kick','ban','warn','gag','mute','slay','respawn','team'] as $op)ok(str_contains($ctl,"'{$op}'")||str_contains($ctl,"$op"),'Schnellaktion vorhanden: '.$op);
foreach(['founder','admin','member','vip','spieler'] as $role)ok(str_contains($css,'role-select-'.$role),'Rollenfarbe vorhanden: '.$role);
ok(!is_dir($root.'/bridge'),'Bridge bleibt vom Webpanel-Paket getrennt');
echo "v1.8.9 Schnellverwaltung/Rollenfarben: bestanden.\n";
