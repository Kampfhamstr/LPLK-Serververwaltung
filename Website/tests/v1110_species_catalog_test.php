<?php
$root1110=dirname(__DIR__);
require_once $root1110.'/src/Services/PetService.php';
require_once $root1110.'/src/Core/Database.php';
require_once $root1110.'/src/Core/Security.php';

function ok1110(bool $condition,string $label):void
{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}

$species1110=\App\Services\PetService::SPECIES;
$tiers1110=\App\Services\PetService::SPECIES_TIERS;
$template1110=(string)file_get_contents($root1110.'/templates/pets.php');
$css1110=(string)file_get_contents($root1110.'/public/assets/app.css');
$schema1110=(string)file_get_contents($root1110.'/migrations/schema.sql');

ok1110(version_compare(trim((string)file_get_contents($root1110.'/VERSION')),'1.11.0','>='),'Webpanel-Version ist 1.11.0 oder neuer');
ok1110(count($species1110)===24,'Der Katalog enthält genau 24 Tierarten');
ok1110(count(array_unique(array_column($species1110,'label')))===24,'Alle Tierarten besitzen einen eindeutigen Namen');
ok1110(isset($species1110['hamster'])&&$species1110['hamster']['label']==='Hamster'&&$species1110['hamster']['emoji']==='🐹'&&$species1110['hamster']['tier']==='classic','Hamster ist als klassischer Begleiter auswählbar');
ok1110(array_keys($tiers1110)===['classic','cool','epic'],'Kategorien Klassisch, Cool und Episch sind definiert');
$tierCounts1110=array_count_values(array_column($species1110,'tier'));
ok1110(($tierCounts1110['classic']??0)===8&&($tierCounts1110['cool']??0)===8&&($tierCounts1110['epic']??0)===8,'Jede Kategorie enthält acht Tiere');
foreach($species1110 as $key1110=>$animal1110){
    ok1110((bool)preg_match('/^[a-z][a-z0-9_]*$/',$key1110),'Gültiger Tier-Schlüssel: '.$key1110);
    ok1110(array_keys($animal1110)===['label','emoji','temperament','tier'],'Rein kosmetische Metadaten: '.$key1110);
}
ok1110(str_contains($template1110,'24 Begleiter')&&str_contains($template1110,'data-species-tier')&&str_contains($template1110,'spielerisch gleich stark'),'Adoption erklärt Auswahl und faire Stärke');
ok1110(str_contains($css1110,'.pet-species-card-cool')&&str_contains($css1110,'.pet-species-card-epic'),'Coole und epische Karten sind sichtbar gestaltet');

$_SESSION['_csrf']='catalog-selftest-token';
$pet=null;$species=$species1110;$speciesTiers=$tiers1110;$leaderboard=[];
ob_start();require $root1110.'/templates/pets.php';$rendered1110=(string)ob_get_clean();
ok1110(substr_count($rendered1110,'name="species"')===24,'Adoptionsansicht rendert 24 Auswahlfelder');
foreach(['classic','cool','epic'] as $tier1110)ok1110(substr_count($rendered1110,'data-species-tier="'.$tier1110.'"')===8,'Adoptionsansicht rendert acht Karten: '.$tier1110);
foreach(array_keys($species1110) as $key1110)ok1110(str_contains($rendered1110,'value="'.$key1110.'"'),'Adoptionsansicht enthält: '.$key1110);
foreach($tiers1110 as $tier1110)ok1110(str_contains($rendered1110,$tier1110['label'])&&str_contains($rendered1110,$tier1110['description']),'Kategorie wird erklärt: '.$tier1110['label']);

// Jede Art wird gegen dasselbe Startprofil in einer isolierten SQLite-Datenbank adoptiert.
$memory1110=new PDO('sqlite::memory:');
$memory1110->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
$memory1110->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE,PDO::FETCH_ASSOC);
$memory1110->exec('PRAGMA foreign_keys=ON');
$memory1110->exec($schema1110);
$databasePdo1110=new ReflectionProperty(\App\Core\Database::class,'pdo');
$databasePdo1110->setAccessible(true);
$originalPdo1110=$databasePdo1110->getValue();
$databasePdo1110->setValue(null,$memory1110);
try{
    $expectedStart1110=null;$index1110=0;
    foreach($species1110 as $key1110=>$animal1110){
        $index1110++;$now1110=date('c');
        $memory1110->prepare("INSERT INTO users(name,email,password_hash,web_role,active,created_at,updated_at) VALUES(?,?,?,'spieler',1,?,?)")
            ->execute(['Katalogtest '.$index1110,'pet'.$index1110.'@example.invalid','test-only',$now1110,$now1110]);
        \App\Services\PetService::adopt($index1110,$key1110,'Pet '.$index1110);
        $stmt1110=$memory1110->prepare('SELECT species_key,level,xp,health,hunger,energy,happiness,bond FROM pets WHERE user_id=?');
        $stmt1110->execute([$index1110]);$stored1110=$stmt1110->fetch();$storedKey1110=array_shift($stored1110);
        if($expectedStart1110===null)$expectedStart1110=$stored1110;
        ok1110($storedKey1110===$key1110&&$stored1110===$expectedStart1110,'Korrekte Art und identische Startwerte: '.$animal1110['label']);
    }
    ok1110((int)$memory1110->query('SELECT COUNT(*) FROM pets')->fetchColumn()===24,'Alle 24 Arten lassen sich adoptieren');
    ok1110((int)$memory1110->query('SELECT COUNT(*) FROM pet_wallets WHERE coins=100 AND training_points=0')->fetchColumn()===24,'Alle Arten erhalten dasselbe Start-Wallet');
    ok1110((int)$memory1110->query("SELECT COUNT(*) FROM pet_wallet_ledger WHERE currency='coins' AND amount=100 AND reason='adoption'")->fetchColumn()===24,'Alle Adoptionen besitzen einen Wallet-Beleg');
    ok1110((int)$memory1110->query("SELECT COUNT(*) FROM pet_inventory WHERE item_key='starter_collar' AND quantity=1")->fetchColumn()===24,'Alle Arten erhalten dasselbe Starter-Inventar');
    ok1110((int)$memory1110->query('SELECT COUNT(*) FROM pet_tasks')->fetchColumn()===72,'Alle Arten erhalten drei Tagesaufgaben');
    $unknownRejected1110=false;try{\App\Services\PetService::adopt(1,'not_a_pet','Unbekannt');}catch(RuntimeException){$unknownRejected1110=true;}
    ok1110($unknownRejected1110,'Unbekannte Tierarten werden weiterhin abgewiesen');
}finally{$databasePdo1110->setValue(null,$originalPdo1110);}

echo "\nv1.11.0 Tierkatalog erfolgreich geprüft.\n";
