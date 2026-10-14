<?php
// Read-only evidence for the separate Zombie test profile; never changes production files or process state.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $uuid='11110006-0000-4000-8000-000000000006';
 $server=Pterodactyl\Models\Server::query()->where('uuid',$uuid)->firstOrFail();
 $allocation=$app->make('db')->table('allocations')->where('id',$server->allocation_id)->first(['ip','port']);
 if($allocation->ip!=='203.0.113.10'||$allocation->port!==27017)throw new RuntimeException('Identity mismatch');
 $root='/var/lib/pterodactyl/volumes/'.$uuid.'/game/csgo/addons/swiftlys2';
 if(realpath($root)!==$root)throw new RuntimeException('Linked path');
 $bridge=json_decode(@file_get_contents($root.'/data/serververwaltung_bridge/status.json'),true)??[];
 $plugin=array_values(array_filter($bridge['plugins']??[],static fn($p)=>($p['id']??'')==='HanZombieRiotS2'));
 $files=[];$pluginRoot=$root.'/plugins/HanZombieRiotS2';
 if(is_dir($pluginRoot)&&realpath($pluginRoot)===$pluginRoot)foreach(new DirectoryIterator($pluginRoot) as $file){
  if($file->isFile()&&!$file->isLink()&&preg_match('/\.(dll|pdb|json)$/D',$file->getFilename()))$files[]=['name'=>$file->getFilename(),'bytes'=>$file->getSize(),'sha256'=>hash_file('sha256',$file->getPathname())];
 }
 $log=(string)shell_exec('docker logs --since 2h --tail 15000 11110006-0000-4000-8000-000000000006 2>&1 | grep -E -B 3 -A 24 "Collection was modified|Error loading resource.*(cuddle|marsh)|HanZombieRiotS2.*Exception" | tail -170');
 $log=preg_replace(['/\x1b\[[0-9;]*m/','/\b7656119\d{10}\b/','/\b[0-9a-fA-F]{32}\b/','/\b(?:\d{1,3}\.){3}\d{1,3}:\d+\b/'],['','[steamid]','[secret]','[endpoint]'],$log);
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'server'=>'11110006','readOnly'=>true,'productionChanged'=>false,'bridgeAge'=>isset($bridge['updated_at'])?time()-strtotime($bridge['updated_at']):null,'plugins'=>$plugin,'files'=>$files,'log'=>substr($log,-11000)],JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Zombie read-only diagnosis failed','type'=>get_class($e)]),"\n";exit(1);}
