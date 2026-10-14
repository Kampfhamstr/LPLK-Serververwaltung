<?php
$root=dirname(__DIR__);
if(!extension_loaded('pdo_sqlite')){
    echo "INFO pdo_sqlite fehlt; v1.9.4 SteamID-Akten-Integrationstest übersprungen.\n";
    return;
}
if(!class_exists(\App\Core\Config::class,false)){
    spl_autoload_register(static function(string $class)use($root):void{
        if(!str_starts_with($class,'App\\'))return;
        $path=$root.'/src/'.str_replace(['App\\','\\'],['','/'],$class).'.php';
        if(is_file($path))require_once$path;
    });
}

use App\Core\Config;
use App\Core\Database;
use App\Services\AntiAdvertisementService;
use App\Services\GlobalBanService;

function ok194(bool $condition,string $label):void{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}

ok194(version_compare(trim((string)file_get_contents($root.'/VERSION')),'1.9.4','>='),'Webpanel-Version ist 1.9.4 oder neuer');

if(!isset($pdo) || !$pdo instanceof PDO){
    $configProperty=new ReflectionProperty(Config::class,'config');
    $configProperty->setValue(null,['database'=>['driver'=>'sqlite','path'=>':memory:'],'timezone'=>'Europe/Berlin']);
    $pdo=Database::connection();
    $pdo->exec((string)file_get_contents($root.'/migrations/schema.sql'));
}
$now=date('c');
$serverId=(int)$pdo->query('SELECT id FROM game_servers ORDER BY id LIMIT 1')->fetchColumn();
if($serverId<=0){
    $pdo->prepare('INSERT INTO game_servers(name,connector_type,created_at,updated_at) VALUES(?,?,?,?)')->execute(['v1.9.4 Testserver','pterodactyl',$now,$now]);
    $serverId=(int)$pdo->lastInsertId();
}

$steam='76561198000001994';
$events=[];
for($i=1;$i<=4;$i++){
    $events[]=[
        'event_id'=>'v194-event-'.$i,
        'event_type'=>'chat_block',
        'steam_id64'=>$steam,
        'player_name'=>'Jaegerservice.xyz',
        'match_value'=>'jaegerservice.xyz',
        'action'=>'bridge_chat_prearchive_stop',
        'message_excerpt'=>'Werbetext '.$i.' jaegerservice.xyz',
        'observed_at'=>date('c',time()+$i),
    ];
}
AntiAdvertisementService::ingestEvents($serverId,$events);

$cases=AntiAdvertisementService::cases(['q'=>$steam,'limit'=>25]);
ok194(count($cases)===1,'Mehrere Werbeereignisse derselben SteamID ergeben genau eine Karte');
$case=$cases[0];
ok194((int)$case['event_count']===4,'Die zusammengeführte Karte zeigt die Anzahl aller Treffer');
ok194(count($case['recent_actions'])>=4,'Die letzten Erkennungsaktionen stehen in der Kartenchronik');

$banId=GlobalBanService::create($steam,'Jaegerservice.xyz','advertising','v1.9.4 Aggregationstest',60,null,null,(int)$case['id']);
$cases=AntiAdvertisementService::cases(['q'=>$steam,'limit'=>25]);
ok194(count($cases)===1&&(int)$cases[0]['active_ban_id']===$banId&&($cases[0]['case_status']??'')==='banned','Aktiver Ban wird SteamID-weit auf derselben Karte erkannt');
ok194(count(AntiAdvertisementService::cases(['q'=>$steam,'status'=>'banned','limit'=>25]))===1,'Statusfilter zeigt die SteamID genau einmal als aktuell gebannt');
ok194(count(AntiAdvertisementService::cases(['q'=>$steam,'status'=>'detected','limit'=>25]))===0,'Veraltete Erkennungskarten werden bei aktivem Ban nicht mehr angezeigt');
$banTimeline=array_filter($cases[0]['recent_actions'],static fn(array $row):bool=>($row['kind']??'')==='ban');
ok194(count($banTimeline)>=1,'Ban-Aktion erscheint in der Chronik');

$duplicateRejected=false;
try{
    GlobalBanService::create($steam,'Jaegerservice.xyz','advertising','Darf nicht erstellt werden',60,null,null,(int)$case['id']);
}catch(RuntimeException $e){
    $duplicateRejected=str_contains($e->getMessage(),'bereits global gebannt');
}
ok194($duplicateRejected,'Ein zweiter aktiver Global-Ban derselben SteamID wird serverseitig abgewiesen');

GlobalBanService::unban($banId,'v1.9.4 Entbannungstest',null);
$cases=AntiAdvertisementService::cases(['q'=>$steam,'limit'=>25]);
ok194(count($cases)===1&&(int)($cases[0]['active_ban_id']??0)===0,'Nach Entbannung bleibt eine Karte ohne veraltete aktive Ban-Aktion');
ok194(count(AntiAdvertisementService::cases(['q'=>$steam,'status'=>'unbanned','limit'=>25]))===1,'Statusfilter führt die entbannte SteamID weiterhin als eine Akte');
$unbanTimeline=array_filter($cases[0]['recent_actions'],static fn(array $row):bool=>($row['kind']??'')==='unban');
ok194(count($unbanTimeline)>=1,'Entbannung mit Grund erscheint in der Chronik');

$moderation=(string)file_get_contents($root.'/templates/moderation.php');
ok194(str_contains($moderation,'foreach($advertisingCases as $event)'),'Template rendert die zusammengeführten SteamID-Akten');
ok194(str_contains($moderation,"\$activeBanId===0")&&str_contains($moderation,"\$activeBanId>0"),'Ban- und Entbannungsaktion richten sich nach demselben aktuellen SteamID-Status');

echo "\nv1.9.4 eindeutige Werbeschutz-Karten pro SteamID erfolgreich geprüft.\n";
