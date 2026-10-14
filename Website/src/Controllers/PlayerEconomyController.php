<?php
namespace App\Controllers;
use App\Core\Auth;
use App\Core\Database;
use App\Core\Security;
use App\Core\View;
use App\Services\PlayerEconomyService as Economy;
use App\Services\ServerRankingService;
use App\Services\WeaponRankingService;
use App\Services\BankNetworkPlan;

final class PlayerEconomyController
{
    private function player(string $id):array
    {
        Auth::requirePermission('players.view');
        $q=Database::connection()->prepare('SELECT id,display_name,steam_id64,community_role FROM game_players WHERE id=?');$q->execute([(int)$id]);$p=$q->fetch();
        if(!$p)throw new \RuntimeException('Spieler nicht gefunden.');return $p;
    }
    private function servers():array{return array_values(array_filter(Database::connection()->query('SELECT id,name,server_identifier FROM game_servers ORDER BY name')->fetchAll(),static fn($s)=>Auth::canServer((int)$s['id'],'view')));}
    private function selected(array $servers):int
    {
        $id=filter_var($_GET['server']??$_POST['server']??0,FILTER_VALIDATE_INT)?:0;
        if(!$id)foreach($servers as $s)if($s['server_identifier']==='11110004'){$id=(int)$s['id'];break;}
        if(!$id&&$servers)$id=(int)$servers[0]['id'];
        if(!$id||!in_array($id,array_map('intval',array_column($servers,'id')),true))throw new \RuntimeException('Kein freigegebener Server.');return $id;
    }
    private function render(array $player,array $servers,int $selected,?array $pending=null,?array $result=null,?string $error=null):void
    {
        $economy=Economy::read($selected,(string)$player['steam_id64']);
        $requestedView=$_GET['view']??$_POST['view']??$_SESSION['economy_view']??'overview';
        $bankView=in_array($requestedView,['overview','statement'],true)?$requestedView:'overview';
        $_SESSION['economy_view']=$bankView;
        View::render('player_economy',compact('player','servers','selected','economy','pending','result','error','bankView')+['title'=>'Bank · '.$player['display_name'],'canManage'=>Economy::canManage($selected,$player)]);
    }
    public function view(string $id):void
    {
        try{$player=$this->player($id);$servers=$this->servers();$selected=$this->selected($servers);$pending=null;$result=null;
            $key=(string)($_GET['request']??'');if($key!==''){$pending=$_SESSION['economy_requests'][$key]??null;if(!$pending||$pending['playerId']!==(int)$id||$pending['serverId']!==$selected)throw new \RuntimeException('Auftrag nicht vorhanden.');$result=Economy::result($pending);}
            $this->render($player,$servers,$selected,$pending,$result);
        }catch(\Throwable $e){http_response_code(400);View::render('error',['title'=>'Bank nicht verfügbar','message'=>$e->getMessage()]);}
    }
    public function prepare(string $id):void
    {
        Security::verifyCsrf();
        try{$player=$this->player($id);$servers=$this->servers();$selected=$this->selected($servers);
            $p=Economy::prepare($selected,$player,(string)($_POST['operation']??''),(string)($_POST['amount']??''),(string)($_POST['reason']??''));
            $key=substr($p['request']['Id'],4);$_SESSION['economy_requests']??=[];
            // Keep pending identities for receipt checks; refuse unbounded session growth.
            if(count($_SESSION['economy_requests'])>=100)throw new \RuntimeException('Zu viele gespeicherte Aufträge. Bestehende Belege zuerst prüfen; neue Anmeldung für weitere Aufträge.');
            $_SESSION['economy_requests'][$key]=$p;$this->render($player,$servers,$selected,$p);
        }catch(\Throwable $e){http_response_code(400);View::render('error',['title'=>'Bankänderung nicht vorbereitet','message'=>$e->getMessage()]);}
    }
    public function confirm(string $id):void
    {
        Security::verifyCsrf();$key=(string)($_POST['request']??'');
        try{$player=$this->player($id);$p=$_SESSION['economy_requests'][$key]??null;if(!$p||$p['playerId']!==(int)$id)throw new \RuntimeException('Bestätigung fehlt.');
            $servers=$this->servers();$selected=$p['serverId'];$_SESSION['economy_requests'][$key]['submitted']=true;
            try{$result=Economy::submit($p,$player);}catch(\Throwable){$result=['state'=>'unknown','message'=>'Auftragsstatus unklar. Den Beleg dieser Auftragsnummer prüfen; denselben Auftrag bei Bedarf erneut senden.'];}
            $this->render($player,$servers,$selected,$p,$result);
        }catch(\Throwable $e){http_response_code(400);View::render('error',['title'=>'Bankänderung nicht bestätigt','message'=>$e->getMessage()]);}
    }
    public function ranking(string $id):void
    {
        Auth::requirePermission('players.view');$servers=$this->servers();$selected=(int)$id;
        if(!in_array($selected,array_map('intval',array_column($servers,'id')),true)){http_response_code(403);View::render('error',['title'=>'Kein Zugriff','message'=>'Keine Serverberechtigung.']);return;}
        $server=current(array_filter($servers,static fn($s)=>(int)$s['id']===$selected));
        $statistics=ServerRankingService::read($selected);$economy=Economy::ranking($selected);$group=BankNetworkPlan::group($server['server_identifier']);
        $weaponRanking=WeaponRankingService::read($selected);$selectedWeapon=WeaponRankingService::selected($_GET['weapon']??null);
        View::render('server_ranking',compact('server','statistics','economy','group','weaponRanking','selectedWeapon')+['title'=>'Ranglisten · '.$server['name']]);
    }
}
