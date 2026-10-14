<?php
namespace App\Core {
 final class Auth {public static bool $permission=true,$server=true;public static function can($p){return $p==='live.whitelist'&&self::$permission;}public static function canAny($permissions){foreach($permissions as $p)if(self::can($p))return true;return false;}public static function canServer($id,$p){return self::$server;}public static function user(){return ['name'=>'Test','web_role'=>'founder'];}}
 final class Database {public static $db;public static function connection(){return self::$db;}}
 final class Security {public static function e($v){return htmlspecialchars((string)$v,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');}public static function csrfToken(){return 'test-csrf';}}
}
namespace App\Services {
 require dirname(__DIR__).'/src/Services/ServerConnectorInterface.php';
 final class LiveServerService {
  public static array $bridge=[];public static string $identifier='11110004';private static int $sequence=0;
  public const COMMANDS_PATH='commands';public static function server($id){return ['id'=>$id,'server_identifier'=>self::$identifier];}
  public static function status($id){return self::$bridge;}public static function newCommandId(){return 'test-'.(++self::$sequence);}
  public static function adminActionNotificationPolicy(){return ['scope'=>'admins','always_target'=>true];}
  public static function result($id,$cid){return ['success'=>true,'message'=>'OK'];}
 }
 final class TestConnector implements ServerConnectorInterface {
  public array $files=[],$reads=[],$writes=[];public function list(string $p):array{return [];}
  public function read(string $p):string{$this->reads[]=$p;if(!array_key_exists($p,$this->files))throw new \RuntimeException('HTTP 404');return $this->files[$p];}
  public function write(string $p,string $v):void{$this->writes[]=[$p,$v];$this->files[$p]=$v;}
  public function exists(string $p):bool{return array_key_exists($p,$this->files);}public function label():string{return 'test';}
 }
 final class ConnectorFactory {public static $connector;public static function make($s){return self::$connector;}}
}
namespace {
 require dirname(__DIR__).'/src/Core/Jsonc.php';
 require dirname(__DIR__).'/src/Services/LplkWhitelistAdapter.php';
 require dirname(__DIR__).'/src/Services/WhitelistLiveService.php';
 use App\Core\Auth;use App\Core\Database;use App\Services\WhitelistLiveService as W;use App\Services\LplkWhitelistAdapter as A;use App\Services\LiveServerService as Live;
 $checks=0;$ok=static function($v,$why)use(&$checks){if(!$v)throw new \RuntimeException($why);$checks++;};
 $reject=static function($fn,$why)use($ok){try{$fn();}catch(\RuntimeException|\InvalidArgumentException $e){$ok(true,$why);return;}$ok(false,$why);};
 $db=Database::$db=new \PDO('sqlite::memory:');$db->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE,PDO::FETCH_ASSOC);
 $db->exec('CREATE TABLE game_players(id INTEGER PRIMARY KEY,display_name TEXT,steam_personaname TEXT,steam_id64 TEXT,community_role TEXT,active INTEGER);CREATE TABLE server_player_groups(server_id INTEGER,player_id INTEGER,role_name TEXT);CREATE TABLE server_managed_players(server_id INTEGER,player_id INTEGER);');
 $a='76561198000000001';$b='76561198000000002';$c='76561198000000003';
 $db->exec("INSERT INTO game_players VALUES(1,'<b>Beispiel Admin</b>','','$a','admin',1),(2,'Beispiel Member','','$b','member',1),(3,'Beispiel VIP','','$c','vip',1);INSERT INTO server_managed_players VALUES(7,1),(7,2),(7,3)");
 $connector=App\Services\ConnectorFactory::$connector=new App\Services\TestConnector;
 $cfg='game/csgo/addons/swiftlys2/configs/plugins/LPLK_Whitelist/config.jsonc';
 $config=['Enabled'=>true,'Mode'=>1,'AdminBypass'=>false,'PermissionForCommands'=>'admin.ban','AddCommand'=>'wl','RemoveCommand'=>'uwl'];
 $baseline=['bridge_online'=>true,'status_age_seconds'=>1,'version'=>'1.6.4','players'=>[],'plugins'=>[['id'=>'LPLK_Whitelist','name'=>'LPLK_Whitelist','status'=>'Loaded','version'=>'0.2.2','path'=>'/evil/other']]];
 $persist=static function(array $ids,array $change=[])use($connector,$config){$connector->files[A::LIST]=implode("\n",$ids)."\n";$connector->files[A::STATUS]=json_encode(array_replace($config,['entries'=>count($ids),'updatedAt'=>gmdate('c'),'reloadError'=>null],$change));};
 $connector->files[$cfg]=json_encode(['Whitelist'=>$config]);Live::$bridge=$baseline;$persist([$b,$a]);
 $state=W::state(7);$ok($state['detected']&&$state['editable']&&$state['count']===2,'LPLK loaded, configured and editable');
 $ok($state['list_path']===A::LIST&&!in_array('/evil/other/whitelist.txt',$connector->reads,true),'Exact LPLK paths');
 $ok($state['add_command']===A::ADD&&$state['remove_command']===A::REMOVE,'Dedicated console aliases');
 $ok(array_column($state['entries'],'steam_id64')===[$a,$b],'UI role/name sorting');$ok($state['steam_ids']===[$b,$a],'File order untouched; IDs remain strings');
 $before=count($connector->writes);$same=W::addOne(7,$a);$ok(!$same['commands']&&count($connector->writes)===$before,'Existing ID produces no command');
 $add=W::addOne(7,$c);$queue=json_decode(end($connector->writes)[1],true);$ok($queue['payload']['command']===A::ADD.' '.$c&&$queue['action']==='server_command','Add through correct console command');
 $pending=$add+['created_at'=>time()];$before=count($connector->writes);$ok(W::verifyPending(7,$pending)['state']==='pending','Bridge OK alone is insufficient');
 $persist([$b,$a,$c]);$ok(W::verifyPending(7,$pending)['confirmed'],'Persisted addition and runtime confirmed');$ok(count($connector->writes)===$before,'Confirmation does not normalize file');
 $remove=W::removeMany(7,[$c]);$queue=json_decode(end($connector->writes)[1],true);$ok(!$remove['bridge_sync']&&$queue['payload']['command']===A::REMOVE.' '.$c,'LPLK removal bypasses legacy reconcile');
 $pending=$remove+['created_at'=>time()-5];$before=count($connector->writes);$ok(W::verifyPending(7,$pending)['state']==='pending'&&count($connector->writes)===$before,'No file removal fallback after 3 seconds');
 $pending['created_at']=time()-25;$ok(W::verifyPending(7,$pending)['state']==='timeout','Unconfirmed removal times out');
 $persist([$a,$b]);$ok(W::verifyPending(7,$pending)['confirmed'],'Removal confirmed from actual state');
 $ok(count(array_filter($connector->writes,static fn($x)=>$x[0]===A::LIST))===0,'Panel never writes active LPLK whitelist');
 $replace=W::replaceWith(7,$a."\n".$c);$ok($replace['added']===[$c]&&$replace['removed']===[$b]&&!$replace['bridge_sync'],'Replace issues only membership deltas');
 $before=count($connector->writes);$reject(fn()=>W::replaceWith(7,$a."\nnot-a-steamid"),'Invalid replacement rejected');$ok(count($connector->writes)===$before,'Invalid replacement queues nothing');
 foreach(['permission','server'] as $denied){Auth::${$denied}=false;$before=count($connector->reads);$ok(!W::state(7)['detected']&&count($connector->reads)===$before,'No unauthorized read');$reject(fn()=>W::addOne(7,$c),'Unauthorized mutation rejected');$reject(fn()=>W::verifyPending(7,$pending),'Revoked rights rejected');Auth::${$denied}=true;}
 Live::$identifier='production';$before=count($connector->reads);$ok(!W::state(8)['detected']&&count($connector->reads)===$before,'LPLK rollout limited to test target');Live::$identifier='11110004';
 foreach(['Unloaded','Error','Disabled',''] as $bad){Live::$bridge=$baseline;Live::$bridge['plugins'][0]['status']=$bad;$ok(!W::state(7)['detected'],'Unloaded LPLK not exposed');}
 Live::$bridge=$baseline;Live::$bridge['status_age_seconds']=60;$ok(!W::state(7)['editable'],'Stale bridge blocks writes');$reject(fn()=>W::addOne(7,$c),'Stale mutation rejected');Live::$bridge=$baseline;
 foreach([['reloadError'=>'bad'],['entries'=>99],['updatedAt'=>'invalid'],['Enabled'=>false],['Mode'=>2]] as $bad){$persist([$a,$b],$bad);$ok(!W::state(7)['editable'],'Invalid/mismatching runtime blocks writes');}
 $persist([$a,$b]);$connector->files[A::LIST]="invalid\n";$ok(!W::state(7)['editable'],'Corrupt list blocks writes');$reject(fn()=>W::addOne(7,$c),'Corrupt list is not replaced');$persist([$a,$b]);
 unset($connector->files[$cfg]);$ok(!W::state(7)['editable'],'Missing config not defaulted for LPLK');$connector->files[$cfg]=json_encode(['Whitelist'=>$config]);
 $connector->files[$cfg]=json_encode(['Whitelist'=>array_replace($config,['Enabled'=>false])]);$persist([$a,$b],['Enabled'=>false]);$ok(W::state(7)['enabled']===false&&W::state(7)['editable'],'Disabled access enforcement displayed; admin can prepare list');$connector->files[$cfg]=json_encode(['Whitelist'=>$config]);$persist([$a,$b]);
 $ok(A::parse("# comment\n$a\n// comment\n$a\n")===[$a],'Plugin-compatible comments and duplicates');
 $reject(fn()=>A::parse('76561197960265727'),'ID below Steam range rejected');$reject(fn()=>A::parse('76561202255233024'),'ID above range rejected');
 $ok(A::validId('76561197960265728')&&A::validId('76561202255233023'),'Exact plugin ID bounds');
 // Legacy remains supported; carry forward the historical sw_ console-prefix fix.
 Live::$bridge['plugins']=[['id'=>'Whitelist','name'=>'Whitelist','author'=>'verneri','status'=>'Loaded','path'=>'/home/container/game/csgo/addons/swiftlys2/plugins/Whitelist']];
 $legacy='game/csgo/addons/swiftlys2/plugins/Whitelist/whitelist.txt';$connector->files[$legacy]=$a."\n";
 $connector->files['game/csgo/addons/swiftlys2/configs/plugins/Whitelist/config.jsonc']=json_encode(['Whitelist'=>$config]);
 $old=W::state(7);$ok($old['detected']&&!$old['lplk']&&$old['add_command']==='sw_wl','Legacy detected with console prefix');
 $legacyAdd=W::addOne(7,$c);$queue=json_decode(end($connector->writes)[1],true);$ok($queue['payload']['command']==='sw_wl '.$c,'Legacy add command corrected');
 $oldRemove=W::removeMany(7,[$a]);$queue=json_decode(end($connector->writes)[1],true);$ok($oldRemove['bridge_sync']&&$queue['action']==='whitelist_remove_sync','Existing legacy remove adapter retained');
 Live::$bridge=$baseline;$whitelistManager=W::state(7);$whitelistPending=[];$server=['id'=>7];
 $source=file_get_contents(dirname(__DIR__).'/templates/server_live.php');$start=strpos($source,'<section class="panel whitelist-live-panel');$end=strpos($source,'</section>',$start)+strlen('</section>');$snippet=substr($source,$start,$end-$start);
 ob_start();eval('use App\Core\Security; use App\Core\Auth; ?>'.$snippet);$html=ob_get_clean();$ok(str_contains($html,'&lt;b&gt;Beispiel Admin&lt;/b&gt;')&&!str_contains($html,'<b>Beispiel Admin</b>'),'Names escaped');$ok(substr_count($html,'type="submit"')===4&&!str_contains($html,'type="submit" disabled'),'Four enabled administration actions');
 $whitelistManager['editable']=false;$whitelistManager['state_error']='Keine aktuelle Verbindung';ob_start();eval('use App\Core\Security; use App\Core\Auth; ?>'.$snippet);$offline=ob_get_clean();$ok(substr_count($offline,'type="submit" disabled')===4,'All four mutation buttons blocked offline');
 if(isset($argv[1])){$css=file_get_contents(dirname(__DIR__).'/public/assets/app.css');file_put_contents($argv[1],'<!doctype html><html lang="de"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Whitelist-Vorschau</title><style>'.$css.'body{padding:24px;max-width:1300px;margin:auto}</style><body><p>Beispieldaten — keine echten Spieler</p>'.$html.'</body></html>');}
 echo "Whitelist integration: $checks checks passed\n";
}
