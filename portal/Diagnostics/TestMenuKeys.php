<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try{
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
 if($server->uuid!=='11110004-0000-4000-8000-000000000004')throw new RuntimeException('Scope');
 $root='/var/lib/pterodactyl/volumes/'.$server->uuid;$path=$root.'/game/csgo/addons/swiftlys2/configs/core.jsonc';
 $source='/var/lib/pterodactyl/volumes/11110003-0000-4000-8000-000000000003/game/csgo/addons/swiftlys2/configs/core.jsonc';
 if(realpath($path)!==$path||realpath($source)!==$source||hash_file('sha256',$path)!=='__TEST_HASH__'||hash_file('sha256',$source)!=='__SOURCE_HASH__')throw new RuntimeException('Configuration changed');
 if(trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"))!=='exited')throw new RuntimeException('Stop first');
 $backup=$root.'/.lplk-rollback';if(!is_dir($backup)&&!mkdir($backup,0700))throw new RuntimeException('Backup directory');
 if(realpath($backup)!==$backup||is_link($backup.'/core-menu.previous.jsonc'))throw new RuntimeException('Backup path');
 if(!file_exists($backup.'/core-menu.previous.jsonc')){if(!copy($path,$backup.'/core-menu.previous.jsonc'))throw new RuntimeException('Backup failed');chmod($backup.'/core-menu.previous.jsonc',0600);}
 $data=base64_decode('__CONTENT__',true);if($data===false||strlen($data)>32768)throw new RuntimeException('Data invalid');
 $tmp=$path.'.lplk-menu-tmp';if(file_exists($tmp)||is_link($tmp))throw new RuntimeException('Unexpected temporary file');
 $f=fopen($tmp,'x');if(!$f)throw new RuntimeException('Write');try{if(fwrite($f,$data)!==strlen($data))throw new RuntimeException('Incomplete write');}finally{fclose($f);}
 if(!chown($tmp,fileowner($path))||!chgrp($tmp,filegroup($path))||!chmod($tmp,fileperms($path)&0777)||!rename($tmp,$path))throw new RuntimeException('Replace');
 if(!hash_equals(hash('sha256',$data),hash_file('sha256',$path)))throw new RuntimeException('Verify');
 ob_end_clean();echo json_encode(['server'=>'11110004','menuKeysCopied'=>true,'productionChanged'=>false]),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Test menu key copy incomplete','type'=>get_class($e)]),"\n";exit(1);}
