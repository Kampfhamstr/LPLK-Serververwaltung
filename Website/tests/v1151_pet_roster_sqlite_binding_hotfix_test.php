<?php
$root1151=dirname(__DIR__);

function ok1151(bool $condition,string $label):void
{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}

$version1151=trim((string)file_get_contents($root1151.'/VERSION'));
$playgroundService1151=(string)file_get_contents($root1151.'/src/Services/PetPlaygroundService.php');
$steamIdentityController1151=(string)file_get_contents($root1151.'/src/Controllers/SteamIdentityController.php');

ok1151(version_compare($version1151,'1.15.1','>='),'Webpanel-Version ist 1.15.1 oder neuer');
ok1151(
    str_contains($playgroundService1151,'CAST(? AS INTEGER)=1')
    &&preg_match('/AND\s+\?\s*=\s*1/', $playgroundService1151)!==1,
    'Steam-verifizierter Roster-Zweig konvertiert den PDO-Parameter explizit für SQLite'
);
ok1151(
    str_contains($steamIdentityController1151,'COALESCE(steam_profile_refresh_attempted_epoch,0)<=CAST(? AS INTEGER)'),
    'Steam-Profilabruf vergleicht die PDO-Cooldown-Grenze als SQLite-Ganzzahl'
);

if(!extension_loaded('pdo_sqlite')){
    fwrite(STDERR,"FEHLER: pdo_sqlite fehlt für den v1.15.1-Roster-Hotfix-Test\n");
    exit(1);
}

$pdo1151=new PDO('sqlite::memory:');
$pdo1151->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
$probe1151=$pdo1151->prepare('SELECT CAST(? AS INTEGER)=1');
$probe1151->execute([1]);
ok1151((int)$probe1151->fetchColumn()===1,'PDO-Execute-Array aktiviert den SQLite-Zweig nach expliziter Konvertierung');

$pdo1151->exec('CREATE TABLE users (id INTEGER PRIMARY KEY, steam_profile_refresh_attempted_epoch INTEGER NULL)');
$pdo1151->exec('INSERT INTO users (id,steam_profile_refresh_attempted_epoch) VALUES (1,NULL)');
$attemptedAt1151=time();
$reserve1151=$pdo1151->prepare('UPDATE users SET steam_profile_refresh_attempted_epoch=? WHERE id=? AND COALESCE(steam_profile_refresh_attempted_epoch,0)<=CAST(? AS INTEGER)');
$reserve1151->execute([$attemptedAt1151,1,$attemptedAt1151-120]);
$firstReservation1151=$reserve1151->rowCount();
$reserve1151->execute([$attemptedAt1151,1,$attemptedAt1151-120]);
ok1151(
    $firstReservation1151===1&&$reserve1151->rowCount()===0,
    'Steam-Profilabruf kann innerhalb von 120 Sekunden nicht erneut reserviert werden'
);

echo PHP_EOL."v1.15.1 Pet-Roster-SQLite-Binding-Hotfix erfolgreich geprüft.\n";
