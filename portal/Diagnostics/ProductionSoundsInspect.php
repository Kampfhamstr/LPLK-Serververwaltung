<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $db=$app->make('db')->connection();$report=[];
 $json=static function($path) {
  if(!is_file($path))return null;if(is_link($path)||realpath($path)!==$path)throw new RuntimeException('Linked file');
  $raw=file_get_contents($path);$parsed=json_decode($raw,true,64);if(json_last_error()===JSON_ERROR_NONE)return $parsed;
  // Preserve quoted strings (including URLs) while removing JSONC comments/trailing commas.
  $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|/\*.*?\*/|//[^\r\n]*~s',static fn($m)=>$m[1]??'', $raw);
  $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|,(\s*[}\]])~s',static fn($m)=>!empty($m[1])?$m[1]:$m[2],$raw);
  try{return json_decode($raw,true,64,JSON_THROW_ON_ERROR);}catch(JsonException $e){throw new RuntimeException('JSON parsing: '.basename($path));}
 };
 foreach(['11110001'=>27015,'11110002'=>27016,'11110006'=>27017,'11110007'=>27018,'11110003'=>27019,'11110005'=>27021] as $id=>$port) {
  $s=$db->table('servers')->where('uuidShort',$id)->first(['id','uuid','name','egg_id','allocation_id','image']);if(!$s)throw new RuntimeException('Missing server');
  $a=$db->table('allocations')->where('id',$s->allocation_id)->first(['ip','port']);if($a->ip!=='203.0.113.10'||$a->port!==$port)throw new RuntimeException('Allocation changed');
  $root='/var/lib/pterodactyl/volumes/'.$s->uuid;$swift=$root.'/game/csgo/addons/swiftlys2';if(realpath($swift)!==$swift)throw new RuntimeException('Unsafe root');
  $bridge=$json($swift.'/data/serververwaltung_bridge/status.json');$deps=$json($swift.'/bin/managed/SwiftlyS2.CS2.deps.json');
  $config=$json($swift.'/plugins/DeathEffects/deatheffects.json');$offset=$json($swift.'/plugins/AddonsManager/resources/gamedata/offsets.jsonc');$addons=$json($swift.'/configs/plugins/AddonsManager/config.jsonc');
  $prefs=$json($swift.'/data/DeathEffects/sound-listening.json');$steam=file_get_contents($root.'/game/csgo/steam.inf');preg_match('/ServerVersion=(\d+)/',$steam,$sv);
  $files=[];foreach(['plugins/DeathEffects/DeathEffects.dll','plugins/DeathEffects/deatheffects.json','plugins/DeathEffects/catalog.json','data/DeathEffects/sound-listening.json','configs/plugins/AddonsManager/config.jsonc','plugins/AddonsManager/AddonsManager.dll','configs/plugins/T3_Admin/config.json'] as $rel){$p=$swift.'/'.$rel;$files[$rel]=is_file($p)?['bytes'=>filesize($p),'sha256'=>hash_file('sha256',$p)]:null;}
  $report[]=['id'=>$id,'uuid'=>$s->uuid,'name'=>$s->name,'ip'=>$a->ip,'port'=>$port,'egg'=>$db->table('eggs')->where('id',$s->egg_id)->value('name'),'eggId'=>$s->egg_id,'image'=>$s->image,'state'=>trim((string)shell_exec('docker inspect --format '.escapeshellarg('{{.State.Status}}').' '.escapeshellarg($s->uuid))),
   'bridgeAge'=>isset($bridge['updated_at'])?time()-strtotime($bridge['updated_at']):null,'humans'=>isset($bridge['players'])?count(array_filter($bridge['players'],static fn($p)=>($p['is_bot']??false)!==true)):null,'map'=>$bridge['map']??null,
   'plugins'=>array_map(static fn($p)=>array_intersect_key($p,array_flip(['id','version','status'])),$bridge['plugins']??[]),'swiftly'=>array_values(array_filter(array_keys($deps['libraries']??[]),fn($x)=>str_starts_with($x,'SwiftlyS2.CS2/'))),'cs2Version'=>$sv[1]??null,
   'soundConfig'=>$config,'addonsConfig'=>$addons,'offset'=>$offset,'preferenceCount'=>is_array($prefs)?count($prefs):0,'preferenceFields'=>is_array($prefs)?array_values(array_unique(array_merge(...array_map(static fn($p)=>is_array($p)?array_keys($p):['legacyBoolean'],array_values($prefs))))):[],
   'files'=>$files,'sounds'=>$json($swift.'/data/LPLK_Sounds/status.json')];
 }
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'servers'=>$report],JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Sound-Bestandsprüfung fehlgeschlagen','type'=>get_class($e),'line'=>$e->getLine()]),"\n";exit(1);}
