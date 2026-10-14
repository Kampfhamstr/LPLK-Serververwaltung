<?php
$root1150Animations=dirname(__DIR__);

function ok1150Animations(bool $condition,string $label):void
{
    if(!$condition){fwrite(STDERR,'FEHLER: '.$label.PHP_EOL);exit(1);}
    echo 'OK  '.$label.PHP_EOL;
}

$version1150Animations=trim((string)file_get_contents($root1150Animations.'/VERSION'));
$template1150Animations=(string)file_get_contents($root1150Animations.'/templates/pets.php');
$service1150Animations=(string)file_get_contents($root1150Animations.'/src/Services/PetService.php');
$gameScript1150Animations=(string)file_get_contents($root1150Animations.'/public/assets/pet-game.js');
$gameStyle1150Animations=(string)file_get_contents($root1150Animations.'/public/assets/pet-game.css');
$companionScript1150Animations=(string)file_get_contents($root1150Animations.'/public/assets/pet-companion.js');
$companionStyle1150Animations=(string)file_get_contents($root1150Animations.'/public/assets/pet-companion.css');
$playgroundScript1150Animations=(string)file_get_contents($root1150Animations.'/public/assets/pet-playground.js');
$playgroundStyle1150Animations=(string)file_get_contents($root1150Animations.'/public/assets/pet-playground.css');

ok1150Animations(version_compare($version1150Animations,'1.15.0','>='),'Webpanel-Version ist 1.15.0 oder neuer');
ok1150Animations(
    str_contains($template1150Animations,'data-pet-action-stage')
    &&str_contains($template1150Animations,'data-pet-action-form')
    &&str_contains($template1150Animations,'data-pet-action-prop')
    &&str_contains($template1150Animations,'data-pet-action-particles')
    &&str_contains($template1150Animations,'data-pet-action-feedback')
    &&str_contains($template1150Animations,'aria-live="polite"'),
    'Pet-Übersicht stellt Bühne, Aktionsformulare, Requisiten, Partikel und zugängliches Feedback bereit'
);

$actions1150Animations=[
    'feed'=>['keyframe'=>'petCareFeed','prop'=>'🥣','success'=>'Lecker!'],
    'pet'=>['keyframe'=>'petCareGroom','prop'=>'🪮','success'=>'genießt die Aufmerksamkeit'],
    'play'=>['keyframe'=>'petCarePlay','prop'=>'🎾','success'=>'Was für ein Spaß'],
    'sleep'=>['keyframe'=>'petCareSleep','prop'=>'🛏️','success'=>'gut ausgeruht'],
    'heal'=>['keyframe'=>'petCareHeal','prop'=>'🩹','success'=>'Grundpflege'],
    'train'=>['keyframe'=>'petCareTrain','prop'=>'🏋️','success'=>'Training geschafft'],
];
foreach($actions1150Animations as $action1150Animations=>$expectation1150Animations){
    ok1150Animations(
        str_contains($service1150Animations,"'{$action1150Animations}'=>[")
        &&str_contains($gameScript1150Animations,"{$action1150Animations}:{")
        &&str_contains($gameScript1150Animations,"prop:'{$expectation1150Animations['prop']}'")
        &&str_contains($gameScript1150Animations,"success:'{$expectation1150Animations['success']}'")
        &&str_contains($gameStyle1150Animations,"pet-action-{$action1150Animations}")
        &&str_contains($gameStyle1150Animations,'@keyframes '.$expectation1150Animations['keyframe']),
        'Eigene bestätigte Aktionsszene ist vollständig definiert: '.$action1150Animations
    );
}

ok1150Animations(
    str_contains($gameScript1150Animations,"form.addEventListener('submit'")
    &&str_contains($gameScript1150Animations,'window.sessionStorage.setItem')
    &&str_contains($gameScript1150Animations,"document.querySelectorAll('.alert-success')")
    &&str_contains($gameScript1150Animations,'.includes(config.success)')
    &&str_contains($gameScript1150Animations,'stage.classList.add(`pet-action-${action}`,\'is-pet-action-active\')')
    &&!str_contains($gameScript1150Animations,'preventDefault'),
    'Nativer POST bleibt unverzögert und die Szene startet nach Redirect nur bei passender Erfolgsmeldung'
);
ok1150Animations(
    !str_contains($gameScript1150Animations,'innerHTML')
    &&!str_contains($gameScript1150Animations,'fetch(')
    &&!str_contains($gameScript1150Animations,'XMLHttpRequest')
    &&!str_contains($gameScript1150Animations,'WebSocket'),
    'Pflegeanimation verwendet sichere DOM-Ausgaben und erzeugt keinen zusätzlichen Netzwerkverkehr'
);
ok1150Animations(
    str_contains($gameStyle1150Animations,'html.pet-motion-off .is-pet-action-active')
    &&str_contains($gameStyle1150Animations,'html.pet-motion-paused .is-pet-action-active')
    &&str_contains($gameStyle1150Animations,'@media(prefers-reduced-motion:reduce)')
    &&str_contains($gameStyle1150Animations,'.pet-action-particles i{opacity:0}')
    &&str_contains($gameScript1150Animations,"const actionPaused=()=>document.hidden||document.documentElement.classList.contains('pet-motion-paused')")
    &&str_contains($gameScript1150Animations,'finishRemainingMs=Math.max(0,finishRemainingMs-(clockNow()-finishStartedAt))')
    &&str_contains($gameScript1150Animations,"document.addEventListener('visibilitychange',syncFinishClock)")
    &&str_contains($gameScript1150Animations,'pauseObserver.observe(document.documentElement'),
    'Animationsschalter, Fokuspausen und Reduced Motion besitzen einen ruhigen Zustand; der Szenentimer pausiert synchron'
);

foreach(['sleep','groom','heal','train'] as $idle1150Animations){
    ok1150Animations(
        str_contains($companionScript1150Animations,"name:'{$idle1150Animations}'")
        &&str_contains($companionScript1150Animations,"className:'pet-behavior-{$idle1150Animations}'")
        &&str_contains($companionStyle1150Animations,'.pet-behavior-'.$idle1150Animations),
        'Sidebar-Leerlaufszene ist in JS und CSS vorhanden: '.$idle1150Animations
    );
}
ok1150Animations(
    str_contains($companionScript1150Animations,'const behaviorClasses=[...new Set(')
    &&str_contains($companionScript1150Animations,"overlay.classList.remove('is-cursor-chase','is-butterfly-chase',...behaviorClasses)")
    &&str_contains($companionScript1150Animations,"const careActionActive=()=>document.body.classList.contains('pet-care-action-active')")
    &&str_contains($companionScript1150Animations,'const companionBlocked=()=>interfaceBlocked()||careActionActive()')
    &&!str_contains($companionScript1150Animations,"document.body.classList.contains('pet-arena-replay-active')||\n      document.body.classList.contains('pet-care-action-active')")
    &&str_contains($companionScript1150Animations,"root.classList.toggle('pet-motion-paused',!windowFocused||document.hidden||interfaceBlocked())"),
    'Begleiter pausiert während Pflegeaktionen, ohne die bestätigte Pflegeanimation selbst einzufrieren'
);

foreach([
    'nap'=>'is-napping',
    'groom'=>'is-grooming',
    'heal'=>'is-healing',
    'train'=>'is-training',
    'snack'=>'is-snacking',
] as $scene1150Animations=>$class1150Animations){
    ok1150Animations(
        str_contains($playgroundScript1150Animations,"{$scene1150Animations}:{className:'{$class1150Animations}'")
        &&str_contains($playgroundStyle1150Animations,'.pet-playground-figure.'.$class1150Animations),
        'Kosmetische Spielwiesen-Leerlaufszene ist umgesetzt: '.$scene1150Animations
    );
}
ok1150Animations(
    str_contains($playgroundScript1150Animations,"const idleTypes=['nap','groom','heal','train','snack'];")
    &&str_contains($playgroundStyle1150Animations,'@media(prefers-reduced-motion:reduce)'),
    'Spielwiese wechselt durch alle neuen Miniszenen und respektiert Reduced Motion'
);

echo PHP_EOL.'v1.15.0 bestätigte Pflegeanimationen und neue Leerlaufszenen erfolgreich geprüft.'.PHP_EOL;
