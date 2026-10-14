<?php
$root1100=dirname(__DIR__);
require_once $root1100.'/src/Services/PetService.php';
require_once $root1100.'/src/Controllers/PetsController.php';
require_once $root1100.'/src/Core/Database.php';
function ok1100(bool $condition,string $label):void{if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}echo "OK  {$label}\n";}
$routes1100=(string)file_get_contents($root1100.'/public/index.php');
$controller1100=(string)file_get_contents($root1100.'/src/Controllers/PetsController.php');
$service1100=(string)file_get_contents($root1100.'/src/Services/PetService.php');
$schema1100=(string)file_get_contents($root1100.'/migrations/schema.sql');
$template1100=(string)file_get_contents($root1100.'/templates/pets.php');
ok1100(version_compare(trim((string)file_get_contents($root1100.'/VERSION')),'1.10.2','>='),'Webpanel-Version ist 1.10.2 oder neuer');
ok1100(str_contains($routes1100,"'/pets'")&&str_contains($routes1100,"'/pets/action'")&&str_contains($routes1100,"'/pets/tasks/{id}/claim'"),'Pets-Routen sind registriert');
ok1100(str_contains($controller1100,"Auth::requirePermission('pets.use')")&&str_contains($controller1100,'Security::verifyCsrf()'),'Login-Recht und CSRF schützen Schreibaktionen');
ok1100(str_contains($service1100,"BEGIN IMMEDIATE")&&str_contains($service1100,'idempotency_key')&&str_contains($schema1100,'UNIQUE(user_id,idempotency_key)'),'Pflegeaktionen sind transaktional und idempotent');
ok1100(str_contains($service1100,"exec('COMMIT')")&&str_contains($service1100,"exec('ROLLBACK')")&&!str_contains($service1100,'->commit()')&&!str_contains($service1100,'->rollBack()'),'SQLite-Transaktionen verwenden konsistent SQL-Steuerung');
ok1100(str_contains($schema1100,'idx_pets_one_active_user')&&str_contains($schema1100,'WHERE active=1'),'Pro Nutzer ist nur ein aktives Pet erlaubt');
ok1100(str_contains($schema1100,'CHECK(coins >= 0)')&&str_contains($schema1100,'pet_wallet_ledger'),'Wallet besitzt Negativschutz und Ledger');
$legacySpecies1100=['cat'=>['Katze','🐈','neugierig'],'dog'=>['Hund','🐕','treu'],'fox'=>['Fuchs','🦊','schlau'],'rabbit'=>['Hase','🐇','flink'],'panda'=>['Panda','🐼','gelassen'],'raccoon'=>['Waschbär','🦝','verspielt'],'owl'=>['Eule','🦉','weise'],'dragon'=>['Kleiner Drache','🐉','mutig']];
foreach($legacySpecies1100 as $key1100=>$expected1100){$actual1100=\App\Services\PetService::SPECIES[$key1100]??[];ok1100([($actual1100['label']??null),($actual1100['emoji']??null),($actual1100['temperament']??null)]===$expected1100,'Ursprüngliche Tierart bleibt kompatibel: '.$key1100);}
ok1100(str_contains($template1100,'aria-label=')&&str_contains($template1100,'progress'),'Statuswerte sind zugänglich ausgezeichnet');
ok1100(str_contains($service1100,"date('Y-m-d')")&&!str_contains($service1100,"gmdate('Y-m-d')"),'Tagesaufgaben folgen der konfigurierten Community-Zeitzone');

// Echter, vollständig isolierter SQLite-Ablauftest. Die produktive Verbindung
// wird nicht geöffnet; PetService erhält nur für diesen Prozess eine :memory:-DB.
$memory1100=new PDO('sqlite::memory:');
$memory1100->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
$memory1100->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE,PDO::FETCH_ASSOC);
$memory1100->exec('PRAGMA foreign_keys=ON');
$memory1100->exec($schema1100);
$now1100=date('c');
$memory1100->prepare("INSERT INTO users(name,email,password_hash,web_role,active,created_at,updated_at) VALUES(?,?,?,'spieler',1,?,?)")
    ->execute(['Pet-Test','pets-test@example.invalid','test-only',$now1100,$now1100]);
$databasePdo1100=new ReflectionProperty(\App\Core\Database::class,'pdo');
$databasePdo1100->setAccessible(true);
$originalPdo1100=$databasePdo1100->getValue();
$databasePdo1100->setValue(null,$memory1100);
try{
    $petId1100=\App\Services\PetService::adopt(1,'cat','Minka');
    ok1100($petId1100===1&&(int)$memory1100->query('SELECT coins FROM pet_wallets WHERE user_id=1')->fetchColumn()===100,'Adoption erzeugt Pet und Start-Wallet atomar');
    $first1100=\App\Services\PetService::performAction(1,'feed','selftest-feed-0001');
    $again1100=\App\Services\PetService::performAction(1,'feed','selftest-feed-0001');
    ok1100(!$first1100['duplicate']&&$again1100['duplicate']&&(int)$memory1100->query('SELECT COUNT(*) FROM pet_actions')->fetchColumn()===1,'Doppelte Pflegeaktion wird exakt einmal gebucht');
    $taskId1100=(int)$memory1100->query("SELECT id FROM pet_tasks WHERE user_id=1 AND action_type='feed'")->fetchColumn();
    \App\Services\PetService::claimTask(1,$taskId1100);
    ok1100((int)$memory1100->query('SELECT coins FROM pet_wallets WHERE user_id=1')->fetchColumn()===120&&(int)$memory1100->query('SELECT COUNT(*) FROM pet_tasks WHERE claimed_at IS NOT NULL')->fetchColumn()===1,'Task-Claim bucht Wallet und Claim atomar');
}finally{$databasePdo1100->setValue(null,$originalPdo1100);}
echo "\nv1.10.2 Pets-Modul erfolgreich geprüft.\n";
