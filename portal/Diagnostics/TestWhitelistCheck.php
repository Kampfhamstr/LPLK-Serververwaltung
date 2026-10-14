<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
$phase='__LPLK_WHITELIST_PHASE__';
try {
    if(!in_array($phase,['add','finish'],true)) throw new DomainException('Ungültige Prüfphase.');
    $web='/var/www/serververwaltung';
    spl_autoload_register(static function($class) use($web) { if(str_starts_with($class,'App\\')) { $p=$web.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($p))require $p; } });
    App\Core\Config::load($web.'/config/app.php');
    $server=App\Services\LiveServerService::server(7);
    if($server['server_identifier']!=='11110004'||$server['name']!=='[TEST][LPLK] Beispielserver') throw new DomainException('Testidentität stimmt nicht.');
    $connector=App\Services\ConnectorFactory::make($server);$details=$connector->serverDetails();$a=$details['attributes']??$details;
    if(($a['uuid']??'')!=='11110004-0000-4000-8000-000000000004')throw new DomainException('Connector-Ziel stimmt nicht.');
    $status=App\Services\LiveServerService::status(7);
    if(empty($status['bridge_online'])||($status['status_age_seconds']??999)>10||count($status['players']??[])!==0)throw new DomainException('Frische Bridge und leerer Testserver erforderlich.');
    $found=false;foreach($status['plugins']??[] as $p)if(($p['id']??'')==='LPLK_Whitelist'&&strtolower((string)($p['status']??''))==='loaded')$found=true;
    if(!$found)throw new DomainException('Whitelist nicht als geladen bestätigt.');
    $path='game/csgo/addons/swiftlys2/plugins/LPLK_Whitelist/whitelist.txt';$fixture='76561197960265728';
    $read=static function()use($connector,$path){$v=preg_split('/\s+/',trim($connector->read($path)),-1,PREG_SPLIT_NO_EMPTY);sort($v,SORT_STRING);return $v;};
    $before=$read();$had=in_array($fixture,$before,true);
    if(($phase==='add'&&$had)||($phase==='finish'&&!$had))throw new DomainException('Prüfeintrag hat unerwarteten Zustand.');
    $command=($phase==='add'?'sw_wl ':'sw_uwl ').$fixture;
    App\Services\LiveServerService::queue(7,'server_command',['command'=>$command]);
    $expected=$phase==='add';$confirmed=false;
    for($i=0;$i<40;$i++){usleep(250000);$after=$read();if(in_array($fixture,$after,true)===$expected){$confirmed=true;break;}}
    if(!$confirmed)throw new DomainException('Befehl hat die gespeicherte Liste nicht bestätigt geändert.');
    $without=static fn($v)=>array_values(array_filter($v,static fn($id)=>$id!==$fixture));
    if($without($before)!==$without($after))throw new DomainException('Weitere Einträge unerwartet verändert.');
    App\Core\Audit::log('server.test.whitelist_check','server','7',['phase'=>$phase,'persisted'=>true]);
    ob_end_clean();echo json_encode(['phase'=>$phase,'fileConfirmed'=>true,'existingEntriesPreserved'=>true,'entries'=>count($after),'survivedRestart'=>$phase==='finish','temporaryEntryRemoved'=>$phase==='finish']),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>$e instanceof DomainException?$e->getMessage():'Whitelist-Prüfung fehlgeschlagen.','type'=>get_class($e),'phase'=>$phase]),"\n";exit(1);}
