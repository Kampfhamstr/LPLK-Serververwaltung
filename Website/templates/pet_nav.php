<?php
use App\Core\Security;

$petNavCurrent=(string)($currentPetPage??'overview');
$petNavPages=[
    'overview'=>['href'=>'/pets','label'=>'Übersicht','icon'=>'⌂'],
    'playground'=>['href'=>'/pets/playground','label'=>'Spielwiese','icon'=>'🐾'],
    'adventure'=>['href'=>'/pets/adventure','label'=>'Abenteuer','icon'=>'🧭'],
    'equipment'=>['href'=>'/pets/equipment','label'=>'Ausrüstung','icon'=>'🎒'],
    'skills'=>['href'=>'/pets/skills','label'=>'Skillbaum','icon'=>'✦'],
    'arena'=>['href'=>'/pets/arena','label'=>'Arena','icon'=>'⚔'],
];
?>
<nav class="subnav pet-game-nav" aria-label="Pet-Bereiche">
  <?php foreach($petNavPages as $petNavKey=>$petNavPage): $petNavActive=$petNavCurrent===$petNavKey; ?>
  <a class="pet-game-nav-link<?=$petNavActive?' is-active':''?>" href="<?=Security::e($petNavPage['href'])?>"<?=$petNavActive?' aria-current="page"':''?>>
    <span aria-hidden="true"><?=Security::e($petNavPage['icon'])?></span>
    <span><?=Security::e($petNavPage['label'])?></span>
  </a>
  <?php endforeach; ?>
</nav>
