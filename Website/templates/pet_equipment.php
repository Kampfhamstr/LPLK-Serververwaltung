<?php
use App\Core\Security;

$equipmentItems=is_array($items??null)?array_values($items):[];
$equipmentProvidedSlots=is_array($equipmentSlots??null)?$equipmentSlots:[];
$equipmentStats=is_array($stats??null)?$stats:[];
$equipmentBreakdown=is_array($equipmentStats['breakdown']??null)?$equipmentStats['breakdown']:[];
$equipmentSlotDefinitions=[
    'head'=>['label'=>'Kopf','icon'=>'🎩','description'=>'Helme, Hüte und kluge Kopfbedeckungen'],
    'body'=>['label'=>'Körper','icon'=>'🛡️','description'=>'Rüstung, Mäntel und Westen'],
    'paws'=>['label'=>'Pfoten','icon'=>'🥾','description'=>'Schuhe, Klauen und flinke Pfotenschützer'],
    'charm'=>['label'=>'Talisman','icon'=>'📿','description'=>'Halsbänder, Amulette und Glücksbringer'],
];
$equipmentBySlot=[];
foreach($equipmentProvidedSlots as $equipmentSlotKey=>$equipmentSlotValue){
    if(is_string($equipmentSlotKey)&&array_key_exists($equipmentSlotKey,$equipmentSlotDefinitions)){
        if(is_array($equipmentSlotValue)){
            $equipmentSlotDefinitions[$equipmentSlotKey]['label']=(string)($equipmentSlotValue['label']??$equipmentSlotDefinitions[$equipmentSlotKey]['label']);
            $equipmentSlotDefinitions[$equipmentSlotKey]['icon']=(string)($equipmentSlotValue['emoji']??$equipmentSlotValue['icon']??$equipmentSlotDefinitions[$equipmentSlotKey]['icon']);
            if(isset($equipmentSlotValue['item'])&&is_array($equipmentSlotValue['item']))$equipmentBySlot[$equipmentSlotKey]=$equipmentSlotValue['item'];
            elseif(isset($equipmentSlotValue['id'])||isset($equipmentSlotValue['item_name']))$equipmentBySlot[$equipmentSlotKey]=$equipmentSlotValue;
        }
        continue;
    }
    if(!is_array($equipmentSlotValue))continue;
    $equipmentSlot=(string)($equipmentSlotValue['slot']??'');
    if(array_key_exists($equipmentSlot,$equipmentSlotDefinitions))$equipmentBySlot[$equipmentSlot]=$equipmentSlotValue['item']??$equipmentSlotValue;
}
foreach($equipmentItems as $equipmentInventoryItem){
    if(!is_array($equipmentInventoryItem)||empty($equipmentInventoryItem['equipped']))continue;
    $equipmentInventorySlot=(string)($equipmentInventoryItem['slot']??'');
    if(array_key_exists($equipmentInventorySlot,$equipmentSlotDefinitions))$equipmentBySlot[$equipmentInventorySlot]=$equipmentInventoryItem;
}
$equipmentStatLabels=['strength'=>'Kraft','agility'=>'Tempo','vitality'=>'Zähigkeit','instinct'=>'Instinkt'];
$equipmentAttributeLabels=['strength_bonus'=>'Kraft','agility_bonus'=>'Tempo','vitality_bonus'=>'Zähigkeit','instinct_bonus'=>'Instinkt','strength'=>'Kraft','agility'=>'Tempo','vitality'=>'Zähigkeit','instinct'=>'Instinkt'];
$equipmentRarityLabels=['common'=>'Gewöhnlich','uncommon'=>'Ungewöhnlich','rare'=>'Selten','epic'=>'Episch','legendary'=>'Legendär'];

include __DIR__.'/pet_nav.php';
include __DIR__.'/pet_game_header.php';
?>
<?php if(!empty($pet)): ?>
<header class="pet-game-page-title">
  <div><span class="pets-eyebrow">Fundstücke</span><h2>Inventar &amp; Ausrüstung</h2><p>Stelle deinen Build aus vier Slots zusammen. Jeder Bonus fließt serverseitig in Abenteuer und Arena ein.</p></div>
  <span class="pet-game-rule-note"><?=count($equipmentItems)?> Gegenstände</span>
</header>

<div class="pet-equipment-overview">
  <section class="panel pet-equipment-slots" aria-labelledby="equipment-slots-title">
    <div class="panel-header"><h2 id="equipment-slots-title">Ausgerüstet</h2><p>Pro Slot kann genau ein Gegenstand aktiv sein.</p></div>
    <div class="pet-slot-grid">
      <?php foreach($equipmentSlotDefinitions as $equipmentSlotKey=>$equipmentSlotDefinition): $equipmentItem=$equipmentBySlot[$equipmentSlotKey]??null; if(!is_array($equipmentItem))$equipmentItem=[]; $equipmentItemId=max(0,(int)($equipmentItem['id']??0)); ?>
      <article class="pet-slot-card<?=$equipmentItem?' is-filled':''?>">
        <div class="pet-slot-heading"><span aria-hidden="true"><?=Security::e($equipmentSlotDefinition['icon'])?></span><div><small><?=Security::e($equipmentSlotDefinition['label'])?></small><strong><?=Security::e((string)($equipmentItem['item_name']??$equipmentItem['name']??'Leer'))?></strong></div></div>
        <?php if($equipmentItem): ?>
          <span class="pet-rarity-label pet-rarity-<?=Security::e((string)($equipmentItem['rarity']??'common'))?>"><?=Security::e($equipmentRarityLabels[(string)($equipmentItem['rarity']??'common')]??(string)($equipmentItem['rarity']??'Gewöhnlich'))?></span>
          <?php if($equipmentItemId>0): ?><form method="post" action="/pets/items/<?=$equipmentItemId?>/unequip">
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>">
            <button class="btn btn-secondary btn-small" type="submit">Ablegen</button>
          </form><?php endif; ?>
        <?php else: ?><p><?=Security::e($equipmentSlotDefinition['description'])?></p><?php endif; ?>
      </article>
      <?php endforeach; ?>
    </div>
  </section>

  <section class="panel pet-stat-breakdown" aria-labelledby="equipment-stats-title">
    <div class="panel-header"><h2 id="equipment-stats-title">Kampfwerte</h2><p>Basis, Ausrüstung und Skillbaum ergeben zusammen den Gesamtwert.</p></div>
    <div class="pet-stat-breakdown-list">
      <?php foreach($equipmentStatLabels as $equipmentStatKey=>$equipmentStatLabel): $equipmentStat=is_array($equipmentBreakdown[$equipmentStatKey]??null)?$equipmentBreakdown[$equipmentStatKey]:[]; $equipmentTotal=(int)($equipmentStat['total']??$equipmentStats[$equipmentStatKey]??0); ?>
      <div class="pet-stat-breakdown-row">
        <div><strong><?=Security::e($equipmentStatLabel)?></strong><span><?=number_format($equipmentTotal,0,',','.')?></span></div>
        <dl><div><dt>Basis</dt><dd><?=number_format((int)($equipmentStat['base']??0),0,',','.')?></dd></div><div><dt>Ausrüstung</dt><dd>+<?=number_format(max(0,(int)($equipmentStat['gear']??0)),0,',','.')?></dd></div><div><dt>Skills</dt><dd>+<?=number_format(max(0,(int)($equipmentStat['skills']??0)),0,',','.')?></dd></div></dl>
      </div>
      <?php endforeach; ?>
      <div class="pet-combat-hp-note"><span>Kampf-HP</span><strong><?=number_format(max(0,(int)($equipmentStats['combat_hp']??0)),0,',','.')?></strong><small>Wird aus Vitalität berechnet und ist nicht deine Pflege-Gesundheit.</small></div>
    </div>
  </section>
</div>

<section class="panel pet-inventory" aria-labelledby="pet-inventory-title">
  <div class="panel-header pet-game-split-header"><div><h2 id="pet-inventory-title">Inventar</h2><p>Gegenstände sind an dein Pet gebunden und können nicht gehandelt werden.</p></div>
    <label class="pet-item-filter">Slot filtern<select data-pet-item-filter><option value="all">Alle Slots</option><?php foreach($equipmentSlotDefinitions as $equipmentSlotKey=>$equipmentSlotDefinition):?><option value="<?=Security::e($equipmentSlotKey)?>"><?=Security::e($equipmentSlotDefinition['label'])?></option><?php endforeach;?></select></label>
  </div>
  <?php if(!$equipmentItems): ?><div class="pet-game-empty-body"><span aria-hidden="true">🎒</span><h3>Noch keine Ausrüstung gefunden</h3><p>Schicke dein Pet auf ein Abenteuer. Dort wartet der erste Gegenstand mit Kampfwerten.</p><a class="btn btn-primary" href="/pets/adventure">Zum Abenteuer</a></div><?php else: ?>
  <div class="pet-item-grid" data-pet-item-grid>
    <?php foreach($equipmentItems as $equipmentIndex=>$equipmentItem): if(!is_array($equipmentItem))continue;
      $equipmentItemId=max(0,(int)($equipmentItem['id']??0));
      $equipmentItemSlot=(string)($equipmentItem['slot']??'');
      $equipmentItemRarity=(string)($equipmentItem['rarity']??'common');
      if(!isset($equipmentRarityLabels[$equipmentItemRarity]))$equipmentItemRarity='common';
      $equipmentItemEquipped=!empty($equipmentItem['equipped']);
      $equipmentItemAttributes=is_array($equipmentItem['attributes']??null)?$equipmentItem['attributes']:[];
      foreach(['strength_bonus','agility_bonus','vitality_bonus','instinct_bonus'] as $equipmentAttributeKey)if(isset($equipmentItem[$equipmentAttributeKey]))$equipmentItemAttributes[$equipmentAttributeKey]=(int)$equipmentItem[$equipmentAttributeKey];
      $equipmentItemTitleId='pet-item-'.$equipmentItemId.'-'.$equipmentIndex;
    ?>
    <article class="pet-item-card pet-rarity-<?=Security::e($equipmentItemRarity)?><?=$equipmentItemEquipped?' is-equipped':''?>" data-pet-item-card data-item-slot="<?=Security::e($equipmentItemSlot)?>" aria-labelledby="<?=Security::e($equipmentItemTitleId)?>">
      <div class="pet-item-card-head"><span class="pet-item-icon" aria-hidden="true"><?=Security::e((string)($equipmentItem['icon']??$equipmentSlotDefinitions[$equipmentItemSlot]['icon']??'🎁'))?></span><div><span class="pet-rarity-label"><?=Security::e($equipmentItem['rarity_label']??$equipmentRarityLabels[$equipmentItemRarity])?></span><h3 id="<?=Security::e($equipmentItemTitleId)?>"><?=Security::e((string)($equipmentItem['item_name']??$equipmentItem['name']??'Unbenannter Gegenstand'))?></h3></div><?php if($equipmentItemEquipped):?><span class="status-pill status-online">Ausgerüstet</span><?php endif;?></div>
      <div class="pet-item-meta"><span><?=Security::e($equipmentSlotDefinitions[$equipmentItemSlot]['label']??ucfirst($equipmentItemSlot?:'Gegenstand'))?></span><span>Stufe <?=max(1,(int)($equipmentItem['item_level']??1))?></span><?php if((int)($equipmentItem['level_required']??1)>1):?><span>Ab Level <?=(int)$equipmentItem['level_required']?></span><?php endif;?></div>
      <dl class="pet-item-attributes">
        <?php $equipmentHasAttribute=false; foreach($equipmentItemAttributes as $equipmentAttributeKey=>$equipmentAttributeValue): $equipmentAttributeValue=(int)$equipmentAttributeValue; if($equipmentAttributeValue===0)continue; $equipmentHasAttribute=true; ?><div><dt><?=Security::e($equipmentAttributeLabels[(string)$equipmentAttributeKey]??(string)$equipmentAttributeKey)?></dt><dd><?=$equipmentAttributeValue>0?'+':''?><?=$equipmentAttributeValue?></dd></div><?php endforeach; ?>
        <?php if(!$equipmentHasAttribute):?><div><dt>Effekt</dt><dd>Kosmetisch</dd></div><?php endif;?>
      </dl>
      <?php if($equipmentItemId>0): ?><form method="post" action="/pets/items/<?=$equipmentItemId?>/<?=$equipmentItemEquipped?'unequip':'equip'?>">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>">
        <button class="btn <?=$equipmentItemEquipped?'btn-secondary':'btn-primary'?>" type="submit"><?=$equipmentItemEquipped?'Ablegen':'Ausrüsten'?></button>
      </form><?php endif; ?>
      <?php if($equipmentItemId>0&&!$equipmentItemEquipped&&($equipmentItem['source_type']??'')!=='starter'): ?><form method="post" action="/pets/items/<?=$equipmentItemId?>/salvage" data-confirm="<?=Security::e((string)($equipmentItem['item_name']??'Diesen Gegenstand'))?> wirklich dauerhaft für Münzen verwerten?">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>">
        <button class="btn btn-secondary btn-small" type="submit">Für Münzen verwerten</button>
      </form><?php endif; ?>
    </article>
    <?php endforeach; ?>
  </div>
  <p class="pet-item-filter-empty" data-pet-item-filter-empty hidden>Für diesen Slot befinden sich keine Gegenstände im Inventar.</p>
  <?php endif; ?>
</section>
<?php endif; ?>
