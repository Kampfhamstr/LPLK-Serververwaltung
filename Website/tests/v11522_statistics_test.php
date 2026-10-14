<?php
namespace App\Core {
 final class Auth {public static bool $players=true,$server=true;public static function can($p){return self::$players;}public static function canServer($id,$p){return self::$server;}}
 final class Security {public static function e($v){return htmlspecialchars((string)$v,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');}}
}
namespace App\Services {
 final class LiveServerService {public static string $id='11110004';public static function server($id){return ['server_identifier'=>self::$id];}}
 final class TestConnector {public array $reads=[];public bool $exists=true;public array $status=['Enabled'=>true];public string $fixture='';public function read($path){$this->reads[]=$path;return str_ends_with($path,'status.json')?json_encode($this->status+['updatedAt'=>gmdate('c')]):$this->fixture;}public function exists($p){return $this->exists;}}
 final class ConnectorFactory {public static $connector;public static function make($server){return self::$connector;}}
}
namespace {
 require dirname(__DIR__).'/src/Services/PlayerStatisticsService.php';
 use App\Services\PlayerStatisticsService as Stats;use App\Services\ConnectorFactory as Factory;use App\Core\Auth;
 $checks=0;$check=static function($ok,$why)use(&$checks){if(!$ok)throw new \RuntimeException($why);$checks++;};
 $fixture=file_get_contents($argv[1]);$steam='76561198000000000';$parsed=Stats::parse($fixture,$steam);
 $check($parsed['extra']['EnemyWeapon']===9,'Pickup fields passed');$check($parsed['pvp']['Kills']===100&&$parsed['bots']['Kills']===40,'PvP and training separated');
 $connector=Factory::$connector=new App\Services\TestConnector();$connector->fixture=$fixture;
 $check(Stats::read(7,$steam)['state']==='ready','Read own profile');$check(count($connector->reads)===2,'Bounded status and one player export');
 foreach(['player','server'] as $denied){Auth::$players=$denied!=='player';Auth::$server=$denied!=='server';$before=count($connector->reads);try{Stats::read(7,$steam);throw new \LogicException('Access accepted');}catch(\RuntimeException){}$check(count($connector->reads)===$before,'Authorization before connector');}Auth::$players=Auth::$server=true;
 App\Services\LiveServerService::$id='production';$before=count($connector->reads);$check(Stats::read(3,$steam)['state']==='not_installed'&&count($connector->reads)===$before,'No production data accessed');App\Services\LiveServerService::$id='11110004';
 $connector->exists=false;$check(Stats::read(7,$steam)['state']==='empty','Missing player not zero');$connector->exists=true;
 $connector->status=['Enabled'=>false];$check(Stats::read(7,$steam)['state']==='disabled','Disabled module explicit');
 $connector->status=['Enabled'=>true,'updatedAt'=>gmdate('c',time()-300)];$check(Stats::read(7,$steam)['state']==='stale','Stale module not live');
 $connector->status=['Enabled'=>true];
 foreach(['steamId'=>'76561198000000001','schema'=>2,'profile'=>'zombie'] as $key=>$value){$bad=json_decode($fixture,true);$bad[$key]=$value;try{Stats::parse(json_encode($bad),$steam);throw new \LogicException('Wrong identity accepted');}catch(\RuntimeException){$checks++;}}
 $bad=json_decode($fixture,true);$bad['extra']['EnemyWeapon']=-1;try{Stats::parse(json_encode($bad),$steam);throw new \LogicException('Negative accepted');}catch(\RuntimeException){$checks++;}
 $connector->fixture='{broken';$check(Stats::read(7,$steam)['state']==='unavailable','Corruption not zero');
 $player=['id'=>1,'display_name'=>'<script>alert(1)</script>','steam_id64'=>$steam];$servers=[['id'=>7,'name'=>'[TEST] Beispielserver']];$selected=7;$statistics=['state'=>'ready','data'=>$parsed];
 ob_start();include dirname(__DIR__).'/templates/player_statistics.php';$html=ob_get_clean();$check(!str_contains($html,'<script>')&&str_contains($html,'&lt;script&gt;'),'Escaped player identity');$check(str_contains($html,'unbekannter Herkunft')&&str_contains($html,'Bottraining'),'Definitions shown');
 if(isset($argv[2])){
  $player['display_name']='Beispielspieler — synthetische Vorschau';ob_start();include dirname(__DIR__).'/templates/player_statistics.php';$body=ob_get_clean();
  $css=file_get_contents(dirname(__DIR__).'/public/assets/app.css');
  file_put_contents($argv[2],'<!doctype html><html lang="de"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Statistik-Vorschau</title><style>'.$css.' body{padding:24px;max-width:1100px;margin:auto} summary{cursor:pointer;padding:14px 0} th{text-align:left}</style><body><p><strong>Synthetische Vorschau — keine echten Spielerdaten</strong></p>'.$body.'</body></html>');
 }
 echo "Player statistics: $checks checks passed\n";
}
