<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try{
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
 if($server->uuid!=='11110004-0000-4000-8000-000000000004')throw new RuntimeException('Scope');
 $root='/var/lib/pterodactyl/volumes/'.$server->uuid;
 $log=(string)shell_exec('docker logs --since 20m --tail 2500 11110004-0000-4000-8000-000000000004 2>&1 | grep -Ei -B 2 -A 2 "resource|vsnd|sound|manifest|overflow|parse|buffer|error|exception|Addons-Offset" | tail -180');
 $log=preg_replace(['/\x1b\[[0-9;]*m/','/\b[0-9a-fA-F]{32}\b/','/\b7656119\d{10}\b/','/\b(?:\d{1,3}\.){3}\d{1,3}:\d+\b/'],['','[redacted]','[steamid]','[endpoint]'],$log);
 $dir=$root.'/game/bin/linuxsteamrt64/steamapps/workshop/content/730/3797047889';$libraries=[];
 foreach(glob($dir.'/*.vpk')?:[] as $vpk){
  if(realpath($vpk)!==$vpk)throw new RuntimeException('VPK link');$f=fopen($vpk,'rb');if(!$f)continue;
  $head=fread($f,12);if(strlen($head)!==12){fclose($f);continue;}$h=unpack('Vsig/Vversion/VtreeSize',$head);
  if($h['sig']!==0x55aa1234||!in_array($h['version'],[1,2],true)||$h['treeSize']>16*1024*1024){fclose($f);continue;}
  $headerSize=$h['version']===2?28:12;fseek($f,$headerSize);$tree=fread($f,$h['treeSize']);$at=0;
  $str=static function()use(&$tree,&$at){$end=strpos($tree,"\0",$at);if($end===false)throw new RuntimeException('Bad tree');$v=substr($tree,$at,$end-$at);$at=$end+1;return $v;};
  while(($ext=$str())!=='')while(($path=$str())!=='')while(($name=$str())!==''){
   if($at+18>strlen($tree))throw new RuntimeException('Tree truncated');$e=unpack('Vcrc/vpreload/varchive/Voffset/Vlength/vterm',substr($tree,$at,18));$at+=18;
   if($e['term']!==0xffff||$at+$e['preload']>strlen($tree))throw new RuntimeException('Bad entry');$preload=substr($tree,$at,$e['preload']);$at+=$e['preload'];
   if($ext!=='vsndevts_c')continue;$resource=($path===' '?'':$path.'/').$name.'.'.$ext;
   $item=['resource'=>$resource,'vpk'=>basename($vpk),'bytes'=>$e['preload']+$e['length'],'archive'=>$e['archive']];
   if($e['length']>8*1024*1024){$libraries[]=$item;continue;}
   if($e['archive']===0x7fff){fseek($f,$headerSize+$h['treeSize']+$e['offset']);$data=$preload.fread($f,$e['length']);}
   else{$archive=preg_replace('/_dir\.vpk$/',sprintf('_%03d.vpk',$e['archive']),$vpk);if($archive===$vpk||realpath($archive)!==$archive)throw new RuntimeException('Archive unavailable');$af=fopen($archive,'rb');if(!$af)throw new RuntimeException('Archive open');fseek($af,$e['offset']);$data=$preload.fread($af,$e['length']);fclose($af);}
   $item['sha256']=hash('sha256',$data);$item['crcValid']=hash('crc32b',$data)===sprintf('%08x',$e['crc']);
   if(strlen($data)>=16){$r=unpack('Vsize/vheader/vversion/Voffset/Vcount',substr($data,0,16));$item['resourceHeader']=$r;$blocks=[];
    if($r['count']<=20)for($i=0;$i<$r['count'];$i++){$p=8+$r['offset']+$i*12;if($p+12>strlen($data))break;$b=unpack('a4type/Voffset/Vsize',substr($data,$p,12));$b['absolute']=$p+4+$b['offset'];$blocks[]=$b;}$item['blocks']=$blocks;
    foreach($blocks as $b)if($b['type']==='RERL'&&$b['absolute']+$b['size']<=strlen($data)&&$b['size']>=8){$rh=unpack('Voffset/Vcount',substr($data,$b['absolute'],8));$item['dependencyCount']=$rh['count'];$names=[];if($rh['count']<=500)for($j=0;$j<$rh['count'];$j++){$ep=$b['absolute']+$rh['offset']+$j*16;if($ep+16>strlen($data))break;$sp=$ep+8+unpack('Pvalue',substr($data,$ep+8,8))['value'];if($sp<0||$sp>=strlen($data))continue;$end=strpos($data,"\0",$sp);if($end!==false)$names[]=substr($data,$sp,min(256,$end-$sp));}$item['dependencyExamples']=array_slice($names,0,5);}
   }$libraries[]=$item;
  }fclose($f);
 }
 $joinPath=$root.'/game/csgo/addons/swiftlys2/data/LPLK_Sounds/join-diagnostic.json';
 $join=is_file($joinPath)&&realpath($joinPath)===$joinPath&&filesize($joinPath)<4096?json_decode(file_get_contents($joinPath),true):null;
 $offsetPath=$root.'/game/csgo/addons/swiftlys2/plugins/AddonsManager/resources/gamedata/offsets.jsonc';
 $offsets=is_file($offsetPath)&&realpath($offsetPath)===$offsetPath&&filesize($offsetPath)<4096?json_decode(file_get_contents($offsetPath),true):null;
 $addonOffset=$offsets['NetworkGameServer::Addons']??null;
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'installedAddonOffsets'=>$addonOffset,'joinDiagnostic'=>$join,'libraries'=>$libraries,'resourceLog'=>mb_substr($log,-8000)],JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Sound diagnostic incomplete','type'=>get_class($e)]),"\n";exit(1);}
