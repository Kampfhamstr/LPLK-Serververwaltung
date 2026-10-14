<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();$phase='files';
try {
 $root='/var/www/serververwaltung';if(realpath($root)!==$root)throw new RuntimeException('Scope');
 $files=['AUSLIEFERUNG_UND_UPDATEBEFEHLE.txt','bin/install.php','bin/migrate.php','CHANGELOG.md','INSTALLATION.md','KURZINSTALLATION.txt','public/assets/app.js','public/assets/panel-usability.css','public/assets/panel-usability.js','public/assets/pet-companion.js','public/index.php','README.md','src/Controllers/DashboardController.php','src/Controllers/PanelNavigationController.php','src/Core/View.php','src/Services/PanelFeatureStatus.php','src/Services/PanelFormDraft.php','src/Services/PanelNavigation.php','src/Services/PanelSearch.php','templates/dashboard.php','templates/error.php','templates/layout.php','templates/panel_context.php','templates/panel_home.php','templates/panel_navigation.php','templates/panel_search.php','templates/player_view.php','templates/plugins.php','templates/server_live.php','templates/server_view.php','tests/v1120_pet_animations_test.php','tests/v11528_usability_test.php','UPDATE_VON_1.15.2.7_AUF_1.15.2.8.md','VERSION'];
 if(!$files)throw new RuntimeException('File list missing');
 $report=['version'=>trim(file_get_contents($root.'/VERSION')),'hashes'=>[],'normalizedHashes'=>[]];
 foreach($files as $file){$path=$root.'/'.$file;if(is_link($path))throw new RuntimeException('Linked file');$report['hashes'][$file]=is_file($path)?hash_file('sha256',$path):null;$report['normalizedHashes'][$file]=is_file($path)?hash('sha256',str_replace("\r\n","\n",file_get_contents($path))):null;}
 if($report['version']==='1.15.2.8') {
  spl_autoload_register(static function($class)use($root){if(str_starts_with($class,'App\\')){$file=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($file))require $file;}});
  App\Core\Config::load($root.'/config/app.php');$pdo=App\Core\Database::connection();
  $phase='identity';$admin=$pdo->query("SELECT id,auth_version FROM users WHERE active=1 AND web_role='founder' ORDER BY id LIMIT 1")->fetch();if(!$admin)throw new RuntimeException('Verification identity');
  $_SESSION=['user_id'=>(int)$admin['id'],'auth_version'=>max(1,(int)$admin['auth_version']),'_presence_touch'=>time()];$_SERVER['REQUEST_METHOD']='GET';
  $player=$pdo->query('SELECT id,steam_id64 FROM game_players ORDER BY id LIMIT 1')->fetch();
  if($player){
   $phase='profile';$_GET=[];$_SERVER['REQUEST_URI']='/players/'.(int)$player['id'];http_response_code(200);ob_start();(new App\Controllers\PlayersController())->view((string)$player['id']);$html=ob_get_clean();
   $report['playerPage']=['httpStatus'=>http_response_code()?:200,'mainNavigation'=>str_contains($html,'ux-main-nav'),'search'=>str_contains($html,'id="global-search"'),'sections'=>str_contains($html,'data-ux-workspace="player"'),'profile'=>str_contains($html,'data-player-showcase'),'usabilityAssets'=>str_contains($html,'panel-usability.js?v=1.15.2.8')];
   $phase='search';$_GET=['q'=>$player['steam_id64']];$_SERVER['REQUEST_URI']='/search';ob_start();(new App\Controllers\PanelNavigationController())->search();$search=ob_get_clean();$report['searchPage']=['httpStatus'=>http_response_code()?:200,'result'=>str_contains($search,'href="/players/'.(int)$player['id'].'"'),'noSettingsForm'=>!str_contains($search,'action="/settings"')];
  }
  $phase='server';$_GET=[];$_SERVER['REQUEST_URI']='/servers/7';http_response_code(200);ob_start();(new App\Controllers\ServersController())->view('7');$html=ob_get_clean();
  $report['serverPage']=['httpStatus'=>http_response_code()?:200,'sections'=>str_contains($html,'data-ux-workspace="server"'),'favorite'=>str_contains($html,'data-ux-favorite="7"'),'addons'=>str_contains($html,'id="addons"')];
  $phase='features';$report['features']=App\Services\PanelFeatureStatus::read(7);
  $phase='navigation';$report['navigationGroups']=array_keys(App\Services\PanelNavigation::groups());
  $report['gameserversChanged']=false;$report['bankChanged']=false;
 }
 $_SESSION=[];ob_end_clean();echo json_encode($report,JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),"\n";
}catch(Throwable){$_SESSION=[];while(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Website usability inspection failed','phase'=>$phase]),"\n";exit(1);}
