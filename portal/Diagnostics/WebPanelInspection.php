<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    $root='/var/www/serververwaltung';
    spl_autoload_register(static function($class) use($root) {
        if(str_starts_with($class,'App\\')) { $p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php'; if(is_file($p)) require $p; }
    });
    App\Core\Config::load($root.'/config/app.php');
    $pdo=App\Core\Database::connection();
    $report=['servers'=>$pdo->query('SELECT id,name,connector_type,base_url,server_identifier,root_path,bridge_fastlive_enabled FROM game_servers ORDER BY id')->fetchAll(),
        'columns'=>array_column($pdo->query('PRAGMA table_info(game_servers)')->fetchAll(),'name'),
        'liveSourceHash'=>hash_file('sha256',$root.'/src/Services/LiveServerService.php'),
        'whitelistSourceHash'=>hash_file('sha256',$root.'/src/Services/WhitelistLiveService.php'),
        'version'=>trim(file_get_contents($root.'/VERSION')),
        'statisticsBaseline'=>array_combine(['src/Controllers/PlayersController.php','templates/player_view.php','public/index.php'],array_map(static fn($f)=>hash('sha256',str_replace("\r\n","\n",file_get_contents($root.'/'.$f))),['src/Controllers/PlayersController.php','templates/player_view.php','public/index.php'])),
        'whitelistNormalizedHash'=>hash('sha256',str_replace("\r\n","\n",file_get_contents($root.'/src/Services/WhitelistLiveService.php'))),
        'whitelistSupportsLplk'=>str_contains(file_get_contents($root.'/src/Services/WhitelistLiveService.php'),'LPLK_Whitelist'),
        'testRoles'=>$pdo->query("SELECT role_name,COUNT(*) AS count FROM server_player_groups WHERE server_id=7 GROUP BY role_name")->fetchAll(),
        'testListFileExists'=>is_file('/var/lib/pterodactyl/volumes/11110004-0000-4000-8000-000000000004/game/csgo/addons/swiftlys2/plugins/LPLK_Whitelist/whitelist.txt')];
    ob_end_clean(); echo json_encode($report,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
} catch(Throwable $e) { if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Webpanel-Metadaten nicht lesbar','type'=>get_class($e)]),"\n"; exit(1); }
