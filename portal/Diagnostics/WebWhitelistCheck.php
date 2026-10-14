<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
$phase='__LPLK_WHITELIST_WEB_PHASE__';$added=false;$cleanupConfirmed=false;
try {
 if(!in_array($phase,['inspect','roundtrip'],true))throw new RuntimeException('Phase');
 $root='/var/www/serververwaltung';
 spl_autoload_register(static function($class)use($root){if(str_starts_with($class,'App\\')){$p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($p))require $p;}});
 App\Core\Config::load($root.'/config/app.php');$pdo=App\Core\Database::connection();
 $server=App\Services\LiveServerService::server(7);
 if($server['server_identifier']!=='11110004')throw new RuntimeException('Test identity');
 $connector=App\Services\ConnectorFactory::make($server);$details=$connector->serverDetails();$attributes=$details['attributes']??$details;
 if(($attributes['uuid']??'')!=='11110004-0000-4000-8000-000000000004')throw new RuntimeException('Connector identity');
 $admin=$pdo->query("SELECT id,auth_version FROM users WHERE active=1 AND web_role='founder' ORDER BY id LIMIT 1")->fetch();if(!$admin)throw new RuntimeException('Verification identity');
 $_SESSION=['user_id'=>(int)$admin['id'],'auth_version'=>max(1,(int)$admin['auth_version']),'_presence_touch'=>time()];
 $files=['VERSION','public/index.php','templates/layout.php','templates/server_live.php','src/Services/WhitelistLiveService.php','src/Services/LplkWhitelistAdapter.php','tests/v11523_whitelist_test.php','bin/install.php','bin/migrate.php','README.md','CHANGELOG.md','INSTALLATION.md','KURZINSTALLATION.txt','UPDATE_VON_1.15.2.2_AUF_1.15.2.3.md','AUSLIEFERUNG_UND_UPDATEBEFEHLE.txt'];
 $hashes=[];foreach($files as $file)$hashes[$file]=is_file($root.'/'.$file)?hash_file('sha256',$root.'/'.$file):null;
 $state=App\Services\WhitelistLiveService::state(7);
 $report=['version'=>trim(file_get_contents($root.'/VERSION')),'hashes'=>$hashes,'detected'=>$state['detected']??false,'editable'=>$state['editable']??null,'entries'=>$state['count']??null,'mode'=>$state['mode']??null,'stateError'=>$state['state_error']??null,'readError'=>$state['read_error']??null,'productionGameServersChanged'=>false];
 if($phase==='roundtrip'){
  if($report['version']!=='1.15.2.3'||empty($state['editable']))throw new RuntimeException('New whitelist view is not ready');
  $status=App\Services\LiveServerService::status(7);
  if(empty($status['bridge_online'])||($status['status_age_seconds']??999)>10||count($status['players']??[])!==0)throw new RuntimeException('Fresh empty testserver required');
  if($state['mode']!==1||($state['enabled']??null)!==true)throw new RuntimeException('Expected test whitelist settings');
  $fixture='76561197960265728';$before=$state['steam_ids'];sort($before,SORT_STRING);
  if(in_array($fixture,$before,true))throw new RuntimeException('Fixture already exists');
  $wait=static function(array $result,int $created):array{
   $pending=$result+['created_at'=>$created];
   for($i=0;$i<60;$i++){usleep(250000);$v=App\Services\WhitelistLiveService::verifyPending(7,$pending);if(!empty($v['confirmed']))return $v;if(($v['state']??'')==='failed')break;}
   throw new RuntimeException('Plugin did not confirm website action');
  };
  try {
   $created=time();$addition=App\Services\WhitelistLiveService::addOne(7,$fixture);$added=true;
   $addResult=$wait($addition,$created);
   $created=time();$removal=App\Services\WhitelistLiveService::removeMany(7,[$fixture]);$removeResult=$wait($removal,$created);
   $after=App\Services\WhitelistLiveService::state(7)['steam_ids'];sort($after,SORT_STRING);
   if($before!==$after)throw new RuntimeException('Original membership changed');
   $cleanupConfirmed=true;
   $report+=['addConfirmed'=>$addResult['confirmed'],'removeConfirmed'=>$removeResult['confirmed'],'originalMembershipPreserved'=>true,'temporaryEntryRemoved'=>true,'finalEntries'=>count($after),'legacyFileFallbackUsed'=>$removal['bridge_sync']??null];
  }finally{
   if($added&&!$cleanupConfirmed){
    $cid=App\Services\LiveServerService::queue(7,'server_command',['command'=>'sw_lplk_wl_remove '.$fixture]);
    for($i=0;$i<40;$i++){usleep(250000);$result=App\Services\LiveServerService::result(7,$cid);$current=App\Services\WhitelistLiveService::state(7);if(!empty($result['success'])&&!empty($current['editable'])&&!in_array($fixture,$current['steam_ids'],true)){$cleanupConfirmed=true;break;}}
   }
  }
 }
 $_SESSION=[];ob_end_clean();echo json_encode($report,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES),"\n";
}catch(Throwable $e){$_SESSION=[];if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Whitelist website verification failed','reason'=>$e instanceof RuntimeException?$e->getMessage():'framework error','type'=>get_class($e),'testEntryWasQueued'=>$added,'cleanupConfirmed'=>$cleanupConfirmed]),"\n";exit(1);}
