<?php
$root=dirname(__DIR__);
if(!extension_loaded('pdo_sqlite')){
    echo "INFO pdo_sqlite fehlt; v1.9.5 Werbeschutz-Regeltest übersprungen.\n";
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

function ok195(bool $condition,string $label):void{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}

ok195(version_compare(trim((string)file_get_contents($root.'/VERSION')),'1.9.5','>='),'Webpanel-Version ist 1.9.5 oder neuer');

$configProperty=new ReflectionProperty(Config::class,'config');
$configProperty->setValue(null,['database'=>['driver'=>'sqlite','path'=>':memory:'],'timezone'=>'Europe/Berlin']);
$pdo=Database::connection();
$pdo->exec((string)file_get_contents($root.'/migrations/schema.sql'));

$rules=AntiAdvertisementService::rules();
ok195(in_array('clash.gg',array_column($rules['blocked_domains'],'rule_value'),true),'Feste Blockdomain ist in der Moderation sichtbar');
ok195(in_array('community.example.invalid',array_column($rules['allowed_domains'],'rule_value'),true),'Feste erlaubte Domain ist in der Moderation sichtbar');
ok195(in_array('free cases',array_column($rules['marketing_indicators'],'rule_value'),true),'System-Erkennungsphrasen sind sichtbar');

AntiAdvertisementService::addRule('blocked_domain','https://spam.example/path',null);
AntiAdvertisementService::addRule('allowed_domain','community.example',null);
AntiAdvertisementService::addRule('blocked_phrase','Claim   free coins',null);
$snapshot=AntiAdvertisementService::activeSnapshot();
ok195((int)$snapshot['version']===2,'Fast-Live-Snapshot verwendet Version 2');
ok195(in_array('spam.example',$snapshot['domains'],true),'Manuelle Blockdomain wird normalisiert und verteilt');
ok195(in_array('community.example',$snapshot['allowed_domains'],true),'Erlaubte Domain wird verteilt');
ok195(in_array('claim free coins',$snapshot['blocked_phrases'],true),'Direkte Sperrphrase wird normalisiert und verteilt');
ok195(in_array('free cases',$snapshot['marketing_keywords'],true),'System-Indikatoren werden verteilt');

AntiAdvertisementService::setRuleActive('blocked_phrase','claim free coins',false,null);
ok195(!in_array('claim free coins',AntiAdvertisementService::activeSnapshot()['blocked_phrases'],true),'Deaktivierte Phrase wird nicht mehr verteilt');

AntiAdvertisementService::setRuleActive('blocked_domain','spam.example',false,null);
$now=date('c');
$pdo->prepare('INSERT INTO game_servers(name,connector_type,created_at,updated_at) VALUES(?,?,?,?)')->execute(['v1.9.5 Testserver','pterodactyl',$now,$now]);
$serverId=(int)$pdo->lastInsertId();
AntiAdvertisementService::ingestEvents($serverId,[['event_id'=>'v195-manual-domain','event_type'=>'chat_block','steam_id64'=>'76561198000001995','player_name'=>'Spammer','match_value'=>'spam.example','action'=>'blocked','observed_at'=>$now]]);
ok195(!in_array('spam.example',AntiAdvertisementService::activeSnapshot()['domains'],true),'Automatisches Lernen reaktiviert keine manuell deaktivierte Domain');

$fixedRejected=false;
try{AntiAdvertisementService::setRuleActive('blocked_domain','jaegerservice.xyz',false,null);}catch(RuntimeException){$fixedRejected=true;}
ok195($fixedRejected,'Feste Systemregeln können nicht deaktiviert werden');

$moderation=(string)file_get_contents($root.'/templates/moderation.php');
$routes=(string)file_get_contents($root.'/public/index.php');
$auth=(string)file_get_contents($root.'/src/Core/Auth.php');
ok195(str_contains($moderation,'Globale Werbeschutzregeln')&&str_contains($moderation,'blocked_phrase'),'Moderation zeigt und verwaltet die drei Regelarten');
ok195(str_contains($routes,"'/moderation/advertising-rules'")&&str_contains($routes,"'/moderation/advertising-rules/toggle'"),'Geschützte POST-Routen sind registriert');
ok195(str_contains($auth,"'moderation.advertising.rules'"),'Eigenes Administrationsrecht ist registriert');

echo "\nv1.9.5 zentrale Werbeschutz-Regelverwaltung erfolgreich geprüft.\n";
