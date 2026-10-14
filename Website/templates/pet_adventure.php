<?php
use App\Core\Security;

$adventureList=is_array($adventures??null)?$adventures:[];
$adventureActive=is_array($activeAdventure??null)?$activeAdventure:[];
$adventureLast=is_array($lastAdventure??null)?$lastAdventure:[];
$adventureTacticCatalog=is_array($adventureTactics??null)&&$adventureTactics?$adventureTactics:[
    'cautious'=>['label'=>'Vorsichtig','description'=>'+8 % auf Prüfungen, etwas kleinere Belohnungen.'],
    'explore'=>['label'=>'Erkundend','description'=>'+15 % Fundchance bei ausgewogenen Prüfungen.'],
    'bold'=>['label'=>'Mutig','description'=>'Schwierigere Prüfungen mit besserer Beutechance.'],
];
$adventureStartsRemaining=max(0,(int)($adventureStartsLeft??3));
$adventureActiveId=max(0,(int)($adventureActive['id']??0));
$adventureReadyAt=(string)($adventureActive['ready_at']??$adventureActive['ends_at']??'');
$adventureReadyEpoch=(int)($adventureActive['ready_at_epoch']??($adventureReadyAt!==''?(strtotime($adventureReadyAt)?:0):0));
$adventureStartedAt=(string)($adventureActive['started_at']??'');
$adventureStartedEpoch=(int)($adventureActive['started_at_epoch']??($adventureStartedAt!==''?(strtotime($adventureStartedAt)?:0):0));
$adventureCanClaim=$adventureActiveId>0&&(!empty($adventureActive['can_claim'])||!empty($adventureActive['ready'])||($adventureReadyEpoch>0&&$adventureReadyEpoch<=time())||($adventureActive['status']??'')==='ready');
$adventureLastClaimed=$adventureLast&&(!empty($adventureLast['claimed'])||!empty($adventureLast['claimed_at'])||(int)($adventureLast['claimed_at_epoch']??0)>0||($adventureLast['status']??'')==='claimed');

include __DIR__.'/pet_nav.php';
include __DIR__.'/pet_game_header.php';
?>
<?php if(!empty($pet)): ?>
<header class="pet-game-page-title">
  <div><span class="pets-eyebrow">Expeditionen</span><h2>Abenteuer</h2><p>Wähle einen Weg, stelle deine Werte auf die Probe und bringe einzigartige Ausrüstung zurück.</p></div>
  <span class="pet-game-rule-note"><?=$adventureStartsRemaining?> von 3 Starts heute übrig · ein Abenteuer gleichzeitig</span>
</header>

<?php if($adventureActive): ?>
<section class="panel pet-adventure-active" aria-labelledby="active-adventure-title" data-ready-at-epoch="<?=$adventureReadyEpoch?>" data-started-at-epoch="<?=$adventureStartedEpoch?>">
  <div class="pet-adventure-active-scene" aria-hidden="true"><span>🗺️</span><span>🐾</span><span>🎒</span></div>
  <div class="pet-adventure-active-copy">
    <span class="status-pill status-warning" data-adventure-state><?=$adventureCanClaim?'Beute bereit':'Unterwegs'?></span>
    <h2 id="active-adventure-title"><?=Security::e((string)($adventureActive['name']??$adventureActive['label']??'Aktives Abenteuer'))?></h2>
    <p><?=Security::e((string)($adventureActive['description']??'Dein Pet erkundet gerade die Umgebung.'))?></p>
    <dl class="pet-adventure-meta">
      <div><dt>Taktik</dt><dd><?=Security::e((string)($adventureActive['tactic_label']??$adventureActive['tactic']??'Erkundend'))?></dd></div>
      <div><dt>Rückkehr</dt><dd><?php if($adventureReadyEpoch>0):?><time datetime="<?=Security::e(date('c',$adventureReadyEpoch))?>"><?=Security::e(date('d.m.Y H:i',$adventureReadyEpoch))?> Uhr</time><?php else:?>Noch unbekannt<?php endif;?></dd></div>
      <div><dt>Verbleibend</dt><dd data-adventure-countdown><?=$adventureCanClaim?'Bereit zum Abholen':'Wird berechnet …'?></dd></div>
    </dl>
    <progress class="pet-adventure-progress" max="100" value="<?=$adventureCanClaim?'100':'0'?>" data-adventure-progress aria-label="Fortschritt des aktiven Abenteuers"></progress>
    <form method="post" action="/pets/adventures/<?=$adventureActiveId?>/claim" class="pet-game-inline-form">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>">
      <button class="btn btn-primary" type="submit" data-enable-when-ready<?=$adventureCanClaim?'':' disabled'?> data-waiting-label="Noch unterwegs">Beute bergen</button>
      <?php if(!$adventureCanClaim): ?><span class="small-muted" data-adventure-wait-note>Der Knopf wird bei der serverseitig festgelegten Rückkehr freigeschaltet.</span><?php endif; ?>
    </form>
  </div>
</section>
<?php else: ?>
<section class="pet-adventure-grid" aria-label="Verfügbare Abenteuer">
  <?php if(!$adventureList): ?>
  <div class="panel pet-game-empty"><div class="panel-header"><h2>Gerade keine Abenteuer verfügbar</h2><p>Schau später wieder vorbei. Dein Pet kann währenddessen trainieren oder sich ausruhen.</p></div></div>
  <?php endif; ?>
  <?php foreach($adventureList as $adventureIndex=>$adventure): if(!is_array($adventure))continue;
      $adventureKey=(string)($adventure['key']??$adventure['adventure_key']??'');
      if($adventureKey==='')continue;
      $adventureName=(string)($adventure['name']??$adventure['label']??'Abenteuer');
      $adventureIcon=(string)($adventure['icon']??'🧭');
      $adventureMinutes=max(1,(int)($adventure['duration_minutes']??$adventure['minutes']??(isset($adventure['duration'])?ceil((int)$adventure['duration']/60):1)));
      $adventureEnergy=max(0,(int)($adventure['energy_cost']??$adventure['energy']??0));
      $adventureLevel=max(1,(int)($adventure['min_level']??$adventure['level']??1));
      $adventureAvailable=(!array_key_exists('available',$adventure)||!empty($adventure['available']))&&$adventureStartsRemaining>0;
      $adventureTacticOptions=is_array($adventure['tactics']??null)&&$adventure['tactics']?$adventure['tactics']:$adventureTacticCatalog;
      $adventureCardId='pet-adventure-'.preg_replace('/[^a-z0-9_-]+/i','-',strtolower($adventureKey)).'-'.$adventureIndex;
  ?>
  <article class="panel pet-adventure-card<?=$adventureAvailable?'':' is-locked'?>" aria-labelledby="<?=Security::e($adventureCardId)?>">
    <div class="pet-adventure-card-head"><span class="pet-adventure-icon" aria-hidden="true"><?=Security::e($adventureIcon)?></span><div><span class="pets-eyebrow"><?=Security::e((string)($adventure['region']??'Expedition'))?></span><h3 id="<?=Security::e($adventureCardId)?>"><?=Security::e($adventureName)?></h3></div></div>
    <p><?=Security::e((string)($adventure['description']??'Eine neue Spur wartet darauf, entdeckt zu werden.'))?></p>
    <dl class="pet-adventure-facts">
      <div><dt>Dauer</dt><dd><?=$adventureMinutes?> Min.</dd></div>
      <div><dt>Energie</dt><dd><?=$adventureEnergy?> Punkte</dd></div>
      <div><dt>Ab Level</dt><dd><?=$adventureLevel?></dd></div>
      <div><dt>Erfolgschance</dt><dd><?=isset($adventure['estimated_chance'])?max(0,min(100,(int)$adventure['estimated_chance'])).' %':Security::e((string)($adventure['check_label']??'Werteabhängig'))?></dd></div>
    </dl>
    <p class="pet-adventure-loot"><strong>Mögliche Beute:</strong> Ausrüstung bis <?=Security::e((string)($adventure['max_rarity_label']??$adventure['max_rarity']??'Selten'))?> sowie Pet-Münzen und XP.</p>
    <?php if(!$adventureAvailable): ?>
      <div class="alert alert-warning pet-game-card-alert"><?=Security::e((string)($adventure['locked_reason']??($adventureStartsRemaining<=0?'Das tägliche Limit von drei Abenteuern ist erreicht.':'Dieses Abenteuer wird ab Level '.$adventureLevel.' freigeschaltet.')))?></div>
    <?php endif; ?>
    <form method="post" action="/pets/adventures/start" class="pet-adventure-start-form">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>">
      <input type="hidden" name="adventure_key" value="<?=Security::e($adventureKey)?>">
      <fieldset class="pet-tactic-picker">
        <legend>Vorgehensweise</legend>
        <?php foreach($adventureTacticOptions as $tacticKey=>$tactic):
            $tacticData=is_array($tactic)?$tactic:['label'=>(string)$tactic,'description'=>''];
            $tacticValue=is_string($tacticKey)?$tacticKey:(string)($tacticData['key']??'explore');
            $tacticId=$adventureCardId.'-tactic-'.preg_replace('/[^a-z0-9_-]+/i','-',strtolower($tacticValue));
        ?>
        <label class="pet-tactic-option" for="<?=Security::e($tacticId)?>"><input id="<?=Security::e($tacticId)?>" type="radio" name="tactic" value="<?=Security::e($tacticValue)?>"<?=$tacticValue==='explore'?' checked':''?> required><span><strong><?=Security::e((string)($tacticData['label']??$tacticValue))?></strong><small><?=Security::e((string)($tacticData['description']??''))?></small></span></label>
        <?php endforeach; ?>
      </fieldset>
      <button class="btn btn-primary" type="submit"<?=$adventureAvailable?'':' disabled'?>><?=Security::e($adventureName)?> starten</button>
    </form>
  </article>
  <?php endforeach; ?>
</section>
<?php endif; ?>

<?php if($adventureLastClaimed):
    $adventureResult=is_array($adventureLast['result']??null)?$adventureLast['result']:[];
    $adventureEvents=$adventureResult['checks']??$adventureLast['events']??$adventureLast['story']??[];
    if(is_string($adventureEvents))$adventureEvents=[$adventureEvents];
    if(!is_array($adventureEvents))$adventureEvents=[];
    $adventureLoot=$adventureResult['item']??$adventureLast['loot']??$adventureLast['items']??[];
    if(isset($adventureLoot['name'])||isset($adventureLoot['item_name'])||isset($adventureLoot['template_key']))$adventureLoot=[$adventureLoot];
    if(!is_array($adventureLoot))$adventureLoot=[];
?>
<section class="panel pet-adventure-result" aria-labelledby="last-adventure-title">
  <div class="panel-header"><span class="status-pill status-online">Geborgen</span><h2 id="last-adventure-title">Letztes Abenteuer: <?=Security::e((string)($adventureLast['name']??$adventureLast['label']??'Expedition'))?></h2><p>Dieses Ergebnis wurde bereits eingelöst und kann nicht erneut beansprucht werden.</p></div>
  <div class="pet-adventure-result-body">
    <?php if($adventureEvents): ?><ol class="pet-adventure-story">
      <?php foreach($adventureEvents as $event): $eventText=is_array($event)?(string)($event['story']??$event['text']??$event['description']??$event['message']??'Ereignis'):(string)$event; ?><li><?php if(is_array($event)&&isset($event['passed'])):?><strong><?=!empty($event['passed'])?'Geschafft':'Nicht geschafft'?>:</strong> <?php endif;?><?=Security::e($eventText)?><?php if(is_array($event)&&isset($event['stat_label'])):?><small> · <?=Security::e((string)$event['stat_label'])?>, Chance <?=max(0,min(100,(int)($event['chance']??0)))?> %</small><?php endif;?></li><?php endforeach; ?>
    </ol><?php endif; ?>
    <div class="pet-reward-summary" aria-label="Belohnungen">
      <span>🪙 <strong>+<?=max(0,(int)($adventureResult['rewards']['coins']??$adventureLast['coins']??$adventureLast['reward_coins']??0))?></strong> Münzen</span>
      <span>⭐ <strong>+<?=max(0,(int)($adventureResult['rewards']['xp']??$adventureLast['xp']??$adventureLast['reward_xp']??0))?></strong> XP</span>
      <?php if((int)($adventureResult['rewards']['training_points']??$adventureLast['training_points']??0)>0):?><span>✦ <strong>+<?=(int)($adventureResult['rewards']['training_points']??$adventureLast['training_points'])?></strong> Trainingspunkte</span><?php endif;?>
    </div>
    <?php if($adventureLoot): ?><div class="pet-loot-list" aria-label="Gefundene Gegenstände">
      <?php foreach($adventureLoot as $loot): if(!is_array($loot))continue; $lootAttributes=$loot['attributes']??$loot['stats']??[]; if(!is_array($lootAttributes))$lootAttributes=[]; foreach(['strength_bonus','agility_bonus','vitality_bonus','instinct_bonus'] as $lootAttributeKey)if(isset($loot[$lootAttributeKey]))$lootAttributes[$lootAttributeKey]=(int)$loot[$lootAttributeKey]; ?>
      <article class="pet-loot-card pet-rarity-<?=Security::e((string)($loot['rarity_key']??$loot['rarity']??'common'))?>">
        <span class="pet-item-icon" aria-hidden="true"><?=Security::e((string)($loot['icon']??'🎁'))?></span>
        <div><span class="pet-rarity-label"><?=Security::e((string)($loot['rarity_label']??$loot['rarity']??'Gewöhnlich'))?></span><h3><?=Security::e((string)($loot['item_name']??$loot['name']??'Gefundener Gegenstand'))?></h3><p><?php $lootParts=[]; $lootAttributeLabels=['strength_bonus'=>'Stärke','agility_bonus'=>'Geschick','vitality_bonus'=>'Vitalität','instinct_bonus'=>'Instinkt']; foreach($lootAttributes as $attribute=>$value)$lootParts[]=Security::e($lootAttributeLabels[(string)$attribute]??(string)$attribute).' '.((int)$value>=0?'+':'').(int)$value; echo implode(' · ',$lootParts); ?></p></div>
      </article>
      <?php endforeach; ?>
    </div><?php endif; ?>
  </div>
</section>
<?php endif; ?>
<?php endif; ?>
