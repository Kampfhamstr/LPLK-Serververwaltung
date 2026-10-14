<?php
namespace App\Services;
use App\Core\Auth;
use App\Core\Database;
use App\Core\Audit;

final class PlayerEconomyService
{
    public const ROOT='game/csgo/addons/swiftlys2/data/LPLK_Economy';
    private static function access(int $serverId):array
    {
        if(!Auth::can('players.view')||!Auth::canServer($serverId,'view'))throw new \RuntimeException('Keine Berechtigung.');
        $s=LiveServerService::server($serverId);
        if(($s['server_identifier']??'')!=='11110004')throw new \RuntimeException('Bankanbindung für diesen Server noch nicht freigegeben.');
        return $s;
    }
    public static function decode(string $raw,int $limit=131072):array
    {
        if(strlen($raw)>$limit)throw new \RuntimeException('Export zu groß.');
        $d=json_decode($raw,true,32,JSON_THROW_ON_ERROR);if(!is_array($d))throw new \RuntimeException('Ungültiger Export.');return $d;
    }
    public static function number(mixed $n,int $min=0):int
    {if(!is_int($n)||$n<$min||$n>9000000000000000)throw new \RuntimeException('Ungültiger Betrag.');return $n;}
    public static function recent(mixed $t):void
    {if(!is_string($t)||!($at=strtotime($t))||$at>time()+60||time()-$at>120)throw new \RuntimeException('Kein aktueller Export.');}
    private static function status(ServerConnectorInterface $c):array
    {
        $d=self::decode($c->read(self::ROOT.'/status.json'),32768);self::recent($d['updatedAt']??null);
        if(($d['version']??null)!=='0.9.0'||($d['profile']??null)!=='standard-test-11110004'||($d['effective']??false)!==true||($d['healthy']??false)!==true||($d['features']['bank']??false)!==true||($d['web']['ExportEnabled']??false)!==true||($d['reloadError']??null)!==null||($d['web']['error']??null)!==null)throw new \RuntimeException('Bank ausgeschaltet, nicht unterstützt oder Export gestört.');
        return $d;
    }
    public static function parse(string $raw,string $steam):array
    {
        $d=self::decode($raw);
        if(($d['schema']??null)!==1||($d['steamId']??null)!==$steam||!BankNetworkPlan::steam($steam)||($d['profile']??null)!=='standard-test-11110004')throw new \RuntimeException('Kontozuordnung stimmt nicht.');
        self::recent($d['generatedAt']??null);
        foreach(['balance','betClaims','permanentTickets','purchasedTickets','vaultLevel','robberLevel','spyLevel'] as $k)self::number($d[$k]??null);
        if(!is_string($d['activeTitle']??null)||strlen($d['activeTitle'])>100)throw new \RuntimeException('Ungültiger Titel.');
        foreach(['period','revision'] as $k)self::number($d[$k]??null,1);
        if(!is_bool($d['pendingCash']??null)||!is_array($d['history']??null)||count($d['history'])>20)throw new \RuntimeException('Ungültiger Kontostatus.');
        foreach($d['history'] as $e){if(!is_array($e)||!is_string($e['Id']??null)||strlen($e['Id'])>128||!is_string($e['Reason']??null)||strlen($e['Reason'])>640||!is_string($e['At']??null)||strtotime($e['At'])===false)throw new \RuntimeException('Ungültiger Verlauf.');self::number($e['Balance']??null);self::number($e['Delta']??null,-9000000000000000);}
        BankStatement::from($d);
        return $d;
    }
    public static function read(int $serverId,string $steam):array
    {
        self::accessPermission($serverId);
        try{$c=ConnectorFactory::make(self::access($serverId));$status=self::status($c);
            if(!BankNetworkPlan::steam($steam))return ['state'=>'empty','message'=>'Gültige SteamID64 fehlt.'];
            $file=self::ROOT.'/web/'.$steam.'.json';if(!$c->exists($file))return ['state'=>'empty','message'=>'Noch kein Bankkonto exportiert.'];
            $d=self::parse($c->read($file),$steam);if($d['period']!==($status['period']??null))throw new \RuntimeException('Bankperiode hat gewechselt.');
            return ['state'=>'ready','data'=>$d,'limit'=>self::number($status['settings']['Limit']??null),'maxSet'=>self::number($status['settings']['MaxManualBankSet']??null,-1),'changesEnabled'=>($status['web']['AdminChangesEnabled']??false)===true&&($status['settings']['AllowEditPlayerBank']??false)===true];
        }catch(\Throwable){return ['state'=>'unavailable','message'=>'Kein aktueller, freigegebener Bankstand verfügbar. Die Anbindung ist zunächst auf den Testserver beschränkt.'];}
    }
    private static function accessPermission(int $serverId):void
    {if(!Auth::can('players.view')||!Auth::canServer($serverId,'view'))throw new \RuntimeException('Keine Berechtigung.');}
    public static function canManage(int $serverId,array $player):bool
    {
        if(!Auth::can('economy.manage')||!Auth::can('players.manage')||!Auth::canServer($serverId,'view')||!Auth::canServer($serverId,'players')||!Auth::canManageGameRoleTarget((string)($player['community_role']??'spieler')))return false;
        $q=Database::connection()->prepare('SELECT role_name FROM server_player_groups WHERE server_id=? AND player_id=?');$q->execute([$serverId,(int)$player['id']]);
        $roles=$q->fetchAll(\PDO::FETCH_COLUMN);foreach($roles as $role)if(!Auth::canAssignGameRole($serverId,$role))return false;
        return true;
    }
    public static function prepare(int $serverId,array $player,string $operation,string $amount,string $reason):array
    {
        if(!self::canManage($serverId,$player))throw new \RuntimeException('Keine Berechtigung für dieses Konto.');
        $state=self::read($serverId,(string)$player['steam_id64']);
        if($state['state']!=='ready'||!$state['changesEnabled']||$state['data']['pendingCash'])throw new \RuntimeException('Webänderungen gesperrt oder Cash-Buchung offen.');
        if(!in_array($operation,['add','remove','set'],true)||!preg_match('/^[0-9]{1,16}$/D',$amount)||strlen(trim($reason))<5||strlen(trim($reason))>120||preg_match('/[\x00-\x1f\x7f]/',$reason))throw new \RuntimeException('Betrag und Begründung (5–120 Zeichen) prüfen.');
        $value=self::number((int)$amount);$d=$state['data'];$after=$operation==='set'?$value:($operation==='add'?$d['balance']+$value:$d['balance']-$value);self::number($after);
        if($after>$d['balance']&&$after>$state['limit']||$operation==='set'&&$state['maxSet']>=0&&$value>$state['maxSet'])throw new \RuntimeException('Kontolimit oder Adminlimit überschritten.');
        $u=Auth::user();$steam=(string)($u['steam_id64']??'');if(!BankNetworkPlan::steam($steam))$steam='';
        $request=['Id'=>'web:'.bin2hex(random_bytes(16)),'Profile'=>$d['profile'],'Period'=>$d['period'],'Account'=>$d['steamId'],'Revision'=>$d['revision'],'UserId'=>(int)$u['id'],'ActorSteamId'=>$steam,'ActorRank'=>Auth::isFounder()?100:10,'Operation'=>$operation,'Amount'=>$value,'Reason'=>trim($reason),'ExpiresAt'=>gmdate('Y-m-d\TH:i:s\Z',time()+300)];
        return ['serverId'=>$serverId,'playerId'=>(int)$player['id'],'before'=>$d['balance'],'after'=>$after,'request'=>$request];
    }
    public static function submit(array $p,array $player):array
    {
        $sid=$p['serverId'];$r=$p['request'];
        if($p['playerId']!==(int)$player['id']||$r['Account']!==(string)$player['steam_id64']||$r['UserId']!==(int)(Auth::user()['id']??0)||!self::canManage($sid,$player))throw new \RuntimeException('Berechtigung oder Ziel geändert.');
        $result=self::result($p);if($result['state']==='applied')return $result;
        $actorSteam=(string)(Auth::user()['steam_id64']??'');if(!BankNetworkPlan::steam($actorSteam))$actorSteam='';
        if($r['ActorRank']!==(Auth::isFounder()?100:10)||$r['ActorSteamId']!==$actorSteam)throw new \RuntimeException('Adminrolle oder Steam-Verknüpfung geändert. Neuen Auftrag vorbereiten.');
        if(strtotime($r['ExpiresAt'])<=time())throw new \RuntimeException('Bestätigung abgelaufen. Zuerst vorhandenen Auftragsbeleg prüfen.');
        $state=self::read($sid,$r['Account']);if($state['state']!=='ready'||!$state['changesEnabled'])throw new \RuntimeException('Bank gesperrt. Zuerst vorhandenen Beleg prüfen.');
        // Revision, period and cash locks are enforced atomically by the ledger.
        // Resending an identical accepted request must be able to recover its receipt.
        // Authenticated connector writes only a new request; never modifies SQLite or balances.
        $c=ConnectorFactory::make(self::access($sid));$id=substr($r['Id'],4);
        $c->write(self::ROOT.'/web-requests/'.$id.'.json',json_encode($r,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR));
        LiveServerService::queue($sid,'server_command',['command'=>'sw_lplk_bank_request '.$id]);
        Audit::log('economy.request','player',(int)$player['id'],['server_id'=>$sid,'request_id'=>$r['Id'],'operation'=>$r['Operation'],'amount'=>$r['Amount'],'reason'=>$r['Reason']]);
        return ['state'=>'pending','message'=>'Auftrag gesendet. Erfolg wird erst mit dem Buchungsbeleg angezeigt.'];
    }
    public static function result(array $p):array
    {
        $r=$p['request'];$s=self::access($p['serverId']);
        if($r['UserId']!==(int)(Auth::user()['id']??0))throw new \RuntimeException('Fremder Auftrag.');
        $id=substr($r['Id'],4);if(!preg_match('/^[a-f0-9]{32}$/D',$id))throw new \RuntimeException('Ungültiger Auftrag.');
        try{$c=ConnectorFactory::make($s);$file=self::ROOT.'/web-results/'.$id.'.json';if(!$c->exists($file))return ['state'=>'pending','message'=>'Noch kein Buchungsbeleg. Gleiche Auftragsnummer erneut prüfen.'];$raw=$c->read($file);}
        catch(\Throwable){return ['state'=>'unknown','message'=>'Beleg derzeit nicht abrufbar. Keine neue Auftragsnummer erzeugen; diesen Auftrag erneut prüfen.'];}
        $d=self::decode($raw,8192);if(($d['schema']??null)!==1||($d['id']??null)!==$id)throw new \RuntimeException('Ungültiger Beleg.');
        if(($d['state']??null)==='rejected')return ['state'=>'rejected','message'=>substr((string)($d['message']??'Abgelehnt'),0,400)];
        $receipt=$d['receipt']??[];$saved=$receipt['Request']??[];
        foreach($r as $k=>$v){if($k==='ExpiresAt'){if(strtotime($saved[$k]??'')!==strtotime($v))throw new \RuntimeException('Beleg gehört zu anderem Auftrag.');}elseif(($saved[$k]??null)!==$v)throw new \RuntimeException('Beleg gehört zu anderem Auftrag.');}
        if(($d['state']??null)!=='applied'||($receipt['Before']??null)!==$p['before']||($receipt['After']??null)!==$p['after'])throw new \RuntimeException('Beleg nicht bestätigt.');
        return ['state'=>'applied','message'=>'Bankänderung bestätigt.','receipt'=>$receipt];
    }
    public static function ranking(int $serverId):array
    {
        self::accessPermission($serverId);
        try{$c=ConnectorFactory::make(self::access($serverId));$s=self::status($c);$d=self::decode($c->read(self::ROOT.'/web/ranking.json'));self::recent($d['generatedAt']??null);
            if(($d['schema']??null)!==1||($d['profile']??null)!=='standard-test-11110004'||($d['scope']??null)!=='server'||($d['period']??null)!==$s['period']||!is_array($d['rows']??null)||count($d['rows'])>100)throw new \RuntimeException('Ungültige Rangliste.');
            $seen=[];$last=PHP_INT_MAX;foreach($d['rows'] as $r){if(!is_string($r['steamId']??null)||!BankNetworkPlan::steam($r['steamId'])||isset($seen[$r['steamId']])||!is_string($r['name']??null)||strlen($r['name'])>200)throw new \RuntimeException('Ungültiger Ranglisteneintrag.');$v=self::number($r['balance']??null);if($v>$last)throw new \RuntimeException('Unsortierte Rangliste.');$last=$v;$seen[$r['steamId']]=true;}
            return ['state'=>'ready','data'=>$d];
        }catch(\Throwable){return ['state'=>'unavailable','message'=>'Bankrangliste noch nicht verfügbar.'];}
    }
}
