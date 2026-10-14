<?php
namespace App\Controllers;
use App\Core\Auth;
use App\Core\Database;
use App\Core\View;
use App\Services\PlayerStatisticsService;
final class PlayerStatisticsController
{
    public function view(string $id):void
    {
        Auth::requirePermission('players.view');
        $pdo=Database::connection();$stmt=$pdo->prepare('SELECT id,display_name,steam_id64 FROM game_players WHERE id=?');$stmt->execute([(int)$id]);$player=$stmt->fetch();
        if(!$player){http_response_code(404);View::render('error',['title'=>'Spieler nicht gefunden','message'=>'Dieser Spieler existiert nicht.']);return;}
        $servers=array_values(array_filter($pdo->query('SELECT id,name,server_identifier FROM game_servers ORDER BY name')->fetchAll(),static fn($s)=>Auth::canServer((int)$s['id'],'view')));
        $selected=filter_var($_GET['server']??0,FILTER_VALIDATE_INT)?:0;
        if(!$selected)foreach($servers as $s)if(($s['server_identifier']??'')==='11110004'){$selected=(int)$s['id'];break;}
        $permitted=array_column($servers,'id');
        if($selected&&!in_array($selected,array_map('intval',$permitted),true)){http_response_code(403);View::render('error',['title'=>'Kein Zugriff','message'=>'Keine Berechtigung für diesen Server.']);return;}
        $statistics=$selected?PlayerStatisticsService::read($selected,(string)$player['steam_id64']):['state'=>'empty','message'=>'Kein freigegebener Server verfügbar.'];
        $economy=$selected?\App\Services\PlayerEconomyService::read($selected,(string)$player['steam_id64']):['state'=>'empty','message'=>'Kein Server gewählt.'];
        View::render('player_statistics',['title'=>'Statistik · '.$player['display_name'],'player'=>$player,'servers'=>$servers,'selected'=>$selected,'statistics'=>$statistics,'economy'=>$economy]);
    }
}
