<?php
// Fixed, reviewable recovery; mutation phases are invoked only after maintenance approval.
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
function recoverySafe(string $p):void {if(is_link($p)||(file_exists($p)&&realpath($p)!==$p))throw new RuntimeException('Unsafe path');}
function recoveryHashes(string $p):array {
 recoverySafe($p); $out=[];
 foreach(new RecursiveIteratorIterator(new RecursiveDirectoryIterator($p,FilesystemIterator::SKIP_DOTS)) as $f){
  recoverySafe($f->getPathname());if($f->isFile())$out[substr($f->getPathname(),strlen($p)+1)]=hash_file('sha256',$f->getPathname());
 }ksort($out);return $out;
}
try {
 $phase='__LPLK_RECOVERY_PHASE__';
 if(!in_array($phase,['check','prepare','apply','start-check','rollback'],true))throw new RuntimeException('Phase');
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();$db=$app->make('db');
 $uuid='11110003-0000-4000-8000-000000000003';$root='/var/lib/pterodactyl/volumes/'.$uuid;$swift=$root.'/game/csgo/addons/swiftlys2';
 $stage=$root.'/.lplk-framework-recovery';$bin=$swift.'/bin';$game=$swift.'/gamedata';$versions=$root.'/egg/versions.txt';
 foreach([$root,$swift,$stage,$bin,$game,$versions] as $p)recoverySafe($p);
 $s=$db->table('servers')->where('uuid',$uuid)->first(['id','allocation_id']);if(!$s)throw new RuntimeException('Server');
 $a=$db->table('allocations')->where('id',$s->allocation_id)->first(['ip','port']);if($a->ip!=='203.0.113.10'||(int)$a->port!==27019)throw new RuntimeException('Identity');
 $flags=$db->table('server_variables as sv')->join('egg_variables as ev','ev.id','=','sv.variable_id')->where('sv.server_id',$s->id)->whereIn('ev.env_variable',['PRERELEASE','INSTALL_SWIFTLY'])->pluck('sv.variable_value','ev.env_variable')->all();
 if(($flags['PRERELEASE']??null)!=='0'||($flags['INSTALL_SWIFTLY']??null)!=='1')throw new RuntimeException('Startup flags changed');
 $state=trim((string)shell_exec('timeout 5 docker inspect --format '.escapeshellarg('{{.State.Status}}').' '.$uuid.' 2>/dev/null'));
 $native=hash_file('sha256',$bin.'/linuxsteamrt64/swiftlys2.so');
 $deps=json_decode(file_get_contents($bin.'/managed/SwiftlyS2.CS2.deps.json'),true,64,JSON_THROW_ON_ERROR);
 $managed=array_values(array_filter(array_keys($deps['libraries']??[]),fn($x)=>str_starts_with($x,'SwiftlyS2.CS2/')));
 $bridge=json_decode(file_get_contents($swift.'/data/serververwaltung_bridge/status.json'),true,64,JSON_THROW_ON_ERROR);
 $age=time()-strtotime($bridge['updated_at']??'1970-01-01');$humans=isset($bridge['players'])?count(array_filter($bridge['players'],fn($p)=>!($p['is_bot']??false))):null;
 $loaded=array_values(array_map(fn($p)=>$p['id'],array_filter($bridge['plugins']??[],fn($p)=>($p['status']??null)==='Loaded')));
 $text=file_get_contents($versions);if(strlen($text)>16384||preg_match_all('/^Swiftly=([^\r\n]+)$/m',$text,$matches)!==1)throw new RuntimeException('Version marker');
 $out=['server'=>'11110003','phase'=>$phase,'at'=>date('c'),'readOnly'=>in_array($phase,['check','start-check'],true),'state'=>$state,'humans'=>$humans,'bridgeAgeSeconds'=>$age,'nativeSha256'=>$native,'managed'=>$managed,'versionMarker'=>$matches[1][0],'loadedPlugins'=>count($loaded),'freeBytes'=>disk_free_space($root),'target'=>'1.4.14'];
 if(in_array($phase,['check','prepare','apply'],true)&&$native!=='c51c4a7962fee5efabbaff2789c247cf64897d5d69e22f30bc98b53d1cc34594')throw new RuntimeException('Installed framework changed; inspect again');
 if(in_array($phase,['prepare','apply'],true)&&disk_free_space($root)<1073741824)throw new RuntimeException('Space reserve');
 if($phase==='prepare'){
  if(file_exists($stage))throw new RuntimeException('Recovery already staged');
  if(!mkdir($stage,0700))throw new RuntimeException('Stage');
  file_put_contents($stage.'/prepared.json',json_encode($out+['previousPlugins'=>$loaded],JSON_THROW_ON_ERROR),LOCK_EX);
  $out['stage']=$stage;
 }
 if(in_array($phase,['apply','rollback','start-check'],true)&&$state!=='exited')throw new RuntimeException('Regular stopped server required');
 if($phase==='apply'){
  foreach(['prepared.json','package.zip'] as $name){recoverySafe($stage.'/'.$name);if(!is_file($stage.'/'.$name))throw new RuntimeException('Missing stage');}
  foreach(['new','old-bin','old-gamedata','applied.json'] as $name)if(file_exists($stage.'/'.$name)||is_link($stage.'/'.$name))throw new RuntimeException('Occupied recovery');
  if(hash_file('sha256',$stage.'/package.zip')!=='d3b9a459f5a5dc984348b6033882f05c07ba62c90b4caa1cdcaa85ac46a865cc')throw new RuntimeException('Package checksum');
  $before=['bin'=>recoveryHashes($bin),'gamedata'=>recoveryHashes($game)];
  $protected=[];foreach(['configs','plugins','data','translations'] as $name)if(is_dir($swift.'/'.$name))$protected[$name]=recoveryHashes($swift.'/'.$name);
  $z=new ZipArchive();if($z->open($stage.'/package.zip')!==true)throw new RuntimeException('ZIP');
  $prefix='swiftlys2-linux-v1.4.14-with-runtimes/addons/swiftlys2/';$expected=[];
  for($i=0;$i<$z->numFiles;$i++){
   $n=$z->getNameIndex($i);if(!str_starts_with($n,$prefix)||str_ends_with($n,'/'))continue;$rel=substr($n,strlen($prefix));
   if(!str_starts_with($rel,'bin/')&&!str_starts_with($rel,'gamedata/'))continue;
   if(str_contains($rel,'..')||!preg_match('~^[a-zA-Z0-9_./+-]+$~D',$rel))throw new RuntimeException('Archive path');
   $dest=$stage.'/new/'.$rel;if(!is_dir(dirname($dest))&&!mkdir(dirname($dest),0755,true))throw new RuntimeException('Extraction directory');
   $bytes=$z->getFromIndex($i);if($bytes===false||file_put_contents($dest,$bytes)!==strlen($bytes))throw new RuntimeException('Extraction');
   $expected[$rel]=hash('sha256',$bytes);chmod($dest,str_ends_with($rel,'.so')||basename($rel)==='dotnet'?0755:0644);
  }$z->close();
  foreach(['bin/linuxsteamrt64/swiftlys2.so','bin/managed/SwiftlyS2.CS2.deps.json','gamedata/cs2/core/signatures.jsonc'] as $key)if(!isset($expected[$key]))throw new RuntimeException('Incomplete package');
  $d=json_decode(file_get_contents($stage.'/new/bin/managed/SwiftlyS2.CS2.deps.json'),true,64,JSON_THROW_ON_ERROR);if(!isset($d['libraries']['SwiftlyS2.CS2/1.4.14']))throw new RuntimeException('Package version');
  $owner=fileowner($bin);$group=filegroup($bin);
  foreach(new RecursiveIteratorIterator(new RecursiveDirectoryIterator($stage.'/new',FilesystemIterator::SKIP_DOTS),RecursiveIteratorIterator::SELF_FIRST) as $f){if(!chown($f->getPathname(),$owner)||!chgrp($f->getPathname(),$group))throw new RuntimeException('Ownership');}
  if(!copy($versions,$stage.'/versions.before'))throw new RuntimeException('Marker backup');
  $manifest=$out+['before'=>$before,'protected'=>$protected,'files'=>$expected,'markerBeforeSha256'=>hash('sha256',$text)];
  file_put_contents($stage.'/manifest.json',json_encode($manifest,JSON_THROW_ON_ERROR),LOCK_EX);
  try {
   if(!rename($bin,$stage.'/old-bin')||!rename($stage.'/new/bin',$bin)||!rename($game,$stage.'/old-gamedata')||!rename($stage.'/new/gamedata',$game))throw new RuntimeException('Framework swap');
   $new=preg_replace('/^Swiftly=[^\r\n]+$/m','Swiftly=v1.4.14',$text);
   if(file_put_contents($versions,$new,LOCK_EX)!==strlen($new))throw new RuntimeException('Marker write');
   foreach($expected as $rel=>$hash)if(hash_file('sha256',$swift.'/'.$rel)!==$hash)throw new RuntimeException('Installed hash');
   foreach($protected as $name=>$hashes)if(recoveryHashes($swift.'/'.$name)!==$hashes)throw new RuntimeException('Protected content changed');
   if(recoveryHashes($stage.'/old-bin')!==$before['bin']||recoveryHashes($stage.'/old-gamedata')!==$before['gamedata'])throw new RuntimeException('Backup hash');
   file_put_contents($stage.'/applied.json',json_encode(['at'=>date('c'),'target'=>'1.4.14','protectedVerified'=>true],JSON_THROW_ON_ERROR),LOCK_EX);
   $out['applied']=true;$out['backup']=$stage;
  }catch(Throwable $e){
   foreach(['bin','gamedata'] as $name)if(is_dir($stage.'/old-'.$name)){if(is_dir($swift.'/'.$name))rename($swift.'/'.$name,$stage.'/failed-'.$name);rename($stage.'/old-'.$name,$swift.'/'.$name);}
   copy($stage.'/versions.before',$versions);throw $e;
  }
 }
 if(in_array($phase,['rollback','start-check'],true)){
  recoverySafe($stage.'/manifest.json');$m=json_decode(file_get_contents($stage.'/manifest.json'),true,64,JSON_THROW_ON_ERROR);
  foreach(['bin','gamedata'] as $name)if(recoveryHashes($stage.'/old-'.$name)!==$m['before'][$name])throw new RuntimeException('Backup changed');
  if($phase==='rollback'){
   if(hash_file('sha256',$stage.'/versions.before')!==$m['markerBeforeSha256'])throw new RuntimeException('Marker backup changed');
   foreach(['bin','gamedata'] as $name){if(file_exists($stage.'/rejected-'.$name))throw new RuntimeException('Rollback already attempted');if(!rename($swift.'/'.$name,$stage.'/rejected-'.$name)||!rename($stage.'/old-'.$name,$swift.'/'.$name))throw new RuntimeException('Rollback swap');}
   if(!copy($stage.'/versions.before',$versions))throw new RuntimeException('Marker restore');$out['rolledBack']=true;
  }else{
   foreach($m['files'] as $rel=>$hash)if(hash_file('sha256',$swift.'/'.$rel)!==$hash)throw new RuntimeException('Installed file changed');
   foreach($m['protected'] as $name=>$hashes)if(recoveryHashes($swift.'/'.$name)!==$hashes)throw new RuntimeException('Protected content changed');
   $out['readyToStart']=true;
  }
 }
 ob_end_clean();echo json_encode($out,JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Inner Circle recovery stopped','reason'=>$e->getMessage(),'line'=>$e->getLine()]),"\n";exit(1);}
