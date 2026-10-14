<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    $root='/var/www/serververwaltung';
    spl_autoload_register(static function($class) use($root) {
        if(str_starts_with($class,'App\\')) { $p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php'; if(is_file($p)) require $p; }
    });
    App\Core\Config::load($root.'/config/app.php'); $pdo=App\Core\Database::connection();
    $source=$pdo->query("SELECT * FROM game_servers WHERE id=3 AND server_identifier='11110003' AND connector_type='pterodactyl'")->fetch();
    if(!$source || $source['base_url']!=='https://lplk.resolve.bar') throw new RuntimeException('Source mismatch');
    $testPath='/var/lib/pterodactyl/volumes/11110004-0000-4000-8000-000000000004';
    if(realpath($testPath)!==$testPath || !is_file($testPath.'/.lplk-test-configured')) throw new RuntimeException('Test target missing');
    $pdo->beginTransaction();
    $row=$pdo->query("SELECT id,name,bridge_fastlive_enabled FROM game_servers WHERE server_identifier='11110004'")->fetch();
    $created=false;
    if(!$row) {
        // Existing encrypted connector credential stays inside the webpanel; fresh server identity.
        // Legacy bridge isolates bans/configuration from the production Fast-Live global snapshots.
        $pdo->prepare('INSERT INTO game_servers(name,connector_type,base_url,server_identifier,secret_enc,root_path,bridge_fastlive_enabled,created_at,updated_at) VALUES(?,?,?,?,?,?,0,?,?)')
            ->execute(['[TEST][LPLK] Beispielserver','pterodactyl',$source['base_url'],'11110004',$source['secret_enc'],'/',date('c'),date('c')]);
        $id=(int)$pdo->lastInsertId(); $created=true;
        foreach(['spieler'=>[],'vip'=>[],'member'=>[],'admin'=>['lplk.admin'],'founder'=>['*']] as $role=>$perms)
            $pdo->prepare('INSERT INTO server_role_permissions(server_id,role_name,permissions_json,updated_at) VALUES(?,?,?,?)')->execute([$id,$role,json_encode($perms),date('c')]);
        $cols=array_column($pdo->query('PRAGMA table_info(user_server_access)')->fetchAll(),'name');
        $copy=array_values(array_diff($cols,['server_id']));
        $fields=implode(',',array_map(static fn($c)=>'"'.str_replace('"','""',$c).'"',$copy));
        $pdo->prepare('INSERT INTO user_server_access(server_id,'.$fields.') SELECT ?,'.$fields.' FROM user_server_access WHERE server_id=3')->execute([$id]);
        $cols=array_column($pdo->query('PRAGMA table_info(server_player_groups)')->fetchAll(),'name');
        $copy=array_values(array_diff($cols,['server_id','id']));
        $fields=implode(',',array_map(static fn($c)=>'"'.str_replace('"','""',$c).'"',$copy));
        $pdo->prepare("INSERT INTO server_player_groups(server_id,".$fields.") SELECT ?,".$fields." FROM server_player_groups WHERE server_id=3 AND role_name IN ('admin','founder')")->execute([$id]);
    } else {
        $id=(int)$row['id'];
        if($row['name']!=='[TEST][LPLK] Beispielserver') throw new RuntimeException('Existing target mismatch');
    }
    $pdo->commit();
    $test=App\Services\LiveServerService::server($id);
    $connector=App\Services\ConnectorFactory::make($test);
    $details=$connector->serverDetails();
    // Verify server before invoking any connector write.
    $a=$details['attributes']??$details;
    if(($a['uuid']??'')!=='11110004-0000-4000-8000-000000000004') throw new RuntimeException('Connector target mismatch');
    $detected=App\Services\SyncService::scan($id);
    $sync=App\Services\SyncService::synchronize($id);
    $status=App\Services\LiveServerService::status($id);
    App\Core\Audit::log('server.test.provision','server',(string)$id,['reference_server_id'=>3,'test_identifier'=>'11110004','fast_live'=>false]);
    ob_end_clean(); echo json_encode(['created'=>$created,'webpanelServerId'=>$id,'url'=>'https://verwaltung.example.invalid/servers/'.$id,'framework'=>$detected['framework']??null,'detectedPlugins'=>count($detected['plugins']??[]),'bridgeOnline'=>$status['bridge_online']??false,'fastLive'=>false,'adminPermissionsSynchronized'=>true],JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
} catch(Throwable $e) { if(isset($pdo)&&$pdo->inTransaction()) $pdo->rollBack(); if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Webpanel-Anbindung noch nicht vollstaendig bestaetigt. Vor Wiederholung vorhandenen Testeintrag pruefen.','type'=>get_class($e)]),"\n"; exit(1); }
