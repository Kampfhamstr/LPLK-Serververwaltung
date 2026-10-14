<?php
namespace App\Core {
 final class Auth {
  public static array $denied=[]; public static bool $server=true,$target=true,$founder=true;
  public static function can($p){return !in_array($p,self::$denied,true);}public static function canServer($id,$p){return self::$server;}public static function canManageGameRoleTarget($r){return self::$target;}public static function canAssignGameRole($s,$r){return self::$target;}
  public static function user(){return ['id'=>7,'steam_id64'=>'76561198000000002'];}public static function isFounder(){return self::$founder;}
 }
 final class Database {public static $db;public static function connection(){return self::$db;}}
 final class Audit {public static array $calls=[];public static function log(string $a,string $t,?int $id=null,array $d=[]):void{self::$calls[]=[$a,$t,$id,$d];}}
 final class Security {public static function e($s){return htmlspecialchars((string)$s,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');}public static function csrfToken(){return 'test';}}
}
namespace App\Services {
 require dirname(__DIR__).'/src/Services/ServerConnectorInterface.php';
 final class FakeConnector implements ServerConnectorInterface {
  public array $files=[],$reads=[],$writes=[];
  public function read(string $p):string{$this->reads[]=$p;return $this->files[$p]??throw new \RuntimeException('missing');}public function write(string $p,string $s):void{$this->writes[]=$p;$this->files[$p]=$s;}public function exists(string $p):bool{return isset($this->files[$p]);}public function list(string $p):array{return [];}public function label():string{return 'test';}
 }
 final class ConnectorFactory {public static $c;public static function make($s){return self::$c;}}
 final class LiveServerService {public static string $identifier='11110004';public static array $commands=[];public static function server($id){return ['id'=>$id,'server_identifier'=>self::$identifier];}public static function queue($s,$a,$p){self::$commands[]=[$s,$a,$p];return 'cmd';}}
}
namespace {
 use App\Core\Auth;use App\Services\PlayerEconomyService as E;use App\Services\ServerRankingService as R;use App\Services\BankNetworkPlan as N;
 foreach(['BankNetworkPlan','BankStatement','PlayerEconomyService','ServerRankingService','WeaponRankingService'] as $class)require dirname(__DIR__).'/src/Services/'.$class.'.php';
 $n=0;$ok=static function($v,$why)use(&$n){$n++;if(!$v)throw new \RuntimeException($why);};$bad=static function($fn,$why)use($ok){try{$fn();}catch(\Throwable){$ok(true,$why);return;}$ok(false,$why);};
 $db=App\Core\Database::$db=new \PDO('sqlite::memory:');$db->exec('CREATE TABLE server_player_groups(server_id INTEGER,player_id INTEGER,role_name TEXT);');
 $c=App\Services\ConnectorFactory::$c=new App\Services\FakeConnector;$steam='76561198000000001';$player=['id'=>1,'display_name'=>'<script>Test</script>','community_role'=>'spieler','steam_id64'=>$steam];
 $d=json_decode(file_get_contents(__DIR__.'/fixtures/lplk-economy.json'),true,32,JSON_THROW_ON_ERROR);$d['generatedAt']=gmdate('c');
 $status=['version'=>'0.9.0','updatedAt'=>gmdate('c'),'profile'=>'standard-test-11110004','effective'=>true,'healthy'=>true,'features'=>['bank'=>true],'web'=>['ExportEnabled'=>true,'AdminChangesEnabled'=>true,'error'=>null],'settings'=>['AllowEditPlayerBank'=>true,'Limit'=>100000,'MaxManualBankSet'=>50000],'period'=>$d['period'],'reloadError'=>null];
 $c->files[E::ROOT.'/status.json']=json_encode($status);$c->files[E::ROOT.'/web/'.$steam.'.json']=json_encode($d);
 $ok(E::read(7,$steam)['state']==='ready','real C# player export parses');
 foreach(['balance','revision','period','pendingCash','generatedAt','steamId','profile'] as $field){$x=$d;$x[$field]=['balance'=>-1,'revision'=>0,'period'=>0,'pendingCash'=>1,'generatedAt'=>'2000-01-01','steamId'=>'../escape','profile'=>'production'][$field];$bad(fn()=>E::parse(json_encode($x),$steam),'bad export '.$field);}
 foreach(['players.view'] as $right){Auth::$denied=[$right];$before=count($c->reads);$bad(fn()=>E::read(7,$steam),'denied read');$ok(count($c->reads)===$before,'denied before network');}Auth::$denied=[];
 Auth::$server=false;$bad(fn()=>E::read(7,$steam),'server scope');Auth::$server=true;
 App\Services\LiveServerService::$identifier='11110001';$before=count($c->reads);$ok(E::read(7,$steam)['state']==='unavailable'&&count($c->reads)===$before,'production not activated');App\Services\LiveServerService::$identifier='11110004';
 foreach(['effective','healthy'] as $flag){$x=$status;$x[$flag]=false;$c->files[E::ROOT.'/status.json']=json_encode($x);$ok(E::read(7,$steam)['state']==='unavailable','disabled status '.$flag);}$c->files[E::ROOT.'/status.json']=json_encode($status);
 foreach(['economy.manage','players.manage'] as $right){Auth::$denied=[$right];$before=count($c->writes);$bad(fn()=>E::prepare(7,$player,'add','5','Korrekturprüfung'),'missing write right');$ok(count($c->writes)===$before,'no unauthorized write');}Auth::$denied=[];
 Auth::$target=false;$bad(fn()=>E::prepare(7,$player,'add','5','Korrekturprüfung'),'protected target');Auth::$target=true;
 foreach([['x','1','Valid reason'],['add','-1','Valid reason'],['add','1e3','Valid reason'],['add','9000000000000001','Valid reason'],['add','1','x'],['add','1',"line\nbreak"],['remove','1','Overdraft check']] as $args)$bad(fn()=>E::prepare(7,$player,...$args),'invalid form');
 $p=E::prepare(7,$player,'add','500','Prüfung <b>Test</b>');$ok($p['before']===0&&$p['after']===500&&count($c->writes)===0,'review has no side effects');$ok($p['request']['UserId']===7&&$p['request']['ActorRank']===100,'actor from authenticated session');
 $result=E::submit($p,$player);$ok($result['state']==='pending'&&count($c->writes)===1,'queue is pending not success');$key=substr($p['request']['Id'],4);$cmd=end(App\Services\LiveServerService::$commands);$ok($cmd[2]['command']==='sw_lplk_bank_request '.$key,'only fixed server command');
 $ok(E::result($p)['state']==='pending','bridge response not financial confirmation');
 Auth::$founder=false;$before=count($c->writes);$bad(fn()=>E::submit($p,$player),'role downgrade invalidates pending authority');$ok(count($c->writes)===$before,'downgrade writes nothing');Auth::$founder=true;
 $bad(fn()=>E::prepare(7,$player,'set','50001','Adminlimit prüfen'),'manual set cap before confirmation');$bad(fn()=>E::prepare(7,$player,'add','100001','Kontolimit prüfen'),'account cap before confirmation');
 $receipt=['Request'=>$p['request'],'Before'=>0,'After'=>500,'AppliedAt'=>gmdate('c')];$c->files[E::ROOT.'/web-results/'.$key.'.json']=json_encode(['schema'=>1,'id'=>$key,'state'=>'applied','receipt'=>$receipt]);
 $before=count($c->writes);$ok(E::submit($p,$player)['state']==='applied'&&count($c->writes)===$before,'confirmed retry retrieves receipt without new queue');
 $receipt['Request']['Amount']=1;$c->files[E::ROOT.'/web-results/'.$key.'.json']=json_encode(['schema'=>1,'id'=>$key,'state'=>'applied','receipt'=>$receipt]);$bad(fn()=>E::result($p),'changed receipt rejected');
 $c->files[E::ROOT.'/web-results/'.$key.'.json']=json_encode(['schema'=>1,'id'=>$key,'state'=>'rejected','message'=>'Prüfung']);$ok(E::result($p)['state']==='rejected','rejected receipt visible');
 $fixture=__DIR__.'/fixtures/lplk-ranking.json';$r=json_decode(file_get_contents($fixture),true,32,JSON_THROW_ON_ERROR);$r['generatedAt']=gmdate('c');$ok(count(R::parse(json_encode($r))['boards']['rating'])>0,'real C# ranking parses');
 $x=$r;$x['boards']['rating'][0]['rank']=9;$bad(fn()=>R::parse(json_encode($x)),'rank tampering');$x=$r;$x['boards']['rating'][0]['shots']=0;$bad(fn()=>R::parse(json_encode($x)),'qualification checked');$x=$r;$x['collection']['PvpEnabled']=false;$bad(fn()=>R::parse(json_encode($x)),'disabled stats no active board');
 $ok(N::group(N::TEST)['id']!==N::group(N::PRODUCTION[0])['id'],'test isolated');$ok(N::group('5642c90d')===null,'TS3 excluded');
 $snap=[];foreach(N::PRODUCTION as $i=>$id)$snap[]=['server'=>$id,'sha256'=>str_repeat((string)($i+1),64),'databaseIdentity'=>'source-'.$id,'period'=>'2026-10','capturedAt'=>gmdate('c'),'frozen'=>true,'pending'=>0,'accounts'=>[['steamId'=>$steam,'balance'=>100+$i]]];
 $plan=N::inventory($snap);$ok(!$plan['activatable']&&$plan['mergeRule']===null&&!$plan['blockers'],'complete inventory still requires approval');$ok(count($plan['players'])===1&&$plan['players'][0]['multipleServers']&&$plan['players'][0]['candidateSum']===615&&$plan['players'][0]['selectedBalance']===null,'overlapping accounts have alternatives no adopted balance');
 $x=$snap;$x[0]['server']=N::TEST;$bad(fn()=>N::inventory($x),'test cannot enter production');$x=$snap;$x[1]['server']=$x[0]['server'];$bad(fn()=>N::inventory($x),'duplicate server');$x=$snap;$x[1]['databaseIdentity']=$x[0]['databaseIdentity'];$ok(count(N::inventory($x)['blockers'])===1,'duplicate backing database flagged');$x=$snap;$x[0]['pending']=1;$ok(count(N::inventory($x)['blockers'])===1,'pending liabilities block migration');$ok(count(N::inventory([])['missingServers'])===6,'missing balances remain missing');$x=$snap;$x[0]['accounts'][]=$x[0]['accounts'][0];$bad(fn()=>N::inventory($x),'duplicate account in one source');
 $servers=[['id'=>7,'name'=>'Test <script>','server_identifier'=>N::TEST]];$selected=7;$economy=['state'=>'ready','data'=>$d,'changesEnabled'=>true];$pending=$p;$result=null;$canManage=true;
 ob_start();require dirname(__DIR__).'/templates/player_economy.php';$html=ob_get_clean();$ok(!str_contains($html,'<script>')&&str_contains($html,'&lt;script&gt;'),'HTML safely escaped');$ok(str_contains($html,'Bankänderung verbindlich bestätigen')&&str_contains($html,'name="_csrf"'),'review step and csrf');
 $server=$servers[0];$statistics=['state'=>'ready','data'=>$r];$economy=['state'=>'unavailable','message'=>'Kein Konto'];$group=N::group(N::TEST);ob_start();require dirname(__DIR__).'/templates/server_ranking.php';$html=ob_get_clean();$ok(str_contains($html,'Bottraining')&&str_contains($html,'noch nicht aktiviert'),'ranking separates bots and prepared bank');
 $d=json_decode(file_get_contents(__DIR__.'/fixtures/lplk-economy.json'),true,32,JSON_THROW_ON_ERROR);$d['generatedAt']=gmdate('c');
 $statement=App\Services\BankStatement::from($d);
 $ok($statement['incoming']===600&&$statement['outgoing']===600&&$statement['net']===0&&$statement['opening']===0&&$statement['closing']===0,'real ledger statement reconciles');
 $ok($statement['count']===6&&$statement['rows'][0]['Id']==='reset:0','zero-value entries and ledger order retained across periods');
 $partial=$d;$partial['history']=array_slice($d['history'],0,3);$s=App\Services\BankStatement::from($partial);
 $ok($s['opening']===600&&$s['outgoing']===600&&$s['net']===-600,'partial history has real carried balance not invented zero');
 $empty=$d;$empty['history']=[];$empty['balance']=123;$s=App\Services\BankStatement::from($empty);
 $ok($s['opening']===null&&$s['closing']===123&&$s['oldest']===null,'empty export does not invent opening or dates');
 $x=$d;$x['balance']=1;$bad(fn()=>E::parse(json_encode($x),$steam),'statement ending mismatch rejected');
 $x=$d;$x['history'][2]['Balance']=501;$bad(fn()=>E::parse(json_encode($x),$steam),'internal ledger gap rejected');
 $x=$d;$x['history'][1]['Id']=$x['history'][0]['Id'];$bad(fn()=>E::parse(json_encode($x),$steam),'duplicate receipt rejected');
 $x=$d;$x['history']=[['Id'=>'invalid','Reason'=>'Invalid','Delta'=>1,'Balance'=>0,'At'=>gmdate('c')]];$bad(fn()=>E::parse(json_encode($x),$steam),'negative inferred opening rejected');
 $large=$d;$large['history']=[];$large['balance']=0;
 for($i=0;$i<20;$i++)$large['history'][]=['Id'=>'large-'.$i,'Reason'=>'Large turnover','Delta'=>$i%2===0?-9000000000000000:9000000000000000,'Balance'=>$i%2===0?0:9000000000000000,'At'=>gmdate('c')];
 $s=App\Services\BankStatement::from(E::parse(json_encode($large),$steam));
 $ok($s['count']===20&&$s['incoming']===90000000000000000&&$s['outgoing']===90000000000000000&&$s['net']===0,'20-entry totals use integer arithmetic');
 $ok(App\Services\BankStatement::money(90000000000000001)==='90.000.000.000.000.001 $','large turnover formatted without floating-point rounding');
 $ok(App\Services\BankStatement::money(-1250)==='−1.250 $'&&App\Services\BankStatement::money(0,true)==='0 $','signed and zero amounts readable');
 $ok(App\Services\BankStatement::date('2026-10-14T12:00:00Z')==='14.10.2026 · 14:00:00'&&App\Services\BankStatement::date('2026-01-14T12:00:00Z')==='14.01.2026 · 13:00:00','Berlin daylight and standard time');
 $pending=null;$result=null;$canManage=false;$bankView='statement';$d['history'][0]['Reason']='<script>unsafe</script>';$economy=['state'=>'ready','data'=>$d,'changesEnabled'=>true];
 $c=App\Services\ConnectorFactory::$c;$beforeWrites=count($c->writes);$beforeCommands=count(App\Services\LiveServerService::$commands);
 ob_start();require dirname(__DIR__).'/templates/player_economy.php';$html=ob_get_clean();
 $ok(str_contains($html,'Kontoauszug')&&str_contains($html,'Saldo danach')&&str_contains($html,'kein vollständiger Monats- oder Jahresauszug'),'statement with explicit partial scope');
 $ok(!str_contains($html,'<script>')&&str_contains($html,'&lt;script&gt;unsafe'),'statement reason and account names escaped');
 $ok(!str_contains($html,'action="/players/1/economy/prepare"'),'read-only viewer has no adjustment form');
 $ok(str_contains($html,'view=overview')&&str_contains($html,'name="view" value="statement"'),'view switch and selected server form preserve statement');
 $ok(count($c->writes)===$beforeWrites&&count(App\Services\LiveServerService::$commands)===$beforeCommands,'statement rendering has no booking or command effects');
 $economy['data']=$empty;ob_start();require dirname(__DIR__).'/templates/player_economy.php';$html=ob_get_clean();$ok(str_contains($html,'Noch keine Buchungen')&&str_contains($html,'Nicht ermittelbar')&&str_contains($html,'123 $'),'empty account truthful');
 $economy=['state'=>'unavailable','message'=>'Kein aktueller Stand'];ob_start();require dirname(__DIR__).'/templates/player_economy.php';$html=ob_get_clean();$ok(!str_contains($html,'Gebuchter Kontostand')&&str_contains($html,'Kein aktueller Stand'),'unavailable account never shown as zero');
 $economy=['state'=>'ready','data'=>$d,'changesEnabled'=>true];$bankView='overview';ob_start();require dirname(__DIR__).'/templates/player_economy.php';$html=ob_get_clean();$ok(str_contains($html,'Lotterie, Ausbau')&&!str_contains($html,'id="statement-title"'),'original overview remains separate');
 $bankView='statement';$canManage=true;$economy['data']['pendingCash']=true;ob_start();require dirname(__DIR__).'/templates/player_economy.php';$html=ob_get_clean();$ok(str_contains($html,'Cash-Buchung in Prüfung')&&!str_contains($html,'action="/players/1/economy/prepare"'),'uncertain cash keeps admin locked');
 echo json_encode(['checks'=>$n,'passed'=>true,'productionTouched'=>false],JSON_PRETTY_PRINT)."\n";
}
