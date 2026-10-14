<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    require getcwd().'/vendor/autoload.php'; $app=require getcwd().'/bootstrap/app.php';
    $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
    if ($server->uuid!=='11110004-0000-4000-8000-000000000004') throw new RuntimeException('Scope mismatch');
    $state=trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004 2>/dev/null"));
    if ($state!=='exited' && $state!=='created') throw new RuntimeException('Stop test server before configuration');
    // Only this non-secret variable changes. Preserve any token entered concurrently by the owner.
    $app->make('db')->table('server_variables')->where('server_id',$server->id)->where('variable_id',84)->update(['variable_value'=>'']);
    $app->make(Pterodactyl\Repositories\Wings\DaemonServerRepository::class)->setServer($server)->sync();
    $root='/var/lib/pterodactyl/volumes/'.$server->uuid;
    $cfg=$root.'/game/csgo/cfg';
    if (realpath($cfg)!==$cfg || is_link($cfg.'/server.cfg')) throw new RuntimeException('Configuration path not isolated');
    $path=$cfg.'/server.cfg';
    // Only bootstrap once; retain all subsequent operator changes.
    if (!is_file($root.'/.lplk-test-configured')) {
        $prior=is_file($path)?file_get_contents($path):'';
        if (trim($prior)!=='') file_put_contents($cfg.'/server.pre-lplk.cfg',$prior,LOCK_EX);
        $password=bin2hex(random_bytes(12));
        $text="// LPLK isolated test profile: Beispielserver\nhostname \"[TEST][LPLK] Beispielserver\"\nsv_password \"".$password."\"\nsv_lan 0\nbot_quota 0\nsv_cheats 0\nsv_hibernate_when_empty 0\n";
        file_put_contents($path.'.tmp',$text,LOCK_EX); rename($path.'.tmp',$path);
        chown($path,fileowner($cfg)); chgrp($path,filegroup($cfg)); chmod($path,0640);
        file_put_contents($root.'/.lplk-test-configured',"2026-10-11\n",LOCK_EX);
    }
    chown($path,fileowner($cfg)); chgrp($path,filegroup($cfg));
    ob_end_clean(); echo json_encode(['configured'=>true,'hostname'=>'[TEST][LPLK] Beispielserver','passwordProtected'=>true,'passwordLocation'=>'Pterodactyl Files: game/csgo/cfg/server.cfg','customParams'=>'','productionChanged'=>false],JSON_PRETTY_PRINT),"\n";
} catch(Throwable $e) { if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Testserver-Konfiguration nicht bestaetigt','type'=>get_class($e)]),"\n"; exit(1); }
