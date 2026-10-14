<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();$stage='identity';
try {
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
 if($server->uuid!=='11110004-0000-4000-8000-000000000004')throw new RuntimeException('Scope mismatch');
 $root='/var/lib/pterodactyl/volumes/'.$server->uuid;$swift=$root.'/game/csgo/addons/swiftlys2';$plugins=$swift.'/plugins';$hold=$root.'/.lplk-connection-isolation';
 foreach([$root,$swift,$plugins] as $path)if(realpath($path)!==$path)throw new RuntimeException('Unsafe root');
 $phase='__LPLK_ISOLATION_PHASE__';if(!in_array($phase,['check','finish-stop','isolate','restore-workshop','restore'],true))throw new RuntimeException('Invalid phase');
 $wlPath=$swift.'/plugins/LPLK_Whitelist/whitelist.txt';$wlHash=hash_file('sha256',$wlPath);
 $wl=json_decode(file_get_contents($swift.'/data/LPLK_Whitelist/status.json'),true,512,JSON_THROW_ON_ERROR);
 if(($wl['Enabled']??false)!==true||($wl['Mode']??0)!==1||($wl['entries']??0)<1)throw new RuntimeException('Whitelist state');
 $bridge=json_decode(file_get_contents($swift.'/data/serververwaltung_bridge/status.json'),true,512,JSON_THROW_ON_ERROR);
 $humans=isset($bridge['players'])?count(array_filter($bridge['players'],static fn($p)=>($p['is_bot']??false)!==true)):null;
 $container=trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"));
 $stage='empty';
 if($phase==='check'){
  $age=time()-strtotime($bridge['updated_at']??'1970-01-01');
  if($container!=='running'||$age<0||$age>10||$humans!==0)throw new RuntimeException('Fresh empty server required');
 }elseif($phase==='finish-stop'){
  $age=time()-strtotime($bridge['updated_at']??'1970-01-01');
  $tail=(string)shell_exec('docker logs --since 10m --tail 120 11110004-0000-4000-8000-000000000004 2>&1');
  if($container!=='running'||$age<120||$age>600||$humans!==0||!str_contains($tail,'HostStateRequest::Start(HSR_QUIT)')||!str_contains($tail,'ss_active -> ss_dead'))throw new RuntimeException('Confirmed stale shutdown required');
  $stage='finish-stop';exec('docker stop --time 10 11110004-0000-4000-8000-000000000004 2>/dev/null',$ignored,$code);
  $container=trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"));
  if($code!==0||$container!=='exited')throw new RuntimeException('Stop incomplete');
 }else{
  if($container!=='exited')throw new RuntimeException('Stopped test server required');
  if(!is_dir($hold)&&!mkdir($hold,0700))throw new RuntimeException('Hold directory');
  if(realpath($hold)!==$hold)throw new RuntimeException('Unsafe hold');
  $stage='paths';$moves=[];
  foreach(['AddonsManager','LPLK_Sounds'] as $id){
   $activate=$phase==='restore'||($phase==='restore-workshop'&&$id==='AddonsManager');
   $from=($activate?$hold:$plugins).'/'.$id;$to=($activate?$plugins:$hold).'/'.$id;
   if(is_link($from)||is_link($to)||is_dir($from)===is_dir($to))throw new RuntimeException('Unexpected isolation state');
   if(is_dir($to)){if(realpath($to)!==$to)throw new RuntimeException('Linked target');continue;}
   if(file_exists($to)||realpath($from)!==$from)throw new RuntimeException('Unsafe isolation state');
   foreach(new RecursiveIteratorIterator(new RecursiveDirectoryIterator($from,FilesystemIterator::SKIP_DOTS)) as $entry)if($entry->isLink()||realpath($entry->getPathname())!==$entry->getPathname())throw new RuntimeException('Linked plugin');
   $moves[]=[$from,$to];
  }
  $stage='move';$done=[];
  try{foreach($moves as [$from,$to]){if(!rename($from,$to))throw new RuntimeException('Move failed');$done[]=[$from,$to];}}
  catch(Throwable $e){foreach(array_reverse($done) as [$from,$to])rename($to,$from);throw $e;}
  if(!hash_equals($wlHash,hash_file('sha256',$wlPath)))throw new RuntimeException('Whitelist changed');
  $record=['checkedAt'=>gmdate('c'),'phase'=>$phase,'plugins'=>['AddonsManager','LPLK_Sounds'],'whitelistPreserved'=>true,'productionChanged'=>false];
  file_put_contents($hold.'/state.json',json_encode($record,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n",LOCK_EX);chmod($hold.'/state.json',0600);
 }
 $locations=[];foreach(['AddonsManager','LPLK_Sounds'] as $id)$locations[$id]=is_dir($plugins.'/'.$id)?'active':(is_dir($hold.'/'.$id)?'isolated':'missing');
 ob_end_clean();echo json_encode(['server'=>'11110004','phase'=>$phase,'container'=>$container,'humans'=>$humans,'locations'=>$locations,'whitelistEntries'=>$wl['entries'],'whitelistPreserved'=>true,'gameVersion'=>trim(file_get_contents($root.'/game/csgo/steam.inf')),'productionChanged'=>false],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Test connection isolation incomplete','stage'=>$stage,'type'=>get_class($e)]),"\n";exit(1);}
