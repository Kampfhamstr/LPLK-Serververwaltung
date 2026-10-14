<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
 $root='/var/www/serververwaltung';
 spl_autoload_register(static function($class)use($root){if(str_starts_with($class,'App\\')){ $p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($p))require $p;}});
 App\Core\Config::load($root.'/config/app.php');
 $pdo=App\Core\Database::connection();
 $row=$pdo->query("SELECT * FROM game_servers WHERE id=7 AND server_identifier='11110004'")->fetch();
 if(!$row || $row['base_url']!=='https://lplk.resolve.bar')throw new RuntimeException('Test identity mismatch');
 if(__LPLK_REPAIR__){
   $connector=App\Services\ConnectorFactory::make($row);
   $details=$connector->serverDetails();if(($details['uuid']??'')!=='11110004-0000-4000-8000-000000000004')throw new RuntimeException('Connector identity mismatch');
   $entries=$connector->list(dirname(App\Services\FastLiveService::REMOTE_CONFIG_PATH));
   if(in_array('fastlive.json',array_column($entries,'name'),true)){
     $config=json_decode($connector->read(App\Services\FastLiveService::REMOTE_CONFIG_PATH),true,512,JSON_THROW_ON_ERROR);
     if(!is_array($config)||!array_key_exists('enabled',$config)||$config['enabled']!==false)throw new RuntimeException('Expected isolated legacy bridge');
   }
   App\Services\FastLiveService::disable(7);
 }
 $files=['VERSION','public/index.php','public/assets/app.js','templates/layout.php','templates/dashboard.php','templates/servers.php','templates/server_view.php','templates/server_live.php','templates/pets.php','src/Controllers/DashboardController.php','bin/install.php','bin/migrate.php'];
 $hashes=[];foreach($files as $f)$hashes[$f]=hash_file('sha256',$root.'/'.$f);
 $cacheInfo=[];foreach(['storage/cache','storage/cache/live-status','storage/cache/live-status/server-7.lock','storage/cache/live-status/server-7.json'] as $p){$f=$root.'/'.$p;$cacheInfo[$p]=['exists'=>file_exists($f),'writable'=>is_writable($f),'uid'=>@fileowner($f),'gid'=>@filegroup($f),'mode'=>substr(sprintf('%o',@fileperms($f)),-4)];}
 $before=App\Services\LiveServerService::statusQuick(7);
 $fresh=App\Services\LiveServerService::status(7);
 $after=App\Services\LiveServerService::statusQuick(7);
 $statuses=[];foreach(['before'=>$before,'fresh'=>$fresh,'after'=>$after] as $key=>$s)$statuses[$key]=['online'=>$s['bridge_online']??null,'available'=>$s['bridge_available']??null,'updated'=>$s['updated_at']??null,'age'=>$s['status_age_seconds']??null,'map'=>$s['map']??null,'reason'=>$s['reason']??null];
 $joins=[];
 if(class_exists(App\Services\ServerJoinService::class))foreach($pdo->query('SELECT * FROM game_servers ORDER BY id')->fetchAll() as $s){
   try{$joins[]=['id'=>(int)$s['id'],'name'=>$s['name']]+App\Services\ServerJoinService::resolve($s);}catch(Throwable){$joins[]=['id'=>(int)$s['id'],'error'=>'address unavailable'];}
 }
 ob_end_clean();echo json_encode(['version'=>trim(file_get_contents($root.'/VERSION')),'cacheInfo'=>$cacheInfo,'hashes'=>$hashes,'testStatus'=>$statuses,'testFastLive'=>App\Services\FastLiveService::enabled(7),'joinAddresses'=>$joins],JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Join/dashboard verification failed','type'=>get_class($e)]);exit(1);}
