<?php
$root=dirname(__DIR__);
$version=trim(file_get_contents($root.'/VERSION'));
$live=file_get_contents($root.'/templates/server_live.php');
$js=file_get_contents($root.'/public/assets/app.js');
$css=file_get_contents($root.'/public/assets/app.css');
$serviceFile=$root.'/src/Services/LiveLayoutService.php';
$service=file_get_contents($serviceFile);
function ok183(bool $ok,string $msg):void{if(!$ok){fwrite(STDERR,"FEHLER: $msg\n");exit(1);}echo "OK  $msg\n";}
ok183(version_compare($version,'1.8.3','>='),'VERSION ist 1.8.3 oder neuer');
ok183(str_contains($live,'live-action-accordion')&&substr_count($live,'live-action-disclosure')>=3,'Spiel-/Mapaktionen sind als kompakte Accordion-Kachel umgesetzt');
ok183(str_contains($live,"Auth::can('live.map')")&&str_contains($live,"Auth::can('live.teleport')"),'bestehende granulare Rechte bleiben an den Unteraktionen erhalten');
ok183(str_contains($live,'data-layout-sizes'),'gespeicherte Kachelbreiten werden an das Frontend übergeben');
ok183(str_contains($js,'allowedSizes=[3,4,6,8,12]')&&str_contains($js,"sizes[section.id]")&&str_contains($js,'dataLiveModuleSpan')===false,'JS speichert ausschließlich definierte Rasterbreiten');
ok183(str_contains($js,"section.dataset.liveModuleSpan")&&str_contains($js,"params.set('layout_json'") ,'JS speichert Breiten zusammen mit dem Layout');
ok183(str_contains($css,'--live-module-span')&&str_contains($css,'#server-actions{--live-module-span:4}')&&str_contains($css,'#players{--live-module-span:8}'),'Standardlayout setzt Spiel-/Mapaktionen 1/3 neben Online-Spielern 2/3');
ok183(str_contains($css,'#server-chat{--live-module-span:8}')&&str_contains($css,'#messages{--live-module-span:4}'),'Serverchat 2/3 und Servernachricht 1/3 sind als Standard vorgesehen');
ok183(str_contains($service,'DEFAULT_SIZES')&&str_contains($service,'ALLOWED_SIZES'),'Backend validiert Kachelbreiten');
require_once $serviceFile;
$sanitized=\App\Services\LiveLayoutService::sanitize(['order'=>['players','server-actions'],'collapsed'=>['players','evil'],'sizes'=>['players'=>8,'server-actions'=>4,'advanced'=>99,'evil'=>3]]);
ok183(($sanitized['sizes']['players']??0)===8&&($sanitized['sizes']['server-actions']??0)===4,'gültige Breiten bleiben erhalten');
ok183(($sanitized['sizes']['advanced']??0)===8&&!isset($sanitized['sizes']['evil']),'ungültige/unerlaubte Breiten und Module werden verworfen');
ok183(!in_array('evil',$sanitized['collapsed'],true),'unerlaubte Module gelangen nicht in collapsed');
echo "v1.8.3 Compact Live Dashboard Test erfolgreich.\n";
