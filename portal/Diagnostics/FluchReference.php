<?php
// Read-only, fixed production reference; only known Fluch settings are exported.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $uuid='11110003-0000-4000-8000-000000000003';$s=Pterodactyl\Models\Server::query()->where('uuid',$uuid)->firstOrFail();
 $a=$app->make('db')->table('allocations')->where('id',$s->allocation_id)->first(['ip','port']);
 if($a->ip!=='203.0.113.10'||$a->port!==27019)throw new RuntimeException('Identity mismatch');
 $root='/var/lib/pterodactyl/volumes/'.$uuid.'/game/csgo/addons/swiftlys2';
 $candidates=['configs/plugins/kampfhamster_fluch/config.jsonc','configs/plugins/KampfhamsterFluch/config.jsonc'];
 $found=array_values(array_filter($candidates,static fn($p)=>is_file($root.'/'.$p)));if(count($found)!==1)throw new RuntimeException('Ambiguous config');
 $paths=['config'=>$found[0],'dll'=>'plugins/KampfhamsterFluch/KampfhamsterFluch.dll','bridge'=>'data/serververwaltung_bridge/status.json'];$hash=[];
 foreach($paths as $key=>$relative){$p=$root.'/'.$relative;if(is_link($p)||realpath($p)!==$p)throw new RuntimeException('Missing/linked '.$key);$hash[$key]=hash_file('sha256',$p);}
 $raw=file_get_contents($root.'/'.$paths['config']);
 $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|/\*.*?\*/|//[^\r\n]*~s',static fn($m)=>$m[1]??'',$raw);
 $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|,(\s*[}\]])~s',static fn($m)=>!empty($m[1])?$m[1]:$m[2],$raw);
 $j=json_decode($raw,true,64,JSON_THROW_ON_ERROR);$c=$j['Fluch']??null;if(!is_array($c))throw new RuntimeException('Missing Fluch section');
 $b=json_decode(file_get_contents($root.'/'.$paths['bridge']),true,64,JSON_THROW_ON_ERROR);
 $plugins=array_values(array_map(static fn($p)=>array_intersect_key($p,array_flip(['id','version','status'])),array_filter($b['plugins']??[],static fn($p)=>stripos($p['id']??'','fluch')!==false)));
 $keys=['AdminPermission','RegisterRawCommands','Commands','AllowSelfTarget','NotifyTargetInChat','ChickenCount','OrbitDurationSeconds','OrbitTickSeconds','HeadOrbitHeight','HeadOrbitRadius','OrbitSpeed','OrbitWaveHeight','OrbitWaveSpeed','ChickenSpawnHeight','ChickenExplosionDamagePerChicken','DespawnChickensAfterExplosion','ShowStartCenterMessage','ShowExplosionCenterMessage','ShowOrbitProgressMessage','StartCenterMessageMs','ExplosionCenterMessageMs','OrbitProgressMessageMs','MenuAutoCloseSeconds'];
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'server'=>'11110003','readOnly'=>true,'productionChanged'=>false,'settings'=>array_intersect_key($c,array_flip($keys)),'configPath'=>$paths['config'],'plugins'=>$plugins,'sha256'=>$hash],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Fluch reference failed','type'=>get_class($e),'reason'=>$e->getMessage()]),"\n";exit(1);}
