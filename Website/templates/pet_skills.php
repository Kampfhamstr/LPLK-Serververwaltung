<?php
use App\Core\Security;

$skillsTreeInput=is_array($skillTree??null)?$skillTree:[];
$skillsRanks=is_array($skillRanks??null)?$skillRanks:[];
$skillsSpent=max(0,(int)($spentSkillPoints??0));
$skillsCap=max(0,(int)($skillPointCap??0));
$skillsAvailable=max(0,$skillsCap-$skillsSpent);
$skillsResetCost=max(0,(int)($resetCost??0));
$skillsWallet=is_array($wallet??null)?$wallet:[];
$skillsTrainingPoints=max(0,(int)($skillsWallet['training_points']??0));
$skillsCoins=max(0,(int)($skillsWallet['coins']??0));
$skillsBranchDefaults=[
    'courage'=>['label'=>'Mut','icon'=>'⚔️','description'=>'Mehr Stärke, kritische Treffer und ein gefährlicher Abschluss.'],
    'guard'=>['label'=>'Schutz','icon'=>'🛡️','description'=>'Vitalität, Rüstung und eine zweite Chance in langen Kämpfen.'],
    'instinct'=>['label'=>'Instinkt','icon'=>'🧭','description'=>'Geschick, Ausweichen und bessere Funde auf Abenteuern.'],
];
$skillsBranches=[];
foreach($skillsTreeInput as $skillsTreeKey=>$skillsTreeEntry){
    if(!is_array($skillsTreeEntry))continue;
    if(isset($skillsTreeEntry['skills'])||isset($skillsTreeEntry['nodes'])){
        $skillsBranchKey=is_string($skillsTreeKey)?$skillsTreeKey:(string)($skillsTreeEntry['key']??'branch-'.count($skillsBranches));
        $skillsBranches[$skillsBranchKey]=[
            'label'=>(string)($skillsTreeEntry['label']??$skillsTreeEntry['name']??$skillsBranchDefaults[$skillsBranchKey]['label']??ucfirst($skillsBranchKey)),
            'icon'=>(string)($skillsTreeEntry['icon']??$skillsBranchDefaults[$skillsBranchKey]['icon']??'✦'),
            'description'=>(string)($skillsTreeEntry['description']??$skillsBranchDefaults[$skillsBranchKey]['description']??''),
            'skills'=>array_values((array)($skillsTreeEntry['skills']??$skillsTreeEntry['nodes']??[])),
        ];
        continue;
    }
    $skillsBranchKey=(string)($skillsTreeEntry['branch']??'instinct');
    if(!isset($skillsBranches[$skillsBranchKey]))$skillsBranches[$skillsBranchKey]=[
        'label'=>(string)($skillsBranchDefaults[$skillsBranchKey]['label']??ucfirst($skillsBranchKey)),
        'icon'=>(string)($skillsBranchDefaults[$skillsBranchKey]['icon']??'✦'),
        'description'=>(string)($skillsBranchDefaults[$skillsBranchKey]['description']??''),
        'skills'=>[],
    ];
    $skillsTreeEntry['key']=$skillsTreeEntry['key']??(is_string($skillsTreeKey)?$skillsTreeKey:'');
    $skillsBranches[$skillsBranchKey]['skills'][]=$skillsTreeEntry;
}

include __DIR__.'/pet_nav.php';
include __DIR__.'/pet_game_header.php';
?>
<?php if(!empty($pet)): ?>
<header class="pet-game-page-title">
  <div><span class="pets-eyebrow">Dein Build</span><h2>Skillbaum</h2><p>Verbessere gezielt deinen Kampfstil. Voraussetzungen und Wirkungen werden vor jedem Kauf angezeigt.</p></div>
  <span class="pet-game-rule-note"><?=$skillsSpent?> / <?=$skillsCap?> Punkte verteilt</span>
</header>

<section class="panel pet-skill-budget" aria-labelledby="pet-skill-budget-title">
  <div>
    <h2 id="pet-skill-budget-title">Skillpunkte</h2>
    <p><strong><?=$skillsAvailable?></strong> Build-Punkte frei · <strong><?=$skillsTrainingPoints?></strong> Trainingspunkte im Guthaben</p>
    <progress max="<?=max(1,$skillsCap)?>" value="<?=min($skillsSpent,max(1,$skillsCap))?>" aria-label="<?=$skillsSpent?> von <?=$skillsCap?> Build-Punkten verteilt"></progress>
  </div>
  <form method="post" action="/pets/skills/reset" data-confirm="Den gesamten Skillbaum wirklich für <?=$skillsResetCost?> Münzen zurücksetzen? Alle investierten Trainingspunkte werden gemäß Serverregel erstattet.">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>">
    <button class="btn btn-secondary" type="submit"<?=($skillsSpent>0&&$skillsCoins>=$skillsResetCost)?'':' disabled'?>>Neu verteilen · <?=$skillsResetCost?> 🪙</button>
    <?php if($skillsSpent<=0):?><small>Noch keine Punkte verteilt.</small><?php elseif($skillsCoins<$skillsResetCost):?><small>Nicht genügend Münzen.</small><?php endif;?>
  </form>
</section>

<?php if(!$skillsBranches): ?>
<section class="panel pet-game-empty"><div class="panel-header"><h2>Skillbaum nicht verfügbar</h2><p>Der Skillkatalog konnte nicht geladen werden. Deine vorhandenen Punkte bleiben erhalten.</p></div></section>
<?php else: ?>
<div class="pet-skill-tree" aria-label="Skillbaum mit drei Spezialisierungen">
  <?php foreach($skillsBranches as $skillsBranchKey=>$skillsBranch): $skillsBranchId='pet-skill-branch-'.preg_replace('/[^a-z0-9_-]+/i','-',strtolower((string)$skillsBranchKey)); ?>
  <section class="panel pet-skill-branch pet-skill-branch-<?=Security::e((string)$skillsBranchKey)?>" aria-labelledby="<?=Security::e($skillsBranchId)?>">
    <header><span class="pet-skill-branch-icon" aria-hidden="true"><?=Security::e((string)$skillsBranch['icon'])?></span><div><span class="pets-eyebrow">Spezialisierung</span><h2 id="<?=Security::e($skillsBranchId)?>"><?=Security::e((string)$skillsBranch['label'])?></h2><p><?=Security::e((string)$skillsBranch['description'])?></p></div></header>
    <ol class="pet-skill-nodes">
      <?php foreach((array)$skillsBranch['skills'] as $skillsIndex=>$skill): if(!is_array($skill))continue;
        $skillKey=(string)($skill['key']??$skill['skill_key']??'');if($skillKey==='')continue;
        $skillName=(string)($skill['name']??$skill['label']??$skillKey);
        $skillRank=max(0,(int)($skillsRanks[$skillKey]??$skill['rank']??0));
        $skillMaxRank=max(1,(int)($skill['max_rank']??$skill['ranks']??1));
        $skillCost=max(1,(int)($skill['cost']??$skill['cost_points']??1));
        $skillAtMaximum=$skillRank>=$skillMaxRank;
        $skillCanUnlock=array_key_exists('can_unlock',$skill)?!empty($skill['can_unlock']):(!$skillAtMaximum&&$skillsAvailable>0&&$skillsTrainingPoints>=$skillCost);
        $skillNodeId='pet-skill-node-'.preg_replace('/[^a-z0-9_-]+/i','-',strtolower($skillKey));
        $skillEffects=$skill['effects']??$skill['effect']??'';
        if(is_array($skillEffects))$skillEffects=implode(' · ',array_map(static fn($value)=>(string)$value,$skillEffects));
        $skillPrerequisite=(string)($skill['prerequisite_label']??'');
        if($skillPrerequisite===''&&is_string($skill['requires']??null))$skillPrerequisite=(string)$skill['requires'];
        elseif($skillPrerequisite===''&&is_array($skill['requires']??null)){
            $skillRequires=$skill['requires'];$skillPrerequisite=(string)($skillRequires['label']??$skillRequires['key']??'Vorgängerskill');
            if(isset($skillRequires['rank']))$skillPrerequisite.=' Rang '.max(1,(int)$skillRequires['rank']);
        }
      ?>
      <li class="pet-skill-node<?=$skillRank>0?' is-unlocked':''?><?=$skillCanUnlock?'':' is-locked'?>" aria-labelledby="<?=Security::e($skillNodeId)?>">
        <div class="pet-skill-node-marker" aria-hidden="true"><?=$skillRank>0?'✓':($skillsIndex+1)?></div>
        <div class="pet-skill-node-body">
          <div class="pet-skill-node-head"><div><span class="pet-skill-type"><?=Security::e((string)($skill['type_label']??$skill['type']??'Passiv'))?></span><h3 id="<?=Security::e($skillNodeId)?>"><?=Security::e($skillName)?></h3></div><strong>Rang <?=$skillRank?> / <?=$skillMaxRank?></strong></div>
          <p><?=Security::e((string)($skill['description']??'Dieser Skill verbessert den Build deines Pets.'))?></p>
          <?php if($skillEffects!==''):?><p class="pet-skill-effect"><strong>Wirkung:</strong> <?=Security::e((string)$skillEffects)?></p><?php endif;?>
          <ul class="pet-skill-requirements" aria-label="Voraussetzungen">
            <?php if((int)($skill['level_required']??$skill['min_level']??1)>1):?><li>Ab Level <?=(int)($skill['level_required']??$skill['min_level'])?></li><?php endif;?>
            <?php if($skillPrerequisite!==''):?><li>Benötigt <?=Security::e($skillPrerequisite)?></li><?php endif;?>
            <?php if(!$skillAtMaximum):?><li><?=$skillCost?> Trainingspunkt<?=$skillCost===1?'':'e'?></li><?php endif;?>
          </ul>
          <?php if($skillAtMaximum):?><span class="status-pill status-online">Voll ausgebaut</span><?php else:?><form method="post" action="/pets/skills/unlock">
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>"><input type="hidden" name="skill_key" value="<?=Security::e($skillKey)?>">
            <button class="btn btn-primary btn-small" type="submit"<?=$skillCanUnlock?'':' disabled'?>>Rang <?=$skillRank+1?> lernen · <?=$skillCost?> ✦</button>
            <?php if(!$skillCanUnlock&&($skill['locked_reason']??'')!==''):?><small class="pet-skill-lock-reason"><?=Security::e((string)$skill['locked_reason'])?></small><?php endif;?>
          </form><?php endif;?>
        </div>
      </li>
      <?php endforeach; ?>
    </ol>
  </section>
  <?php endforeach; ?>
</div>
<?php endif; ?>

<section class="panel pet-skill-help" aria-labelledby="pet-skill-help-title"><div class="panel-header"><h2 id="pet-skill-help-title">So wirken deine Entscheidungen</h2></div><div class="pet-skill-help-grid"><p><strong>Abenteuer:</strong> Instinkt verbessert Fundchancen, während Stärke, Geschick und Vitalität schwierige Ereignisse sicherer machen.</p><p><strong>Arena:</strong> Der Server friert Skills, Ausrüstung und Werte beim Start ein. Spätere Änderungen manipulieren keinen laufenden Kampf.</p><p><strong>Fairness:</strong> Jede Tierart nutzt denselben Baum. Deine Wahl des Pets bleibt Geschmackssache, dein Build entscheidet die Taktik.</p></div></section>
<?php endif; ?>
