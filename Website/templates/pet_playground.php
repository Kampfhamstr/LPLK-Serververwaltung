<?php
use App\Core\Security;
use App\Services\PetService;

$playgroundOwnPet=is_array($pet??null)?$pet:[];
$playgroundOwnPetId=max(0,(int)($playgroundOwnPet['id']??0));
$playgroundCatalog=PetService::SPECIES;
$playgroundRawPets=is_array($playgroundPets??null)?array_values($playgroundPets):[];
$playgroundHasExplicitPlaymates=isset($playmates)&&is_array($playmates);
$playgroundRawPlaymates=$playgroundHasExplicitPlaymates?array_values($playmates):[];
$playgroundStarts=max(0,(int)($playStartsLeft??0));
$playgroundSteamVerified=!empty($playSteamVerified);
$playgroundBlockedReason=trim((string)($playBlockedReason??''));
$playgroundCooldownRaw=$playCooldownUntil??null;
$playgroundCooldownEpoch=is_numeric($playgroundCooldownRaw)
    ?max(0,(int)$playgroundCooldownRaw)
    :($playgroundCooldownRaw?(strtotime((string)$playgroundCooldownRaw)?:0):0);
$playgroundCoolingDown=$playgroundCooldownEpoch>time();
$playgroundFeatured=is_array($featuredPlaySession??null)?$featuredPlaySession:[];
$playgroundSessions=is_array($playSessions??null)?array_values($playSessions):[];
$playgroundCooldownOnlyBlock=$playgroundCoolingDown&&str_starts_with($playgroundBlockedReason,'Kurze Pfotenpause:');
$playgroundEmptyRosterBlock=str_contains($playgroundBlockedReason,'kein anderes Community-Pet');
$playgroundHardBlocked=$playgroundBlockedReason!==''&&!$playgroundCooldownOnlyBlock&&!$playgroundEmptyRosterBlock;

$playgroundNormalizePet=static function(array $entry,bool $self=false)use($playgroundCatalog,$playgroundOwnPetId):?array{
    $petId=max(0,(int)($entry['pet_id']??$entry['id']??0));
    if($petId<=0)return null;
    $speciesKey=(string)($entry['species_key']??'');
    $species=$playgroundCatalog[$speciesKey]??[];
    $name=trim((string)($entry['name']??$entry['pet_name']??'Pet'))?:'Pet';
    $owner=trim((string)($entry['owner_name']??$entry['owner']??($self?'Du':'Community')));
    if($owner==='')$owner=$self?'Du':'Community';
    $tier=(string)($entry['tier']??$species['tier']??'classic');
    if(!in_array($tier,['classic','cool','epic'],true))$tier='classic';
    return [
        'pet_id'=>$petId,
        'name'=>$name,
        'level'=>max(1,min(20,(int)($entry['level']??1))),
        'owner_name'=>$owner,
        'emoji'=>(string)($entry['emoji']??$species['emoji']??'🐾'),
        'species_label'=>(string)($entry['species_label']??$species['label']??'Pet'),
        'tier'=>$tier,
        'is_self'=>$self||!empty($entry['is_self'])||$petId===$playgroundOwnPetId,
        'owner_online'=>!empty($entry['owner_online']),
        'mood_label'=>trim((string)($entry['mood_label']??'Spielbereit'))?:'Spielbereit',
        'can_interact'=>!array_key_exists('can_interact',$entry)||!empty($entry['can_interact']),
        'target_resting'=>!empty($entry['target_resting']),
        'locked_reason'=>trim((string)($entry['locked_reason']??'')),
        'pair_cooldown_until'=>max(0,(int)($entry['pair_cooldown_until']??0)),
    ];
};

$playgroundPetsNormalized=[];
$playgroundSeenPetIds=[];
foreach($playgroundRawPets as $playgroundRawPet){
    if(!is_array($playgroundRawPet))continue;
    $playgroundPet=$playgroundNormalizePet($playgroundRawPet);
    if(!$playgroundPet||isset($playgroundSeenPetIds[$playgroundPet['pet_id']]))continue;
    $playgroundSeenPetIds[$playgroundPet['pet_id']]=true;
    $playgroundPetsNormalized[]=$playgroundPet;
}
if($playgroundOwnPetId>0&&!isset($playgroundSeenPetIds[$playgroundOwnPetId])){
    $playgroundSpeciesKey=(string)($playgroundOwnPet['species_key']??'');
    $playgroundSpecies=$playgroundCatalog[$playgroundSpeciesKey]??[];
    $playgroundSelfFallback=$playgroundNormalizePet([
        'pet_id'=>$playgroundOwnPetId,
        'name'=>(string)($playgroundOwnPet['name']??'Dein Pet'),
        'level'=>(int)($playgroundOwnPet['level']??1),
        'owner_name'=>'Du',
        'emoji'=>(string)($playgroundSpecies['emoji']??'🐾'),
        'species_label'=>(string)($playgroundSpecies['label']??'Pet'),
        'tier'=>(string)($playgroundSpecies['tier']??'classic'),
        'mood_label'=>'Bereit zum Spielen',
    ],true);
    if($playgroundSelfFallback)array_unshift($playgroundPetsNormalized,$playgroundSelfFallback);
}
$playgroundPetsNormalized=array_slice($playgroundPetsNormalized,0,12);
$playgroundPetsById=[];
foreach($playgroundPetsNormalized as $playgroundPet)$playgroundPetsById[$playgroundPet['pet_id']]=$playgroundPet;

$playgroundPlaymatesNormalized=[];
if($playgroundHasExplicitPlaymates){
    $playgroundSeenPlaymateIds=[];
    foreach($playgroundRawPlaymates as $playgroundRawPlaymate){
        if(!is_array($playgroundRawPlaymate))continue;
        $playgroundPlaymate=$playgroundNormalizePet($playgroundRawPlaymate);
        if(!$playgroundPlaymate||$playgroundPlaymate['is_self']||isset($playgroundSeenPlaymateIds[$playgroundPlaymate['pet_id']]))continue;
        $playgroundSeenPlaymateIds[$playgroundPlaymate['pet_id']]=true;
        $playgroundPlaymatesNormalized[]=$playgroundPlaymate;
    }
}else{
    foreach($playgroundPetsNormalized as $playgroundPet)if(!$playgroundPet['is_self'])$playgroundPlaymatesNormalized[]=$playgroundPet;
}
$playgroundAvailablePlaymates=array_values(array_filter($playgroundPlaymatesNormalized,static fn(array $entry):bool=>!empty($entry['can_interact'])));

$playgroundInteractionDefaults=[
    'greet'=>['key'=>'greet','label'=>'Begrüßen','emoji'=>'💞','description'=>'Die beiden Pets laufen aufeinander zu und begrüßen sich freundlich.'],
    'sniff'=>['key'=>'sniff','label'=>'Zusammen schnüffeln','emoji'=>'👃','description'=>'Eine neugierige Schnüffelrunde über die Spielwiese.'],
    'chase'=>['key'=>'chase','label'=>'Fangen spielen','emoji'=>'💨','description'=>'Ein kurzes, wildes Fangspiel mit vertauschten Rollen.'],
    'ball'=>['key'=>'ball','label'=>'Ball spielen','emoji'=>'⚽','description'=>'Die Pets spielen sich gemeinsam einen Ball zu.'],
];
$playgroundInteractions=$playgroundInteractionDefaults;
foreach((is_array($playInteractions??null)?$playInteractions:[]) as $playgroundInteractionIndex=>$playgroundInteraction){
    if(!is_array($playgroundInteraction))continue;
    $playgroundInteractionKey=(string)($playgroundInteraction['key']??(is_string($playgroundInteractionIndex)?$playgroundInteractionIndex:''));
    if(!isset($playgroundInteractionDefaults[$playgroundInteractionKey]))continue;
    $playgroundInteractions[$playgroundInteractionKey]=[
        'key'=>$playgroundInteractionKey,
        'label'=>(string)($playgroundInteraction['label']??$playgroundInteractionDefaults[$playgroundInteractionKey]['label']),
        'emoji'=>(string)($playgroundInteraction['emoji']??$playgroundInteractionDefaults[$playgroundInteractionKey]['emoji']),
        'description'=>(string)($playgroundInteraction['description']??$playgroundInteractionDefaults[$playgroundInteractionKey]['description']),
    ];
}

$playgroundLastEventId=max(0,(int)($playStateCursor??0),(int)($playgroundFeatured['id']??0));
foreach($playgroundSessions as $playgroundSession){
    if(is_array($playgroundSession))$playgroundLastEventId=max($playgroundLastEventId,(int)($playgroundSession['id']??0));
}
$playgroundFeaturedType=(string)($playgroundFeatured['interaction_type']??$playgroundFeatured['interaction_key']??'');
$playgroundFeaturedActorId=max(0,(int)($playgroundFeatured['actor_pet_id']??$playgroundFeatured['pet_id']??0));
$playgroundFeaturedTargetId=max(0,(int)($playgroundFeatured['target_pet_id']??0));
$playgroundOtherCount=count(array_filter($playgroundPetsNormalized,static fn(array $entry):bool=>!$entry['is_self']));
$playgroundCanPlay=$playgroundSteamVerified&&$playgroundOwnPetId>0&&count($playgroundAvailablePlaymates)>0&&$playgroundStarts>0&&!$playgroundCoolingDown&&$playgroundBlockedReason==='';
$playgroundCooldownIso=$playgroundCooldownEpoch>0?date('c',$playgroundCooldownEpoch):'';
$playgroundFormStatus=$playgroundBlockedReason;
if($playgroundFormStatus===''&&!$playgroundPlaymatesNormalized)$playgroundFormStatus='Für eine gemeinsame Runde muss mindestens ein anderes Pet auf der Spielwiese sein.';
elseif($playgroundFormStatus===''&&!$playgroundAvailablePlaymates)$playgroundFormStatus='Alle aktuellen Spielpaare machen gerade ihre kurze Pfotenpause.';
elseif($playgroundFormStatus===''&&$playgroundStarts<=0)$playgroundFormStatus='Für heute sind alle gemeinsamen Spielrunden verbraucht.';

include __DIR__.'/pet_nav.php';
include __DIR__.'/pet_game_header.php';
?>

<section class="pet-game-page-title" aria-labelledby="pet-playground-title">
  <div><span class="pets-eyebrow">Gemeinsam statt allein</span><h2 id="pet-playground-title">Die Community-Spielwiese</h2><p>Hier begegnet dein Begleiter echten Pets anderer Webpanel-Nutzer. Namen, Level und Besitzer bleiben bei jeder Szene sichtbar.</p></div>
  <span class="pet-game-rule-note">Sozial &amp; fair · keine Kampfwerte</span>
</section>

<?php if(!$playgroundSteamVerified):?>
<div class="alert alert-warning pet-playground-steam-gate" role="status" data-playground-steam-gate>
  <strong>Steam-Verifizierung für die Community-Spielwiese erforderlich.</strong>
  Andere Pets, Besitzer und gemeinsame Spielrunden werden erst sichtbar, wenn dein Steam-Konto vollständig verknüpft und verifiziert ist.
  <a class="btn btn-secondary btn-small" href="/profile#steam-verification">Steam jetzt verknüpfen</a>
</div>
<?php endif;?>

<section
  class="panel pet-playground"
  data-pet-playground
  data-playground-state-url="/pets/playground/state"
  data-playground-self-id="<?=$playgroundOwnPetId?>"
  data-playground-last-event-id="<?=$playgroundLastEventId?>"
  data-playground-featured-actor-id="<?=$playgroundFeaturedActorId?>"
  data-playground-featured-target-id="<?=$playgroundFeaturedTargetId?>"
  data-playground-featured-type="<?=Security::e($playgroundFeaturedType)?>"
  data-playground-starts-left="<?=$playgroundStarts?>"
  data-playground-hard-blocked="<?=$playgroundHardBlocked?'1':'0'?>"
  data-playground-blocked-reason="<?=Security::e($playgroundBlockedReason)?>"
  data-playground-global-cooldown-epoch="<?=$playgroundCooldownEpoch?>"
  data-playground-server-now-epoch="<?=max(0,(int)($serverNowEpoch??time()))?>"
  aria-labelledby="pet-playground-stage-title"
>
  <div class="pet-playground-toolbar">
    <div><span class="pets-eyebrow">Live-Spielrunde</span><h2 id="pet-playground-stage-title">Auf der Wiese ist etwas los</h2><p id="pet-playground-help">Die Pet-Liste wird bei sichtbarer Seite ungefähr alle 15 Sekunden aktualisiert. Automatische Bewegungen sind rein kosmetisch.</p></div>
    <dl class="pet-playground-metrics" aria-label="Spielwiesenstatus">
      <div><dt>Pets hier</dt><dd data-playground-pet-count><?=count($playgroundPetsNormalized)?></dd></div>
      <div><dt>Spielkameraden</dt><dd data-playground-mate-count><?=$playgroundOtherCount?></dd></div>
      <div><dt>Spielrunden frei</dt><dd data-playground-starts-left-output aria-live="polite"><?=$playgroundStarts?></dd></div>
    </dl>
  </div>

  <div class="pet-playground-stage" data-playground-stage aria-describedby="pet-playground-help">
    <div class="pet-playground-scenery" aria-hidden="true"><span class="pet-playground-tree">🌳</span><span class="pet-playground-flowers">🌼　🌷</span><span class="pet-playground-pond">〰</span></div>
    <p class="pet-playground-caption" data-playground-caption aria-hidden="true"><?=count($playgroundPetsNormalized)>1?'Die Pets suchen sich schon einen Spielkameraden …':'Noch wartet dein Pet auf Gesellschaft.'?></p>
    <p class="pet-playground-sr-only" data-playground-live aria-live="polite" aria-atomic="true"></p>
    <span class="pet-playground-ball" data-playground-ball hidden aria-hidden="true">⚽</span>
    <span class="pet-playground-chase-cloud" data-playground-chase-cloud hidden aria-hidden="true">💨</span>
    <ul class="pet-playground-actors" data-playground-roster aria-label="Pets auf der Spielwiese">
      <?php foreach($playgroundPetsNormalized as $playgroundPet): ?>
      <li
        class="pet-playground-actor pet-playground-tier-<?=Security::e($playgroundPet['tier'])?><?=$playgroundPet['is_self']?' is-self':''?>"
        data-playground-actor
        data-pet-id="<?=$playgroundPet['pet_id']?>"
        data-pet-name="<?=Security::e($playgroundPet['name'])?>"
        data-is-self="<?=$playgroundPet['is_self']?'1':'0'?>"
        aria-label="<?=Security::e($playgroundPet['name'].', Level '.$playgroundPet['level'].', '.$playgroundPet['species_label'].', Besitzer '.$playgroundPet['owner_name'].($playgroundPet['is_self']?', dein Pet':''))?>"
      >
        <span class="pet-playground-motion" data-playground-motion aria-hidden="true"><span class="pet-playground-figure" data-playground-figure><span class="pet-playground-emoji" data-playground-emoji><?=Security::e($playgroundPet['emoji'])?></span><span class="pet-playground-effect" data-playground-effect></span></span></span>
        <span class="pet-playground-nameplate">
          <span class="pet-playground-name-row"><strong data-playground-name><?=Security::e($playgroundPet['name'])?></strong><?php if($playgroundPet['is_self']):?><em>Deins</em><?php endif;?></span>
          <span><b data-playground-level>Level <?=$playgroundPet['level']?></b> · <span data-playground-species><?=Security::e($playgroundPet['species_label'])?></span></span>
          <small>Besitzer: <span data-playground-owner><?=Security::e($playgroundPet['owner_name'])?></span><?php if($playgroundPet['owner_online']):?> · <span class="pet-playground-online">online</span><?php endif;?></small>
          <small class="pet-playground-mood" data-playground-mood><?=Security::e($playgroundPet['mood_label'])?></small>
        </span>
      </li>
      <?php endforeach; ?>
    </ul>
    <div class="pet-playground-empty" data-playground-empty<?=count($playgroundPetsNormalized)?' hidden':''?>><span aria-hidden="true">🐾</span><strong>Die Spielwiese ist gerade leer</strong><small>Beim nächsten Besuch können schon wieder Community-Pets da sein.</small></div>
  </div>
</section>

<div class="pet-playground-lower-grid">
  <section class="panel pet-playground-play-panel" aria-labelledby="pet-playground-play-title">
    <div class="panel-header"><h2 id="pet-playground-play-title">Eine gemeinsame Spielrunde starten</h2><p>Wähle einen echten Spielkameraden und eine Aktion. Das Ereignis erscheint kurz darauf auch bei anderen Besuchern der Spielwiese.</p></div>
    <form method="post" action="/pets/playground/play" class="pet-playground-play-form" data-playground-play-form aria-disabled="<?=$playgroundSteamVerified?'false':'true'?>">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>">
      <label for="pet-playground-target">Spielkamerad
        <select id="pet-playground-target" name="target_pet_id" data-playground-target required<?=$playgroundSteamVerified&&$playgroundAvailablePlaymates?'':' disabled'?>>
          <?php if(!$playgroundPlaymatesNormalized):?><option value="">Gerade ist kein anderes Pet verfügbar</option><?php elseif(!$playgroundAvailablePlaymates):?><option value="">Alle Spielpaare machen gerade Pfotenpause</option><?php endif;?>
          <?php foreach($playgroundPlaymatesNormalized as $playgroundPlaymate):?><option value="<?=$playgroundPlaymate['pet_id']?>" data-pair-cooldown-epoch="<?=$playgroundPlaymate['pair_cooldown_until']?>" data-server-allowed="<?=$playgroundPlaymate['can_interact']?'1':'0'?>" data-target-resting="<?=$playgroundPlaymate['target_resting']?'1':'0'?>" data-locked-reason="<?=Security::e($playgroundPlaymate['locked_reason'])?>"<?=$playgroundPlaymate['locked_reason']!==''?' title="'.Security::e($playgroundPlaymate['locked_reason']).'"':''?><?=!$playgroundPlaymate['can_interact']?' disabled':''?>><?=Security::e($playgroundPlaymate['emoji'].' '.$playgroundPlaymate['name'].' · Level '.$playgroundPlaymate['level'].' · '.$playgroundPlaymate['owner_name'])?></option><?php endforeach;?>
        </select>
      </label>
      <fieldset class="pet-playground-interactions"<?=$playgroundSteamVerified?'':' disabled'?>><legend>Was sollen die Pets machen?</legend>
        <?php foreach($playgroundInteractions as $playgroundInteractionKey=>$playgroundInteraction): $playgroundInteractionId='playground-interaction-'.$playgroundInteractionKey; ?>
        <label class="pet-playground-interaction" for="<?=Security::e($playgroundInteractionId)?>">
          <input id="<?=Security::e($playgroundInteractionId)?>" type="radio" name="interaction_type" value="<?=Security::e($playgroundInteractionKey)?>"<?=$playgroundInteractionKey==='greet'?' checked':''?> required>
          <span aria-hidden="true"><?=Security::e($playgroundInteraction['emoji'])?></span><strong><?=Security::e($playgroundInteraction['label'])?></strong><small><?=Security::e($playgroundInteraction['description'])?></small>
        </label>
        <?php endforeach; ?>
      </fieldset>
      <?php if($playgroundCooldownEpoch>0):?><p class="pet-playground-cooldown" data-playground-cooldown data-playground-cooldown-epoch="<?=$playgroundCooldownEpoch?>">Nächste gemeinsame Runde: <time datetime="<?=Security::e($playgroundCooldownIso)?>" data-playground-cooldown-text><?=$playgroundCoolingDown?Security::e(date('H:i:s',$playgroundCooldownEpoch)):'jetzt möglich'?></time></p><?php endif;?>
      <p class="pet-form-blocker" data-playground-blocker data-playground-transient-blocker<?=$playgroundFormStatus===''?' hidden':''?>><?=Security::e($playgroundFormStatus)?></p>
      <button class="btn btn-primary" type="submit" data-playground-submit<?=$playgroundCanPlay?'':' disabled'?>>Gemeinsam spielen · <?=$playgroundStarts?> frei</button>
    </form>
  </section>

  <section class="panel pet-playground-history" aria-labelledby="pet-playground-history-title">
    <div class="panel-header"><h2 id="pet-playground-history-title">Letzte Begegnungen</h2><p>Kurze soziale Ereignisse ohne XP-, Energie- oder Attributänderungen.</p></div>
    <?php if(!$playgroundSessions):?>
    <div class="pet-playground-history-empty"><span aria-hidden="true">🫶</span><p>Noch keine gemeinsame Spielrunde. Vielleicht beginnt hier gleich die erste Freundschaft.</p></div>
    <?php else:?>
    <ol class="pet-playground-session-list">
      <?php foreach(array_slice($playgroundSessions,0,8) as $playgroundSession): if(!is_array($playgroundSession))continue;
        $playgroundSessionType=(string)($playgroundSession['interaction_type']??$playgroundSession['interaction_key']??'greet');
        $playgroundSessionInteraction=$playgroundInteractions[$playgroundSessionType]??$playgroundInteractions['greet'];
        $playgroundSessionActorId=max(0,(int)($playgroundSession['actor_pet_id']??$playgroundSession['pet_id']??0));
        $playgroundSessionTargetId=max(0,(int)($playgroundSession['target_pet_id']??0));
        $playgroundSessionActor=(string)($playgroundSession['actor_name']??$playgroundSession['pet_name']??($playgroundPetsById[$playgroundSessionActorId]['name']??'Ein Pet'));
        $playgroundSessionTarget=(string)($playgroundSession['target_name']??($playgroundPetsById[$playgroundSessionTargetId]['name']??'ein Spielkamerad'));
        $playgroundSessionCreated=(string)($playgroundSession['created_at']??'');
        $playgroundSessionTimestamp=max(0,(int)($playgroundSession['created_at_epoch']??0));
        if($playgroundSessionTimestamp<=0&&$playgroundSessionCreated!=='')$playgroundSessionTimestamp=strtotime($playgroundSessionCreated)?:0;
        $playgroundSessionTimeLabel=trim((string)($playgroundSession['created_at_label']??''));
      ?>
      <li><span class="pet-playground-session-icon" aria-hidden="true"><?=Security::e($playgroundSessionInteraction['emoji'])?></span><div><strong><?=Security::e($playgroundSessionActor)?> &amp; <?=Security::e($playgroundSessionTarget)?></strong><span><?=Security::e($playgroundSessionInteraction['label'])?></span></div><?php if($playgroundSessionTimestamp>0):?><time datetime="<?=Security::e(date('c',$playgroundSessionTimestamp))?>"><?=Security::e($playgroundSessionTimeLabel!==''?$playgroundSessionTimeLabel:date('d.m. · H:i',$playgroundSessionTimestamp))?></time><?php elseif($playgroundSessionTimeLabel!==''):?><span class="pet-playground-session-time"><?=Security::e($playgroundSessionTimeLabel)?></span><?php endif;?></li>
      <?php endforeach;?>
    </ol>
    <?php endif;?>
  </section>
</div>

<?php if($playgroundFeatured):
  $playgroundFeaturedInteraction=$playgroundInteractions[$playgroundFeaturedType]??$playgroundInteractions['greet'];
  $playgroundFeaturedActor=(string)($playgroundFeatured['actor_name']??$playgroundFeatured['pet_name']??($playgroundPetsById[$playgroundFeaturedActorId]['name']??'Dein Pet'));
  $playgroundFeaturedTarget=(string)($playgroundFeatured['target_name']??($playgroundPetsById[$playgroundFeaturedTargetId]['name']??'sein Spielkamerad'));
  $playgroundFeaturedSummary=trim((string)($playgroundFeatured['summary']??''));
?>
<section class="panel pet-playground-featured" aria-labelledby="pet-playground-featured-title">
  <span class="pet-playground-featured-icon" aria-hidden="true"><?=Security::e($playgroundFeaturedInteraction['emoji'])?></span><div><span class="pets-eyebrow">Gerade passiert</span><h2 id="pet-playground-featured-title"><?=Security::e($playgroundFeaturedActor)?> &amp; <?=Security::e($playgroundFeaturedTarget)?></h2><p><?=Security::e($playgroundFeaturedSummary!==''?$playgroundFeaturedSummary:($playgroundFeaturedInteraction['label'].' – eine neue Begegnung auf der Community-Spielwiese.'))?></p></div>
</section>
<?php endif;?>
