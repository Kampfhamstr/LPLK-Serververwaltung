<?php
namespace App\Core {
 final class Auth {public static bool $allowed=true;public static function can($p){return self::$allowed;}public static function canServer($id,$p){return self::$allowed&&$id===7;}}
 final class Security {public static function e($v){return htmlspecialchars((string)$v,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');}}
}
namespace App\Services {
 final class WeaponTestConnector {public array $files=[],$reads=[];public function read($p){$this->reads[]=$p;return $this->files[$p]??throw new \RuntimeException('missing');}}
 final class ConnectorFactory {public static $connector;public static function make($s){return self::$connector;}}
 final class LiveServerService {public static string $identifier='11110004';public static function server($id){return ['server_identifier'=>self::$identifier];}}
}
namespace {
 use App\Services\WeaponRankingService;use App\Services\PlayerShowcaseService;use App\Core\Security;
 foreach(['BankNetworkPlan','PlayerEconomyService','PlayerStatisticsService','WeaponRankingService','PlayerShowcaseService'] as $class)require dirname(__DIR__).'/src/Services/'.$class.'.php';
 $checks=0;$ok=static function($condition,$name)use(&$checks){$checks++;if(!$condition)throw new \RuntimeException($name);};
 $reject=static function($data,$name)use($ok){try{WeaponRankingService::parse(json_encode($data));}catch(\Throwable){$ok(true,$name);return;}$ok(false,$name);};
 $raw=json_decode(file_get_contents(__DIR__.'/fixtures/lplk-weapons.json'),true);$raw['generatedAt']=gmdate('c');
 $data=WeaponRankingService::parse(json_encode($raw));$ok(count($data['boards'])===44,'C# exporter and PHP catalog agree');
 $ok(array_column($data['boards']['knife'],'rank')===[1,1,3,4,5],'Ties and rank gaps from actual C# fixture');
 $ok(count($data['boards']['taser'])===4,'Zeus separate from knife');
 foreach(['../knife','<script>',[],null,'unknown'] as $bad)$ok(WeaponRankingService::selected($bad)==='knife','Untrusted selector safely defaults');
 $ok(WeaponRankingService::selected('taser')==='taser','Valid weapon selection');
 $x=$raw;$x['boards']['knife'][1]['steamId']=$x['boards']['knife'][0]['steamId'];$reject($x,'Duplicate player rejected');
 foreach([0,-1,1.5,2000000000001] as $value){$x=$raw;$x['boards']['knife'][0]['value']=$value;$reject($x,'Invalid kill count');}
 $x=$raw;$x['boards']['knife'][0]['rank']=2;$reject($x,'Incorrect rank rejected');
 $x=$raw;$x['boards']['knife'][2]['value']=100;$reject($x,'Wrong ordering rejected');
 $x=$raw;$x['boards']['world']=[];$reject($x,'Unknown catalog entry rejected');
 $x=$raw;unset($x['boards']['taser']);$reject($x,'Incomplete catalog rejected');
 $x=$raw;$x['collection']['WeaponTracking']=false;$reject($x,'Disabled populated export rejected');
 $x=$raw;$x['generatedAt']='2000-01-01T00:00:00Z';$reject($x,'Stale export rejected');
 $steam='76561198000000000';$player=App\Services\PlayerStatisticsService::parse(file_get_contents(__DIR__.'/fixtures/lplk-statistics.json'),$steam);
 foreach(['pvp','extra'] as $group)foreach($player[$group] as $key=>$value)if(is_int($value))$player[$group][$key]=0;
 $show=PlayerShowcaseService::compose($player,['state'=>'unavailable'],$steam,['state'=>'ready','data'=>$data]);
 $ok($show['cards'][0]['weapon']==='knife'&&$show['cards'][0]['value']==='Geteilter Platz 1','Knife podium is honest about ties');
 $ok(!array_filter($show['cards'],fn($c)=>($c['weapon']??null)==='taser'),'Fewer than five competitors does not award Zeus podium');
 $ok(str_contains($show['rules'],'ohne Warmup')&&str_contains($show['rules'],'mit Teamkills'),'Weapon rules shown separately');
 $player['collection']['PvpEnabled']=false;$show=PlayerShowcaseService::compose($player,['state'=>'unavailable'],$steam,['state'=>'ready','data'=>$data]);
 $ok($show['state']==='ready'&&count($show['cards'])>0&&!$show['facts'],'Weapon ranking works when PvP counters disabled');
 $player['collection']['WeaponTracking']=false;$ok(!PlayerShowcaseService::compose($player,['state'=>'unavailable'],$steam,['state'=>'ready','data'=>$data])['cards'],'Disabled weapon collection cannot display old podium');
 $connector=App\Services\ConnectorFactory::$connector=new App\Services\WeaponTestConnector();$root='game/csgo/addons/swiftlys2/data/LPLK_Statistics';
 $status=['version'=>'0.2.1','Enabled'=>true,'WeaponTracking'=>true,'updatedAt'=>gmdate('c'),'saveError'=>null,'reloadError'=>null,'weaponExportError'=>null];
 $connector->files[$root.'/status.json']=json_encode($status);$connector->files[$root.'/web/weapons.json']=json_encode($raw);
 $ok(WeaponRankingService::read(7)['state']==='ready','New runtime reads actual export');$ok(count($connector->reads)===2,'No per-player remote scan');
 foreach(['WeaponTracking'=>false,'Enabled'=>false,'version'=>'0.2.0','weaponExportError'=>'failed','updatedAt'=>'2000-01-01'] as $key=>$value){$x=$status;$x[$key]=$value;$connector->files[$root.'/status.json']=json_encode($x);$ok(WeaponRankingService::read(7)['state']!=='ready','Runtime or freshness overrides saved export');}
 App\Services\LiveServerService::$identifier='production';$before=count($connector->reads);$ok(WeaponRankingService::read(7)['state']==='unavailable'&&count($connector->reads)===$before,'Production gate before remote read');
 App\Core\Auth::$allowed=false;try{WeaponRankingService::read(7);$denied=false;}catch(\Throwable){$denied=true;}$ok($denied&&count($connector->reads)===$before,'Permission denied before remote access');
 $server=['id'=>7];$selectedWeapon='knife';$_GET=['weapon'=>'knife'];$weaponRanking=['state'=>'ready','data'=>$data];$weaponRanking['data']['boards']['knife'][0]['name']='<script>alert(1)</script>';
 ob_start();require dirname(__DIR__).'/templates/weapon_ranking.php';$html=ob_get_clean();
 $ok(str_contains($html,'&lt;script&gt;')&&!str_contains($html,'<script>'),'Player names escaped');$ok(substr_count($html,'<table>')===1&&substr_count($html,'<optgroup')===8,'Single weapon table with eight groups');
 $selectedWeapon='hegrenade';ob_start();require dirname(__DIR__).'/templates/weapon_ranking.php';$html=ob_get_clean();$ok(str_contains($html,'noch keine Kills')&&!str_contains($html,'<table>'),'Empty selected weapon honest state');
 echo json_encode(['checks'=>$checks,'passed'=>true,'syntheticDataOnly'=>true],JSON_PRETTY_PRINT)."\n";
}
