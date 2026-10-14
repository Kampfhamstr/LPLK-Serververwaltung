<?php
$root1120=dirname(__DIR__);
require_once $root1120.'/src/Services/PetService.php';
require_once $root1120.'/src/Core/Database.php';

function ok1120(bool $condition,string $label):void
{
    if(!$condition){fwrite(STDERR,'FEHLER: '.$label.PHP_EOL);exit(1);}
    echo 'OK  '.$label.PHP_EOL;
}

$layout1120=(string)file_get_contents($root1120.'/templates/layout.php');
$layout1120=str_replace("<?php require __DIR__.'/panel_navigation.php'; ?>",(string)file_get_contents($root1120.'/templates/panel_navigation.php'),$layout1120);
$petsTemplate1120=(string)file_get_contents($root1120.'/templates/pets.php');
$service1120=(string)file_get_contents($root1120.'/src/Services/PetService.php');
$scriptPath1120=$root1120.'/public/assets/pet-companion.js';
$stylePath1120=$root1120.'/public/assets/pet-companion.css';
$script1120=is_file($scriptPath1120)?(string)file_get_contents($scriptPath1120):'';
$style1120=is_file($stylePath1120)?(string)file_get_contents($stylePath1120):'';
$schema1120=(string)file_get_contents($root1120.'/migrations/schema.sql');
$assetVersion1120=trim((string)file_get_contents($root1120.'/VERSION'));

ok1120(version_compare(trim((string)file_get_contents($root1120.'/VERSION')),'1.12.0','>='),'Webpanel-Version ist 1.12.0 oder neuer');
ok1120(is_file($scriptPath1120)&&is_file($stylePath1120),'Pet-Animation liegt in eigenen lokalen JS- und CSS-Assets');
ok1120(str_contains($layout1120,'/assets/pet-companion.css?v='.$assetVersion1120)&&str_contains($layout1120,'/assets/pet-companion.js?v='.$assetVersion1120),'Layout bindet beide Pet-Assets mit aktuellem Cache-Buster ein');
ok1120(!str_contains($script1120,'http://')&&!str_contains($script1120,'https://')&&!str_contains($style1120,'http://')&&!str_contains($style1120,'https://'),'Pet-Animation lädt keine externen Netzwerk-Assets');

ok1120(str_contains($service1120,'public static function sidebarPet(int $userId): ?array'),'PetService bietet eine schreibfreie Sidebar-Abfrage');
ok1120(str_contains($layout1120,'$sidebarPet=$me &&')&&str_contains($layout1120,"Config::get('pets.enabled',true)")&&str_contains($layout1120,"Auth::can('pets.use')")&&str_contains($layout1120,'PetService::sidebarPet'),'Feature-Schalter, Anmeldung und pets.use begrenzen den Sidebar-Begleiter');
$navEnd1120=strpos($layout1120,'</nav>');
$petHome1120=strpos($layout1120,'data-sidebar-pet');
$sidebarBottom1120=strpos($layout1120,'<div class="sidebar-bottom"');
ok1120($navEnd1120!==false&&$petHome1120!==false&&$sidebarBottom1120!==false&&$navEnd1120<$petHome1120&&$petHome1120<$sidebarBottom1120,'Pet sitzt dauerhaft zwischen linker Navigation und Nutzerbereich');
ok1120(str_contains($layout1120,'data-sidebar-pet-character')&&str_contains($layout1120,'data-pet-motion-toggle')&&str_contains($layout1120,'data-pet-status'),'Sidebar enthält Figur, Status und persönlichen Animationsschalter');
ok1120(str_contains($layout1120,'Security::e($sidebarPet[\'name\'])')&&str_contains($layout1120,'Security::e($sidebarPet[\'emoji\'])')&&str_contains($layout1120,'Security::e($sidebarPet[\'tier\'])'),'Dynamische Pet-Daten werden im Layout HTML-escaped');
ok1120(str_contains($petsTemplate1120,'data-pet-main-character')&&str_contains($petsTemplate1120,'Security::e($animal[\'emoji\'])'),'Pet-Seite stellt eine sichere Hauptfigur für Leerlaufaktionen bereit');

ok1120(str_contains($layout1120,'data-pet-escape-overlay')&&str_contains($layout1120,'data-pet-escape-character')&&str_contains($layout1120,'data-pet-butterfly'),'Nicht-interaktives Ausbruchs-Overlay mit Pet und Schmetterlingen ist vorhanden');
ok1120(str_contains($script1120,'const IDLE_DELAY_MS=30000;')&&str_contains($script1120,'IDLE_DELAY_MS-(Date.now()-lastActivityAt)')&&str_contains($script1120,'startEscape();'),'Ausbruch startet erst nach 30 Sekunden Mausleerlauf');
ok1120(str_contains($script1120,"?'cursor':'butterfly'")&&str_contains($script1120,'lastPointer')&&str_contains($script1120,'startButterflies()')&&str_contains($script1120,"butterflies[step%butterflies.length].getBoundingClientRect()"),'Pet kann Mauszeiger oder sichtbare Schmetterlinge jagen');
ok1120(str_contains($script1120,'escapePet.animate([')&&str_contains($script1120,'moveTo(dockPoint(),750,token,false)')&&str_contains($script1120,"home.classList.remove('is-away')"),'Ausbruch wird animiert und endet wieder sicher im Sidebar-Platz');
foreach(['sniff','hop','sleep','shake','piddle'] as $behavior1120){
    ok1120(str_contains($script1120,'pet-behavior-'.$behavior1120)&&str_contains($style1120,'.pet-behavior-'.$behavior1120),'Leerlaufaktion ist in JS und CSS definiert: '.$behavior1120);
}
ok1120(str_contains($style1120,'animation:petSidebarRoam')&&str_contains($style1120,'@keyframes petSidebarRoam'),'Sidebar-Pet bewegt sich fortlaufend in seinem Habitat');

ok1120(str_contains($script1120,"media('(prefers-reduced-motion: reduce)')")&&str_contains($script1120,'reducedMotion.matches')&&str_contains($style1120,'@media(prefers-reduced-motion:reduce)'),'Systemwunsch für reduzierte Bewegung wird vollständig respektiert');
ok1120(str_contains($script1120,"media('(hover: hover) and (pointer: fine)')")&&str_contains($script1120,'finePointer.matches'),'Ausbruch ist auf Geräte mit feinem Zeiger begrenzt');
ok1120(str_contains($script1120,"document.addEventListener('visibilitychange',syncLifecycle)")&&str_contains($script1120,'document.hidden')&&str_contains($script1120,"window.addEventListener('pagehide'"),'Verdeckte oder verlassene Seiten stoppen Timer und Animationen');
ok1120(str_contains($script1120,'window.localStorage')&&str_contains($script1120,'serververwaltung.petMotion.v1.'),'Animationswahl bleibt benutzerspezifisch gespeichert');
ok1120(!str_contains($script1120,'innerHTML')&&!str_contains($script1120,'setInterval'),'Pet-Skript verwendet weder HTML-Injektion noch dauerhafte Intervalle');
ok1120(!str_contains($script1120,'fetch(')&&!str_contains($script1120,'XMLHttpRequest')&&!str_contains($script1120,'WebSocket'),'Kosmetische Animation sendet keine Netzwerk-Anfragen');
ok1120((bool)preg_match('/\.pet-escape-overlay\s*,\s*\.pet-escape-overlay \*\s*\{[^}]*pointer-events\s*:\s*none\s*!important/s',$style1120),'Ausbruchs-Overlay und alle Kinder lassen Klicks vollständig durch');

// Die Methode wird gegen eine vollständig isolierte :memory:-Datenbank geprüft.
// total_changes() belegt zusätzlich, dass jede Sidebar-Abfrage rein lesend bleibt.
$memory1120=new PDO('sqlite::memory:');
$memory1120->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
$memory1120->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE,PDO::FETCH_ASSOC);
$memory1120->exec('PRAGMA foreign_keys=ON');
$memory1120->exec($schema1120);
$now1120=date('c');
$insertUser1120=$memory1120->prepare('INSERT INTO users(id,name,email,password_hash,web_role,active,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?)');
$insertUser1120->execute([1,'Aktiver Besitzer','active-pet@example.invalid','test-only','spieler',1,$now1120,$now1120]);
$insertUser1120->execute([2,'Inaktiver Besitzer','inactive-pet@example.invalid','test-only','spieler',1,$now1120,$now1120]);
$insertUser1120->execute([3,'Fallback Besitzer','fallback-pet@example.invalid','test-only','spieler',1,$now1120,$now1120]);
$insertPet1120=$memory1120->prepare('INSERT INTO pets(user_id,species_key,name,last_state_at,active,created_at,updated_at) VALUES(?,?,?,?,?,?,?)');
$insertPet1120->execute([1,'dog','Pixel',$now1120,1,$now1120,$now1120]);
$insertPet1120->execute([1,'cat','Altes Pet',$now1120,0,$now1120,$now1120]);
$insertPet1120->execute([2,'hamster','Pause',$now1120,0,$now1120,$now1120]);
$insertPet1120->execute([3,'unbekanntes_tier','Mystery',$now1120,1,$now1120,$now1120]);

$databasePdo1120=new ReflectionProperty(\App\Core\Database::class,'pdo');
$databasePdo1120->setAccessible(true);
$originalPdo1120=$databasePdo1120->getValue();
$databasePdo1120->setValue(null,$memory1120);
try{
    $changesBefore1120=(int)$memory1120->query('SELECT total_changes()')->fetchColumn();
    $active1120=\App\Services\PetService::sidebarPet(1);
    $inactive1120=\App\Services\PetService::sidebarPet(2);
    $fallback1120=\App\Services\PetService::sidebarPet(3);
    $missing1120=\App\Services\PetService::sidebarPet(999);
    $changesAfter1120=(int)$memory1120->query('SELECT total_changes()')->fetchColumn();

    ok1120(is_array($active1120)&&$active1120['name']==='Pixel'&&$active1120['species_key']==='dog'&&$active1120['label']==='Hund'&&$active1120['emoji']==='🐕'&&$active1120['tier']==='classic','Aktives bekanntes Pet erhält vollständige Sidebar-Metadaten');
    ok1120($inactive1120===null&&$missing1120===null,'Inaktive oder fehlende Pets erscheinen nicht in der Sidebar');
    ok1120(is_array($fallback1120)&&$fallback1120['species_key']==='unbekanntes_tier'&&$fallback1120['label']==='Pet'&&$fallback1120['emoji']==='🐾'&&$fallback1120['tier']==='classic','Unbekannte historische Tierart erhält einen sicheren Fallback');
    ok1120($changesBefore1120===$changesAfter1120,'sidebarPet führt keinerlei Datenbank-Schreibzugriff aus');
}finally{
    $databasePdo1120->setValue(null,$originalPdo1120);
}

echo PHP_EOL.'v1.12.0 Pet-Animationen erfolgreich geprüft.'.PHP_EOL;
