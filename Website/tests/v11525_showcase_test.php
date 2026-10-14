<?php
namespace App\Core {
 final class Auth {public static bool $players=true,$server=true;public static function can($p){return self::$players;}public static function canServer($id,$p){return self::$server&&$id===7;}public static function canAny($p){return false;}}
 final class Database {public static $db;public static function connection(){return self::$db;}}
 final class Security {public static function e($s){return htmlspecialchars((string)$s,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');}public static function csrfToken(){return 'test';}}
}
namespace App\Services {
 final class ShowcaseConnector {public array $files=[],$reads=[];public function read($path){$this->reads[]=$path;return $this->files[$path]??throw new \RuntimeException('missing');}public function exists($path){return isset($this->files[$path]);}}
 final class ConnectorFactory {public static $connector;public static function make($s){return self::$connector;}}
 final class LiveServerService {public static string $identifier='11110004';public static function server($id){return ['server_identifier'=>self::$identifier];}}
}
namespace {
 use App\Services\PlayerShowcaseService as Showcase;use App\Services\PlayerStatisticsService as Stats;use App\Core\Auth;
 foreach(['BankNetworkPlan','PlayerEconomyService','ServerRankingService','PlayerStatisticsService','PlayerShowcaseService'] as $class)require dirname(__DIR__).'/src/Services/'.$class.'.php';
 require dirname(__DIR__).'/src/Controllers/PlayerShowcaseController.php';
 $n=0;$ok=static function($test,$message)use(&$n){$n++;if(!$test)throw new \RuntimeException($message);};
 $steam='76561198000000000';$raw=json_decode(file_get_contents(__DIR__.'/fixtures/lplk-statistics.json'),true);$raw['generatedAt']=gmdate('c');$data=Stats::parse(json_encode($raw),$steam);
 $player=['id'=>12,'display_name'=>'Alter Name','steam_personaname'=>'Neuer Name','steam_id64'=>$steam,'steam_profile_url'=>'javascript:alert(1)','steam_last_synced_at'=>null,'steam_visibility'=>3];
 $identity=Showcase::identity($player);$ok($identity['name']==='Neuer Name','Steam name takes priority');$ok($identity['profileUrl']==='https://steamcommunity.com/profiles/'.$steam,'Canonical identity-based Steam link ignores unsafe stored URL');
 $ok($identity['avatarUrl']==='/players/12/avatar'&&$identity['syncedAt']===null,'Known cached avatar is accessible even without sync timestamp');
 $x=$player;$x['steam_id64']='../private';$id=Showcase::identity($x);$ok($id['avatarUrl']===null&&$id['profileUrl']===null,'Invalid identity never forms URL');
 $x=$player;$x['steam_personaname']='';$ok(Showcase::identity($x)['name']==='Alter Name','Display name fallback');$x['display_name']='';$ok(Showcase::identity($x)['initial']==='S','Empty identity fallback');
 $ok(Showcase::date('2026-10-14T12:00:00Z')==='14.10.2026 14:00'&&Showcase::date('2026-01-14T12:00:00Z')==='14.01.2026 13:00','Berlin timezone across DST');$ok(Showcase::date('not-a-date')===null,'Invalid date hidden');
 $summary=Showcase::compose($data,['state'=>'unavailable'],$steam);
 $ok(count($summary['cards'])===2&&$summary['cards'][0]['kind']==='milestone','Personal milestones work without ranking');
 $ok($summary['facts'][0]['value']==='100'&&$summary['facts'][1]['value']==='1,25'&&$summary['facts'][2]['value']==='45,0 %','Only PvP counts and recomputed ratios');
 $ok(str_contains($summary['facts'][3]['label'],'Bottraining'),'Mixed playtime is labelled honestly');
 $zero=$data;foreach(['pvp','extra'] as $g)foreach($zero[$g] as $k=>$v)if(is_int($v))$zero[$g][$k]=0;
 $s=Showcase::compose($zero,['state'=>'unavailable'],$steam);$ok(!$s['cards']&&$s['facts'][1]['value']==='—'&&$s['facts'][2]['value']==='—','No invented ranking or division by zero');
 $x=$data;$x['pvp']['Kills']=24;$x['pvp']['Headshots']=24;$x['extra']=$zero['extra'];$s=Showcase::compose($x,['state'=>'unavailable'],$steam);$ok(!$s['cards'],'High percentage needs minimum kills');
 $x['pvp']['Kills']=25;$x['pvp']['Headshots']=10;$ok(Showcase::compose($x,['state'=>'unavailable'],$steam)['cards'][0]['key']==='headshots','Threshold boundary qualifies');
 $x['pvp']['Headshots']=26;$ok(Showcase::compose($x,['state'=>'unavailable'],$steam)['state']==='unavailable','Contradictory ratio refused');
 $x=$zero;$x['extra']['UnknownWeaponOrigin']=1000;$ok(!Showcase::compose($x,['state'=>'unavailable'],$steam)['cards'],'Unknown weapon origins never counted');
 $x=$data;$x['collection']['PvpEnabled']=false;$s=Showcase::compose($x,['state'=>'unavailable'],$steam);$ok($s['state']==='disabled'&&!$s['cards']&&!$s['facts'],'Disabled PvP does not surface old achievements');
 $rank=['schema'=>1,'profile'=>'standard','generatedAt'=>gmdate('c'),'collection'=>['PvpEnabled'=>true,'BotTrainingEnabled'=>true,'MinShots'=>250,'MinKills'=>25,'MinBotShots'=>250,'MinBotKills'=>25,'CountPvpWarmup'=>false,'CountTeamKills'=>false],'boards'=>array_fill_keys(['rating','kills','headshots','pickup','playtime','botAccuracy'],[])];
 $board=[];for($i=0;$i<5;$i++)$board[]=['rank'=>$i+1,'steamId'=>$i===1?$steam:(string)(76561198000000001+$i),'name'=>'Player '.$i,'value'=>100-$i*10,'shots'=>1000,'kills'=>100];
 $rank['boards']['headshots']=$board;
 $rank=App\Services\ServerRankingService::parse(json_encode($rank));$s=Showcase::compose($data,['state'=>'ready','data'=>$rank],$steam);
 $ok($s['cards'][0]['kind']==='ranking'&&$s['cards'][0]['value']==='Platz 2','Real qualified podium rank shown');
 $ok(count(array_filter($s['cards'],fn($c)=>$c['key']==='headshots'))===1,'No duplicate headshot rank and milestone');
 $small=$rank;$small['boards']['headshots']=array_slice($board,0,4);$s=Showcase::compose($data,['state'=>'ready','data'=>$small],$steam);$ok(!array_filter($s['cards'],fn($c)=>$c['kind']==='ranking'),'Small comparison group does not award podium');
 $tie=$rank;$tie['boards']['headshots'][2]['rank']=2;$tie['boards']['headshots'][2]['value']=90;$s=Showcase::compose($data,['state'=>'ready','data'=>$tie],$steam);$ok($s['cards'][0]['value']==='Geteilter Platz 2','Tied rank explicitly labelled');
 $full=$rank;foreach(['rating','kills','pickup'] as $metric)$full['boards'][$metric]=$board;$s=Showcase::compose($data,['state'=>'ready','data'=>$full],$steam);$ok(count($s['cards'])===3,'Banner capped to three highlights');
 $s=Showcase::compose($data,['state'=>'stale','data'=>$full],$steam);$ok(!array_filter($s['cards'],fn($c)=>$c['kind']==='ranking'),'Unavailable ranking cannot leak stale podium');
 $c=App\Services\ConnectorFactory::$connector=new App\Services\ShowcaseConnector();$root='game/csgo/addons/swiftlys2/data/LPLK_Statistics';
 $runtime=['Enabled'=>true,'PvpEnabled'=>true,'updatedAt'=>gmdate('c'),'version'=>'0.2.0','saveError'=>null,'reloadError'=>null];
 $c->files[$root.'/status.json']=json_encode($runtime);$c->files[$root.'/web/'.$steam.'.json']=json_encode($raw);$c->files[$root.'/web/ranking.json']=json_encode($rank);
 $ok(Showcase::read(7,$steam)['state']==='ready','Actual guarded statistics and ranking services integrate');$ok(count($c->reads)===4,'One account and ranking snapshot with two status reads; no all-player scan');
 foreach(['PvpEnabled'=>false,'version'=>'0.1.0','saveError'=>'failed','reloadError'=>'failed'] as $field=>$value){$x=$runtime;$x[$field]=$value;$c->files[$root.'/status.json']=json_encode($x);$state=Showcase::read(7,$steam);$ok($state['state']!=='ready'&&!$state['cards'],'Runtime flag or error overrides saved player export: '.$field);}$c->files[$root.'/status.json']=json_encode($runtime);
 App\Core\Database::$db=new \PDO('sqlite::memory:');App\Core\Database::$db->exec("CREATE TABLE game_players(id INTEGER,steam_id64 TEXT);INSERT INTO game_players VALUES(12,'$steam');");
 $invoke=static function($id){http_response_code(200);ob_start();(new App\Controllers\PlayerShowcaseController())->highlights($id);return [http_response_code(),json_decode(ob_get_clean(),true)];};
 $_GET=['server'=>'7'];$before=count($c->reads);Auth::$players=false;[$status,$out]=$invoke('12');$ok($status===403&&count($c->reads)===$before,'Denied player permission before remote reads');Auth::$players=true;
 $_GET=['server'=>'8'];[$status,$out]=$invoke('12');$ok($status===403&&count($c->reads)===$before,'Server permission before remote reads');
 $_GET=['server'=>['7']];[$status,$out]=$invoke('12');$ok($status===403,'Malformed server parameter rejected');
 $_GET=['server'=>'7'];[$status,$out]=$invoke('999');$ok($status===404&&count($c->reads)===$before,'Missing player no remote reads');[$status,$out]=$invoke('../12');$ok($status===404,'Malformed player ID rejected');
 [$status,$out]=$invoke('12');$ok($status===200&&$out['state']==='ready','Read-only JSON endpoint returns highlights');
 App\Services\LiveServerService::$identifier='production';$before=count($c->reads);[$status,$out]=$invoke('12');$ok($out['state']==='not_installed'&&count($c->reads)===$before,'Production stats remain gated');App\Services\LiveServerService::$identifier='11110004';
 $c->files[$root.'/status.json']=json_encode(['Enabled'=>true,'updatedAt'=>'2000-01-01']);[$status,$out]=$invoke('12');$ok($out['state']==='stale'&&!$out['cards'],'Stale source never displays zero stats');
 $player+=['community_role'=>'spieler','main_player_id'=>null,'steam_sync_error'=>null];$player['steam_personaname']='<script>alert(1)</script>';$isSecondary=false;$secondaryAccounts=[];$roles=['spieler'=>'Spieler'];$canSteamSync=false;$steamConfigured=false;$showcaseServers=[['id'=>7,'name'=>'Test <script>','server_identifier'=>'11110004']];
 ob_start();require dirname(__DIR__).'/templates/player_showcase.php';$html=ob_get_clean();$ok(!str_contains($html,'<script>')&&str_contains($html,'&lt;script&gt;'),'Profile and server text escaped');$ok(str_contains($html,'/players/12/avatar')&&str_contains($html,'steamcommunity.com/profiles/'),'Avatar and profile visible without prior sync marker');$ok(!str_contains($html,'javascript:')&&!str_contains($html,'action="/players/12/steam-sync"'),'Unsafe stored profile link ignored and sync control permission guarded');
 $showcaseServers=[];ob_start();require dirname(__DIR__).'/templates/player_showcase.php';$html=ob_get_clean();$ok(str_contains($html,'Kein freigegebener Server')&&!str_contains($html,'/servers/0/ranking'),'No server permission leaves identity usable');
 echo json_encode(['checks'=>$n,'passed'=>true,'productionTouched'=>false],JSON_PRETTY_PRINT)."\n";
}
