<?php
namespace App\Core {
 final class Auth {
  public static array $rights=[],$servers=[];
  public static function can($p){return in_array($p,self::$rights,true);}
  public static function canAny($ps){return (bool)array_filter($ps,self::can(...));}
  public static function canServer($id,$p){return in_array($id,self::$servers,true);}
 }
 final class Config {public static bool $pets=true;public static function get($k,$d=null){return $k==='pets.enabled'?self::$pets:$d;}}
 final class Database {public static $db;public static function connection(){return self::$db;}}
 final class Security {public static function e($v){return htmlspecialchars((string)$v,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');}}
}
namespace {
 use App\Core\Auth;use App\Core\Database;use App\Services\PanelNavigation as N;use App\Services\PanelSearch as S;use App\Services\PanelFeatureStatus as F;use App\Services\PanelFormDraft as D;
 foreach(['PanelNavigation','PanelSearch','PanelFeatureStatus','PanelFormDraft'] as $file)require dirname(__DIR__).'/src/Services/'.$file.'.php';
 $checks=0;$ok=static function($v,$why)use(&$checks){$checks++;if(!$v)throw new \RuntimeException($why);};
 $ok(N::groups()===[],'no permission means no navigation');
 Auth::$rights=['dashboard.view','servers.view','players.view','community.forum.view','pets.use','users.view','users.invite','moderation.view','plugins.view','audit.view','settings.view'];
 $ok(array_keys(N::groups())===['Übersicht','Server','Spieler','Community','Verwaltung'],'stable five groups');
 App\Core\Config::$pets=false;$ok(count(N::groups()['Community'])===1,'disabled pets not offered');
 $ok(N::active('/players/12','/players')&&!N::active('/players-other','/players'),'path boundary');
 Auth::$servers=[7];
 $server=['id'=>7,'name'=>'Test'];$player=['id'=>12,'display_name'=>'Max','steam_id64'=>'76561198000000012'];
 $ok(N::context('/servers/8',null,['id'=>8,'name'=>'Private'])===null,'restricted server context denied');
 $ctx=N::context('/servers/7',null,$server);$ok(count($ctx['links'])===4,'no live or settings without permission');
 $ctx=N::context('/players/12',$player,null,8);$ok(!str_contains(json_encode($ctx),'server=8'),'foreign server not propagated');
 $ctx=N::context('/players/12',$player,null,7);$ok(str_contains(json_encode($ctx),'server=7'),'selected permitted server retained');
 $ok(N::context('/players/13',$player,null)===null,'mismatched player context rejected');
 $ok(str_contains(N::commandStatus('queued'),'ausstehend')&&N::commandStatus('success')==='Vom Server bestätigt','queued never claimed confirmed');
 $ok(N::commandStatus('something-new')==='Status noch unklar','unknown status honest');
 $db=Database::$db=new \PDO('sqlite::memory:');$db->setAttribute(\PDO::ATTR_DEFAULT_FETCH_MODE,\PDO::FETCH_ASSOC);
 $db->exec('CREATE TABLE game_players(id INTEGER,display_name TEXT,steam_personaname TEXT,steam_id64 TEXT);CREATE TABLE game_servers(id INTEGER,name TEXT)');
 $db->prepare('INSERT INTO game_players VALUES(?,?,?,?)')->execute([12,'Max <script>alert(1)</script>','Max 100%','76561198000000012']);
 $db->exec("INSERT INTO game_servers VALUES(7,'Test öffentlich'),(8,'Test geheim')");
 $found=S::find('Test');$ok(count($found)===1&&$found[0]['url']==='/servers/7','server search filters before result cap');
 $ok(count(S::find('76561198000000012'))===1,'SteamID lookup');
 $ok(count(S::find('%'))===1,'percent treated literally');
 $ok(S::find('_')===[],'underscore treated literally');
 $ok(S::find("' OR 1=1 --")===[],'SQL injection is a literal search');
 $ok(S::find('')===[]&&S::find(str_repeat('a',161))===[],'empty and oversized query bounded');
 Auth::$rights=['servers.view'];$ok(S::find('Max')===[],'no player search without permission');
 Auth::$rights=['players.view'];$ok(S::find('Test')===[],'no server search without permission');
 $stmt=$db->prepare('INSERT INTO game_players VALUES(?,?,?,?)');for($i=0;$i<30;$i++)$stmt->execute([100+$i,'Bulk '.$i,'','76561198000000100']);
 $ok(count(S::find('Bulk'))===15,'bounded player results');
 $query='Max';$results=S::find($query);$searchError=null;ob_start();require dirname(__DIR__).'/templates/panel_search.php';$html=ob_get_clean();$ok(!str_contains($html,'<script>alert'),'search escapes player content');
 $now=time();$stats=['updatedAt'=>gmdate('c',$now),'version'=>'0.2.1','Enabled'=>true];
 $ok(F::classify('statistics',$stats,$now)==='active','enabled statistics');
 $stats['Enabled']=false;$ok(F::classify('statistics',$stats,$now)==='disabled','disabled statistics');
 $stats['updatedAt']=gmdate('c',$now-200);$ok(F::classify('statistics',$stats,$now)==='stale','stale disabled snapshot not asserted current');
 $stats['updatedAt']=gmdate('c',$now+200);$ok(F::classify('statistics',$stats,$now)==='stale','future status rejected');
 $ok(F::classify('statistics',[],$now)==='stale','missing status not disabled');
 $stats=['updatedAt'=>gmdate('c',$now),'version'=>'0.2.1','Enabled'=>true,'reloadError'=>'failure'];$ok(F::classify('statistics',$stats,$now)==='unknown','reload failure not active');
 $bank=['updatedAt'=>gmdate('c',$now),'version'=>'0.9.0','profile'=>'standard-test-11110004','effective'=>true,'healthy'=>true,'features'=>['bank'=>true],'web'=>['ExportEnabled'=>true]];
 $ok(F::classify('economy',$bank,$now)==='active','bank active');$bank['features']['bank']=false;$ok(F::classify('economy',$bank,$now)==='disabled','bank internal switch respected');
 $bank['features']['bank']=true;$bank['healthy']=false;$ok(F::classify('economy',$bank,$now)==='unknown','unhealthy bank not offered as active');
 $_SESSION=[];D::remember('/players/12',['status'=>'active','email'=>'private@example.test','secret'=>'dont-store','_csrf'=>'dont-store'],5);
 $ok(!isset($_SESSION['_panel_form_draft']['fields']['secret'])&&!isset($_SESSION['_panel_form_draft']['fields']['_csrf']),'draft never stores credentials or csrf');
 $ok(D::take('/players/13',5)===null,'draft path scoped');$ok(D::take('/players/12',6)===null,'draft user scoped');
 D::remember('/players/12/economy/prepare',['server'=>'7','amount'=>'100','reason'=>'Prüfung','request'=>'never-replay'],5);$draft=D::take('/players/12/economy',5);
 $ok($draft['fields']['amount']==='100'&&!isset($draft['fields']['request']),'bank draft preserves input but no submission identity');
 $ok(D::take('/players/12/economy',5)===null,'draft consumed once');
 D::remember('/players/12',['status'=>'active'],5);$_SESSION['_panel_form_draft']['expires']=time()-1;$ok(D::take('/players/12',5)===null,'draft expiry');
 D::remember('/settings',['password'=>'secret'],5);$ok(!isset($_SESSION['_panel_form_draft']),'settings secrets never captured');
 echo json_encode(['suite'=>'panel-usability','checks'=>$checks,'passed'=>true],JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE)."\n";
}
