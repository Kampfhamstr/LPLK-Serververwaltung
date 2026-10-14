<?php
$root1152=isset($argv[1])&&is_dir($argv[1])?$argv[1]:dirname(__DIR__);
require_once $root1152.'/src/Core/Database.php';
require_once $root1152.'/src/Services/FastLiveService.php';

function ok1152(bool $condition,string $label):void
{
    if(!$condition)throw new RuntimeException('FEHLER: '.$label);
    echo "OK  {$label}\n";
}

if(!extension_loaded('pdo_sqlite'))throw new RuntimeException('pdo_sqlite fehlt für den Fast-Live-Integrationstest.');
$property1152=new ReflectionProperty(App\Core\Database::class,'pdo');
$original1152=$property1152->getValue();
$pdo1152=new PDO('sqlite::memory:',null,null,[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC]);
$property1152->setValue(null,$pdo1152);
try{
    $pdo1152->exec((string)file_get_contents($root1152.'/migrations/schema.sql'));
    $now1152=date('c');
    $server1152=$pdo1152->prepare('INSERT INTO game_servers(id,name,connector_type,created_at,updated_at) VALUES(?,?,?,?,?)');
    foreach([1,2] as $sid1152)$server1152->execute([$sid1152,'Queue-Test','pterodactyl',$now1152,$now1152]);
    foreach([
        ['id'=>'respawn-test','action'=>'respawn','payload'=>new stdClass()],
        ['id'=>'slay-legacy','action'=>'slay','payload'=>[]],
        ['id'=>'team-test','action'=>'team','payload'=>['team'=>'ct','enabled'=>false,'minutes'=>0,'nested'=>new stdClass(),'list'=>[]]],
    ] as $command1152){
        $command1152['target_steamid']='76561198000000001';
        App\Services\FastLiveService::queueCommand(1,$command1152);
    }
    App\Services\FastLiveService::queueCommand(2,['id'=>'other-server','action'=>'respawn','payload'=>new stdClass()]);
    $deliver1152=new ReflectionMethod(App\Services\FastLiveService::class,'deliverCommands');
    $wire1152=json_encode(['commands'=>$deliver1152->invoke(null,1,25)],JSON_THROW_ON_ERROR);
    $decoded1152=json_decode($wire1152);
    ok1152(count($decoded1152->commands)===3,'Auslieferung bleibt auf den angefragten Server begrenzt');
    ok1152($decoded1152->commands[0]->payload instanceof stdClass,'Respawn-Payload bleibt nach SQLite und erneuter JSON-Ausgabe ein Objekt');
    ok1152($decoded1152->commands[1]->payload instanceof stdClass,'Ältere leere Array-Payload wird für die Bridge repariert');
    ok1152($decoded1152->commands[0]->target_steamid==='76561198000000001','SteamID64 bleibt unverändert als String erhalten');
    $payload1152=$decoded1152->commands[2]->payload;
    ok1152($payload1152->team==='ct'&&$payload1152->enabled===false&&$payload1152->minutes===0
        &&$payload1152->nested instanceof stdClass&&$payload1152->list===[],'Nutzdaten behalten Strings, Booleans, Zahlen, Objekte und Listen');
    ok1152($deliver1152->invoke(null,1,25)===[],'Frisch ausgelieferte Befehle werden nicht sofort doppelt geliefert');
    $pdo1152->exec("UPDATE bridge_command_queue SET delivered_at='2000-01-01T00:00:00+00:00' WHERE server_id=1");
    ok1152(count($deliver1152->invoke(null,1,25))===3,'Unbestätigte aktuelle Befehle werden nach dem Retry-Fenster erneut geliefert');

    $ingest1152=new ReflectionMethod(App\Services\FastLiveService::class,'ingestResults');
    $ack1152=$ingest1152->invoke(null,1,[['id'=>'respawn-test','success'=>true,'message'=>'OK'],['id'=>'slay-legacy','success'=>false,'message'=>'Spieler nicht online.']]);
    ok1152(count($ack1152)===2&&App\Services\FastLiveService::result(1,'respawn-test')['success']===true,'Bridge-Erfolg wird gespeichert und kann abgefragt werden');
    ok1152(App\Services\FastLiveService::result(1,'slay-legacy')['message']==='Spieler nicht online.','Konkreter Bridge-Fehler bleibt für die Schnellverwaltung erhalten');
    ok1152(App\Services\FastLiveService::result(2,'respawn-test')===null,'Ergebnis eines anderen Servers wird nicht veröffentlicht');
    $pdo1152->exec("UPDATE bridge_command_queue SET requested_at='2000-01-01T00:00:00+00:00',delivered_at='2000-01-01T00:00:00+00:00' WHERE command_id='team-test'");
    ok1152($deliver1152->invoke(null,1,25)===[],'Veralteter unbestätigter Befehl wird nach Wiederherstellung nicht erneut ausgeführt');
    ok1152(App\Services\FastLiveService::result(1,'team-test')['success']===false,'Abgelaufener Befehl erhält eine lesbare Fehlerrückmeldung');
    if(isset($argv[2]))file_put_contents($argv[2],$wire1152);
}finally{
    $property1152->setValue(null,$original1152);
}
echo "v1.15.2 Fast-Live-Befehls-Roundtrip erfolgreich.\n";
