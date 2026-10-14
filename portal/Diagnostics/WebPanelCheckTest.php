<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
$stage='bootstrap';
try {
    $root='/var/www/serververwaltung';
    spl_autoload_register(static function($class) use($root) {
        if(str_starts_with($class,'App\\')) { $p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php'; if(is_file($p)) require $p; }
    });
    App\Core\Config::load($root.'/config/app.php');
    $server=App\Services\LiveServerService::server(7);
    if($server['server_identifier']!=='11110004' || $server['name']!=='[TEST][LPLK] Beispielserver') throw new RuntimeException('Scope mismatch');
    $stage='status'; $status=App\Services\LiveServerService::status(7);
    if(empty($status['bridge_online'])) throw new RuntimeException('Bridge offline');
    $commands=[];
    $stage='queue';
    foreach(['sw_admin','sw_adminmenu','sw_menu','sw_lplk_parachute','sw_lplk_bombwires','sw_lplk_maps','sw_lplk_whitelist','sw_lplk_sounds','sw_lplk_anticamp','sw_lplk_chatguard','sw_lplk_antiteamflash','sw_lplk_teams','sw_lplk_weapons','sw_lplk_combatrules','sw_lplk_statistics','sw_lplk_demos','sw_lplk_thirdperson','sw_lplk_fluch','sw_lplk_nadeeffects','sw_lplk_economy','sw_bet','sw_bets','sw_lotto','sw_kopfgeld','sw_bounty','kopfgeld','bounty','sw_titel','sw_title','sw_titles','sw_bankraub','sw_spionlog','sw_ranking','sw_bbhelp','sw_tp','sw_thirdperson'] as $command)
        $commands[$command]=App\Services\LiveServerService::queue(7,'server_command',['command'=>$command]);
    $results=[];
    $stage='results';
    for($i=0;$i<8;$i++) {
        usleep(500000);
        foreach($commands as $command=>$id) {
            if(isset($results[$command])) continue;
            try { $r=App\Services\LiveServerService::result(7,$id); if($r!==null) $results[$command]=$r; }
            catch(RuntimeException $pending) { /* Legacy result file may not exist yet. Retry within the bounded loop. */ }
        }
        if(count($results)===count($commands)) break;
    }
    App\Core\Audit::log('server.test.command_check','server','7',['commands'=>array_keys($commands)]);
    ob_end_clean(); echo json_encode(['serverId'=>7,'bridgeOnline'=>true,'results'=>$results],JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
} catch(Throwable $e) { if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Webpanel-Befehlstest nicht bestaetigt','stage'=>$stage,'statusAge'=>$status['status_age_seconds']??null,'bridgeAvailable'=>$status['bridge_available']??null,'type'=>get_class($e)]),"\n"; exit(1); }




