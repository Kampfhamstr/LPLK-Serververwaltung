<?php
$root=dirname(__DIR__);
if(!extension_loaded('pdo_sqlite')){
    echo "INFO pdo_sqlite fehlt; v1.9.3 Datenbank-Integrationstest übersprungen.\n";
    return;
}
spl_autoload_register(static function(string $class)use($root):void{
    if(!str_starts_with($class,'App\\'))return;
    $path=$root.'/src/'.str_replace(['App\\','\\'],['','/'],$class).'.php';
    if(is_file($path))require_once$path;
});

use App\Core\Config;
use App\Core\Database;
use App\Services\AntiAdvertisementService;
use App\Services\GlobalBanService;

function ok193db(bool $condition,string $label):void{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}

$configProperty=new ReflectionProperty(Config::class,'config');
$configProperty->setValue(null,['database'=>['driver'=>'sqlite','path'=>':memory:'],'timezone'=>'Europe/Berlin']);
$pdo=Database::connection();
$pdo->exec((string)file_get_contents($root.'/migrations/schema.sql'));
$now=date('c');
$pdo->prepare('INSERT INTO game_servers(name,connector_type,created_at,updated_at) VALUES(?,?,?,?)')->execute(['Testserver','pterodactyl',$now,$now]);

$insertChat=$pdo->prepare('INSERT INTO server_chat_messages(server_id,message_uid,steam_id64,player_name,message,sent_at,received_at) VALUES(?,?,?,?,?,?,?)');
$steam='76561198000000001';
$insertChat->execute([1,'v193-chat-1',$steam,'SpamBot','Besucht spam-one.example für Free Commends',$now,$now]);
$eventId=AntiAdvertisementService::reportChatMessage((int)$pdo->lastInsertId(),null);
$event=AntiAdvertisementService::getEvent($eventId);
ok193db(($event['review_status']??'')==='reported','Manuelle Chatmeldung wartet auf Prüfung');
$identity=$pdo->query("SELECT active FROM global_advertising_identities WHERE steam_id64='{$steam}'")->fetchColumn();
ok193db((int)$identity===0,'Ungeprüfte Meldung sperrt die SteamID nicht');

AntiAdvertisementService::reviewEvent($eventId,'protected','Bestätigt',null);
$identity=$pdo->query("SELECT active FROM global_advertising_identities WHERE steam_id64='{$steam}'")->fetchColumn();
ok193db((int)$identity===1,'Bestätigte Meldung aktiviert den globalen Werbeschutz');
$domain=$pdo->query("SELECT active FROM global_advertising_domains WHERE domain='spam-one.example'")->fetchColumn();
ok193db((int)$domain===1,'Bestätigte Werbedomain wird serverübergreifend gelernt');

$banId=GlobalBanService::create($steam,'SpamBot','advertising','Integrationstest',60,null,null,$eventId);
$event=AntiAdvertisementService::getEvent($eventId);
ok193db(($event['review_status']??'')==='banned'&&(int)($event['global_ban_id']??0)===$banId,'Werbefall ist dauerhaft mit dem Ban verknüpft');
GlobalBanService::unban($banId,'Fehlalarm bestätigt',null);
$event=AntiAdvertisementService::getEvent($eventId);
$identity=$pdo->query("SELECT active FROM global_advertising_identities WHERE steam_id64='{$steam}'")->fetchColumn();
ok193db(($event['review_status']??'')==='unbanned'&&(int)$identity===0,'Unban bleibt historisch sichtbar und löst die SteamID-Sperre');
$history=GlobalBanService::search(['status'=>'history','q'=>$steam,'limit'=>25]);
ok193db(count($history)===1&&($history[0]['unban_reason']??'')==='Fehlalarm bestätigt','Entbannungsgrund ist in der Ban-Historie erhalten');

$steam2='76561198000000002';
$insertChat->execute([1,'v193-chat-2',$steam2,'FalsePositive','Unsere Seite spam-two.example ist legitim',$now,$now]);
$eventId2=AntiAdvertisementService::reportChatMessage((int)$pdo->lastInsertId(),null);
AntiAdvertisementService::reviewEvent($eventId2,'protected','Zunächst bestätigt',null);
AntiAdvertisementService::reviewEvent($eventId2,'false_positive','Nachprüfung',null);
$identity2=$pdo->query("SELECT active FROM global_advertising_identities WHERE steam_id64='{$steam2}'")->fetchColumn();
$domain2=$pdo->query("SELECT active FROM global_advertising_domains WHERE domain='spam-two.example'")->fetchColumn();
ok193db((int)$identity2===0&&(int)$domain2===0,'Fehlalarm deaktiviert allein daraus gelernte SteamID und Domain');

echo "\nv1.9.3 Werbeschutz-Datenbankworkflow erfolgreich.\n";
