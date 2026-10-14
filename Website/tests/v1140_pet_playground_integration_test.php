<?php
$root1140=dirname(__DIR__);

require_once $root1140.'/src/Core/Database.php';
require_once $root1140.'/src/Core/Auth.php';
require_once $root1140.'/src/Services/PetStatsService.php';
require_once $root1140.'/src/Services/PetGameService.php';
require_once $root1140.'/src/Services/PetService.php';
require_once $root1140.'/src/Services/PetPlaygroundService.php';

function ok1140(bool $condition,string $label):void
{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}

function rejects1140(callable $operation,string $label,string $messagePart=''):void
{
    $error=null;
    try{$operation();}catch(\Throwable $caught){$error=$caught;}
    ok1140($error instanceof \Throwable&&($messagePart===''||str_contains($error->getMessage(),$messagePart)),$label);
}

function scalar1140(PDO $pdo,string $sql,array $parameters=[]):mixed
{
    $statement=$pdo->prepare($sql);$statement->execute($parameters);return $statement->fetchColumn();
}

function row1140(PDO $pdo,string $sql,array $parameters=[]):array
{
    $statement=$pdo->prepare($sql);$statement->execute($parameters);return $statement->fetch()?:[];
}

function petState1140(PDO $pdo,int $userId,int $petId):array
{
    return [
        'pet'=>row1140($pdo,'SELECT level,xp,health,hunger,energy,happiness,bond,last_state_at,version,updated_at FROM pets WHERE id=? AND user_id=?',[$petId,$userId]),
        'wallet'=>row1140($pdo,'SELECT coins,training_points,updated_at FROM pet_wallets WHERE user_id=?',[$userId]),
        'ledger'=>(int)scalar1140($pdo,'SELECT COUNT(*) FROM pet_wallet_ledger WHERE user_id=?',[$userId]),
    ];
}

function petFromRoster1140(array $roster,int $petId):?array
{
    foreach($roster as $pet)if(is_array($pet)&&(int)($pet['pet_id']??0)===$petId)return $pet;
    return null;
}

$routes1140=(string)file_get_contents($root1140.'/public/index.php');
$controller1140=(string)file_get_contents($root1140.'/src/Controllers/PetsController.php');
$layout1140=(string)file_get_contents($root1140.'/templates/layout.php');
$template1140=(string)file_get_contents($root1140.'/templates/pet_playground.php');
$script1140=(string)file_get_contents($root1140.'/public/assets/pet-playground.js');
$styles1140=(string)file_get_contents($root1140.'/public/assets/pet-playground.css');
$schema1140=(string)file_get_contents($root1140.'/migrations/schema.sql');
$version1140=trim((string)file_get_contents($root1140.'/VERSION'));

ok1140(version_compare($version1140,'1.14.0','>='),'Webpanel-Version ist 1.14.0 oder neuer');
foreach([
    "get('/pets/playground'",
    "get('/pets/playground/state'",
    "post('/pets/playground/play'",
] as $route1140)ok1140(str_contains($routes1140,$route1140),'Route registriert: '.$route1140);

$playMethodStart1140=strpos($controller1140,'public function playTogether(): void');
$playMethodEnd1140=$playMethodStart1140===false?false:strpos($controller1140,'private function gameData',$playMethodStart1140);
$playMethod1140=$playMethodStart1140===false?'':substr($controller1140,$playMethodStart1140,$playMethodEnd1140===false?1800:$playMethodEnd1140-$playMethodStart1140);
ok1140(
    str_contains($controller1140,"Auth::requirePermission('pets.use')")
    &&str_contains($playMethod1140,'Security::verifyCsrf();')
    &&str_contains($controller1140,"header('Cache-Control: no-store')"),
    'Spielwiese verlangt pets.use, schützt Schreibzugriffe mit CSRF und liefert Status ohne Cache'
);
ok1140(
    is_file($root1140.'/public/assets/pet-playground.js')
    &&is_file($root1140.'/public/assets/pet-playground.css')
    &&str_contains($layout1140,'/assets/pet-playground.js?v='.$version1140)
    &&str_contains($layout1140,'/assets/pet-playground.css?v='.$version1140),
    'Versionierte Spielwiesen-Assets werden ausschließlich für die Pet-Spielwiese eingebunden'
);
ok1140(
    str_contains($script1140,"matchMedia('(prefers-reduced-motion: reduce)')")
    &&str_contains($script1140,'document.hidden')
    &&str_contains($script1140,'textContent')
    &&str_contains($styles1140,'@media(prefers-reduced-motion:reduce)'),
    'Animationen respektieren reduzierte Bewegung und pausieren bei unsichtbarer Seite'
);
ok1140(
    str_contains($template1140,'data-playground-name')
    &&str_contains($template1140,'data-playground-level')
    &&str_contains($template1140,'data-playground-owner')
    &&str_contains($template1140,'Besitzer:'),
    'Pet-Name, Level und Besitzer sind als dauerhaftes Namensschild vorgesehen'
);
ok1140(str_contains($schema1140,'CREATE TABLE IF NOT EXISTS pet_playground_interactions'),'Schema enthält synchronisierte Pet-Spielmomente');

// Das Produktionsschema wird bewusst zweimal auf eine vollständig isolierte
// SQLite-Datenbank angewendet. So deckt der Test frische Installationen und
// wiederholte Migrationen ab, ohne produktive Daten zu berühren.
$memory1140=new PDO('sqlite::memory:');
$memory1140->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
$memory1140->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE,PDO::FETCH_ASSOC);
$memory1140->setAttribute(PDO::ATTR_EMULATE_PREPARES,false);
$memory1140->exec('PRAGMA foreign_keys=ON');
$memory1140->exec($schema1140);
$memory1140->exec($schema1140);
ok1140(
    (int)scalar1140($memory1140,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='pet_playground_interactions'")===1
    &&(int)scalar1140($memory1140,"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'idx_pet_playground_%'")>=4,
    'Spielwiesen-Schema und Indizes sind bei zweifacher Ausführung idempotent'
);

$nowIso1140=date('c');
$insertUser1140=$memory1140->prepare(
    "INSERT INTO users(name,email,password_hash,web_role,active,steam_id64,steam_personaname,steam_verified_at,last_seen_at,created_at,updated_at) VALUES(?,?,?,'spieler',1,?,?,?,?,?,?)"
);
$insertUser1140->execute(['Besitzer Alpha','pet-v1140-a@example.invalid','test-only','76561198011400001','AlphaSteam',$nowIso1140,$nowIso1140,$nowIso1140,$nowIso1140]);
$userA1140=(int)$memory1140->lastInsertId();
$insertUser1140->execute(['Besitzer Bravo','pet-v1140-b@example.invalid','test-only','76561198011400002','BravoSteam',$nowIso1140,$nowIso1140,$nowIso1140,$nowIso1140]);
$userB1140=(int)$memory1140->lastInsertId();
$insertUser1140->execute(['Besitzer Charlie','pet-v1140-c@example.invalid','test-only','76561198011400003','CharlieSteam',$nowIso1140,$nowIso1140,$nowIso1140,$nowIso1140]);
$userC1140=(int)$memory1140->lastInsertId();
$insertUser1140->execute(['Besitzer Delta','pet-v1140-d@example.invalid','test-only','76561198011400004','DeltaSteam',$nowIso1140,$nowIso1140,$nowIso1140,$nowIso1140]);
$userD1140=(int)$memory1140->lastInsertId();
$insertUser1140->execute(['Besitzer Echo','pet-v1140-e@example.invalid','test-only','76561198011400005','EchoSteam',$nowIso1140,$nowIso1140,$nowIso1140,$nowIso1140]);
$userE1140=(int)$memory1140->lastInsertId();
$insertUser1140->execute(['Besitzer Foxtrott','pet-v1140-f@example.invalid','test-only','76561198011400006','FoxtrottSteam',$nowIso1140,$nowIso1140,$nowIso1140,$nowIso1140]);
$userF1140=(int)$memory1140->lastInsertId();
$insertUser1140->execute(['Besitzer Golf','pet-v1140-g@example.invalid','test-only','76561198011400007','GolfSteam',$nowIso1140,$nowIso1140,$nowIso1140,$nowIso1140]);
$userG1140=(int)$memory1140->lastInsertId();
$extraUserIds1140=[];
for($index1140=1;$index1140<=7;$index1140++){
    $insertUser1140->execute(['Besitzer Zusatz '.$index1140,'pet-v1140-extra-'.$index1140.'@example.invalid','test-only',(string)(76561198011400007+$index1140),'ZusatzSteam'.$index1140,$nowIso1140,$nowIso1140,$nowIso1140,$nowIso1140]);
    $extraUserIds1140[]=(int)$memory1140->lastInsertId();
}

$databasePdo1140=new ReflectionProperty(\App\Core\Database::class,'pdo');
$databasePdo1140->setAccessible(true);
$originalPdo1140=$databasePdo1140->getValue();
$databasePdo1140->setValue(null,$memory1140);

try{
    $petA1140=\App\Services\PetService::adopt($userA1140,'cat','Minka');
    $petB1140=\App\Services\PetService::adopt($userB1140,'hamster','Flocke');
    $petC1140=\App\Services\PetService::adopt($userC1140,'dragon','Funke');
    $petD1140=\App\Services\PetService::adopt($userD1140,'otter','Kiesel');
    $petE1140=\App\Services\PetService::adopt($userE1140,'rabbit','Hoppel');
    $petF1140=\App\Services\PetService::adopt($userF1140,'fox','Kupfer');
    $petG1140=\App\Services\PetService::adopt($userG1140,'owl','Feder');
    $extraPetIds1140=[];
    foreach($extraUserIds1140 as $index1140=>$extraUserId1140){
        $extraPetIds1140[]=\App\Services\PetService::adopt($extraUserId1140,'dog','Extra '.($index1140+1));
    }
    $memory1140->prepare('UPDATE pets SET level=7,xp=900,updated_at=? WHERE id=? AND user_id=?')->execute([date('c'),$petB1140,$userB1140]);

    // Ein fremdes Pet auf laufendem Abenteuer darf weder im Roster erscheinen
    // noch über eine direkt eingesandte ID als Ziel erreichbar sein.
    $started1140=time();$ready1140=$started1140+600;
    $memory1140->prepare(
        "INSERT INTO pet_adventures(user_id,pet_id,adventure_key,status,difficulty,idempotency_key,started_at_epoch,ready_at_epoch,result_json,created_at,updated_at) VALUES(?,?,?,'running',?,?,?,?,?,?,?)"
    )->execute([$userC1140,$petC1140,'sunny_meadow',1,'v1140-running-away-0001',$started1140,$ready1140,'{"test":true}',date('c'),date('c')]);

    $overview1140=\App\Services\PetPlaygroundService::overview($userA1140);
    $roster1140=$overview1140['playgroundPets']??[];
    $selfRoster1140=petFromRoster1140($roster1140,$petA1140);
    $bravoRoster1140=petFromRoster1140($roster1140,$petB1140);
    ok1140(
        $selfRoster1140!==null
        &&$bravoRoster1140!==null
        &&(string)$bravoRoster1140['name']==='Flocke'
        &&(int)$bravoRoster1140['level']===7
        &&(string)$bravoRoster1140['owner_name']==='BravoSteam',
        'Roster zeigt echtes eigenes und fremdes Pet mit Pet-Name, Level und Besitzer'
    );
    ok1140(
        petFromRoster1140($roster1140,$petC1140)===null
        &&petFromRoster1140($roster1140,$petD1140)!==null,
        'Laufendes Abenteuer blendet nur das betroffene fremde Pet aus der Spielwiese aus'
    );

    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petA1140,'greet','v1140-play-self-000001'),
        'Das eigene Pet kann nicht als Spielkamerad gewählt werden',
        'nicht auf der Spielwiese'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,999999,'greet','v1140-play-missing-0001'),
        'Eine unbekannte Pet-ID wird neutral abgewiesen',
        'nicht auf der Spielwiese'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petC1140,'greet','v1140-play-away-000001'),
        'Ein unterwegs befindliches Ziel-Pet bleibt auch bei direktem Request gesperrt',
        'nicht auf der Spielwiese'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petB1140,'unknown','v1140-play-type-000001'),
        'Nur fest definierte Spielanimationen werden akzeptiert',
        'gültige Spielart'
    );

    $beforeActor1140=petState1140($memory1140,$userA1140,$petA1140);
    $beforeTarget1140=petState1140($memory1140,$userB1140,$petB1140);
    $firstKey1140='v1140-play-first-000001';
    $firstPlay1140=\App\Services\PetPlaygroundService::play($userA1140,$petB1140,'greet',$firstKey1140);
    $firstEventId1140=(int)$firstPlay1140['interaction_id'];
    $afterFirstActor1140=petState1140($memory1140,$userA1140,$petA1140);
    $afterFirstTarget1140=petState1140($memory1140,$userB1140,$petB1140);
    ok1140(
        !$firstPlay1140['duplicate']
        &&$firstEventId1140>0
        &&$beforeActor1140===$afterFirstActor1140
        &&$beforeTarget1140===$afterFirstTarget1140,
        'Spielmoment schreibt genau ein Animationsevent und lässt beide Pet-Werte, Wallets, Ledger und Versionen unverändert'
    );

    $duplicate1140=\App\Services\PetPlaygroundService::play($userA1140,$petB1140,'greet',$firstKey1140);
    ok1140(
        $duplicate1140['duplicate']
        &&(int)$duplicate1140['interaction_id']===$firstEventId1140
        &&(int)scalar1140($memory1140,'SELECT COUNT(*) FROM pet_playground_interactions WHERE actor_user_id=? AND idempotency_key=?',[$userA1140,$firstKey1140])===1,
        'Identischer Doppelrequest liefert dasselbe Event und erzeugt keinen zweiten Datensatz'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petB1140,'ball','v1140-play-first-000001'),
        'Idempotenzschlüssel ist fest an den Interaktionstyp gebunden',
        'anderen Spielmoment'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petD1140,'greet','v1140-play-first-000001'),
        'Idempotenzschlüssel ist fest an das Ziel-Pet gebunden',
        'anderen Spielmoment'
    );

    $stateAfterFirst1140=\App\Services\PetPlaygroundService::state($userA1140,0);
    $stateFirstIds1140=array_map('intval',array_column($stateAfterFirst1140['events']??[],'id'));
    ok1140(
        in_array($firstEventId1140,$stateFirstIds1140,true)
        &&(int)($stateAfterFirst1140['cursor']??0)>=$firstEventId1140
        &&petFromRoster1140($stateAfterFirst1140['pets']??[],$petC1140)===null,
        'Status-Endpunkt liefert sichtbare neue Events, Cursor und den gefilterten Live-Roster'
    );
    ok1140(
        array_key_exists('playStartsLeft',$stateAfterFirst1140)
        &&array_key_exists('playDailyLimit',$stateAfterFirst1140)
        &&array_key_exists('playCooldownUntil',$stateAfterFirst1140)
        &&array_key_exists('playBlockedReason',$stateAfterFirst1140)
        &&(int)$stateAfterFirst1140['playStartsLeft']===23
        &&(int)$stateAfterFirst1140['playDailyLimit']===24
        &&(int)$stateAfterFirst1140['playCooldownUntil']>(int)$stateAfterFirst1140['server_time']
        &&str_contains((string)$stateAfterFirst1140['playBlockedReason'],'Pfotenpause'),
        'Status-Endpunkt liefert live das Restkontingent, Tageslimit, Cooldown-Ende und den aktuellen Sperrgrund'
    );

    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petD1140,'sniff','v1140-play-global-00001'),
        'Globaler Kurz-Cooldown verhindert sofortigen Zielwechsel',
        'Pfotenpause'
    );

    // Nach 20 Sekunden ist der globale Cooldown beendet, die symmetrische
    // Paarpause von 60 Sekunden muss jedoch weiterhin greifen.
    $memory1140->prepare('UPDATE pet_playground_interactions SET created_at_epoch=? WHERE id=?')->execute([time()-20,$firstEventId1140]);
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petB1140,'sniff','v1140-play-pair-000001'),
        'Paarweiser Cooldown bleibt nach Ablauf des globalen Cooldowns aktiv',
        'beiden Pets'
    );

    $memory1140->prepare('UPDATE pet_playground_interactions SET created_at_epoch=? WHERE id=?')->execute([time()-70,$firstEventId1140]);
    $beforeSecondActor1140=petState1140($memory1140,$userA1140,$petA1140);
    $beforeSecondTarget1140=petState1140($memory1140,$userD1140,$petD1140);
    $secondPlay1140=\App\Services\PetPlaygroundService::play($userA1140,$petD1140,'ball','v1140-play-second-00001');
    $secondEventId1140=(int)$secondPlay1140['interaction_id'];
    $stateAfterCursor1140=\App\Services\PetPlaygroundService::state($userA1140,$firstEventId1140);
    $stateAfterCursorIds1140=array_map('intval',array_column($stateAfterCursor1140['events']??[],'id'));
    ok1140(
        !$secondPlay1140['duplicate']
        &&$secondEventId1140>$firstEventId1140
        &&in_array($secondEventId1140,$stateAfterCursorIds1140,true)
        &&!in_array($firstEventId1140,$stateAfterCursorIds1140,true)
        &&petState1140($memory1140,$userA1140,$petA1140)===$beforeSecondActor1140
        &&petState1140($memory1140,$userD1140,$petD1140)===$beforeSecondTarget1140,
        'Event-Cursor liefert nur spätere Spielmomente; auch beim zweiten Paar bleiben beide Pet- und Walletzustände unverändert'
    );

    // Die beiden echten Events werden auf exakt 24 Tagesereignisse ergänzt.
    // Vergangene, bereits abgelaufene Animationen zählen bewusst weiterhin
    // gegen das serverseitige Tageslimit.
    $period1140=date('Y-m-d');
    $dailyCount1140=(int)scalar1140($memory1140,'SELECT COUNT(*) FROM pet_playground_interactions WHERE actor_user_id=? AND actor_pet_id=? AND period_key=?',[$userA1140,$petA1140,$period1140]);
    $insertEvent1140=$memory1140->prepare('INSERT INTO pet_playground_interactions(actor_user_id,actor_pet_id,target_user_id,target_pet_id,interaction_type,period_key,idempotency_key,created_at_epoch,expires_at_epoch,created_at) VALUES(?,?,?,?,?,?,?,?,?,?)');
    for($index1140=$dailyCount1140;$index1140<24;$index1140++){
        $eventTime1140=time()-7200-$index1140;
        $insertEvent1140->execute([$userA1140,$petA1140,$userD1140,$petD1140,'chase',$period1140,sprintf('v1140-daily-fill-%04d',$index1140),$eventTime1140,$eventTime1140+180,date('c',$eventTime1140)]);
    }
    ok1140(
        (int)scalar1140($memory1140,'SELECT COUNT(*) FROM pet_playground_interactions WHERE actor_user_id=? AND actor_pet_id=? AND period_key=?',[$userA1140,$petA1140,$period1140])===24,
        'Testaufbau erreicht exakt das serverseitige Tageskontingent'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petB1140,'chase','v1140-play-daily-limit'),
        'Der 25. gemeinsame Spielmoment eines Tages wird abgewiesen',
        'alle 24'
    );
    ok1140(
        petState1140($memory1140,$userA1140,$petA1140)===$beforeActor1140
        &&petState1140($memory1140,$userB1140,$petB1140)===$beforeTarget1140,
        'Auch Cooldown-, Cursor- und Limitprüfungen verändern Pet-Werte, Wallets, Ledger und Versionen nicht'
    );

    // Die serverseitige Auswahl von zwölf Figuren ist autoritativ. Auch ein
    // ansonsten aktives Pet darf nicht durch eine direkt eingesandte ID an der
    // sichtbaren Auswahl vorbei als Ziel verwendet werden.
    $memory1140->exec('DELETE FROM pet_playground_interactions');
    $capState1140=\App\Services\PetPlaygroundService::state($userA1140,0);
    $capRosterIds1140=array_map('intval',array_column($capState1140['pets']??[],'pet_id'));
    $outsidePetIds1140=array_values(array_filter($extraPetIds1140,static fn(int $petId):bool=>!in_array($petId,$capRosterIds1140,true)));
    $outsidePetId1140=(int)($outsidePetIds1140[0]??0);
    ok1140(
        count($capRosterIds1140)===12
        &&$outsidePetId1140>0
        &&(int)scalar1140($memory1140,'SELECT COUNT(*) FROM pets p JOIN users u ON u.id=p.user_id WHERE p.id=? AND p.active=1 AND u.active=1',[$outsidePetId1140])===1,
        'Autoritativer Roster enthält höchstens zwölf Pets und lässt mindestens ein weiterhin aktives Pet außerhalb'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$outsidePetId1140,'greet','v1140-play-outside-cap'),
        'Aktives Pet außerhalb des autoritativen Zwölfer-Rosters wird neutral abgewiesen',
        'nicht auf der Spielwiese'
    );

    // Exakt 60 Eingänge werden auf drei fremde Initiatoren verteilt. So bleibt
    // deren jeweiliges Tageskontingent bei 20 und das Pet des Testnutzers hat
    // weder einen globalen noch einen paarweisen Cooldown zum Ziel.
    $memory1140->exec('DELETE FROM pet_playground_interactions');
    $incomingActors1140=[
        ['user_id'=>$userD1140,'pet_id'=>$petD1140],
        ['user_id'=>$userE1140,'pet_id'=>$petE1140],
        ['user_id'=>$userF1140,'pet_id'=>$petF1140],
    ];
    $incomingPeriod1140=date('Y-m-d');
    $incomingInsert1140=$memory1140->prepare('INSERT INTO pet_playground_interactions(actor_user_id,actor_pet_id,target_user_id,target_pet_id,interaction_type,period_key,idempotency_key,created_at_epoch,expires_at_epoch,created_at) VALUES(?,?,?,?,?,?,?,?,?,?)');
    for($index1140=0;$index1140<60;$index1140++){
        $incomingActor1140=$incomingActors1140[$index1140%count($incomingActors1140)];
        $incomingEpoch1140=time()-7200-$index1140;
        $incomingInsert1140->execute([
            $incomingActor1140['user_id'],$incomingActor1140['pet_id'],$userB1140,$petB1140,'sniff',$incomingPeriod1140,
            sprintf('v1140-incoming-%04d',$index1140),$incomingEpoch1140,$incomingEpoch1140+180,date('c',$incomingEpoch1140),
        ]);
    }
    $incomingState1140=\App\Services\PetPlaygroundService::state($userA1140,0);
    $restingTarget1140=petFromRoster1140($incomingState1140['pets']??[],$petB1140);
    $incomingOverview1140=\App\Services\PetPlaygroundService::overview($userA1140);
    $restingOverviewTarget1140=petFromRoster1140($incomingOverview1140['playgroundPets']??[],$petB1140);
    $incomingActorMax1140=(int)scalar1140($memory1140,'SELECT COALESCE(MAX(total),0) FROM (SELECT COUNT(*) AS total FROM pet_playground_interactions WHERE period_key=? GROUP BY actor_user_id,actor_pet_id) AS incoming_actor_counts',[$incomingPeriod1140]);
    ok1140(
        (int)scalar1140($memory1140,'SELECT COUNT(*) FROM pet_playground_interactions WHERE target_pet_id=? AND period_key=?',[$petB1140,$incomingPeriod1140])===60
        &&$incomingActorMax1140===20
        &&is_array($restingTarget1140)
        &&is_array($restingOverviewTarget1140)
        &&!empty($restingTarget1140['target_resting'])
        &&!empty($restingOverviewTarget1140['target_resting'])
        &&empty($restingTarget1140['can_interact'])
        &&empty($restingOverviewTarget1140['can_interact'])
        &&str_contains((string)($restingTarget1140['locked_reason']??''),'ruht'),
        'Exakt 60 Tageseingänge markieren das Ziel in Status und Übersicht ausdrücklich als ruhend und nicht mehr bespielbar'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petB1140,'ball','v1140-play-incoming-61'),
        'Der 61. eingehende Spielmoment wird auch serverseitig gesperrt',
        'ruht sich für heute'
    );

    // Die Paarpause richtet sich nach dem echten Zeitstempel und darf nicht an
    // einer Tagesgrenze verschwinden. Der umgekehrte Actor hält zugleich den
    // globalen und täglichen Actor-Zustand des Testnutzers vollständig frei.
    $memory1140->exec('DELETE FROM pet_playground_interactions');
    $crossDayNow1140=time();$crossDayEpoch1140=$crossDayNow1140-20;
    $incomingInsert1140->execute([
        $userB1140,$petB1140,$userA1140,$petA1140,'greet',date('Y-m-d',$crossDayNow1140-86400),
        'v1140-yesterday-pair-01',$crossDayEpoch1140,$crossDayNow1140+180,date('c',$crossDayEpoch1140),
    ]);
    $crossDayState1140=\App\Services\PetPlaygroundService::state($userA1140,0);
    $crossDayMate1140=petFromRoster1140($crossDayState1140['pets']??[],$petB1140);
    ok1140(
        is_array($crossDayMate1140)
        &&empty($crossDayMate1140['can_interact'])
        &&(int)($crossDayMate1140['pair_cooldown_until']??0)>(int)$crossDayState1140['server_time']
        &&str_contains((string)($crossDayMate1140['locked_reason']??''),'Pause')
        &&(int)$crossDayState1140['playStartsLeft']===24
        &&$crossDayState1140['playCooldownUntil']===null,
        'Paar-Cooldown bleibt über den period_key des Vortags aktiv, ohne globales oder tägliches Actor-Limit auszulösen'
    );
    rejects1140(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1140,$petB1140,'chase','v1140-play-yesterday-pair'),
        'Server erzwingt die Paarpause auch bei einem Event mit gestrigem period_key',
        'beiden Pets'
    );

    // Pagination wird mit 105 aktuell sichtbaren Events isoliert geprüft. Fünf
    // verschiedene Initiatoren erzeugen jeweils nur 21 Tagesereignisse; auch
    // kein Ziel erreicht das Eingangslimit von 60. Damit umgeht der Test die
    // Fachlimits nicht durch einen unrealistischen Einzelakteur.
    $memory1140->exec('DELETE FROM pet_playground_interactions');
    $paginationParticipants1140=[
        ['user_id'=>$userB1140,'pet_id'=>$petB1140],
        ['user_id'=>$userD1140,'pet_id'=>$petD1140],
        ['user_id'=>$userE1140,'pet_id'=>$petE1140],
        ['user_id'=>$userF1140,'pet_id'=>$petF1140],
        ['user_id'=>$userG1140,'pet_id'=>$petG1140],
    ];
    $paginationTypes1140=['greet','sniff','chase','ball'];
    $paginationNow1140=time();$paginationPeriod1140=date('Y-m-d',$paginationNow1140);
    $paginationInsert1140=$memory1140->prepare('INSERT INTO pet_playground_interactions(actor_user_id,actor_pet_id,target_user_id,target_pet_id,interaction_type,period_key,idempotency_key,created_at_epoch,expires_at_epoch,created_at) VALUES(?,?,?,?,?,?,?,?,?,?)');
    for($index1140=0;$index1140<105;$index1140++){
        $actor1140=$paginationParticipants1140[$index1140%count($paginationParticipants1140)];
        $target1140=$paginationParticipants1140[($index1140+1)%count($paginationParticipants1140)];
        $createdEpoch1140=$paginationNow1140-240+$index1140;
        $paginationInsert1140->execute([
            $actor1140['user_id'],$actor1140['pet_id'],$target1140['user_id'],$target1140['pet_id'],
            $paginationTypes1140[$index1140%count($paginationTypes1140)],$paginationPeriod1140,
            sprintf('v1140-page-event-%04d',$index1140),$createdEpoch1140,$paginationNow1140+600,date('c',$createdEpoch1140),
        ]);
    }
    $maxActorDay1140=(int)scalar1140($memory1140,'SELECT COALESCE(MAX(total),0) FROM (SELECT COUNT(*) AS total FROM pet_playground_interactions WHERE period_key=? GROUP BY actor_user_id,actor_pet_id) AS actor_counts',[$paginationPeriod1140]);
    $maxTargetDay1140=(int)scalar1140($memory1140,'SELECT COALESCE(MAX(total),0) FROM (SELECT COUNT(*) AS total FROM pet_playground_interactions WHERE period_key=? GROUP BY target_pet_id) AS target_counts',[$paginationPeriod1140]);
    ok1140(
        (int)scalar1140($memory1140,'SELECT COUNT(*) FROM pet_playground_interactions')===105
        &&$maxActorDay1140<=24
        &&$maxTargetDay1140<=60,
        'Paginationstest seedet 105 sichtbare Events innerhalb der Actor- und Ziel-Tageslimits'
    );

    $allPaginationIds1140=array_map('intval',$memory1140->query('SELECT id FROM pet_playground_interactions ORDER BY id')->fetchAll(PDO::FETCH_COLUMN));
    $firstPage1140=\App\Services\PetPlaygroundService::state($userA1140,0);
    $firstPageIds1140=array_map('intval',array_column($firstPage1140['events']??[],'id'));
    $expectedFirstPageIds1140=array_slice($allPaginationIds1140,0,100);
    ok1140(
        count($firstPageIds1140)===100
        &&$firstPageIds1140===$expectedFirstPageIds1140
        &&(int)$firstPage1140['cursor']===$expectedFirstPageIds1140[99],
        'Erste Statusseite liefert genau 100 Events und setzt den Cursor ohne Sprung auf den letzten gescannten Datensatz'
    );

    $secondPage1140=\App\Services\PetPlaygroundService::state($userA1140,(int)$firstPage1140['cursor']);
    $secondPageIds1140=array_map('intval',array_column($secondPage1140['events']??[],'id'));
    $expectedSecondPageIds1140=array_slice($allPaginationIds1140,100);
    ok1140(
        count($secondPageIds1140)===5
        &&$secondPageIds1140===$expectedSecondPageIds1140
        &&(int)$secondPage1140['cursor']===$expectedSecondPageIds1140[4],
        'Zweite Statusseite liefert lückenlos die fünf verbleibenden Events'
    );
}finally{
    $databasePdo1140->setValue(null,$originalPdo1140);
}

echo "\nv1.14.0 Community-Spielwiese, Animationsevents und Sicherheitsgrenzen erfolgreich geprüft.\n";
