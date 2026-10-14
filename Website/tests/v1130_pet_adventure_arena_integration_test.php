<?php
$root1130=dirname(__DIR__);

require_once $root1130.'/src/Core/Database.php';
require_once $root1130.'/src/Services/PetStatsService.php';
require_once $root1130.'/src/Services/PetGameService.php';
require_once $root1130.'/src/Services/PetService.php';
require_once $root1130.'/src/Services/PetAdventureService.php';
require_once $root1130.'/src/Services/PetSkillService.php';
require_once $root1130.'/src/Services/PetArenaService.php';

function ok1130(bool $condition,string $label):void
{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}

function rejects1130(callable $operation,string $label,string $messagePart=''):void
{
    $error=null;
    try{$operation();}catch(\Throwable $caught){$error=$caught;}
    ok1130($error instanceof \Throwable&&($messagePart===''||str_contains($error->getMessage(),$messagePart)),$label);
}

function scalar1130(PDO $pdo,string $sql,array $parameters=[]):mixed
{
    $statement=$pdo->prepare($sql);$statement->execute($parameters);return $statement->fetchColumn();
}

function row1130(PDO $pdo,string $sql,array $parameters=[]):array
{
    $statement=$pdo->prepare($sql);$statement->execute($parameters);return $statement->fetch()?:[];
}

function economy1130(PDO $pdo,int $userId,int $petId):array
{
    $wallet=row1130($pdo,'SELECT coins,training_points FROM pet_wallets WHERE user_id=?',[$userId]);
    return [
        'coins'=>(int)($wallet['coins']??0),
        'training_points'=>(int)($wallet['training_points']??0),
        'xp'=>(int)scalar1130($pdo,'SELECT xp FROM pets WHERE id=? AND user_id=?',[$petId,$userId]),
        'items'=>(int)scalar1130($pdo,'SELECT COUNT(*) FROM pet_items WHERE pet_id=? AND user_id=?',[$petId,$userId]),
        'ledger'=>(int)scalar1130($pdo,'SELECT COUNT(*) FROM pet_wallet_ledger WHERE user_id=?',[$userId]),
    ];
}

$routes1130=(string)file_get_contents($root1130.'/public/index.php');
$controller1130=(string)file_get_contents($root1130.'/src/Controllers/PetsController.php');
$schema1130=(string)file_get_contents($root1130.'/migrations/schema.sql');
$version1130=trim((string)file_get_contents($root1130.'/VERSION'));

ok1130(version_compare($version1130,'1.13.0','>='),'Webpanel-Version ist 1.13.0 oder neuer');
foreach([
    "get('/pets/adventure'","get('/pets/equipment'","get('/pets/skills'","get('/pets/arena'",
    "post('/pets/adventures/start'","post('/pets/adventures/{id}/claim'",
    "post('/pets/items/{id}/equip'","post('/pets/items/{id}/unequip'","post('/pets/items/{id}/salvage'",
    "post('/pets/skills/unlock'","post('/pets/skills/reset'","post('/pets/arena/fight'",
] as $route1130)ok1130(str_contains($routes1130,$route1130),'Route registriert: '.$route1130);
ok1130(str_contains($controller1130,"Auth::requirePermission('pets.use')")&&substr_count($controller1130,'Security::verifyCsrf();')>=9,'Pet-Spielrouten verlangen pets.use und schützen alle Schreibaktionen mit CSRF');
foreach(['pet_adventures','pet_items','pet_skill_ranks','pet_skill_spends','pet_arena_profiles','pet_arena_battles'] as $table1130){
    ok1130(str_contains($schema1130,'CREATE TABLE IF NOT EXISTS '.$table1130),'Schema enthält '.$table1130);
}
ok1130(str_contains($schema1130,'idx_pet_adventures_one_running')&&str_contains($schema1130,"WHERE status='running'"),'Datenbank erlaubt nur ein laufendes Abenteuer pro Pet');
ok1130(str_contains($schema1130,'idx_pet_items_one_equipped_slot')&&str_contains($schema1130,'WHERE equipped=1'),'Datenbank erlaubt nur einen ausgerüsteten Gegenstand je Slot');

// Vollständig isolierter Integrationstest. Das Schema wird absichtlich zweimal
// ausgeführt, damit eine wiederholte Migration ebenso geprüft ist wie der Ablauf.
$memory1130=new PDO('sqlite::memory:');
$memory1130->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
$memory1130->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE,PDO::FETCH_ASSOC);
$memory1130->setAttribute(PDO::ATTR_EMULATE_PREPARES,false);
$memory1130->exec('PRAGMA foreign_keys=ON');
$memory1130->exec($schema1130);
$memory1130->exec($schema1130);
ok1130((int)scalar1130($memory1130,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE 'pet_%'")>=12,'Pet-Schema ist bei zweifacher Ausführung idempotent');

$now1130=date('c');
$insertUser1130=$memory1130->prepare("INSERT INTO users(name,email,password_hash,web_role,active,created_at,updated_at) VALUES(?,?,?,'spieler',1,?,?)");
$insertUser1130->execute(['Pet-Abenteuer-Test A','pet-v1130-a@example.invalid','test-only',$now1130,$now1130]);
$userA1130=(int)$memory1130->lastInsertId();
$insertUser1130->execute(['Pet-Abenteuer-Test B','pet-v1130-b@example.invalid','test-only',$now1130,$now1130]);
$userB1130=(int)$memory1130->lastInsertId();

$databasePdo1130=new ReflectionProperty(\App\Core\Database::class,'pdo');
$databasePdo1130->setAccessible(true);
$originalPdo1130=$databasePdo1130->getValue();
$databasePdo1130->setValue(null,$memory1130);

try{
    // Adoption legt das neue Startitem an. Katze und Drache erhalten exakt
    // dasselbe Startprofil; die Tierart bleibt damit rein kosmetisch.
    $petA1130=\App\Services\PetService::adopt($userA1130,'cat','Minka');
    $petB1130=\App\Services\PetService::adopt($userB1130,'dragon','Fünkchen');
    ok1130((int)scalar1130($memory1130,"SELECT COUNT(*) FROM pet_items WHERE source_type='starter' AND slot='charm' AND rarity='common' AND equipped=1")===2,'Jede Adoption erzeugt genau ein ausgerüstetes Starteritem');
    $profileA1130=row1130($memory1130,'SELECT level,xp,health,hunger,energy,happiness,bond FROM pets WHERE id=?',[$petA1130]);
    $profileB1130=row1130($memory1130,'SELECT level,xp,health,hunger,energy,happiness,bond FROM pets WHERE id=?',[$petB1130]);
    $rawPetA1130=\App\Services\PetGameService::activePet($memory1130,$userA1130,false);
    $rawPetB1130=\App\Services\PetGameService::activePet($memory1130,$userB1130,false);
    $statsA1130=\App\Services\PetStatsService::stats($memory1130,$rawPetA1130);
    $statsB1130=\App\Services\PetStatsService::stats($memory1130,$rawPetB1130);
    $fairStatsA1130=array_intersect_key($statsA1130,array_flip(['strength','agility','vitality','instinct','combat_hp','power']));
    $fairStatsB1130=array_intersect_key($statsB1130,array_flip(['strength','agility','vitality','instinct','combat_hp','power']));
    ok1130($profileA1130===$profileB1130&&$fairStatsA1130===$fairStatsB1130,'Alle Tierarten starten mit identischen Zustands- und Spielwerten');

    // Eine bereits vorhandene Wallet darf den Startbonus nicht vom Ledger
    // entkoppeln. Das deckt auch den gewünschten Hamster als Tierart ab.
    $insertUser1130->execute(['Pet-Abenteuer-Test C','pet-v1130-c@example.invalid','test-only',$now1130,$now1130]);
    $userC1130=(int)$memory1130->lastInsertId();
    $memory1130->prepare('INSERT INTO pet_wallets(user_id,coins,training_points,updated_at) VALUES(?,?,?,?)')->execute([$userC1130,7,2,$now1130]);
    $petC1130=\App\Services\PetService::adopt($userC1130,'hamster','Mochi');
    ok1130((int)scalar1130($memory1130,'SELECT coins FROM pet_wallets WHERE user_id=?',[$userC1130])===107&&(int)scalar1130($memory1130,"SELECT COUNT(*) FROM pet_wallet_ledger WHERE user_id=? AND currency='coins' AND amount=100 AND reason='adoption'",[$userC1130])===1,'Adoption addiert den Startbonus auch bei bestehender Wallet exakt zum Ledger');
    ok1130((string)scalar1130($memory1130,'SELECT species_key FROM pets WHERE id=?',[$petC1130])==='hamster','Hamster kann als vollwertig gleichstarkes Pet adoptiert werden');

    // Abenteuer: Startrequest ist idempotent, ein zweites Abenteuer ist
    // gesperrt und die Serverzeit verhindert vorzeitiges Beanspruchen.
    $adventureStart1130=\App\Services\PetAdventureService::start($userA1130,'sunny_meadow','explore','v1130-adventure-start-0001');
    $energyAfterStart1130=(int)scalar1130($memory1130,'SELECT energy FROM pets WHERE id=?',[$petA1130]);
    $adventureDuplicate1130=\App\Services\PetAdventureService::start($userA1130,'sunny_meadow','explore','v1130-adventure-start-0001');
    ok1130(!$adventureStart1130['duplicate']&&$adventureDuplicate1130['duplicate']&&$adventureDuplicate1130['adventure_id']===$adventureStart1130['adventure_id'],'Doppelter Abenteuerstart liefert dasselbe Abenteuer');
    ok1130((int)scalar1130($memory1130,'SELECT COUNT(*) FROM pet_adventures WHERE user_id=?',[$userA1130])===1&&(int)scalar1130($memory1130,'SELECT energy FROM pets WHERE id=?',[$petA1130])===$energyAfterStart1130,'Doppelter Abenteuerstart verbraucht weder Energie noch einen weiteren Datensatz');
    rejects1130(static fn()=>\App\Services\PetAdventureService::start($userA1130,'sunny_meadow','cautious','v1130-adventure-start-0001'),'Ein Abenteuer-Schlüssel ist fest an seine Nutzdaten gebunden','anderes Abenteuer');
    rejects1130(static fn()=>\App\Services\PetAdventureService::start($userA1130,'sunny_meadow','cautious','v1130-adventure-start-0002'),'Ein zweites laufendes Abenteuer wird abgewiesen','noch auf Abenteuer');
    rejects1130(static fn()=>\App\Services\PetAdventureService::claim($userA1130,(int)$adventureStart1130['adventure_id'],'v1130-adventure-early-0001'),'Ein Abenteuer kann nicht vor der Serverzeit abgeholt werden','noch unterwegs');
    rejects1130(static fn()=>\App\Services\PetAdventureService::claim($userB1130,(int)$adventureStart1130['adventure_id'],'v1130-adventure-foreign-01'),'Ein fremdes Abenteuer kann nicht beansprucht werden','gehört nicht zu dir');

    $startedEpoch1130=(int)scalar1130($memory1130,'SELECT started_at_epoch FROM pet_adventures WHERE id=?',[(int)$adventureStart1130['adventure_id']]);
    $memory1130->prepare('UPDATE pet_adventures SET ready_at_epoch=? WHERE id=?')->execute([$startedEpoch1130,(int)$adventureStart1130['adventure_id']]);
    $claimKey1130='v1130-adventure-claim-0001';
    $claimed1130=\App\Services\PetAdventureService::claim($userA1130,(int)$adventureStart1130['adventure_id'],$claimKey1130);
    $economyAfterClaim1130=economy1130($memory1130,$userA1130,$petA1130);
    $claimedDuplicate1130=\App\Services\PetAdventureService::claim($userA1130,(int)$adventureStart1130['adventure_id'],$claimKey1130);
    ok1130(!$claimed1130['duplicate']&&$claimedDuplicate1130['duplicate']&&economy1130($memory1130,$userA1130,$petA1130)===$economyAfterClaim1130,'Doppelter Claim schreibt Belohnungen exakt einmal gut');
    rejects1130(static fn()=>\App\Services\PetAdventureService::claim($userA1130,(int)$adventureStart1130['adventure_id'],'v1130-adventure-claim-other'),'Ein bereits beanspruchtes Abenteuer lehnt einen anderen Claim-Schlüssel ab','bereits abgeholt');
    ok1130((int)scalar1130($memory1130,"SELECT COUNT(*) FROM pet_items WHERE source_type='adventure' AND source_id=?",[(int)$adventureStart1130['adventure_id']])<=1,'Ein Abenteuer erzeugt unabhängig vom Zufall höchstens einen Loot-Gegenstand');

    // Ein beschädigter Server-Snapshot darf niemals als Null-Belohnung endgültig
    // abgeholt werden. Die gesamte Claim-Transaktion muss unverändert bleiben.
    $corruptStart1130=\App\Services\PetAdventureService::start($userB1130,'sunny_meadow','explore','v1130-adventure-corrupt-01');
    $memory1130->prepare("UPDATE pet_adventures SET ready_at_epoch=started_at_epoch,result_json='{}' WHERE id=?")->execute([(int)$corruptStart1130['adventure_id']]);
    $corruptBefore1130=economy1130($memory1130,$userB1130,$petB1130);
    rejects1130(static fn()=>\App\Services\PetAdventureService::claim($userB1130,(int)$corruptStart1130['adventure_id'],'v1130-corrupt-claim-0001'),'Beschädigte Abenteuer-Snapshots werden vor dem Statuswechsel abgewiesen','Snapshot');
    ok1130((string)scalar1130($memory1130,'SELECT status FROM pet_adventures WHERE id=?',[(int)$corruptStart1130['adventure_id']])==='running'&&economy1130($memory1130,$userB1130,$petB1130)===$corruptBefore1130,'Ein ungültiger Snapshot verändert Status, XP, Wallet und Inventar nicht');

    // Ein zweiter Gegenstand im Charm-Slot prüft atomaren Slotwechsel,
    // Idempotenz und die Besitzgrenze ohne vom zufälligen Abenteuerloot
    // abhängig zu sein.
    $insertItem1130=$memory1130->prepare('INSERT INTO pet_items(user_id,pet_id,template_key,item_name,slot,rarity,item_level,level_required,strength_bonus,agility_bonus,vitality_bonus,instinct_bonus,equipped,source_type,source_id,source_index,created_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)');
    $insertItem1130->execute([$userA1130,$petA1130,'integration_charm','Prüf-Talisman','charm','rare',1,1,3,0,0,2,0,'arena',900001,0,date('c')]);
    $item1130=(int)$memory1130->lastInsertId();
    rejects1130(static fn()=>\App\Services\PetGameService::equipItem($userB1130,$item1130,'v1130-item-foreign-0001'),'Fremde Ausrüstung kann nicht angelegt werden','gehört nicht');
    $equipped1130=\App\Services\PetGameService::equipItem($userA1130,$item1130,'v1130-item-equip-000001');
    $equippedDuplicate1130=\App\Services\PetGameService::equipItem($userA1130,$item1130,'v1130-item-equip-000001');
    ok1130(!$equipped1130['duplicate']&&$equippedDuplicate1130['duplicate'],'Ausrüstungsrequest ist idempotent');
    ok1130((int)scalar1130($memory1130,"SELECT COUNT(*) FROM pet_items WHERE pet_id=? AND slot='charm' AND equipped=1",[$petA1130])===1&&(int)scalar1130($memory1130,'SELECT equipped FROM pet_items WHERE id=?',[$item1130])===1,'Slotwechsel legt das alte Item ab und genau das neue Item an');
    rejects1130(static fn()=>\App\Services\PetService::performAction($userA1130,'feed','v1130-item-equip-000001'),'Der gemeinsame Pet-Aktionsschlüssel kann nicht zwischen Aktionsarten wechseln','andere Pet-Aktion');
    \App\Services\PetGameService::unequipItem($userA1130,$item1130,'v1130-item-unequip-0001');
    $coinsBeforeSalvage1130=(int)scalar1130($memory1130,'SELECT coins FROM pet_wallets WHERE user_id=?',[$userA1130]);
    $salvaged1130=\App\Services\PetGameService::salvageItem($userA1130,$item1130,'v1130-item-salvage-0001');
    $salvagedDuplicate1130=\App\Services\PetGameService::salvageItem($userA1130,$item1130,'v1130-item-salvage-0001');
    ok1130(!$salvaged1130['duplicate']&&$salvagedDuplicate1130['duplicate']&&(int)scalar1130($memory1130,'SELECT COUNT(*) FROM pet_items WHERE id=?',[$item1130])===0,'Ein abgelegtes Item wird idempotent verwertet und entfernt');
    ok1130((int)scalar1130($memory1130,'SELECT coins FROM pet_wallets WHERE user_id=?',[$userA1130])===$coinsBeforeSalvage1130+(int)$salvaged1130['coins'],'Verwerten schreibt den Münzerlös exakt einmal gut');

    // Skillkosten steigen pro Rang. Voraussetzungen, Kosten, Rückerstattung
    // und der idempotente Reset werden anhand realer Walletbuchungen geprüft.
    $memory1130->prepare('UPDATE pets SET level=8,updated_at=? WHERE id=?')->execute([date('c'),$petA1130]);
    $memory1130->prepare('UPDATE pet_wallets SET coins=500,training_points=50,updated_at=? WHERE user_id=?')->execute([date('c'),$userA1130]);
    rejects1130(static fn()=>\App\Services\PetSkillService::unlock($userA1130,'eagle_eye','v1130-skill-prerequisite'),'Skill-Voraussetzungen werden serverseitig erzwungen','Benötigt');
    $clawsOne1130=\App\Services\PetSkillService::unlock($userA1130,'sharp_claws','v1130-skill-claws-rank01');
    $clawsOneDuplicate1130=\App\Services\PetSkillService::unlock($userA1130,'sharp_claws','v1130-skill-claws-rank01');
    rejects1130(static fn()=>\App\Services\PetSkillService::unlock($userA1130,'light_paws','v1130-skill-claws-rank01'),'Ein Skill-Schlüssel ist fest an seinen Knoten gebunden','andere Pet-Aktion');
    $clawsTwo1130=\App\Services\PetSkillService::unlock($userA1130,'sharp_claws','v1130-skill-claws-rank02');
    $eyeOne1130=\App\Services\PetSkillService::unlock($userA1130,'eagle_eye','v1130-skill-eye-rank0001');
    ok1130(!$clawsOne1130['duplicate']&&$clawsOneDuplicate1130['duplicate']&&[$clawsOne1130['cost'],$clawsTwo1130['cost'],$eyeOne1130['cost']]=== [1,2,1],'Skillkosten steigen je Knotenrang und Doppelrequests buchen nicht erneut');
    ok1130((int)scalar1130($memory1130,'SELECT training_points FROM pet_wallets WHERE user_id=?',[$userA1130])===46&&(int)scalar1130($memory1130,"SELECT COUNT(*) FROM pet_skill_spends WHERE user_id=? AND event_type='unlock'",[$userA1130])===3,'Skillkäufe ziehen zusammen genau vier Trainingspunkte ab');
    $coinsBeforeReset1130=(int)scalar1130($memory1130,'SELECT coins FROM pet_wallets WHERE user_id=?',[$userA1130]);
    $reset1130=\App\Services\PetSkillService::reset($userA1130,'v1130-skill-reset-000001');
    $resetState1130=economy1130($memory1130,$userA1130,$petA1130);
    $resetDuplicate1130=\App\Services\PetSkillService::reset($userA1130,'v1130-skill-reset-000001');
    ok1130(!$reset1130['duplicate']&&$resetDuplicate1130['duplicate']&&$reset1130['refunded']===4,'Skillreset erstattet exakt die tatsächlich bezahlten Punkte und ist idempotent');
    ok1130((int)scalar1130($memory1130,'SELECT COUNT(*) FROM pet_skill_ranks WHERE pet_id=?',[$petA1130])===0&&$resetState1130['training_points']===50&&$resetState1130['coins']===$coinsBeforeReset1130-\App\Services\PetSkillService::RESET_COST&&economy1130($memory1130,$userA1130,$petA1130)===$resetState1130,'Skillreset leert Ränge und bucht Münzen sowie Rückerstattung nur einmal');

    // Arenaausgang ist absichtlich nicht festgelegt. Geprüft werden allein
    // die unveränderlichen Sicherheits- und Simulationsverträge.
    $fight1130=\App\Services\PetArenaService::fight($userA1130,'meadow_bully','aggressive','v1130-arena-fight-000001');
    $battle1130=row1130($memory1130,'SELECT * FROM pet_arena_battles WHERE id=? AND user_id=?',[(int)$fight1130['battle_id'],$userA1130]);
    $snapshot1130=json_decode((string)$battle1130['stats_snapshot_json'],true,512,JSON_THROW_ON_ERROR);
    $battleLog1130=json_decode((string)$battle1130['battle_log_json'],true,512,JSON_THROW_ON_ERROR);
    ok1130(in_array($battle1130['result'],['win','loss','draw'],true)&&count($battleLog1130)===(int)$battle1130['rounds']&&(int)$battle1130['rounds']>=1&&(int)$battle1130['rounds']<=12,'Arena simuliert unabhängig vom Zufall höchstens zwölf protokollierte Runden');
    ok1130(($snapshot1130['rules_version']??null)==='1.13.0'&&isset($snapshot1130['seed'],$snapshot1130['player'],$snapshot1130['opponent'],$snapshot1130['pet_stats'])&&($snapshot1130['tactic']??null)==='aggressive','Arena speichert einen vollständigen, versionierten Server-Snapshot');
    $arenaState1130=economy1130($memory1130,$userA1130,$petA1130);
    $fightDuplicate1130=\App\Services\PetArenaService::fight($userA1130,'meadow_bully','aggressive','v1130-arena-fight-000001');
    ok1130(!$fight1130['duplicate']&&$fightDuplicate1130['duplicate']&&$fightDuplicate1130['battle_id']===$fight1130['battle_id'],'Doppelter Arenarequest liefert denselben Kampf');
    ok1130((int)scalar1130($memory1130,'SELECT COUNT(*) FROM pet_arena_battles WHERE user_id=?',[$userA1130])===1&&economy1130($memory1130,$userA1130,$petA1130)===$arenaState1130,'Doppelter Arenarequest verändert XP, Wallet und Datenbank nicht erneut');
    rejects1130(static fn()=>\App\Services\PetArenaService::fight($userA1130,'meadow_bully','defensive','v1130-arena-fight-000001'),'Ein Arena-Schlüssel ist fest an Gegner und Taktik gebunden','anderen Arenakampf');
}finally{
    $databasePdo1130->setValue(null,$originalPdo1130);
}

echo "\nv1.13.0 Pet-Abenteuer, Ausrüstung, Skillbaum und Arena erfolgreich geprüft.\n";
