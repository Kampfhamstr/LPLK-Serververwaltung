<?php
// Run in its own PHP process: Auth deliberately caches the signed-in user.
$root=dirname(__DIR__);
spl_autoload_register(static function(string $class)use($root):void{
    if(!str_starts_with($class,'App\\'))return;
    $file=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';
    if(is_file($file))require_once $file;
});
$pdo=new PDO('sqlite::memory:',null,null,[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC]);
(new ReflectionProperty(App\Core\Database::class,'pdo'))->setValue(null,$pdo);
$pdo->exec((string)file_get_contents($root.'/migrations/schema.sql'));
$now=date('c');
$pdo->prepare('INSERT INTO users(id,name,email,password_hash,web_role,created_at,updated_at) VALUES(1,?,?,?,?,?,?)')
    ->execute(['Test','test@example.invalid','unused','member',$now,$now]);
foreach([1,2] as $sid){
    $pdo->prepare('INSERT INTO game_servers(id,name,connector_type,bridge_fastlive_enabled,bridge_fastlive_secret_enc,created_at,updated_at) VALUES(?,?,?,?,?,?,?)')
        ->execute([$sid,'Test','pterodactyl',1,'test-only',$now,$now]);
    $pdo->prepare('INSERT INTO user_server_access(user_id,server_id,can_live_admin,updated_at) VALUES(1,?,1,?)')->execute([$sid,$now]);
}
$_SESSION=['user_id'=>1,'auth_version'=>1,'_presence_touch'=>time(),
    '_quick_manage_pending'=>['own-command'=>['server_id'=>1,'created_at'=>time()]]];
App\Services\FastLiveService::queueCommand(1,['id'=>'own-command','action'=>'respawn','payload'=>new stdClass()]);
$controller=new App\Controllers\QuickManageController();
$checks=0;
$check=static function(bool $condition,string $label)use(&$checks):void{
    if(!$condition)throw new RuntimeException($label);
    $checks++;
};
$request=static function(int $serverId,string $commandId)use($controller):array{
    $_GET=['command_id'=>$commandId];http_response_code(200);ob_start();
    try{$controller->result((string)$serverId);$body=(string)ob_get_contents();}finally{ob_end_clean();}
    return [http_response_code(),json_decode($body,true,512,JSON_THROW_ON_ERROR)];
};
[$code,$body]=$request(1,'own-command');$check($code===200&&$body['state']==='pending','Ausstehender Auftrag darf keinen Erfolg melden');
[$code]=$request(2,'own-command');$check($code===404,'Sitzungsauftrag darf nicht über einen anderen Server abgefragt werden');
[$code]=$request(1,'unknown-command');$check($code===404,'Fremder Auftrag darf nicht über eine erratene ID gelesen werden');
[$code]=$request(1,'../status');$check($code===404,'Ungültige Befehls-ID wird abgelehnt');
$_SESSION['_quick_manage_pending']['own-command']['created_at']=time()-901;
[$code]=$request(1,'own-command');$check($code===404,'Abgelaufene Sitzungsfreigabe wird abgelehnt');
$_SESSION['_quick_manage_pending']['own-command']['created_at']=time();
$ingest=new ReflectionMethod(App\Services\FastLiveService::class,'ingestResults');
$ingest->invoke(null,1,[['id'=>'own-command','success'=>false,'message'=>'Spieler ist nicht online.']]);
[$code,$body]=$request(1,'own-command');$check($code===200&&$body['state']==='failed'&&$body['message']==='Spieler ist nicht online.','Bridge-Fehler wird unverändert zurückgegeben');
$ingest->invoke(null,1,[['id'=>'own-command','success'=>true,'message'=>'OK']]);
[$code,$body]=$request(1,'own-command');$check($code===200&&$body['state']==='confirmed','Bridge-Bestätigung wird als bestätigt zurückgegeben');
$pdo->exec('UPDATE user_server_access SET can_live_admin=0 WHERE server_id=1');
[$code]=$request(1,'own-command');$check($code===403,'Entzogener Serverzugriff blockiert die Ergebnisabfrage');
echo "OK  Schnellverwaltungs-Ergebnis: {$checks} Prüfungen zu Wartestatus, Bestätigung, Fehler und Zugriffsschutz\n";
