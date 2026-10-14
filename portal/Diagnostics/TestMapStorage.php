<?php
// Provision sufficient bounded storage for the requested complete test collection. No map-limit adoption.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
    require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
    if($server->uuid!=='11110004-0000-4000-8000-000000000004'||$server->node_id!==2)throw new RuntimeException('Scope mismatch');
    if(trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"))!=='exited')throw new RuntimeException('Stop test server first');
    $root='/var/lib/pterodactyl/volumes/'.$server->uuid;$cfg=$root.'/game/csgo/addons/swiftlys2/configs/plugins/LPLK_Maps/config.jsonc';
    if(realpath($cfg)!==$cfg||is_link($cfg)||filesize($cfg)>1024*1024)throw new RuntimeException('Config path mismatch');
    $data=json_decode(file_get_contents($cfg),true,512,JSON_THROW_ON_ERROR);
    if(empty($data['LPLK_Maps']['MeasurementOnly'])||($data['LPLK_Maps']['CollectionId']??'')!=='3500873770')throw new RuntimeException('Measurement-only collection required');
    $items=json_decode(file_get_contents($root.'/game/csgo/addons/swiftlys2/data/LPLK_Maps/workshop-cache.json'),true,512,JSON_THROW_ON_ERROR);
    $bytes=array_sum(array_column($items,'Bytes'));$free=disk_free_space($root);
    if($bytes>80*1024*1024*1024||$free<$bytes+20*1024*1024*1024)throw new RuntimeException('Complete pool plus storage reserve not available');
    $previous=$server->disk;if($previous>163840)throw new RuntimeException('Unexpected existing quota');
    $server->disk=163840;$server->save();
    try{$app->make(Pterodactyl\Repositories\Wings\DaemonServerRepository::class)->setServer($server)->sync();}
    catch(Throwable $e){$server->disk=$previous;$server->save();throw $e;}
    $data['LPLK_Maps']['MaxWorkshopGiB']=80;$data['LPLK_Maps']['MinimumFreeDiskGiB']=20;
    $tmp=$cfg.'.'.bin2hex(random_bytes(6)).'.tmp';
    file_put_contents($tmp,json_encode($data,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n",LOCK_EX);
    chown($tmp,fileowner($cfg));chgrp($tmp,filegroup($cfg));chmod($tmp,fileperms($cfg)&0777);rename($tmp,$cfg);
    ob_end_clean();echo json_encode(['testServer'=>'11110004','diskMiB'=>$server->disk,'workshopGiB'=>80,'freeReserveGiB'=>20,'expectedBytes'=>$bytes,'freeBytesBefore'=>$free,'mapLimitsAdopted'=>false]),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Test-Mapspeicher nicht bestätigt','type'=>get_class($e)]),"\n";exit(1);}
