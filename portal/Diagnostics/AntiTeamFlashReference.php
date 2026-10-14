<?php
// Fixed read-only reference; never return credentials, players or complete configs.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $uuid='11110003-0000-4000-8000-000000000003';
 $server=Pterodactyl\Models\Server::query()->where('uuid',$uuid)->firstOrFail();
 $allocation=$app->make('db')->table('allocations')->where('id',$server->allocation_id)->first(['ip','port']);
 if($allocation->ip!=='203.0.113.10'||$allocation->port!==27019)throw new RuntimeException('Identity mismatch');
 $root='/var/lib/pterodactyl/volumes/'.$uuid.'/game/csgo/addons/swiftlys2';
 $config=$root.'/configs/plugins/AntiTeamFlash/config.jsonc';$dll=$root.'/plugins/AntiTeamFlash/AntiTeamFlash.dll';
 foreach([$root,$config,$dll] as $path)if(realpath($path)!==$path||is_link($path))throw new RuntimeException('Unsafe reference path');
 $text=file_get_contents($config);$json=json_decode($text,true,512,JSON_THROW_ON_ERROR);
 $main=$json['Main']??null;
 if(!is_array($main)||!is_bool($main['Enabled']??null)||!is_bool($main['FlashOwner']??null)||!is_string($main['AccessPermission']??null))throw new RuntimeException('Unknown settings');
 $bridge=json_decode(file_get_contents($root.'/data/serververwaltung_bridge/status.json'),true,512,JSON_THROW_ON_ERROR);
 $plugins=array_values(array_filter($bridge['plugins']??[],static fn($p)=>($p['id']??'')==='AntiTeamFlash'));
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'server'=>'11110003','readOnly'=>true,'productionChanged'=>false,'settings'=>array_intersect_key($main,array_flip(['Enabled','FlashOwner','AccessPermission'])),'plugins'=>$plugins,'configSha256'=>hash('sha256',$text),'dllSha256'=>hash_file('sha256',$dll),'binarySourceIdentityConfirmed'=>false],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'AntiTeamFlash reference failed','type'=>get_class($e)]),"\n";exit(1);}
