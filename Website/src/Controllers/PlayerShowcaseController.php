<?php
namespace App\Controllers;
use App\Core\Auth;
use App\Core\Database;
use App\Services\PlayerShowcaseService;

final class PlayerShowcaseController
{
    public function highlights(string $id):void
    {
        header('Content-Type: application/json; charset=utf-8');header('Cache-Control: private, no-store');
        $server=filter_var($_GET['server']??null,FILTER_VALIDATE_INT);
        if(!Auth::can('players.view')||!$server||$server<1||!Auth::canServer($server,'view')){http_response_code(403);echo json_encode(['state'=>'forbidden','message'=>'Keine Berechtigung für diese Statistiken.']);return;}
        if(!ctype_digit($id)||(int)$id<1){http_response_code(404);echo json_encode(['state'=>'missing','message'=>'Spieler nicht gefunden.']);return;}
        $stmt=Database::connection()->prepare('SELECT steam_id64 FROM game_players WHERE id=?');$stmt->execute([(int)$id]);$steam=$stmt->fetchColumn();
        if($steam===false){http_response_code(404);echo json_encode(['state'=>'missing','message'=>'Spieler nicht gefunden.']);return;}
        try{$result=PlayerShowcaseService::read($server,(string)$steam);}catch(\Throwable){http_response_code(503);$result=['state'=>'unavailable','message'=>'Statistiken sind gerade nicht erreichbar. Dein Steamprofil bleibt verfügbar.'];}
        echo json_encode($result,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);
    }
}
