<?php
// Explicitly authorized 2026-10-11. Fixed scope; idempotent; no production mutations.
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    require getcwd().'/vendor/autoload.php';
    $app=require getcwd().'/bootstrap/app.php';
    $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $db=$app->make('db')->connection();
    if ((int)$db->selectOne("SELECT GET_LOCK('lplk-inner-circle-test-create', 0) AS acquired")->acquired!==1) throw new RuntimeException('Creation already running');
    $external='lplk-inner-circle-test-20261011';
    $existing=Pterodactyl\Models\Server::query()->where('external_id',$external)->first();
    $created=false;
    if ($existing) {
        if ($existing->node_id!==2 || $existing->egg_id!==15 || $existing->allocation_id!==64) throw new RuntimeException('Existing target mismatch');
        $server=$existing;
    } else {
        $ref=Pterodactyl\Models\Server::query()->where('uuid','11110003-0000-4000-8000-000000000003')->firstOrFail();
        $alloc=Pterodactyl\Models\Allocation::query()->findOrFail(64);
        if ($ref->node_id!==2 || $ref->egg_id!==15 || $ref->owner_id!==1 || $alloc->node_id!==2 || $alloc->server_id!==null || $alloc->ip!=='203.0.113.10' || $alloc->port!==27020) throw new RuntimeException('Reference or free allocation changed');
        if (disk_free_space('/var/lib/pterodactyl/volumes')<120*1024*1024*1024) throw new RuntimeException('Insufficient reserve');
        $used=trim((string)shell_exec("ss -H -lntu | awk '{print $5}' | grep -E '(^|:)27020$'"));
        if ($used!=='') throw new RuntimeException('Port already listening');
        $env=[
            'SRCDS_MAP'=>'de_dust2','SRCDS_APPID'=>'730','SRCDS_MAXPLAYERS'=>'10','SRCDS_STOP_UPDATE'=>'0','SRCDS_VALIDATE'=>'0',
            'STEAM_ACC'=>'','GAME_TYPE'=>'0','GAME_MODE'=>'0','CLEANUP_ENABLED'=>'0','ENABLE_FILTER'=>'1',
            'CUSTOM_PARAMS'=>'+hostname "[LPLK] TEST - Beispielserver"','PREFIX_TEXT'=>'LPLK-Test','ALLOW_TOKENLESS'=>'0',
            'INSTALL_METAMOD'=>'0','INSTALL_CSS'=>'0','INSTALL_SWIFTLY'=>'1','INSTALL_MODSHARP'=>'0','PRERELEASE'=>'0',
            'SYNC_LOCATION'=>'','GDB_DEBUG_PORT'=>''
        ];
        // Do not inherit source environment, shared mounts, token, database, admins or integration keys.
        $server=$app->make(Pterodactyl\Services\Servers\ServerCreationService::class)->handle([
            'external_id'=>$external,'name'=>'[TEST][LPLK] Beispielserver',
            'description'=>'Isolierter LPLK-Addon-Testserver. Vorlage: Beispielserver. Keine Produktionsdaten. Erstbetrieb nach Einrichtung.',
            'owner_id'=>1,'node_id'=>2,'egg_id'=>15,'nest_id'=>2,'allocation_id'=>64,
            'image'=>$ref->image,'startup'=>$ref->egg->startup,'environment'=>$env,
            'memory'=>8192,'swap'=>0,'disk'=>102400,'io'=>500,'cpu'=>400,'threads'=>null,'oom_disabled'=>false,
            'skip_scripts'=>false,'start_on_completion'=>false,'database_limit'=>0,'allocation_limit'=>0,'backup_limit'=>1
        ]);
        $created=true;
    }
    $db->selectOne("SELECT RELEASE_LOCK('lplk-inner-circle-test-create') AS released");
    ob_end_clean(); echo json_encode(['created'=>$created,'id'=>$server->id,'identifier'=>$server->uuidShort,'uuid'=>$server->uuid,'name'=>$server->name,'ip'=>'203.0.113.10','port'=>27020,'status'=>$server->status,'memoryMiB'=>$server->memory,'diskMiB'=>$server->disk,'startOnCompletion'=>false,'needsOwnSteamToken'=>true],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
} catch (Throwable $e) {
    if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Testserver-Anlage nicht erfolgreich bestaetigt. Vor Wiederholung vorhandene externe Kennung pruefen.','type'=>get_class($e), 'validationFields'=>$e instanceof Illuminate\Validation\ValidationException?array_keys($e->errors()):[]]),"\n"; exit(1);
}
