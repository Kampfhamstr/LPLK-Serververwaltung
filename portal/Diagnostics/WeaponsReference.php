<?php
// Fixed read-only, field-limited inspection. No player identities or statistics.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
function weaponJson(string $path):array {
 if(is_link($path)||realpath($path)!==$path)throw new RuntimeException('Unsafe reference path');
 $raw=file_get_contents($path);
 $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|/\*.*?\*/|//[^\r\n]*~s',static fn($m)=>$m[1]??'',$raw);
 $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|,(\s*[}\]])~s',static fn($m)=>!empty($m[1])?$m[1]:$m[2],$raw);
 return json_decode($raw,true,64,JSON_THROW_ON_ERROR);
}
try {
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $uuid='11110003-0000-4000-8000-000000000003';$s=Pterodactyl\Models\Server::query()->where('uuid',$uuid)->firstOrFail();
 $a=$app->make('db')->table('allocations')->where('id',$s->allocation_id)->first(['ip','port']);
 if($a->ip!=='203.0.113.10'||$a->port!==27019)throw new RuntimeException('Identity mismatch');
 $root='/var/lib/pterodactyl/volumes/'.$uuid.'/game/csgo/addons/swiftlys2';
 $paths=['config'=>'configs/plugins/headshotonly/config.jsonc','runtime'=>'data/headshotonly/runtime_settings.json','aws'=>'configs/plugins/AdvancedWeaponSystem/config.toml','headshotDll'=>'plugins/HeadshotOnly/HeadshotOnly.dll','awsDll'=>'plugins/cs2-advanced-weapon-system/cs2-advanced-weapon-system.dll'];$hash=[];
 foreach($paths as $key=>$relative){$p=$root.'/'.$relative;if(is_link($p)||realpath($p)!==$p)throw new RuntimeException('Missing/linked '.$key);$hash[$key]=hash_file('sha256',$p);}
 $config=weaponJson($root.'/'.$paths['config'])['HeadshotOnly'];$runtime=weaponJson($root.'/'.$paths['runtime']);
 $fields=['Headshot'=>['DefaultEnabled','FirearmsOnly'],'Weapons'=>['DefaultRestrictionEnabled','DefaultPreset','RestrictUnknownWeapons','ShowRestrictionAlert'],'GoldenRule'=>['Enabled','Weapons','MinimumLeadRounds','EnabledInWarmup','AnnounceAtRoundStart','ShowBlockedShotAlert']];
 $settings=['Enabled'=>$config['Enabled']??null];foreach($fields as $key=>$names)$settings[$key]=array_intersect_key($config[$key]??[],array_flip($names));
 $toml=file_get_contents($root.'/'.$paths['aws']);$structure=[];$other=0;
 foreach(preg_split('/\R/',$toml) as $line){$line=trim($line);if($line===''||str_starts_with($line,'#'))continue;if(preg_match('/^\[(AdvancedWeaponSystem|Main)(\.WeaponDatas)?\]$/D',$line)||preg_match('/^WeaponDatas\s*=\s*\{\s*\}$/D',$line))$structure[]=$line;else $other++;}
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'server'=>'11110003','readOnly'=>true,'productionChanged'=>false,'settings'=>$settings,'runtime'=>array_intersect_key($runtime,array_flip(['HeadshotOnlyEnabled','WeaponRestrictionEnabled','ActivePreset','AllowedWeapons'])),'aws'=>['structure'=>$structure,'unparsedLines'=>$other],'sha256'=>$hash],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Weapons reference failed','type'=>get_class($e),'detail'=>$e instanceof RuntimeException?$e->getMessage():'parse failed']),"\n";exit(1);}
