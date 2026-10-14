<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();$stage='identity';
try{
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
 if($server->uuid!=='11110004-0000-4000-8000-000000000004')throw new RuntimeException('Scope');
 $root='/var/lib/pterodactyl/volumes/'.$server->uuid;$swift=$root.'/game/csgo/addons/swiftlys2';
 $path=$swift.'/configs/plugins/LPLK_Sounds/config.json';$wl=$swift.'/plugins/LPLK_Whitelist/whitelist.txt';
 if(realpath($path)!==$path||realpath($wl)!==$wl)throw new RuntimeException('Unsafe path');
 if(trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"))!=='exited')throw new RuntimeException('Stop first');
 $enabled=__LPLK_SOUNDS_ENABLED__;$before=file_get_contents($path);$wlHash=hash_file('sha256',$wl);
 $stage='validate';$config=json_decode($before,true,512,JSON_THROW_ON_ERROR);
 if(!is_array($config)||!is_bool($config['Enabled']??null)||!is_bool($enabled))throw new RuntimeException('Unexpected config');
 // Preserve formatting and all unrelated settings; reject ambiguous duplicate switches.
 $after=preg_replace('/("Enabled"\s*:\s*)(true|false)/','$1'.($enabled?'true':'false'),$before,-1,$count);
 if($count!==1)throw new RuntimeException('Ambiguous switch');
 $expected=$config;$expected['Enabled']=$enabled;
 if(json_decode($after,true,512,JSON_THROW_ON_ERROR)!==$expected)throw new RuntimeException('Unexpected change');
 $backup=$root.'/.lplk-rollback';if(!is_dir($backup)&&!mkdir($backup,0700))throw new RuntimeException('Backup directory');
 if(realpath($backup)!==$backup||is_link($backup.'/sounds-config.before-diagnostic.json'))throw new RuntimeException('Unsafe backup');
 if(!file_exists($backup.'/sounds-config.before-diagnostic.json')){if(!copy($path,$backup.'/sounds-config.before-diagnostic.json')||!chmod($backup.'/sounds-config.before-diagnostic.json',0600))throw new RuntimeException('Backup failed');}
 if($before!==$after){
  $stage='write';$tmp=$path.'.lplk-enabled-tmp';if(file_exists($tmp)||is_link($tmp))throw new RuntimeException('Unexpected temporary file');
  $f=fopen($tmp,'x');if(!$f)throw new RuntimeException('Write');try{if(fwrite($f,$after)!==strlen($after))throw new RuntimeException('Incomplete write');}finally{fclose($f);}
  if(!chown($tmp,fileowner($path))||!chgrp($tmp,filegroup($path))||!chmod($tmp,fileperms($path)&0777)||!rename($tmp,$path))throw new RuntimeException('Replace');
 }
 $stage='verify';if(!hash_equals(hash('sha256',$after),hash_file('sha256',$path))||!hash_equals($wlHash,hash_file('sha256',$wl)))throw new RuntimeException('Verification');
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'server'=>'11110004','soundsEnabled'=>$enabled,'pluginRetained'=>true,'whitelistPreserved'=>true,'productionChanged'=>false],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Test sound switch incomplete','stage'=>$stage,'type'=>get_class($e)]),"\n";exit(1);}
