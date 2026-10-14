<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    require getcwd().'/vendor/autoload.php'; $app=require getcwd().'/bootstrap/app.php';
    $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
    if($server->uuid!=='11110004-0000-4000-8000-000000000004') throw new RuntimeException('Scope mismatch');
    $root='/var/lib/pterodactyl/volumes/'.$server->uuid;
    if(realpath($root)!==$root) throw new RuntimeException('Linked root');
    $state=json_decode(shell_exec("docker inspect --format '{{json .State}}' 11110004-0000-4000-8000-000000000004"),true);
    $bridge=json_decode(file_get_contents($root.'/game/csgo/addons/swiftlys2/data/serververwaltung_bridge/status.json'),true);
    if(empty($state['Running']) || time()-strtotime($bridge['updated_at']??'1970-01-01')>30) throw new RuntimeException('Fresh running state required');
    $loaded=array_filter($bridge['plugins']??[],fn($p)=>($p['id']??'')==='LPLK_Parachute' && ($p['version']??'')==='0.1.1' && ($p['status']??'')==='Loaded');
    if(!$loaded) throw new RuntimeException('Replacement not verified');
    $path=$root.'/game/bin/linuxsteamrt64/core'; $freed=0;
    if(file_exists($path)) {
        if(is_link($path)||realpath($path)!==$path||!is_file($path)||filemtime($path)>=strtotime($state['StartedAt'])) throw new RuntimeException('Not the old dump');
        $f=fopen($path,'rb'); $header=fread($f,20); fclose($f);
        if(substr($header,0,4)!=="\x7fELF"||ord($header[5])!==1||unpack('v',substr($header,16,2))[1]!==4) throw new RuntimeException('Not an ELF core dump');
        $stat=stat($path); $freed=isset($stat['blocks'])?$stat['blocks']*512:filesize($path);
        if(!unlink($path)) throw new RuntimeException('Cleanup failed');
    }
    ob_end_clean(); echo json_encode(['removed'=>'old test-start core dump','bytesFreed'=>$freed,'productionChanged'=>false]),"\n";
} catch(Throwable $e) { if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Testbereinigung nicht bestaetigt','type'=>get_class($e)]),"\n"; exit(1); }
