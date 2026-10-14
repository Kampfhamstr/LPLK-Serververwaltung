<?php
namespace App\Services;
use App\Core\Auth;
use App\Core\Database;

final class PanelSearch
{
    /** Local queries only: no connector, Steam API or game-server request. */
    public static function find(string $query): array
    {
        $query = trim($query);
        if ($query === '' || strlen($query) > 160) return [];
        $like = '%'.str_replace(['\\','%','_'],['\\\\','\\%','\\_'],$query).'%';
        $pdo=Database::connection(); $out=[];
        if (Auth::can('players.view')) {
            $stmt=$pdo->prepare("SELECT id,display_name,steam_personaname,steam_id64 FROM game_players WHERE display_name LIKE ? ESCAPE '\' OR steam_personaname LIKE ? ESCAPE '\' OR steam_id64 LIKE ? ESCAPE '\' ORDER BY COALESCE(NULLIF(steam_personaname,''),display_name) COLLATE NOCASE LIMIT 15");
            $stmt->execute([$like,$like,$like]);
            foreach($stmt->fetchAll() as $p) $out[]=['kind'=>'Spieler','label'=>$p['steam_personaname']?:$p['display_name'],'detail'=>$p['steam_id64'],'url'=>'/players/'.(int)$p['id']];
        }
        if (Auth::can('servers.view')) {
            $stmt=$pdo->prepare("SELECT id,name FROM game_servers WHERE name LIKE ? ESCAPE '\' ORDER BY name COLLATE NOCASE"); $stmt->execute([$like]); $count=0;
            while($s=$stmt->fetch()) {
                if(!Auth::canServer((int)$s['id'],'view')) continue;
                $out[]=['kind'=>'Server','label'=>$s['name'],'detail'=>'Serverübersicht öffnen','url'=>'/servers/'.(int)$s['id']];
                if(++$count===15) break;
            }
        }
        return $out;
    }
}
