<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    require getcwd().'/vendor/autoload.php'; $app=require getcwd().'/bootstrap/app.php';
    $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
    if ($server->uuid!=='11110004-0000-4000-8000-000000000004') throw new RuntimeException('Scope mismatch');
    if(trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"))!=='exited') throw new RuntimeException('Stop first');
    $root='/var/lib/pterodactyl/volumes/'.$server->uuid;
    $swift=$root.'/game/csgo/addons/swiftlys2'; $plugins=$swift.'/plugins';
    if(realpath($plugins)!==$plugins || realpath($root.'/.lplk-staging')!==$root.'/.lplk-staging') throw new RuntimeException('Isolated real paths required');
    $owner=fileowner($plugins); $group=filegroup($plugins); $files=[];
    $write=static function($dest,$data) use($plugins,$owner,$group) {
        $parent=dirname($dest);
        if (!str_starts_with($dest,$plugins.'/') || is_link($dest)) throw new RuntimeException('Unsafe target');
        if(!is_dir($parent)) mkdir($parent,0755,true);
        if(realpath($parent)!==$parent) throw new RuntimeException('Linked parent');
        file_put_contents($dest.'.lplk-tmp',$data,LOCK_EX); chown($dest.'.lplk-tmp',$owner); chgrp($dest.'.lplk-tmp',$group); chmod($dest.'.lplk-tmp',0644); rename($dest.'.lplk-tmp',$dest);
        chown($parent,$owner); chgrp($parent,$group);
    };
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_ChatGuardSet' && is_dir($plugins.'/SerververwaltungAntiAdvertisement')) throw new RuntimeException('Legacy chat filter must not run alongside the new module');
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_AntiTeamFlash' && is_dir($plugins.'/AntiTeamFlash')) throw new RuntimeException('Legacy teamflash plugin must not run alongside the new module');
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_TeamsSet' && is_dir($plugins.'/KampfhamsterTeamSwitchBlocker')) throw new RuntimeException('Legacy team rules must not run alongside the new module');
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_StatisticsSet') {
        foreach(['HeadshotOnly','K4-DamageInfo','NadeKillStatTrak','K4-GOTV'] as $legacy)if(is_dir($plugins.'/'.$legacy))throw new RuntimeException('Legacy statistics/demo plugins must not run alongside new modules');
        foreach(['LPLK_Admin','LPLK_Statistics','LPLK_Demos'] as $required)if(!is_file($root.'/.lplk-staging/'.$required.'.zip'))throw new RuntimeException('Statistics set incomplete');
    }
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_WeaponsSet') {
        foreach(['HeadshotOnly','headshotonly','cs2-advanced-weapon-system','AdvancedWeaponSystem','GunsMenuSW2'] as $legacy)if(is_dir($plugins.'/'.$legacy))throw new RuntimeException('Legacy weapon plugins must not run alongside new modules');
        foreach(['LPLK_Admin','LPLK_Weapons','LPLK_CombatRules','LPLK_Maps','LPLK_Sounds'] as $required)if(!is_file($root.'/.lplk-staging/'.$required.'.zip'))throw new RuntimeException('Weapons set incomplete');
    }
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_ThirdPersonSet') {
        if(is_dir($plugins.'/ThirdPerson'))throw new RuntimeException('Legacy ThirdPerson must not run alongside new camera');
        foreach(['LPLK_Admin','LPLK_ThirdPerson'] as $required)if(!is_file($root.'/.lplk-staging/'.$required.'.zip'))throw new RuntimeException('Camera set incomplete');
    }
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_FluchSet') {
        foreach(['KampfhamsterFluch','kampfhamster_fluch'] as $legacy)if(is_dir($plugins.'/'.$legacy))throw new RuntimeException('Legacy Fluch must not run alongside new module');
        foreach(['LPLK_Admin','LPLK_Fluch'] as $required)if(!is_file($root.'/.lplk-staging/'.$required.'.zip'))throw new RuntimeException('Fluch set incomplete');
    }
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_EconomySet') {
        foreach(['BetBank','betbank'] as $legacy)if(is_dir($plugins.'/'.$legacy))throw new RuntimeException('Legacy BetBank must not run alongside Economy');
        foreach(['LPLK_Admin','LPLK_Economy'] as $required)if(!is_file($root.'/.lplk-staging/'.$required.'.zip'))throw new RuntimeException('Economy set incomplete');
        foreach(['data/betbank','data/BetBank'] as $legacy)if(is_dir($swift.'/'.$legacy))throw new RuntimeException('Legacy bank data must remain outside isolated test profile');
        // Stopped database/config rollback before the legacy command corrections.
        $dataDir=$swift.'/data/LPLK_Economy';$dataBackup=$root.'/.lplk-rollback/LPLK_Economy.data-before-legacy-commands.zip';
        if(is_file($dataDir.'/economy.sqlite3')&&!is_file($dataBackup)) {
            if(realpath($dataDir)!==$dataDir||is_link($dataBackup))throw new RuntimeException('Unsafe Economy backup path');
            if(!is_dir(dirname($dataBackup)))mkdir(dirname($dataBackup),0700);
            if(realpath(dirname($dataBackup))!==dirname($dataBackup))throw new RuntimeException('Linked backup parent');
            $tempBackup=$dataBackup.'.incoming';if(file_exists($tempBackup)||is_link($tempBackup))throw new RuntimeException('Data backup staging exists');
            $dataZip=new ZipArchive();if($dataZip->open($tempBackup,ZipArchive::CREATE|ZipArchive::EXCL)!==true)throw new RuntimeException('Data backup open failed');
            $dataHashes=[];
            foreach(['economy.sqlite3','economy.sqlite3-wal','economy.sqlite3-shm','settings.json'] as $name) {
                $source=$dataDir.'/'.$name;if(!file_exists($source))continue;
                if(is_link($source)||realpath($source)!==$source)throw new RuntimeException('Linked bank source');
                $dataHashes[$name]=hash_file('sha256',$source);if(!$dataZip->addFile($source,$name))throw new RuntimeException('Data backup file failed');
            }
            if(!$dataZip->close()||!chmod($tempBackup,0600))throw new RuntimeException('Data backup failed');
            $dataZip=new ZipArchive();if($dataZip->open($tempBackup)!==true)throw new RuntimeException('Data backup reopen failed');
            foreach($dataHashes as $name=>$hash)if(hash('sha256',$dataZip->getFromName($name))!==$hash||hash_file('sha256',$dataDir.'/'.$name)!==$hash)throw new RuntimeException('Data backup checksum mismatch');
            $dataZip->close();if(!rename($tempBackup,$dataBackup))throw new RuntimeException('Data backup activate failed');
        }
    }
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_NadeEffectsSet') {
        foreach(['KampfhamsterNadeBoom','kampfhamster_nadeboom'] as $legacy)if(is_dir($plugins.'/'.$legacy))throw new RuntimeException('Legacy NadeBoom must not run alongside new module');
        foreach(['LPLK_Admin','LPLK_NadeEffects'] as $required)if(!is_file($root.'/.lplk-staging/'.$required.'.zip'))throw new RuntimeException('NadeEffects set incomplete');
    }
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_WebEconomySet') {
        foreach(['BetBank','betbank','HeadshotOnly','K4-DamageInfo','NadeKillStatTrak'] as $legacy)if(is_dir($plugins.'/'.$legacy))throw new RuntimeException('Legacy bank/statistics cannot run alongside new modules');
        foreach(['LPLK_Economy'=>['economy.sqlite3','economy.sqlite3-wal','economy.sqlite3-shm','settings.json'],'LPLK_Statistics'=>['statistics.json','settings.json']] as $module=>$names) {
            if(!is_file($root.'/.lplk-staging/'.$module.'.zip'))throw new RuntimeException('Web Economy set incomplete');
            $dir=$swift.'/data/'.$module;$backup=$root.'/.lplk-rollback/'.$module.'.data-before-web-economy.zip';
            if(realpath($dir)!==$dir||is_link($backup)||realpath(dirname($backup))!==dirname($backup))throw new RuntimeException('Unsafe snapshot path');
            if(!is_file($backup)) {
                $tmp=$backup.'.incoming';if(file_exists($tmp)||is_link($tmp))throw new RuntimeException('Snapshot staging occupied');
                $z=new ZipArchive();if($z->open($tmp,ZipArchive::CREATE|ZipArchive::EXCL)!==true)throw new RuntimeException('Snapshot open failed');$hashes=[];
                foreach($names as $name){$f=$dir.'/'.$name;if(!file_exists($f))continue;if(is_link($f)||realpath($f)!==$f)throw new RuntimeException('Linked snapshot source');$hashes[$name]=hash_file('sha256',$f);if(!$z->addFile($f,$name))throw new RuntimeException('Snapshot entry failed');}
                if(!$z->close()||!chmod($tmp,0600))throw new RuntimeException('Snapshot close failed');$z=new ZipArchive();if($z->open($tmp)!==true)throw new RuntimeException('Snapshot verify failed');foreach($hashes as $name=>$hash)if(hash('sha256',$z->getFromName($name))!==$hash||hash_file('sha256',$dir.'/'.$name)!==$hash)throw new RuntimeException('Snapshot hash mismatch');$z->close();if(!rename($tmp,$backup))throw new RuntimeException('Snapshot activate failed');
            }
        }
        // Scoped authorization: requested web administration, enabled only on the existing test profile.
        $cfg=$swift.'/data/LPLK_Economy/settings.json';$settings=json_decode(file_get_contents($cfg),true,32,JSON_THROW_ON_ERROR);
        if(($settings['Profile']??'')!=='standard-test-11110004')throw new RuntimeException('Unexpected economy profile');
        if(!array_key_exists('WebBank',$settings)){$settings['WebBank']=['ExportEnabled'=>true,'AdminChangesEnabled'=>true];$tmp=$cfg.'.web-incoming';if(file_exists($tmp)||is_link($tmp))throw new RuntimeException('Configuration staging occupied');if(file_put_contents($tmp,json_encode($settings,JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR)."\n",LOCK_EX)===false||!chown($tmp,$owner)||!chgrp($tmp,$group)||!chmod($tmp,0644)||!rename($tmp,$cfg))throw new RuntimeException('Configuration write failed');}
    }
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_Statistics') {
        $dir=$swift.'/data/LPLK_Statistics';$backup=$root.'/.lplk-rollback/LPLK_Statistics.data-before-weapon-rankings.zip';
        if(realpath($dir)!==$dir||is_link($backup)||realpath(dirname($backup))!==dirname($backup))throw new RuntimeException('Unsafe statistics backup path');
        if(!is_file($backup)) {
            $tmp=$backup.'.incoming';if(file_exists($tmp)||is_link($tmp))throw new RuntimeException('Snapshot staging occupied');
            $z=new ZipArchive();if($z->open($tmp,ZipArchive::CREATE|ZipArchive::EXCL)!==true)throw new RuntimeException('Snapshot open failed');$hashes=[];
            foreach(['statistics.json','settings.json'] as $name){$f=$dir.'/'.$name;if(!is_file($f)||is_link($f)||realpath($f)!==$f)throw new RuntimeException('Missing or unsafe statistics source');$hashes[$name]=hash_file('sha256',$f);if(!$z->addFile($f,$name))throw new RuntimeException('Snapshot entry failed');}
            if(!$z->close()||!chmod($tmp,0600))throw new RuntimeException('Snapshot close failed');$z=new ZipArchive();if($z->open($tmp)!==true)throw new RuntimeException('Snapshot verify failed');foreach($hashes as $name=>$hash)if(hash('sha256',$z->getFromName($name))!==$hash||hash_file('sha256',$dir.'/'.$name)!==$hash)throw new RuntimeException('Snapshot hash mismatch');$z->close();if(!rename($tmp,$backup))throw new RuntimeException('Snapshot activation failed');
        }
    }
    foreach(['LPLK_Parachute','LPLK_BombWires','LPLK_Maps','LPLK_Admin','LPLK_Whitelist','SerververwaltungBridge','LPLK_Sounds','AddonsManager','LPLK_AntiCamp','LPLK_ChatGuard','LPLK_AntiTeamFlash','LPLK_Teams','LPLK_Weapons','LPLK_CombatRules','LPLK_Statistics','LPLK_Demos','LPLK_ThirdPerson','LPLK_Fluch','LPLK_NadeEffects','LPLK_Economy'] as $id) {
        if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_WebEconomySet'&&!in_array($id,['LPLK_Economy','LPLK_Statistics'],true))continue;
        if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_EconomySet'&&!in_array($id,['LPLK_Admin','LPLK_Economy'],true))continue;
        if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_NadeEffectsSet'&&!in_array($id,['LPLK_Admin','LPLK_NadeEffects'],true))continue;
        if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_FluchSet'&&!in_array($id,['LPLK_Admin','LPLK_Fluch'],true))continue;
        if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_ThirdPersonSet'&&!in_array($id,['LPLK_Admin','LPLK_ThirdPerson'],true))continue;
        if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_StatisticsSet'&&!in_array($id,['LPLK_Admin','LPLK_Statistics','LPLK_Demos'],true))continue;
        if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_WeaponsSet'&&!in_array($id,['LPLK_Admin','LPLK_Weapons','LPLK_CombatRules','LPLK_Maps','LPLK_Sounds'],true))continue;
        if(!in_array('__LPLK_DEPLOY_COMPONENT__',['LPLK_WebEconomySet','LPLK_WeaponsSet','LPLK_StatisticsSet','LPLK_ThirdPersonSet','LPLK_FluchSet','LPLK_NadeEffectsSet','LPLK_EconomySet'],true))
        if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_TeamsSet'?!in_array($id,['SerververwaltungBridge','LPLK_Teams'],true):('__LPLK_DEPLOY_COMPONENT__'==='LPLK_ChatGuardSet'?!in_array($id,['SerververwaltungBridge','LPLK_ChatGuard'],true):('__LPLK_DEPLOY_COMPONENT__'==='LPLK_MenuSet'?!str_starts_with($id,'LPLK_'):('__LPLK_DEPLOY_COMPONENT__'!=='' && $id!=='__LPLK_DEPLOY_COMPONENT__'))))continue;
        $package=$root.'/.lplk-staging/'.$id.'.zip';
        if(!is_file($package)) continue;
        $expected=trim(file_get_contents($root.'/.lplk-staging/'.$id.'.sha256'));
        if(!preg_match('/^[a-f0-9]{64}$/D',$expected) || !hash_equals($expected,hash_file('sha256',$package))) throw new RuntimeException('Checksum mismatch');
        $zip=new ZipArchive(); if($zip->open($package)!==true) throw new RuntimeException('Invalid ZIP');
        $entries=[];
        for($i=0;$i<$zip->numFiles;$i++) {
            $name=$zip->getNameIndex($i); if(str_ends_with($name,'/')) continue;
            if(!str_starts_with($name,$id.'/') || str_contains($name,'..') || str_contains($name,'\\') || !preg_match('/^[A-Za-z0-9_.\/-]+$/D',$name)) throw new RuntimeException('Unsafe ZIP path');
            $stat=$zip->statIndex($i); if($stat['size']>50*1024*1024) throw new RuntimeException('Oversized entry');
            $entries[$name]=$zip->getFromIndex($i);
        }
        if(!isset($entries[$id.'/'.$id.'.dll'])) throw new RuntimeException('Plugin DLL missing');
        // Keep one complete prior test-module copy until the replacement has passed runtime checks.
        $rollback=$root.'/.lplk-rollback';
        if(!is_dir($rollback)) mkdir($rollback,0700);
        if(realpath($rollback)!==$rollback) throw new RuntimeException('Unsafe rollback path');
        $previous=$rollback.'/'.$id.'.previous.zip';
        if(is_link($previous)) throw new RuntimeException('Linked rollback');
        if(is_dir($plugins.'/'.$id) && (!is_file($previous) || in_array('__LPLK_DEPLOY_COMPONENT__',['LPLK_MenuSet','LPLK_Statistics','LPLK_BombWires','LPLK_Admin','LPLK_StatisticsSet','LPLK_ThirdPersonSet','LPLK_FluchSet','LPLK_NadeEffectsSet','LPLK_EconomySet','LPLK_WebEconomySet'],true) || ('__LPLK_DEPLOY_COMPONENT__'==='LPLK_WeaponsSet' && in_array($id,['LPLK_Admin','LPLK_Maps','LPLK_Sounds'],true)) || ('__LPLK_DEPLOY_COMPONENT__'==='LPLK_TeamsSet' && $id==='SerververwaltungBridge'))) {
            // Preserve the current tested bridge/admin when replacing these components.
            // Create and verify the replacement before atomically replacing an older test backup.
            $incoming=$previous.'.incoming';
            if(file_exists($incoming)||is_link($incoming))throw new RuntimeException('Backup staging already exists');
            $backup=new ZipArchive(); if($backup->open($incoming,ZipArchive::CREATE|ZipArchive::EXCL)!==true) throw new RuntimeException('Backup open failed');
            $oldHashes=[];
            $iterator=new RecursiveIteratorIterator(new RecursiveDirectoryIterator($plugins.'/'.$id,FilesystemIterator::SKIP_DOTS));
            foreach($iterator as $old) {
                if($old->isLink() || realpath($old->getPathname())!==$old->getPathname()) throw new RuntimeException('Linked old plugin');
                if($old->isFile()) {
                    $relative=substr($old->getPathname(),strlen($plugins)+1);
                    $oldHashes[$relative]=hash_file('sha256',$old->getPathname());
                    if(!$backup->addFile($old->getPathname(),$relative))throw new RuntimeException('Backup entry failed');
                }
            }
            if(!$backup->close()||!chmod($incoming,0600))throw new RuntimeException('Backup close failed');
            $verify=new ZipArchive();if($verify->open($incoming)!==true)throw new RuntimeException('Backup reopen failed');
            foreach($oldHashes as $relative=>$hash)if(hash('sha256',$verify->getFromName($relative))!==$hash||hash_file('sha256',$plugins.'/'.$relative)!==$hash)throw new RuntimeException('Backup verification failed');
            $verify->close();if(!rename($incoming,$previous))throw new RuntimeException('Backup activate failed');
        }
        foreach($entries as $name=>$data) {
            $write($plugins.'/'.$name,$data);
            if(!hash_equals(hash('sha256',$data),hash_file('sha256',$plugins.'/'.$name))) throw new RuntimeException('Installed file verification failed');
        }
        $zip->close(); $files[]=['plugin'=>$id,'sha256'=>$expected];
    }
    // First initialization only: one explicitly reviewed Workshop sound asset on this test server.
    if(is_file($plugins.'/AddonsManager/AddonsManager.dll')) {
        $dir=$swift.'/configs/plugins/AddonsManager';$cfg=$dir.'/config.jsonc';
        if(is_link($cfg)||is_link($dir))throw new RuntimeException('Linked addon config');
        if(!file_exists($cfg)) {
            if(!is_dir($dir)&&!mkdir($dir,0755,true))throw new RuntimeException('Config directory failed');
            if(realpath($dir)!==$dir)throw new RuntimeException('Linked config parent');
            $data=json_encode(['Main'=>['Addons'=>['3797047889'],'BlockDisconnectMessages'=>true,'CacheClientsWithAddons'=>true,'CacheClientsDurationInSeconds'=>0,'ExtraAddonsTimeoutInSeconds'=>10,'RedownloadAddonOnMount'=>false,'CheckForUpdatesOnMapStart'=>true]],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n";
            $f=fopen($cfg,'x');if(!$f)throw new RuntimeException('Config create failed');try{if(fwrite($f,$data)!==strlen($data))throw new RuntimeException('Config write failed');}finally{fclose($f);}
            chown($dir,$owner);chgrp($dir,$group);chown($cfg,$owner);chgrp($cfg,$group);chmod($cfg,0644);
        }
    }
    if('__LPLK_DEPLOY_COMPONENT__'==='LPLK_StatisticsSet') {
        // SourceTV preparation only on the stopped, isolated test server. No recorder auto-start.
        $desired='+tv_port 28020 +tv_enable 1 +tv_autorecord 0 +tv_maxclients 0';
        $row=$app->make('db')->table('server_variables')->where('server_id',$server->id)->where('variable_id',84);
        $current=(string)$row->value('variable_value');
        if($current!==''&&$current!==$desired)throw new RuntimeException('Custom startup parameters require review');
        if($app->make('db')->table('allocations')->where('port',28020)->whereNotNull('server_id')->exists())throw new RuntimeException('TV port already assigned');
        if(trim((string)shell_exec("ss -H -lntu 'sport = :28020'"))!=='')throw new RuntimeException('TV port already listening');
        $row->update(['variable_value'=>$desired]);
        $app->make(Pterodactyl\Repositories\Wings\DaemonServerRepository::class)->setServer($server)->sync();
    }
    ob_end_clean(); echo json_encode(['deployed'=>$files,'economyDataRollback'=>isset($dataBackup)&&is_file($dataBackup)?['file'=>basename($dataBackup),'bytes'=>filesize($dataBackup),'sha256'=>hash_file('sha256',$dataBackup)]:null,'bridgeRuntime'=>'isolated-built-package','productionChanged'=>false,'runtimeVerificationPending'=>true],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
} catch(Throwable $e) { if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Testdeployment nicht vollstaendig bestaetigt; Server gestoppt lassen.','type'=>get_class($e)]),"\n"; exit(1); }






