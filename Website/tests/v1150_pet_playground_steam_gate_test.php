<?php
$root1150=dirname(__DIR__);

require_once $root1150.'/src/Core/Database.php';
require_once $root1150.'/src/Services/PetStatsService.php';
require_once $root1150.'/src/Services/PetGameService.php';
require_once $root1150.'/src/Services/PetService.php';
require_once $root1150.'/src/Services/PetPlaygroundService.php';
require_once $root1150.'/src/Services/PetArenaService.php';

function ok1150(bool $condition,string $label):void
{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}

function rejects1150(callable $operation,string $label,string $messagePart=''):void
{
    $error=null;
    try{$operation();}catch(\Throwable $caught){$error=$caught;}
    ok1150($error instanceof \Throwable&&($messagePart===''||str_contains($error->getMessage(),$messagePart)),$label);
}

function scalar1150(PDO $pdo,string $sql,array $parameters=[]):mixed
{
    $statement=$pdo->prepare($sql);$statement->execute($parameters);return $statement->fetchColumn();
}

function rosterPet1150(array $roster,int $petId):?array
{
    foreach($roster as $pet)if(is_array($pet)&&(int)($pet['pet_id']??0)===$petId)return $pet;
    return null;
}

function method1150(string $source,string $method,string $nextMethod):string
{
    $start=strpos($source,'public function '.$method.'(');
    if($start===false)return '';
    $end=strpos($source,'public function '.$nextMethod.'(',$start+1);
    return substr($source,$start,$end===false?null:$end-$start);
}

$version1150=trim((string)file_get_contents($root1150.'/VERSION'));
$playgroundService1150=(string)file_get_contents($root1150.'/src/Services/PetPlaygroundService.php');
$petService1150=(string)file_get_contents($root1150.'/src/Services/PetService.php');
$arenaService1150=(string)file_get_contents($root1150.'/src/Services/PetArenaService.php');
$template1150=(string)file_get_contents($root1150.'/templates/pet_playground.php');
$security1150=(string)file_get_contents($root1150.'/src/Core/Security.php');
$steamController1150=(string)file_get_contents($root1150.'/src/Controllers/SteamIdentityController.php');
$migration1150=(string)file_get_contents($root1150.'/bin/migrate.php');
$schema1150=(string)file_get_contents($root1150.'/migrations/schema.sql');

ok1150(version_compare($version1150,'1.15.0','>='),'Webpanel-Version ist 1.15.0 oder neuer');
ok1150(
    str_contains($playgroundService1150,'private static function steamIdentity')
    &&str_contains($playgroundService1150,"preg_match('/^7656119\\d{10}$/',\$steamId)===1")
    &&str_contains($playgroundService1150,"trim((string)(\$row[\$prefix.'steam_personaname']??''))"),
    'Spielwiese verlangt eine gültige SteamID64, Verifizierungszeit und einen echten Steam-Personanamen'
);
ok1150(
    !str_contains($playgroundService1150,'Auth::displayName')
    &&!str_contains($playgroundService1150,'u.name AS owner_name')
    &&!str_contains($playgroundService1150,'actor_owner.name')
    &&!str_contains($playgroundService1150,'target_owner.name')
    &&str_contains($playgroundService1150,"'owner_name'=>\$owner??'Nicht öffentlich'"),
    'Spielwiesen-Besitzerlabels besitzen keinen Fallback auf interne Nutzernamen'
);
ok1150(
    str_contains($template1150,'if(!$playgroundSteamVerified)')
    &&str_contains($template1150,'data-playground-steam-gate')
    &&str_contains($template1150,'/profile#steam-verification')
    &&str_contains($template1150,"aria-disabled=\"<?=\$playgroundSteamVerified?'false':'true'?>\"")
    &&str_contains($template1150,"<fieldset class=\"pet-playground-interactions\"<?=\$playgroundSteamVerified?'':' disabled'?>>"),
    'Nicht verifizierte Nutzer sehen den Steam-Hinweis und erhalten einen deaktivierten Spielbereich'
);
ok1150(
    str_contains($petService1150,'trim(u.steam_personaname) AS owner_name')
    &&str_contains($petService1150,"trim(COALESCE(u.steam_verified_at,''))<>''")
    &&!str_contains($petService1150,'u.name AS owner_name'),
    'Auch die öffentliche Pet-Rangliste verwendet ausschließlich verifizierte Steam-Personanamen'
);
ok1150(
    str_contains($arenaService1150,'trim(u.steam_personaname) AS owner_name')
    &&str_contains($arenaService1150,"trim(COALESCE(u.steam_verified_at,''))<>''")
    &&!str_contains($arenaService1150,'u.name AS owner_name'),
    'Auch die Arena-Rangliste verwendet ausschließlich verifizierte Steam-Personanamen'
);

$connect1150=method1150($steamController1150,'connect','callback');
$callback1150=method1150($steamController1150,'callback','refresh');
$refresh1150=method1150($steamController1150,'refresh','avatar');
ok1150(
    str_contains($security1150,'public static function issueSteamOpenIdFlowCookie(string $state)')
    &&str_contains($security1150,'private static function steamOpenIdFlowSessionId(): ?string')
    &&str_contains($security1150,"'path' => '/profile/steam/callback'")
    &&str_contains($security1150,"'samesite' => 'Lax'")
    &&str_contains($security1150,"'expires_at'=>\$now + 600")
    &&str_contains($security1150,"hash_hmac('sha256'")
    &&!str_contains($security1150,'reissueSessionCookie'),
    'Steam verwendet eine signierte, 10 Minuten gültige Callback-Brücke statt das strikte Haupt-Session-Cookie zu lockern'
);
ok1150(
    ($laxPosition1150=strpos($connect1150,'Security::issueSteamOpenIdFlowCookie($state);'))!==false
    &&($redirectPosition1150=strpos($connect1150,"header('Location: '"))!==false
    &&str_contains($connect1150,"\$_SESSION['_created'] = \$startedAt")
    &&str_contains($connect1150,"\$_SESSION['steam_openid_started_at'] = \$startedAt")
    &&$laxPosition1150<$redirectPosition1150,
    'Steam-Connect fixiert die rotierte Session für den Flow und erstellt die begrenzte Callback-Brücke vor dem Redirect'
);
ok1150(
    ($restorePosition1150=strpos($callback1150,'Security::clearSteamOpenIdFlowCookie($state);'))!==false
    &&($stateCheckPosition1150=strpos($callback1150,"if (\$state === ''"))!==false
    &&$restorePosition1150<$stateCheckPosition1150
    &&str_contains($callback1150,'!hash_equals($expectedState, $state)')
    &&str_contains($callback1150,'$started < time() - 600'),
    'Steam-Callback löscht die einmalige Brücke und validiert State samt 600-Sekunden-Fenster'
);
ok1150(
    str_contains($schema1150,'steam_profile_refresh_attempted_epoch INTEGER NULL')
    &&str_contains($migration1150,"'steam_profile_refresh_attempted_epoch' => 'INTEGER NULL'")
    &&str_contains($refresh1150,'COALESCE(steam_profile_refresh_attempted_epoch,0)<=CAST(? AS INTEGER)')
    &&str_contains($refresh1150,'$attemptedAt - 120')
    &&str_contains($refresh1150,'$reserve->rowCount() !== 1')
    &&str_contains($refresh1150,'SteamService::ensureAvatarCached($steamId, $avatar, true);'),
    'Jeder manuelle Steam-Abrufversuch wird atomar und kontoweit gedrosselt; ein erlaubter Versuch erzwingt den Avatar-Refresh'
);
ok1150(
    str_contains($callback1150,'Der echte Steam-Anzeigename konnte nicht geladen werden')
    &&str_contains($steamController1150,"return ['steamid'=>\$steamId, 'personaname'=>''];")
    &&!str_contains($steamController1150,"'personaname'=>'Steam '")
    &&!str_contains($steamController1150,"\$name = 'Steam '"),
    'Ohne API- oder lokalen Profildatensatz wird kein künstlicher Steam-Besitzername erzeugt'
);
ok1150(
    str_contains($migration1150,"steam_personaname=NULL,steam_profile_synced_at=NULL")
    &&str_contains($migration1150,"('Steam ' || substr(trim(steam_id64),-6))")
    &&str_contains($playgroundService1150,"hash_equals('Steam '.substr(\$steamId,-6),\$persona)"),
    'Upgrade-Migration und Laufzeit-Gate sperren künstliche Steam-Namen aus älteren Versionen'
);

$memory1150=new PDO('sqlite::memory:');
$memory1150->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
$memory1150->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE,PDO::FETCH_ASSOC);
$memory1150->setAttribute(PDO::ATTR_EMULATE_PREPARES,false);
$memory1150->exec('PRAGMA foreign_keys=ON');
$memory1150->exec($schema1150);
$memory1150->exec($schema1150);
ok1150(
    (int)scalar1150($memory1150,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='pet_playground_interactions'")===1,
    'Produktionsschema bleibt bei zweifacher Ausführung idempotent'
);

$nowIso1150=date('c');
$insertUser1150=$memory1150->prepare(
    "INSERT INTO users(name,email,password_hash,web_role,active,steam_id64,steam_personaname,steam_verified_at,last_seen_at,created_at,updated_at) VALUES(?,?,?,'spieler',1,?,?,?,?,?,?)"
);
$addUser1150=static function(string $internalName,string $email,?string $steamId,?string $persona,?string $verifiedAt)use($memory1150,$insertUser1150,$nowIso1150):int{
    $insertUser1150->execute([$internalName,$email,'test-only',$steamId,$persona,$verifiedAt,$nowIso1150,$nowIso1150,$nowIso1150]);
    return (int)$memory1150->lastInsertId();
};

$userA1150=$addUser1150('INTERN-ALPHA-NICHT-ZEIGEN','pet-v1150-a@example.invalid','76561198011500001','SteamAlpha',$nowIso1150);
$userB1150=$addUser1150('INTERN-BRAVO-NICHT-ZEIGEN','pet-v1150-b@example.invalid','76561198011500002','SteamBravo',$nowIso1150);
$userNoId1150=$addUser1150('INTERN-OHNE-ID-NICHT-ZEIGEN','pet-v1150-no-id@example.invalid',null,'PersonaOhneId',$nowIso1150);
$userNoVerified1150=$addUser1150('INTERN-OHNE-VERIFY-NICHT-ZEIGEN','pet-v1150-no-verify@example.invalid','76561198011500003','PersonaOhneVerify',null);
$userNoPersona1150=$addUser1150('INTERN-OHNE-PERSONA-NICHT-ZEIGEN','pet-v1150-no-persona@example.invalid','76561198011500004','   ',$nowIso1150);
$userBadId1150=$addUser1150('INTERN-FALSCHE-ID-NICHT-ZEIGEN','pet-v1150-bad-id@example.invalid','7656119801150000X','PersonaMitFalscherId',$nowIso1150);
$userSynthetic1150=$addUser1150('INTERN-PLATZHALTER-NICHT-ZEIGEN','pet-v1150-synthetic@example.invalid','76561198011500006','Steam 500006',$nowIso1150);
$userPatternReal1150=$addUser1150('INTERN-ECHTES-MUSTER-NICHT-ZEIGEN','pet-v1150-real-pattern@example.invalid','76561198011500007','Steam 500007',$nowIso1150);
$memory1150->prepare('UPDATE users SET steam_profile_url=? WHERE id=?')->execute(['https://steamcommunity.com/profiles/76561198011500007',$userPatternReal1150]);
$userVerifiedNoPet1150=$addUser1150('INTERN-VERIFIZIERT-OHNE-PET','pet-v1150-verified-no-pet@example.invalid','76561198011500005','SteamOhnePet',$nowIso1150);
$userUnverifiedNoPet1150=$addUser1150('INTERN-UNVERIFIZIERT-OHNE-PET','pet-v1150-unverified-no-pet@example.invalid',null,null,null);

$attemptEpoch1150=time();
$reserveRefresh1150=$memory1150->prepare('UPDATE users SET steam_profile_refresh_attempted_epoch=? WHERE id=? AND COALESCE(steam_profile_refresh_attempted_epoch,0)<=CAST(? AS INTEGER)');
$reserveRefresh1150->execute([$attemptEpoch1150,$userA1150,$attemptEpoch1150-120]);
$firstReserveCount1150=$reserveRefresh1150->rowCount();
$reserveRefresh1150->execute([$attemptEpoch1150,$userA1150,$attemptEpoch1150-120]);
ok1150(
    $firstReserveCount1150===1&&$reserveRefresh1150->rowCount()===0,
    'Atomare Steam-Abrufreservierung lässt pro Konto im Zwei-Minuten-Fenster nur einen Versuch zu'
);

$databasePdo1150=new ReflectionProperty(\App\Core\Database::class,'pdo');
$databasePdo1150->setAccessible(true);
$originalPdo1150=$databasePdo1150->getValue();
$databasePdo1150->setValue(null,$memory1150);

try{
    $petA1150=\App\Services\PetService::adopt($userA1150,'cat','Minka');
    $petB1150=\App\Services\PetService::adopt($userB1150,'hamster','Flocke');
    $petNoId1150=\App\Services\PetService::adopt($userNoId1150,'dog','Ohne Id');
    $petNoVerified1150=\App\Services\PetService::adopt($userNoVerified1150,'rabbit','Ohne Verify');
    $petNoPersona1150=\App\Services\PetService::adopt($userNoPersona1150,'otter','Ohne Persona');
    $petBadId1150=\App\Services\PetService::adopt($userBadId1150,'fox','Falsche Id');
    $petSynthetic1150=\App\Services\PetService::adopt($userSynthetic1150,'panda','Platzhalter');
    $petPatternReal1150=\App\Services\PetService::adopt($userPatternReal1150,'moon_stag','Echtes Muster');
    $memory1150->prepare('UPDATE pets SET level=7,xp=900 WHERE id=?')->execute([$petB1150]);

    $arenaProfile1150=$memory1150->prepare('INSERT INTO pet_arena_profiles(pet_id,user_id,rating,wins,losses,draws,season_key,created_at,updated_at) VALUES(?,?,?,?,0,0,?,?,?)');
    foreach([
        [$petA1150,$userA1150,1100,5],[$petB1150,$userB1150,1090,4],[$petNoId1150,$userNoId1150,1080,3],
        [$petNoVerified1150,$userNoVerified1150,1070,2],[$petNoPersona1150,$userNoPersona1150,1060,2],
        [$petBadId1150,$userBadId1150,1050,1],[$petSynthetic1150,$userSynthetic1150,1040,1],
        [$petPatternReal1150,$userPatternReal1150,1030,1],
    ] as [$arenaPet1150,$arenaUser1150,$arenaRating1150,$arenaWins1150]){
        $arenaProfile1150->execute([$arenaPet1150,$arenaUser1150,$arenaRating1150,$arenaWins1150,date('Y-m'),$nowIso1150,$nowIso1150]);
    }

    $verifiedNoPetOverview1150=\App\Services\PetPlaygroundService::overview($userVerifiedNoPet1150);
    $verifiedNoPetState1150=\App\Services\PetPlaygroundService::state($userVerifiedNoPet1150,0);
    $unverifiedNoPetOverview1150=\App\Services\PetPlaygroundService::overview($userUnverifiedNoPet1150);
    ok1150(
        ($verifiedNoPetOverview1150['playSteamVerified']??false)===true
        &&($verifiedNoPetState1150['playSteamVerified']??false)===true
        &&($unverifiedNoPetOverview1150['playSteamVerified']??true)===false
        &&str_contains((string)$verifiedNoPetOverview1150['playBlockedReason'],'Adoptiere'),
        'Steam-Gate-Status bleibt auch ohne adoptiertes Pet wahrheitsgemäß'
    );

    $overviewA1150=\App\Services\PetPlaygroundService::overview($userA1150);
    $rosterA1150=$overviewA1150['playgroundPets']??[];
    $alpha1150=rosterPet1150($rosterA1150,$petA1150);
    $bravo1150=rosterPet1150($rosterA1150,$petB1150);
    ok1150(
        ($overviewA1150['playSteamVerified']??false)===true
        &&count($rosterA1150)===3
        &&$alpha1150!==null&&(string)$alpha1150['owner_name']==='SteamAlpha'
        &&$bravo1150!==null&&(string)$bravo1150['owner_name']==='SteamBravo'
        &&(string)$bravo1150['name']==='Flocke'&&(int)$bravo1150['level']===7,
        'Verifizierter Roster zeigt Name, Level und ausschließlich echte Steam-Besitzerlabels'
    );
    $patternReal1150=rosterPet1150($rosterA1150,$petPatternReal1150);
    ok1150(
        $patternReal1150!==null&&(string)$patternReal1150['owner_name']==='Steam 500007',
        'Ein echter, per Profil-URL belegter Steamname im alten Platzhalterformat bleibt zulässig'
    );
    ok1150(
        rosterPet1150($rosterA1150,$petNoId1150)===null
        &&rosterPet1150($rosterA1150,$petNoVerified1150)===null
        &&rosterPet1150($rosterA1150,$petNoPersona1150)===null
        &&rosterPet1150($rosterA1150,$petBadId1150)===null
        &&rosterPet1150($rosterA1150,$petSynthetic1150)===null
        &&!str_contains((string)json_encode($overviewA1150,JSON_UNESCAPED_UNICODE),'INTERN-'),
        'Unvollständige oder manipulierte Steam-Identitäten bleiben vollständig aus dem öffentlichen Roster'
    );

    $stateA1150=\App\Services\PetPlaygroundService::state($userA1150,0);
    ok1150(
        ($stateA1150['playSteamVerified']??false)===true
        &&count($stateA1150['pets']??[])===3
        &&!str_contains((string)json_encode($stateA1150,JSON_UNESCAPED_UNICODE),'INTERN-'),
        'Live-State wendet dasselbe Steam-Gate ohne interne Namenslecks an'
    );

    $unverifiedUsers1150=[
        [$userNoId1150,$petNoId1150,'no-id'],
        [$userNoVerified1150,$petNoVerified1150,'no-verify'],
        [$userNoPersona1150,$petNoPersona1150,'no-persona'],
        [$userBadId1150,$petBadId1150,'bad-id'],
        [$userSynthetic1150,$petSynthetic1150,'synthetic-name'],
    ];
    foreach($unverifiedUsers1150 as [$unverifiedUser1150,$unverifiedPet1150,$keyPart1150]){
        $privateOverview1150=\App\Services\PetPlaygroundService::overview($unverifiedUser1150);
        $privateState1150=\App\Services\PetPlaygroundService::state($unverifiedUser1150,0);
        $privateRoster1150=$privateOverview1150['playgroundPets']??[];
        $privateSelf1150=rosterPet1150($privateRoster1150,$unverifiedPet1150);
        ok1150(
            ($privateOverview1150['playSteamVerified']??true)===false
            &&count($privateRoster1150)===1
            &&$privateSelf1150!==null&&(string)$privateSelf1150['owner_name']==='Nicht öffentlich'
            &&empty($privateOverview1150['playmates'])
            &&($privateState1150['playSteamVerified']??true)===false
            &&count($privateState1150['pets']??[])===1
            &&empty($privateState1150['events'])
            &&str_contains((string)($privateOverview1150['playBlockedReason']??''),'Steam'),
            'Unverifizierte Identität '.$keyPart1150.' erhält nur die anonyme eigene Vorschau mit Gate-Hinweis'
        );
        rejects1150(
            static fn()=>\App\Services\PetPlaygroundService::play($unverifiedUser1150,$petB1150,'greet','v1150-unverified-'.$keyPart1150.'-0001'),
            'POST wird für unverifizierte Identität '.$keyPart1150.' serverseitig blockiert',
            'Steam-Konto'
        );
    }

    foreach([
        [$petNoId1150,'no-id'],
        [$petNoVerified1150,'no-verify'],
        [$petNoPersona1150,'no-persona'],
        [$petBadId1150,'bad-id'],
        [$petSynthetic1150,'synthetic-name'],
    ] as [$ineligibleTarget1150,$keyPart1150]){
        rejects1150(
            static fn()=>\App\Services\PetPlaygroundService::play($userA1150,$ineligibleTarget1150,'sniff','v1150-target-'.$keyPart1150.'-000001'),
            'Direkter POST auf nicht öffentliche Zielidentität '.$keyPart1150.' wird neutral abgewiesen',
            'nicht auf der Spielwiese'
        );
    }

    $leaderboard1150=\App\Services\PetService::overview($userA1150)['leaderboard']??[];
    $leaderboardOwners1150=array_column($leaderboard1150,'owner_name');
    ok1150(
        count($leaderboard1150)===3
        &&in_array('SteamAlpha',$leaderboardOwners1150,true)
        &&in_array('SteamBravo',$leaderboardOwners1150,true)
        &&in_array('Steam 500007',$leaderboardOwners1150,true)
        &&!str_contains((string)json_encode($leaderboard1150,JSON_UNESCAPED_UNICODE),'INTERN-'),
        'Pet-Rangliste schließt unverifizierte Besitzer aus und veröffentlicht nur Steam-Personanamen'
    );
    $arenaLeaderboard1150=\App\Services\PetArenaService::overview($userA1150)['arenaLeaderboard']??[];
    $arenaLeaderboardOwners1150=array_column($arenaLeaderboard1150,'owner_name');
    ok1150(
        count($arenaLeaderboard1150)===3
        &&in_array('SteamAlpha',$arenaLeaderboardOwners1150,true)
        &&in_array('SteamBravo',$arenaLeaderboardOwners1150,true)
        &&in_array('Steam 500007',$arenaLeaderboardOwners1150,true)
        &&!str_contains((string)json_encode($arenaLeaderboard1150,JSON_UNESCAPED_UNICODE),'INTERN-'),
        'Arena-Rangliste schließt unverifizierte Besitzer aus und veröffentlicht nur Steam-Personanamen'
    );

    $playKey1150='v1150-valid-play-000001';
    $played1150=\App\Services\PetPlaygroundService::play($userA1150,$petB1150,'greet',$playKey1150);
    ok1150(empty($played1150['duplicate'])&&(int)$played1150['target_pet_id']===$petB1150,'Verifizierte Pets können einen Spielmoment starten');
    $afterPlay1150=\App\Services\PetPlaygroundService::overview($userA1150);
    $session1150=$afterPlay1150['playSessions'][0]??[];
    ok1150(
        (string)($session1150['actor_owner_name']??'')==='SteamAlpha'
        &&(string)($session1150['target_owner_name']??'')==='SteamBravo'
        &&!str_contains((string)json_encode($afterPlay1150['playSessions']??[],JSON_UNESCAPED_UNICODE),'INTERN-'),
        'Spielhistorie zeigt für beide Seiten ausschließlich Steam-Personanamen'
    );

    $memory1150->prepare("UPDATE users SET steam_personaname='   ' WHERE id=?")->execute([$userB1150]);
    $afterTargetRevocation1150=\App\Services\PetPlaygroundService::overview($userA1150);
    ok1150(
        rosterPet1150($afterTargetRevocation1150['playgroundPets']??[],$petB1150)===null
        &&empty($afterTargetRevocation1150['playSessions']),
        'Widerruf der Zielidentität entfernt Pet und frühere Begegnung sofort aus öffentlichen Antworten'
    );
    rejects1150(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1150,$petB1150,'greet',$playKey1150),
        'Idempotenz-Wiederholung umgeht kein widerrufenes Ziel-Gate',
        'nicht auf der Spielwiese'
    );

    $memory1150->prepare('UPDATE users SET steam_personaname=? WHERE id=?')->execute(['SteamBravo',$userB1150]);
    $memory1150->prepare('UPDATE users SET steam_verified_at=NULL WHERE id=?')->execute([$userA1150]);
    rejects1150(
        static fn()=>\App\Services\PetPlaygroundService::play($userA1150,$petB1150,'greet',$playKey1150),
        'Idempotenz-Wiederholung umgeht kein widerrufenes Absender-Gate',
        'Steam-Konto'
    );
    ok1150(
        (int)scalar1150($memory1150,'SELECT COUNT(*) FROM pet_playground_interactions')===1,
        'Blockierte POSTs erzeugen keine zusätzlichen Spielmomente'
    );
}finally{
    $databasePdo1150->setValue(null,$originalPdo1150);
}

echo "v1.15.0 Pet-Playground-Steam-Gate-Test erfolgreich.\n";
