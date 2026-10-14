<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 $root='/var/www/serververwaltung';if(realpath($root)!==$root)throw new RuntimeException('Scope');
 $files=['VERSION','templates/layout.php','src/Services/PlayerShowcaseService.php','src/Services/PlayerStatisticsService.php','src/Services/ServerRankingService.php','src/Services/WeaponRankingService.php','src/Controllers/PlayerEconomyController.php','templates/server_ranking.php','templates/weapon_ranking.php','public/assets/player-showcase.js','tests/v11527_weapons_test.php','tests/v11524_economy_test.php','tests/fixtures/lplk-weapons.json','bin/install.php','bin/migrate.php','README.md','CHANGELOG.md','INSTALLATION.md','KURZINSTALLATION.txt','UPDATE_VON_1.15.2.6_AUF_1.15.2.7.md','AUSLIEFERUNG_UND_UPDATEBEFEHLE.txt'];
 $hashes=[];$normalized=[];foreach($files as $file){$path=$root.'/'.$file;if(is_link($path))throw new RuntimeException('Linked file');$hashes[$file]=is_file($path)?hash_file('sha256',$path):null;$normalized[$file]=is_file($path)?hash('sha256',str_replace("\r\n","\n",file_get_contents($path))):null;}
 $version=trim(file_get_contents($root.'/VERSION'));$report=['version'=>$version,'hashes'=>$hashes,'normalizedHashes'=>$normalized,'productionGameServersChanged'=>false];
 spl_autoload_register(static function($class)use($root){if(str_starts_with($class,'App\\')){$path=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($path))require $path;}});
 App\Core\Config::load($root.'/config/app.php');$pdo=App\Core\Database::connection();
 $report['knownProfiles']=$pdo->query("SELECT COUNT(*) total,SUM(CASE WHEN steam_personaname IS NOT NULL AND steam_personaname<>'' THEN 1 ELSE 0 END) withSteamName,SUM(CASE WHEN COALESCE(steam_avatar_full_url,steam_avatar_medium_url,steam_avatar_url,'')<>'' THEN 1 ELSE 0 END) withAvatarSource FROM game_players")->fetch(PDO::FETCH_ASSOC);
 if(in_array($version,['1.15.2.6','1.15.2.7'],true)){
  $admin=$pdo->query("SELECT id,auth_version FROM users WHERE active=1 AND web_role='founder' ORDER BY id LIMIT 1")->fetch();if(!$admin)throw new RuntimeException('Verification identity');
  $_SESSION=['user_id'=>(int)$admin['id'],'auth_version'=>max(1,(int)$admin['auth_version']),'_presence_touch'=>time()];
  $playerId=$pdo->query('SELECT id FROM game_players ORDER BY id LIMIT 1')->fetchColumn();
  if($playerId){
   $_SERVER['REQUEST_URI']='/players/'.(int)$playerId;http_response_code(200);ob_start();(new App\Controllers\PlayersController())->view((string)$playerId);$html=ob_get_clean();
   $report['profilePage']=['httpStatus'=>http_response_code()?:200,'banner'=>str_contains($html,'data-player-showcase'),'avatar'=>str_contains($html,'data-showcase-avatar'),'steamLink'=>str_contains($html,'https://steamcommunity.com/profiles/'),'assets'=>str_contains($html,'/assets/player-showcase.css?v='.$version)&&str_contains($html,'/assets/player-showcase.js?v='.$version)];
   $_GET=['server'=>'7'];ob_start();(new App\Controllers\PlayerShowcaseController())->highlights((string)$playerId);$json=ob_get_clean();$payload=json_decode($json,true,32,JSON_THROW_ON_ERROR);
   $report['highlights']=['httpStatus'=>http_response_code()?:200,'state'=>$payload['state'],'cards'=>count($payload['cards']??[])];
   if($version==='1.15.2.7'){
    $weapons=App\Services\WeaponRankingService::read(7);$report['weaponRanking']=['state'=>$weapons['state'],'boards'=>isset($weapons['data']['boards'])?count($weapons['data']['boards']):null,'rankedRows'=>isset($weapons['data']['boards'])?array_sum(array_map('count',$weapons['data']['boards'])):null];
    $_GET=['weapon'=>'taser'];$_SERVER['REQUEST_URI']='/servers/7/ranking?weapon=taser';http_response_code(200);ob_start();(new App\Controllers\PlayerEconomyController())->ranking('7');$rankingHtml=ob_get_clean();
    $report['rankingPage']=['httpStatus'=>http_response_code()?:200,'weaponGroups'=>substr_count($rankingHtml,'<optgroup'),'selectedZeus'=>str_contains($rankingHtml,'value="taser" selected'),'section'=>str_contains($rankingHtml,'id="waffen"')];
   }
  }
 }
 $_SESSION=[];ob_end_clean();echo json_encode($report,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES),"\n";
}catch(Throwable){$_SESSION=[];if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Player showcase website inspection failed']),"\n";exit(1);}
