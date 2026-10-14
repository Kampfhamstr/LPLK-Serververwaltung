<?php
if(!in_array('sqlite',PDO::getAvailableDrivers(),true)){
    echo "SKIP PDO_SQLite ist in dieser Testumgebung nicht installiert.\n";
    exit(0);
}

$root=dirname(__DIR__);
$schema=file_get_contents($root.'/migrations/schema.sql');

$pdo=new PDO('sqlite::memory:');
$pdo->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
$pdo->exec('PRAGMA foreign_keys=ON');
$pdo->exec($schema);

function okSchema(bool $ok,string $msg):void{
    if(!$ok) throw new RuntimeException($msg);
    echo "OK  {$msg}\n";
}

$userColumns=[];
foreach($pdo->query('PRAGMA table_info(users)')->fetchAll(PDO::FETCH_ASSOC) as $c){
    $userColumns[$c['name']]=$c;
}
okSchema(isset($userColumns['auth_version']),'users.auth_version wird angelegt');
okSchema((string)$userColumns['auth_version']['dflt_value']==='1','auth_version Default ist 1');

$playerColumns=[];
foreach($pdo->query('PRAGMA table_info(game_players)')->fetchAll(PDO::FETCH_ASSOC) as $c){
    $playerColumns[$c['name']]=$c;
}
okSchema(!isset($playerColumns['auth_version']),'game_players erhält keine auth_version');

$table=$pdo->query("SELECT name FROM sqlite_master WHERE type='table' AND name='password_reset_requests'")->fetchColumn();
okSchema($table==='password_reset_requests','password_reset_requests wird angelegt');

$indexes=$pdo->query("SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'idx_password_reset_%' ORDER BY name")->fetchAll(PDO::FETCH_COLUMN);
okSchema(count($indexes)===3,'drei Passwort-Reset-Indizes werden angelegt');

echo "\nPassword-Recovery-Schema-Test erfolgreich.\n";
