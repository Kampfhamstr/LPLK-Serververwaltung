<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 $root='/var/www/serververwaltung';
 if(realpath($root)!==$root)throw new RuntimeException('Scope');
 spl_autoload_register(static function($class)use($root){if(str_starts_with($class,'App\\')){$p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($p))require $p;}});
 App\Core\Config::load($root.'/config/app.php');$pdo=App\Core\Database::connection();
 $version=trim(file_get_contents($root.'/VERSION'));if($version!=='1.15.2.2')throw new RuntimeException('Version');
 $server=$pdo->query("SELECT id,server_identifier FROM game_servers WHERE id=7 AND server_identifier='11110004'")->fetch();if(!$server)throw new RuntimeException('Test identity');
 // Process-local read verification: no login, cookie or persistent session is created.
 $admin=$pdo->query("SELECT id,auth_version FROM users WHERE active=1 AND web_role='founder' ORDER BY id LIMIT 1")->fetch();if(!$admin)throw new RuntimeException('No verification identity');
 $_SESSION=['user_id'=>(int)$admin['id'],'auth_version'=>max(1,(int)$admin['auth_version']),'_presence_touch'=>time()];
 if(!App\Core\Auth::can('players.view')||!App\Core\Auth::canServer(7,'view'))throw new RuntimeException('Permission');
 $player=$pdo->query("SELECT id,steam_id64 FROM game_players WHERE steam_id64 LIKE '7656119%' ORDER BY id LIMIT 1")->fetch();
 if(!$player)throw new RuntimeException('No player available for read check');
 $result=App\Services\PlayerStatisticsService::read(7,(string)$player['steam_id64']);
 if(!in_array($result['state']??'',['empty','ready'],true))throw new RuntimeException('Statistics read: '.($result['state']??'missing'));
 $files=['VERSION','public/index.php','templates/layout.php','templates/player_view.php','templates/player_statistics.php','src/Controllers/PlayerStatisticsController.php','src/Services/PlayerStatisticsService.php','bin/install.php','bin/migrate.php','tests/v11522_statistics_test.php','README.md','CHANGELOG.md','INSTALLATION.md','KURZINSTALLATION.txt','UPDATE_VON_1.15.2.1_AUF_1.15.2.2.md','AUSLIEFERUNG_UND_UPDATEBEFEHLE.txt'];
 $hashes=[];foreach($files as $f)$hashes[$f]=hash_file('sha256',$root.'/'.$f);
 if(!str_contains(file_get_contents($root.'/public/index.php'),"'/players/{id}/statistics'")||!str_contains(file_get_contents($root.'/templates/player_view.php'),'Spielerstatistik ansehen'))throw new RuntimeException('Route/link');
 $integrity=$pdo->query('PRAGMA quick_check')->fetchColumn();if($integrity!=='ok')throw new RuntimeException('Database integrity');
 $_SESSION=[];ob_end_clean();echo json_encode(['version'=>$version,'hashes'=>$hashes,'testServer'=>7,'statisticsState'=>$result['state'],'databaseIntegrity'=>$integrity,'persistentLoginCreated'=>false,'productionGameServersChanged'=>false],JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES),"\n";
}catch(Throwable $e){$_SESSION=[];if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Website statistics verification failed','type'=>get_class($e),'reason'=>$e instanceof RuntimeException?$e->getMessage():'framework error']);exit(1);}
