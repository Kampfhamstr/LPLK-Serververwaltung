<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    $root='/var/www/serververwaltung';
    if(realpath($root)!==$root) throw new RuntimeException('Scope');
    spl_autoload_register(static function($class)use($root){
        if(str_starts_with($class,'App\\')){ $file=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php'; if(is_file($file)) require $file; }
    });
    App\Core\Config::load($root.'/config/app.php');
    $rows=App\Core\Database::connection()->query('SELECT id,name FROM game_servers ORDER BY id')->fetchAll();
    $report=['checkedAt'=>gmdate('c'),'version'=>trim(file_get_contents($root.'/VERSION')),'servers'=>[]];
    foreach($rows as $row){
        $status=App\Services\LiveServerService::status((int)$row['id']);
        $teams=['CT'=>0,'T'=>0,'Spectator'=>0,'Unassigned'=>0]; $bots=0;
        foreach($status['players']??[] as $player){
            if(!is_array($player))continue;
            $team=(string)($player['team']??'Unassigned'); if(!isset($teams[$team]))$team='Unassigned';
            $teams[$team]++; if(!empty($player['is_bot']))$bots++;
        }
        $report['servers'][]=['id'=>(int)$row['id'],'name'=>$row['name'],'online'=>!empty($status['bridge_online']),
            'updatedAt'=>$status['updated_at']??null,'ageSeconds'=>$status['status_age_seconds']??null,
            'map'=>$status['map']??null,'ctScore'=>$status['ct_score']??null,'tScore'=>$status['t_score']??null,
            'roundsPlayed'=>$status['rounds_played']??null,'teams'=>$teams,'bots'=>$bots];
    }
    $report['serverListHasLiveOverview']=str_contains(file_get_contents($root.'/templates/servers.php'),'server_live_overview.php');
    $report['gameserversChanged']=false;
    ob_end_clean(); echo json_encode($report,JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),"\n";
}catch(Throwable){while(ob_get_level())ob_end_clean();echo '{"error":"Live-Serverdaten konnten nicht geprueft werden"}';exit(1);}
