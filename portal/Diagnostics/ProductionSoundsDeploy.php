<?php
// Explicitly authorized sound-only migration. Never accepts arbitrary servers, paths or packages.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
function safePath(string $path):void {if(is_link($path)||(file_exists($path)&&realpath($path)!==$path))throw new RuntimeException('Linked path');}
function readJsonc(string $path):array {
 safePath($path);$raw=file_get_contents($path);$j=json_decode($raw,true,64);if(json_last_error()===JSON_ERROR_NONE&&is_array($j))return $j;
 $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|/\*.*?\*/|//[^\r\n]*~s',static fn($m)=>$m[1]??'',$raw);
 $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|,(\s*[}\]])~s',static fn($m)=>!empty($m[1])?$m[1]:$m[2],$raw);
 return json_decode($raw,true,64,JSON_THROW_ON_ERROR);
}
function putOwned(string $path,string $data,int $owner,int $group):void {
 safePath($path);$parent=dirname($path);if(!is_dir($parent)&&!mkdir($parent,0755,true))throw new RuntimeException('mkdir failed');safePath($parent);
 $tmp=$path.'.lplk-new';if(file_exists($tmp)||is_link($tmp))throw new RuntimeException('Temp exists');
 if(file_put_contents($tmp,$data,LOCK_EX)!==strlen($data)||!chown($tmp,$owner)||!chgrp($tmp,$group)||!chmod($tmp,0644)||!rename($tmp,$path))throw new RuntimeException('Write failed');
 chown($parent,$owner);chgrp($parent,$group);
}
function treeHashes(string $path):array {
 safePath($path);$result=[];if(!is_dir($path))return $result;
 foreach(new RecursiveIteratorIterator(new RecursiveDirectoryIterator($path,FilesystemIterator::SKIP_DOTS)) as $f){safePath($f->getPathname());if($f->isFile())$result[substr($f->getPathname(),strlen($path)+1)]=hash_file('sha256',$f->getPathname());}
 ksort($result);return $result;
}
function readVdf(string $file):array {
 safePath($file);$text=file_get_contents($file);if(strlen($text)>2*1024*1024)throw new RuntimeException('VDF too large');
 preg_match_all('~"((?:[^"\\\\]|\\\\.)*)"|([{}])~s',$text,$m,PREG_SET_ORDER);$tokens=[];foreach($m as $t)$tokens[]=isset($t[2])&&$t[2]!==''?$t[2]:stripcslashes($t[1]);
 $i=0;$parse=function(bool $nested=false)use(&$parse,&$tokens,&$i):array{$out=[];while($i<count($tokens)){$key=$tokens[$i++];if($key==='}'){if(!$nested)throw new RuntimeException('VDF bracket');return $out;}if($key==='{'||isset($out[$key])||$i>=count($tokens))throw new RuntimeException('VDF key');$value=$tokens[$i++];$out[$key]=$value==='{'?$parse(true):$value;}if($nested)throw new RuntimeException('VDF unclosed');return $out;};return $parse();
}
function writeVdf(array $data,int $level=0):string {$out='';$indent=str_repeat("\t",$level);$q=fn($x)=>'"'.addcslashes((string)$x,"\\\"").'"';foreach($data as $k=>$v){$out.=$indent.$q($k).(is_array($v)?"\n".$indent."{\n".writeVdf($v,$level+1).$indent."}\n":"\t\t".$q($v)."\n");}return $out;}
function soundDiagnosticLogTail(string $log):string {
 // Only diagnostic text is repaired. State/config/package JSON stays strict.
 $valid=json_decode(json_encode($log,JSON_INVALID_UTF8_SUBSTITUTE|JSON_THROW_ON_ERROR),true,512,JSON_THROW_ON_ERROR);
 return mb_strcut($valid,-4500,null,'UTF-8');
}
try {
 $id='__LPLK_SOUND_SERVER__';$phase='__LPLK_SOUND_PHASE__';
 $scope=['11110001'=>['11110001-0000-4000-8000-000000000001',27015],'11110002'=>['11110002-0000-4000-8000-000000000002',27016],'11110006'=>['11110006-0000-4000-8000-000000000006',27017],'11110007'=>['11110007-0000-4000-8000-000000000007',27018],'11110003'=>['11110003-0000-4000-8000-000000000003',27019],'11110005'=>['11110005-0000-4000-8000-000000000005',27021]];
 if(!isset($scope[$id])||!in_array($phase,['prepare','check','deploy','start-check','status','rollback','seed'],true))throw new RuntimeException('Scope mismatch');
 [$uuid,$port]=$scope[$id];require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();$db=$app->make('db')->connection();
 $s=$db->table('servers')->where('uuid',$uuid)->first(['id','allocation_id','egg_id']);if(!$s)throw new RuntimeException('Missing server');$a=$db->table('allocations')->where('id',$s->allocation_id)->first(['ip','port']);
 if($a->ip!=='203.0.113.10'||$a->port!==$port||!in_array($s->egg_id,[15,20],true))throw new RuntimeException('Server identity changed');
 $root='/var/lib/pterodactyl/volumes/'.$uuid;$swift=$root.'/game/csgo/addons/swiftlys2';$plugins=$swift.'/plugins';$stage=$root.'/.lplk-sounds';$backup=$stage.'/rollback';
 foreach([$root,$swift,$plugins,$stage,$backup] as $p)safePath($p);$owner=fileowner($plugins);$group=filegroup($plugins);
 $state=trim((string)shell_exec('docker inspect --format '.escapeshellarg('{{.State.Status}}').' '.escapeshellarg($uuid)));
 $bridge=readJsonc($swift.'/data/serververwaltung_bridge/status.json');$age=time()-strtotime($bridge['updated_at']??'1970-01-01');
 $humans=isset($bridge['players'])?count(array_filter($bridge['players'],static fn($p)=>($p['is_bot']??false)!==true)):null;
 $result=['checkedAt'=>gmdate('c'),'id'=>$id,'uuid'=>$uuid,'phase'=>$phase,'state'=>$state,'humans'=>$humans,'bridgeAge'=>$age];
 if($phase==='status') {
  $result['plugins']=array_map(static fn($p)=>array_intersect_key($p,array_flip(['id','version','status'])),$bridge['plugins']??[]);
  $status=$swift.'/data/LPLK_Sounds/status.json';$result['sounds']=is_file($status)?readJsonc($status):null;
  $result['workshopStorage']=[];
  foreach(['/steamapps','/game/bin/linuxsteamrt64/steamapps','/steam/steamapps'] as $rel){$p=$root.$rel;$f=$p.'/libraryfolders.vdf';$vpk=$p.'/workshop/content/730/3797047889/3797047889_dir.vpk';$paths=[];if(is_file($f)){safePath($f);preg_match_all('/"path"\s+"([^"]+)"/',file_get_contents($f),$m);$paths=$m[1];}
   $acf=$p.'/workshop/appworkshop_730.acf';$meta=is_file($acf)?readVdf($acf):[];$result['workshopStorage'][]=['relative'=>$rel,'exists'=>is_dir($p),'libraryPaths'=>$paths,'vpkBytes'=>is_file($vpk)?filesize($vpk):null,'itemInstalled'=>$meta['AppWorkshop']['WorkshopItemsInstalled']['3797047889']??null,'itemDetails'=>$meta['AppWorkshop']['WorkshopItemDetails']['3797047889']??null];}
  $log=(string)shell_exec('docker logs --since 5m --tail 2200 '.escapeshellarg($uuid).' 2>&1 | grep -Ei -A 2 "LPLK_Sounds|AddonsManager|UGC|DownloadItem|hibernate_when_empty|Unknown command|(Plugin.*fail|Exception|error.*plugin)|SIGNONSTATE_FULL" | tail -70');
  $log=preg_replace(['/\x1b\[[0-9;]*m/','/\b7656119\d{10}\b/','/\b[0-9a-fA-F]{32}\b/','/\b(?:\d{1,3}\.){3}\d{1,3}:\d+\b/'],['','[steamid]','[secret]','[endpoint]'],$log);$result['log']=soundDiagnosticLogTail($log);
  if(is_file($stage.'/applied.json')){
   $result['deployment']=readJsonc($stage.'/applied.json');$manifest=readJsonc($backup.'/manifest.json');
   if($id==='11110006')$result['sameCollectionErrorBeforeSoundDeployment']=(int)trim((string)shell_exec('docker logs --since 8h --until '.escapeshellarg($result['deployment']['appliedAt']).' '.escapeshellarg($uuid).' 2>&1 | grep -c '.escapeshellarg('Collection was modified; enumeration operation may not execute.')));
   $loaded=array_values(array_map(fn($p)=>$p['id'],array_filter($bridge['plugins']??[],fn($p)=>($p['status']??null)==='Loaded')));
   $result['missingPreviousPlugins']=array_values(array_diff($manifest['originalLoaded'],['DeathEffects'],$loaded));
   $result['oldPluginDisabled']=!file_exists($plugins.'/DeathEffects')&&!in_array('DeathEffects',$loaded,true);
   $result['backupVerified']=true;foreach(['DeathEffects','AddonsManager'] as $name)if(treeHashes($backup.'/'.$name)!==$manifest['plugins'][$name])$result['backupVerified']=false;
   $result['installedFilesVerified']=true;
   foreach($manifest['packages'] as $name=>$hash){$zp=$stage.'/'.$name.'.zip';safePath($zp);if(!hash_equals($hash,hash_file('sha256',$zp)))throw new RuntimeException('Staged package changed');$zip=new ZipArchive();if($zip->open($zp)!==true)throw new RuntimeException('ZIP check failed');
    for($i=0;$i<$zip->numFiles;$i++){$rel=$zip->getNameIndex($i);if(str_ends_with($rel,'/'))continue;if(!str_starts_with($rel,$name.'/')||str_contains($rel,'..')||!preg_match('~^[A-Za-z0-9_./-]+$~D',$rel))throw new RuntimeException('Unsafe check path');$path=$plugins.'/'.$rel;safePath($path);if(!is_file($path)||hash_file('sha256',$path)!==hash('sha256',$zip->getFromIndex($i)))$result['installedFilesVerified']=false;}$zip->close();
   }
   $result['runtimeReady']=$state==='running'&&$age>=0&&$age<=10&&$result['oldPluginDisabled']&&$result['backupVerified']&&$result['installedFilesVerified']&&count($result['missingPreviousPlugins'])===0&&in_array('LPLK_Sounds',$loaded,true)&&($result['sounds']['precached']??0)===88&&($result['sounds']['unavailable']??null)===null;
  }
 } elseif($phase==='seed') {
  if($id!=='11110007'||$state!=='exited'||!is_file($stage.'/applied.json'))throw new RuntimeException('Stopped migrated Surf server required');
  $source='/var/lib/pterodactyl/volumes/11110002-0000-4000-8000-000000000002';$rel='/game/bin/linuxsteamrt64/steamapps/workshop';$from=$source.$rel.'/content/730/3797047889';$to=$root.$rel.'/content/730/3797047889';$acf=$root.$rel.'/appworkshop_730.acf';
  safePath($from);safePath(dirname($to));safePath($to);safePath($acf);
  $sourceStatus=readJsonc($source.'/game/csgo/addons/swiftlys2/data/LPLK_Sounds/status.json');if(($sourceStatus['precached']??0)!==88||($sourceStatus['unavailable']??null)!==null)throw new RuntimeException('Source not confirmed');
  $src=readVdf($source.$rel.'/appworkshop_730.acf');$dst=readVdf($acf);$installed=$src['AppWorkshop']['WorkshopItemsInstalled']['3797047889']??null;$details=$src['AppWorkshop']['WorkshopItemDetails']['3797047889']??null;
  if(!is_array($installed)||!is_array($details)||$installed['manifest']!=='4574545595080573785'||($details['latest_manifest']??null)!==$installed['manifest']||($dst['AppWorkshop']['WorkshopItemDetails']['3797047889']['latest_manifest']??null)!==$installed['manifest'])throw new RuntimeException('Workshop revisions not confirmed equal');
  if(disk_free_space($root)<(int)$installed['size']+1024*1024*1024)throw new RuntimeException('Space reserve missing');
  $hashes=treeHashes($from);$bytes=0;foreach($hashes as $file=>$hash){if($file!=='publish_data.txt'&&!preg_match('~^3797047889(?:_dir|_[0-9]{3})?\.vpk$~D',$file))throw new RuntimeException('Unexpected asset file: '.basename($file));$bytes+=filesize($from.'/'.$file);}
  if($bytes!==(int)$installed['size']||!isset($hashes['3797047889_dir.vpk']))throw new RuntimeException('Source size mismatch');
  $new=$stage.'/sound-asset-new';if(file_exists($new)||file_exists($backup.'/sound-workshop.before.acf'))throw new RuntimeException('Asset seed already attempted');if(!mkdir($new,0755))throw new RuntimeException('Seed mkdir');
  foreach($hashes as $file=>$hash){if(!copy($from.'/'.$file,$new.'/'.$file)||hash_file('sha256',$new.'/'.$file)!==$hash||hash_file('sha256',$from.'/'.$file)!==$hash)throw new RuntimeException('Asset copy mismatch');chown($new.'/'.$file,$owner);chgrp($new.'/'.$file,$group);chmod($new.'/'.$file,0644);}
  if(readVdf($source.$rel.'/appworkshop_730.acf')['AppWorkshop']['WorkshopItemsInstalled']['3797047889']!==$installed)throw new RuntimeException('Source changed during copy');
  if(!copy($acf,$backup.'/sound-workshop.before.acf'))throw new RuntimeException('Manifest backup failed');if(is_dir($to)&&!rename($to,$backup.'/sound-workshop-incomplete'))throw new RuntimeException('Preserve partial asset failed');
  chown($new,$owner);chgrp($new,$group);if(!rename($new,$to))throw new RuntimeException('Asset activate failed');
  $dst['AppWorkshop']['WorkshopItemsInstalled']['3797047889']=$installed;$dst['AppWorkshop']['WorkshopItemDetails']['3797047889']=$details;
  $dst['AppWorkshop']['SizeOnDisk']=(string)array_sum(array_map(fn($i)=>(int)($i['size']??0),$dst['AppWorkshop']['WorkshopItemsInstalled']));
  putOwned($acf,writeVdf($dst),$owner,$group);if(readVdf($acf)!==$dst||treeHashes($to)!==$hashes)throw new RuntimeException('Seed verification failed');
  $result['assetSeed']=['workshopId'=>'3797047889','manifest'=>$installed['manifest'],'source'=>'11110002','bytes'=>$bytes,'files'=>$hashes,'otherWorkshopEntriesPreserved'=>true];
  file_put_contents($stage.'/asset-seed.json',json_encode($result,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),LOCK_EX);
 } elseif($phase==='start-check') {
  if($state!=='exited'||(!is_file($stage.'/applied.json')&&!is_file($stage.'/rolled-back.json')))throw new RuntimeException('Applied stopped state required');
 } elseif($phase==='rollback') {
  if($state!=='exited'||!is_file($backup.'/manifest.json'))throw new RuntimeException('Stopped backup required');$manifest=readJsonc($backup.'/manifest.json');
  foreach(['DeathEffects','AddonsManager'] as $name){if(treeHashes($backup.'/'.$name)!==$manifest['plugins'][$name])throw new RuntimeException('Backup changed');}
  foreach(['LPLK_Sounds','AddonsManager'] as $name){if(is_dir($plugins.'/'.$name)){safePath($plugins.'/'.$name);if(file_exists($stage.'/failed-'.$name))throw new RuntimeException('Failure copy exists');if(!rename($plugins.'/'.$name,$stage.'/failed-'.$name))throw new RuntimeException('Retire failed');}}
  foreach(['DeathEffects','AddonsManager'] as $name)if(!rename($backup.'/'.$name,$plugins.'/'.$name))throw new RuntimeException('Restore failed');
  putOwned($swift.'/configs/plugins/AddonsManager/config.jsonc',file_get_contents($backup.'/addons-config.jsonc'),$owner,$group);
  foreach(['configs/plugins/LPLK_Sounds','data/LPLK_Sounds'] as $rel){if(is_dir($swift.'/'.$rel)){safePath($swift.'/'.$rel);if(!rename($swift.'/'.$rel,$stage.'/failed-'.str_replace('/','-',$rel)))throw new RuntimeException('Retire data failed');}}
  if(is_file($stage.'/applied.json'))rename($stage.'/applied.json',$stage.'/rolled-back.json');$result['restored']=true;
 } elseif($phase==='check'&&is_file($stage.'/applied.json')) {
  if($state!=='running'||$age<0||$age>10||$humans!==0)throw new RuntimeException('Fresh empty server required');
 } else {
  if(in_array($phase,['check','prepare'],true)&&($state!=='running'||$age<0||$age>10||$humans===null))throw new RuntimeException('Fresh running bridge required');
  if($phase==='check'&&$humans!==0)throw new RuntimeException('Occupied: no restart');
  if($phase==='deploy'&&$state!=='exited')throw new RuntimeException('Stop first');
  $deps=readJsonc($swift.'/bin/managed/SwiftlyS2.CS2.deps.json');$versions=array_values(array_filter(array_keys($deps['libraries']??[]),fn($x)=>str_starts_with($x,'SwiftlyS2.CS2/')));
  if(count($versions)!==1||!in_array($versions[0],['SwiftlyS2.CS2/1.4.13','SwiftlyS2.CS2/1.4.14-beta.23','SwiftlyS2.CS2/1.4.14'],true))throw new RuntimeException('API not reviewed');
  if(!preg_match('/^ServerVersion=2000930\s*$/m',file_get_contents($root.'/game/csgo/steam.inf')))throw new RuntimeException('Game version changed: review native offset');
  foreach(['DeathEffects','AddonsManager'] as $name)if(!is_file($plugins.'/'.$name.'/'.$name.'.dll'))throw new RuntimeException('Expected old plugin missing');
  if(file_exists($plugins.'/LPLK_Sounds')||file_exists($swift.'/data/LPLK_Sounds')||file_exists($swift.'/configs/plugins/LPLK_Sounds')||file_exists($backup))throw new RuntimeException('Already migrated or conflicting target');
  $old=readJsonc($plugins.'/DeathEffects/deatheffects.json');$addons=readJsonc($swift.'/configs/plugins/AddonsManager/config.jsonc');
  $cfg=['Enabled'=>$old['Enabled']??true,'AutoCatalogFromAddon'=>$old['AutoCatalogFromAddon']??true,'RequireAddonsManager'=>true,'WorkshopIds'=>array_values(array_unique(array_merge($old['WorkshopIds']??[],['3797047889']))),'BroadcastCooldownSeconds'=>$old['BroadcastCooldownSeconds']??5,'DeadPlayersOnlyToDead'=>$old['DeadPlayersOnlyToDead']??true];
  foreach(['Enabled','AutoCatalogFromAddon','DeadPlayersOnlyToDead'] as $key)if(!is_bool($cfg[$key]))throw new RuntimeException('Unexpected sound setting');
  if(!is_numeric($cfg['BroadcastCooldownSeconds'])||$cfg['BroadcastCooldownSeconds']<1||$cfg['BroadcastCooldownSeconds']>60)throw new RuntimeException('Sound cooldown invalid');
  if(!isset($addons['Main']['Addons'])||!is_array($addons['Main']['Addons']))throw new RuntimeException('Addon config mismatch');
  foreach(array_merge($cfg['WorkshopIds'],$addons['Main']['Addons']) as $wid)if(!is_string($wid)||!preg_match('/^[1-9][0-9]{0,19}$/D',$wid))throw new RuntimeException('Invalid Workshop id');
  $addons['Main']['Addons']=array_values(array_unique(array_merge($addons['Main']['Addons'],$cfg['WorkshopIds'])));
  $prefsPath=$swift.'/data/DeathEffects/sound-listening.json';$prefs=is_file($prefsPath)?readJsonc($prefsPath):[];
  foreach($prefs as $steam=>$v){if(!preg_match('/^7656119[0-9]{10}$/D',(string)$steam))throw new RuntimeException('Preference id invalid');if(is_bool($v))continue;if(!is_array($v)||($v['VolumePercent']??100)<0||($v['VolumePercent']??100)>100)throw new RuntimeException('Preference invalid');}
  $result['migration']=['framework'=>$versions[0],'preferences'=>count($prefs),'soundConfig'=>$cfg,'workshopAddons'=>$addons['Main']['Addons']];
  if($phase==='prepare') {if(!is_dir($stage)&&!mkdir($stage,0700))throw new RuntimeException('Staging failed');$result['staging']=$stage;}
  if($phase==='deploy') {
   // Validate every package completely before changing the loader or active configuration.
   $hashes=json_decode('__LPLK_SOUND_HASHES__',true,16,JSON_THROW_ON_ERROR);$entries=[];
   foreach(['LPLK_Sounds','AddonsManager'] as $name){$zipPath=$stage.'/'.$name.'.zip';safePath($zipPath);if(!hash_equals($hashes[$name]??'',hash_file('sha256',$zipPath)))throw new RuntimeException('Package hash mismatch');$zip=new ZipArchive();if($zip->open($zipPath)!==true)throw new RuntimeException('ZIP failed');
    for($i=0;$i<$zip->numFiles;$i++){$rel=$zip->getNameIndex($i);if(str_ends_with($rel,'/'))continue;if(!str_starts_with($rel,$name.'/')||str_contains($rel,'..')||!preg_match('~^[A-Za-z0-9_./-]+$~D',$rel)||$zip->statIndex($i)['size']>50*1024*1024)throw new RuntimeException('Unsafe entry');$entries[$rel]=$zip->getFromIndex($i);}
    if(!isset($entries[$name.'/'.$name.'.dll']))throw new RuntimeException('DLL missing');$zip->close();
   }
   $offset=json_decode($entries['AddonsManager/resources/gamedata/offsets.jsonc'],true,64,JSON_THROW_ON_ERROR);if(($offset['NetworkGameServer::Addons']['linux']??null)!==376)throw new RuntimeException('Native offset invalid');
   $manifest=['server'=>$uuid,'createdAt'=>gmdate('c'),'plugins'=>[],'preferencesSha256'=>is_file($prefsPath)?hash_file('sha256',$prefsPath):null,'packages'=>$hashes,'originalLoaded'=>array_values(array_map(fn($p)=>$p['id'],array_filter($bridge['plugins']??[],fn($p)=>($p['status']??null)==='Loaded')))];
   foreach(['DeathEffects','AddonsManager'] as $name)$manifest['plugins'][$name]=treeHashes($plugins.'/'.$name);
   if(!mkdir($backup,0700))throw new RuntimeException('Backup mkdir failed');
   if(!copy($swift.'/configs/plugins/AddonsManager/config.jsonc',$backup.'/addons-config.jsonc'))throw new RuntimeException('Config backup failed');
   file_put_contents($backup.'/manifest.json',json_encode($manifest,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),LOCK_EX);
   foreach($entries as $rel=>$bytes)putOwned($stage.'/new/'.$rel,$bytes,$owner,$group);
   foreach(['DeathEffects','AddonsManager'] as $name){if(!rename($plugins.'/'.$name,$backup.'/'.$name)||treeHashes($backup.'/'.$name)!==$manifest['plugins'][$name])throw new RuntimeException('Plugin backup failed');}
   foreach(['LPLK_Sounds','AddonsManager'] as $name){$new=$stage.'/new/'.$name;foreach(new RecursiveIteratorIterator(new RecursiveDirectoryIterator($new,FilesystemIterator::SKIP_DOTS),RecursiveIteratorIterator::SELF_FIRST) as $f){chown($f->getPathname(),$owner);chgrp($f->getPathname(),$group);}chown($new,$owner);chgrp($new,$group);if(!rename($new,$plugins.'/'.$name))throw new RuntimeException('Activate failed');}
   putOwned($swift.'/configs/plugins/LPLK_Sounds/config.json',json_encode($cfg,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n",$owner,$group);
   putOwned($swift.'/configs/plugins/AddonsManager/config.jsonc',json_encode($addons,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n",$owner,$group);
   if(is_file($prefsPath)){putOwned($swift.'/data/LPLK_Sounds/sound-listening.json',file_get_contents($prefsPath),$owner,$group);if(hash_file('sha256',$swift.'/data/LPLK_Sounds/sound-listening.json')!==$manifest['preferencesSha256'])throw new RuntimeException('Preference copy mismatch');}
   $result['backup']=$backup;$result['packages']=$hashes;$result['oldPluginDisabled']=!is_dir($plugins.'/DeathEffects');$result['appliedAt']=gmdate('c');
   file_put_contents($stage.'/applied.json',json_encode($result,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),LOCK_EX);
  }
 }
 ob_end_clean();echo json_encode($result,JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>$e instanceof RuntimeException?$e->getMessage():'Sound migration failed','type'=>get_class($e),'line'=>$e->getLine()]),"\n";exit(1);}
