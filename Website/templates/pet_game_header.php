<?php
use App\Core\Security;
use App\Services\PetService;

$petHeaderPet=is_array($pet??null)?$pet:[];
$petHeaderWallet=is_array($wallet??null)?$wallet:[];
$petHeaderStats=is_array($stats??null)?$stats:[];
$petHeaderSpeciesKey=(string)($petHeaderPet['species_key']??'');
$petHeaderSpecies=PetService::SPECIES[$petHeaderSpeciesKey]??[];
$petHeaderName=(string)($petHeaderPet['name']??'Dein Pet');
$petHeaderSpeciesLabel=(string)($petHeaderPet['species_label']??$petHeaderSpecies['label']??'Pet');
$petHeaderEmoji=(string)($petHeaderPet['emoji']??$petHeaderSpecies['emoji']??'🐾');
$petHeaderLevel=max(1,(int)($petHeaderPet['level']??1));
$petHeaderXp=max(0,(int)($petHeaderPet['xp']??0));
$petHeaderPower=max(0,(int)($petHeaderStats['power']??$petHeaderStats['total']??0));
$petHeaderCombatHp=max(0,(int)($petHeaderStats['combat_hp']??0));
$petHeaderCoins=max(0,(int)($petHeaderWallet['coins']??0));
$petHeaderPoints=max(0,(int)($petHeaderWallet['training_points']??0));
$petHeaderCombatStats=[
    'strength'=>['label'=>'Kraft','value'=>(int)($petHeaderStats['strength']??0)],
    'agility'=>['label'=>'Tempo','value'=>(int)($petHeaderStats['agility']??0)],
    'vitality'=>['label'=>'Zähigkeit','value'=>(int)($petHeaderStats['vitality']??0)],
    'instinct'=>['label'=>'Instinkt','value'=>(int)($petHeaderStats['instinct']??0)],
];
?>
<?php if(!$petHeaderPet): ?>
<section class="panel pet-game-empty" aria-labelledby="pet-game-empty-title">
  <div class="panel-header"><h2 id="pet-game-empty-title">Noch kein Pet adoptiert</h2><p>Adoptiere zuerst einen Begleiter, bevor du Abenteuer, Ausrüstung, Skills und Arena freischaltest.</p></div>
  <p><a class="btn btn-primary" href="/pets">Zur Adoption</a></p>
</section>
<?php else: ?>
<section class="panel pet-game-header" aria-labelledby="pet-game-pet-name">
  <div class="pet-game-header-main">
    <div class="pet-game-avatar" data-pet-main-character role="img" aria-label="<?=Security::e($petHeaderSpeciesLabel)?>">
      <span class="pet-emoji-actor" aria-hidden="true"><?=Security::e($petHeaderEmoji)?></span>
      <span class="pet-mischief-mark" aria-hidden="true">💧</span>
    </div>
    <div class="pet-game-identity">
      <span class="pets-eyebrow"><?=Security::e($petHeaderSpeciesLabel)?></span>
      <h2 id="pet-game-pet-name"><?=Security::e($petHeaderName)?></h2>
      <p>Level <?=$petHeaderLevel?> · <?=number_format($petHeaderXp,0,',','.')?> XP</p>
    </div>
  </div>
  <dl class="pet-game-summary" aria-label="Kampfwerte">
    <div class="pet-game-summary-primary"><dt>Kampfkraft</dt><dd><?=number_format($petHeaderPower,0,',','.')?></dd></div>
    <?php if($petHeaderCombatHp>0): ?><div><dt>Kampf-HP</dt><dd><?=number_format($petHeaderCombatHp,0,',','.')?></dd></div><?php endif; ?>
    <?php foreach($petHeaderCombatStats as $petHeaderStat): ?><div><dt><?=Security::e($petHeaderStat['label'])?></dt><dd><?=number_format($petHeaderStat['value'],0,',','.')?></dd></div><?php endforeach; ?>
  </dl>
  <div class="pet-game-wallet" aria-label="Pet-Guthaben">
    <span title="Pet-Münzen">🪙 <strong><?=number_format($petHeaderCoins,0,',','.')?></strong><small>Münzen</small></span>
    <span title="Trainingspunkte">✦ <strong><?=number_format($petHeaderPoints,0,',','.')?></strong><small>Trainingspunkte</small></span>
  </div>
</section>
<?php endif; ?>
