<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    $web='/var/www/serververwaltung';
    spl_autoload_register(static function($class) use($web) { if(str_starts_with($class,'App\\')) { $p=$web.'/src/'.str_replace('\\','/',substr($class,4)).'.php'; if(is_file($p)) require $p; } });
    App\Core\Config::load($web.'/config/app.php');
    $server=App\Services\LiveServerService::server(7);
    if($server['server_identifier']!=='11110004'||$server['name']!=='[TEST][LPLK] Beispielserver') throw new DomainException('Testidentität stimmt nicht.');
    $details=App\Services\ConnectorFactory::make($server)->serverDetails();$a=$details['attributes']??$details;
    if(($a['uuid']??'')!=='11110004-0000-4000-8000-000000000004') throw new DomainException('Connector-Ziel stimmt nicht.');
    if(trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"))!=='exited') throw new DomainException('Testserver zuerst stoppen.');
    $swift='/var/lib/pterodactyl/volumes/11110004-0000-4000-8000-000000000004/game/csgo/addons/swiftlys2';
    $plugin=$swift.'/plugins/LPLK_Whitelist';$list=$plugin.'/whitelist.txt';$cfg=$swift.'/configs/plugins/LPLK_Whitelist/config.jsonc';
    if(realpath($plugin)!==$plugin||!is_file($plugin.'/LPLK_Whitelist.dll')) throw new DomainException('Plugin fehlt.');
    if(file_exists($list)||file_exists($cfg)||is_link($list)||is_link($cfg)) throw new DomainException('Bestehende Testkonfiguration bleibt erhalten.');
    $ids=App\Core\Database::connection()->query("SELECT DISTINCT gp.steam_id64 FROM server_player_groups spg JOIN game_players gp ON gp.id=spg.player_id WHERE spg.server_id=7 AND spg.role_name IN ('admin','founder') AND gp.active=1")->fetchAll(PDO::FETCH_COLUMN);
    if(count($ids)<1||count($ids)>100) throw new DomainException('Testrollen müssen geprüft werden.');
    foreach($ids as $id) if(!preg_match('/^765611[0-9]{11}$/D',(string)$id)) throw new DomainException('Ungültige Testrolle.');
    $owner=fileowner($plugin);$group=filegroup($plugin);
    $config=json_encode(['Whitelist'=>['Enabled'=>true,'Mode'=>1,'AdminBypass'=>false,'PermissionForCommands'=>'admin.ban','KickMessage'=>'Dieser LPLK-Testserver ist nur fuer freigeschaltete Tester zugaenglich.','AddCommand'=>'wl','RemoveCommand'=>'uwl']],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n";
    foreach([$list=>implode("\n",$ids)."\n",$cfg=>$config] as $path=>$body) {
        $parent=dirname($path);if(!is_dir($parent)) { if(!mkdir($parent,0755,true)) throw new RuntimeException('mkdir'); chown($parent,$owner);chgrp($parent,$group); }
        if(realpath($parent)!==$parent) throw new DomainException('Verknüpfter Zielpfad.');
        $f=fopen($path,'x');if(!$f) throw new RuntimeException('create');
        try { if(fwrite($f,$body)!==strlen($body)) throw new RuntimeException('write'); } finally { fclose($f); }
        chown($path,$owner);chgrp($path,$group);chmod($path,0644);
    }
    ob_end_clean();echo json_encode(['server'=>'11110004','seededTestStaff'=>count($ids),'mode'=>1,'adminBypass'=>false,'productionChanged'=>false]),"\n";
} catch(Throwable $e) { if(ob_get_level()) ob_end_clean();echo json_encode(['error'=>$e instanceof DomainException?$e->getMessage():'Testliste nicht vollständig initialisiert; vor Wiederholung prüfen.','type'=>get_class($e)]),"\n";exit(1); }
