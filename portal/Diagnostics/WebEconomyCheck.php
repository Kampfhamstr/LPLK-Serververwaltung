<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 $root='/var/www/serververwaltung';if(realpath($root)!==$root)throw new RuntimeException('Scope');
 $files=['VERSION','public/index.php','templates/layout.php','templates/player_view.php','templates/player_statistics.php','templates/player_economy.php','templates/player_bank_statement.php','src/Services/BankStatement.php','public/assets/bank-statement.css','public/assets/bank-statement.js','templates/server_ranking.php','templates/server_live.php','src/Core/Auth.php','src/Controllers/PlayerStatisticsController.php','src/Controllers/PlayerEconomyController.php','src/Services/BankNetworkPlan.php','src/Services/PlayerEconomyService.php','src/Services/ServerRankingService.php','tests/v11524_economy_test.php','tests/fixtures/lplk-economy.json','tests/fixtures/lplk-ranking.json','bin/bank-migration-plan.php','bin/install.php','bin/migrate.php','README.md','CHANGELOG.md','INSTALLATION.md','KURZINSTALLATION.txt','BANKVERBUND.md','UPDATE_VON_1.15.2.3_AUF_1.15.2.4.md','AUSLIEFERUNG_UND_UPDATEBEFEHLE.txt'];$hashes=[];$normalized=[];
 foreach($files as $file){$p=$root.'/'.$file;if(is_link($p))throw new RuntimeException('Linked file');$hashes[$file]=is_file($p)?hash_file('sha256',$p):null;$normalized[$file]=is_file($p)?hash('sha256',str_replace("\r\n","\n",file_get_contents($p))):null;}
 $version=trim(file_get_contents($root.'/VERSION'));$report=['version'=>$version,'hashes'=>$hashes,'normalizedHashes'=>$normalized,'productionGameServersChanged'=>false];
 if($version==='1.15.2.4'){
  spl_autoload_register(static function($class)use($root){if(str_starts_with($class,'App\\')){$p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($p))require $p;}});
  App\Core\Config::load($root.'/config/app.php');$pdo=App\Core\Database::connection();
  $s=App\Services\LiveServerService::server(7);if($s['server_identifier']!=='11110004')throw new RuntimeException('Test identity');
  $admin=$pdo->query("SELECT id,auth_version FROM users WHERE active=1 AND web_role='founder' ORDER BY id LIMIT 1")->fetch();if(!$admin)throw new RuntimeException('Verification identity');
  $_SESSION=['user_id'=>(int)$admin['id'],'auth_version'=>max(1,(int)$admin['auth_version']),'_presence_touch'=>time()];
  $bank=App\Services\PlayerEconomyService::ranking(7);$stats=App\Services\ServerRankingService::read(7);
  $report+=['bankState'=>$bank['state'],'bankRows'=>count($bank['data']['rows']??[]),'statisticsState'=>$stats['state'],'statisticsBoards'=>array_map('count',$stats['data']['boards']??[]),'bankManagePermission'=>App\Core\Auth::can('economy.manage'),'productionGroup'=>App\Services\BankNetworkPlan::group('11110001'),'testGroup'=>App\Services\BankNetworkPlan::group('11110004')];
  $playerId=$pdo->query('SELECT id FROM game_players ORDER BY id LIMIT 1')->fetchColumn();
  if($playerId){
   $_GET=['server'=>'7','view'=>'statement'];$_SERVER['REQUEST_URI']='/players/'.(int)$playerId.'/economy?server=7&view=statement';
   ob_start();(new App\Controllers\PlayerEconomyController())->view((string)$playerId);$html=ob_get_clean();
   $report['accountPage']=['httpStatus'=>http_response_code()?:200,'statementSelected'=>str_contains($html,'view=statement" aria-current="page"'),'assetsLinked'=>str_contains($html,'/assets/bank-statement.css?v=1.15.2.4')&&str_contains($html,'/assets/bank-statement.js?v=1.15.2.4'),'hasViewSwitch'=>str_contains($html,'Kontoauszug &amp; Bilanz'),'viewRemembered'=>($_SESSION['economy_view']??null)==='statement'];
  }else{$report['accountPage']=['state'=>'no-panel-player-for-read-only-check'];}
 }
 $_SESSION=[];ob_end_clean();echo json_encode($report,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES),"\n";
}catch(Throwable){$_SESSION=[];if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Economy website inspection failed']),"\n";exit(1);}
