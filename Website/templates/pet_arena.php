<?php
use App\Core\Security;

$arenaData=is_array($arenaProfile??null)?$arenaProfile:[];
$arenaOpponentList=is_array($opponents??null)?array_values($opponents):[];
$arenaBattleList=is_array($arenaBattles??null)?array_values($arenaBattles):[];
$arenaTickets=max(0,(int)($arenaTicketsLeft??0));
$arenaCooldownRaw=$arenaCooldownUntil??null;
$arenaCooldownEpoch=is_numeric($arenaCooldownRaw)?(int)$arenaCooldownRaw:($arenaCooldownRaw?(strtotime((string)$arenaCooldownRaw)?:0):0);
$arenaCoolingDown=$arenaCooldownEpoch>time();
$arenaRating=max(0,(int)($arenaData['rating']??1000));
$arenaWins=max(0,(int)($arenaData['wins']??0));
$arenaLosses=max(0,(int)($arenaData['losses']??0));
$arenaDraws=max(0,(int)($arenaData['draws']??0));
$arenaLeague=(string)($arenaData['league_label']??$arenaData['league']??'Bronze');
$arenaTacticInput=is_array($arenaTactics??null)?$arenaTactics:[];
$arenaTacticDefaults=[
    'aggressive'=>['label'=>'Aggressiv','icon'=>'⚔️','description'=>'+12 % Schaden, −10 % Verteidigung'],
    'defensive'=>['label'=>'Defensiv','icon'=>'🛡️','description'=>'+15 % Verteidigung, −8 % Schaden'],
    'tricky'=>['label'=>'Taktisch','icon'=>'🎯','description'=>'+8 % Treffer, +6 % Ausweichen, −5 % Schaden'],
];
$arenaTactics=[];
foreach($arenaTacticDefaults as $arenaTacticKey=>$arenaTacticFallback){
    $arenaTacticProvided=is_array($arenaTacticInput[$arenaTacticKey]??null)?$arenaTacticInput[$arenaTacticKey]:[];
    $arenaTactics[$arenaTacticKey]=[
        'label'=>(string)($arenaTacticProvided['label']??$arenaTacticFallback['label']),
        'icon'=>$arenaTacticFallback['icon'],
        'description'=>(string)($arenaTacticProvided['description']??$arenaTacticFallback['description']),
    ];
}
$arenaResultLabels=['win'=>'Sieg','loss'=>'Niederlage','draw'=>'Unentschieden'];

include __DIR__.'/pet_nav.php';
include __DIR__.'/pet_game_header.php';
?>
<?php if(!empty($pet)): ?>
<header class="pet-game-page-title">
  <div><span class="pets-eyebrow">Saison <?=Security::e((string)($arenaData['season_label']??$arenaData['season_key']??'Aktuell'))?></span><h2>Arena</h2><p>Kämpfe dich durch acht feste PvE-Herausforderer. Jeder Kampf wird vollständig und manipulationssicher auf dem Server berechnet.</p></div>
  <?php if(!empty($arenaData['season_ends_at'])):?><span class="pet-game-rule-note">Saisonende <time datetime="<?=Security::e((string)$arenaData['season_ends_at'])?>"><?=Security::e((string)($arenaData['season_ends_label']??$arenaData['season_ends_at']))?></time></span><?php endif;?>
</header>

<section class="pet-arena-profile" aria-label="Dein Arena-Profil">
  <article class="panel pet-arena-league"><span class="pet-arena-league-emblem" aria-hidden="true">🏆</span><div><span>Aktuelle Liga</span><strong><?=Security::e($arenaLeague)?></strong><small><?=number_format($arenaRating,0,',','.')?> Wertung</small></div></article>
  <article class="panel"><span>Tagestickets</span><strong><?=$arenaTickets?> / <?=max($arenaTickets,(int)($arenaData['daily_ticket_limit']??5))?></strong><small>Jeder Kampf verbraucht ein Ticket und 8 Energie.</small></article>
  <article class="panel"><span>Siege</span><strong><?=$arenaWins?></strong><small><?=$arenaLosses?> Niederlagen · <?=$arenaDraws?> Unentschieden</small></article>
  <article class="panel"><span>Kampfkraft</span><strong><?=number_format(max(0,(int)($stats['power']??0)),0,',','.')?></strong><small>Aktueller Build vor dem Snapshot</small></article>
</section>

<?php if($arenaCoolingDown): ?>
<div class="alert alert-info pet-arena-cooldown" data-arena-cooldown-epoch="<?=$arenaCooldownEpoch?>" role="status">Dein Pet sammelt sich noch. Nächster Kampf <strong data-arena-cooldown-text><?=Security::e(date('H:i:s',$arenaCooldownEpoch))?> Uhr</strong>.</div>
<?php endif; ?>

<section class="panel pet-arena-matchmaking" aria-labelledby="pet-arena-opponents-title">
  <div class="panel-header"><h2 id="pet-arena-opponents-title">Gegner wählen</h2><p>Besiege jede Stufe, um die nächste freizuschalten. Angriff, Verteidigung und Taktik greifen sichtbar ineinander.</p></div>
  <?php if(!$arenaOpponentList): ?><div class="pet-game-empty-body"><span aria-hidden="true">⚔️</span><h3>Keine Gegner verfügbar</h3><p>Die Arena stellt gerade neue Paarungen zusammen. Dein Rating und deine Tickets bleiben unverändert.</p></div><?php else: ?>
  <div class="pet-arena-opponent-grid">
    <?php foreach($arenaOpponentList as $arenaOpponentIndex=>$arenaOpponent): if(!is_array($arenaOpponent))continue;
      $arenaOpponentKey=(string)($arenaOpponent['key']??$arenaOpponent['opponent_key']??'');if($arenaOpponentKey==='')continue;
      $arenaOpponentId='arena-opponent-'.preg_replace('/[^a-z0-9_-]+/i','-',strtolower($arenaOpponentKey)).'-'.$arenaOpponentIndex;
      $arenaOpponentTicketCost=max(0,(int)($arenaOpponent['ticket_cost']??1));
      $arenaOpponentAvailable=!array_key_exists('available',$arenaOpponent)||!empty($arenaOpponent['available']);
      $arenaCanFight=$arenaOpponentAvailable&&!$arenaCoolingDown&&($arenaOpponentTicketCost===0||$arenaTickets>=$arenaOpponentTicketCost);
      $arenaOpponentStats=is_array($arenaOpponent['stats']??null)?$arenaOpponent['stats']:[];
    ?>
    <article class="pet-arena-opponent<?=$arenaCanFight?'':' is-locked'?>" aria-labelledby="<?=Security::e($arenaOpponentId)?>">
      <div class="pet-arena-opponent-head"><span class="pet-arena-opponent-avatar" aria-hidden="true"><?=Security::e((string)($arenaOpponent['emoji']??'🐾'))?></span><div><span class="pet-rarity-label"><?=Security::e(!empty($arenaOpponent['is_npc'])?(string)($arenaOpponent['difficulty_label']??'PvE-Arena'):(string)($arenaOpponent['league_label']??$arenaOpponent['league']??'Arena'))?></span><h3 id="<?=Security::e($arenaOpponentId)?>"><?=Security::e((string)($arenaOpponent['name']??$arenaOpponent['pet_name']??'Unbekannter Rivale'))?></h3><p><?=Security::e((string)($arenaOpponent['owner_name']??(!empty($arenaOpponent['is_npc'])?'Arena-NPC':'Community-Gegner')))?></p></div></div>
      <dl class="pet-arena-opponent-stats"><div><dt>Level</dt><dd><?=max(1,(int)($arenaOpponent['level']??1))?></dd></div><div><dt>Wertung</dt><dd><?=number_format(max(0,(int)($arenaOpponent['rating']??1000)),0,',','.')?></dd></div><div><dt>Kampfkraft</dt><dd><?=number_format(max(0,(int)($arenaOpponent['power']??$arenaOpponentStats['power']??0)),0,',','.')?></dd></div><div><dt>Stil</dt><dd><?=Security::e((string)($arenaOpponent['style_label']??$arenaOpponent['style']??'Ausgewogen'))?></dd></div></dl>
      <form method="post" action="/pets/arena/fight" class="pet-arena-fight-form">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>"><input type="hidden" name="opponent_key" value="<?=Security::e($arenaOpponentKey)?>">
        <fieldset class="pet-tactic-picker pet-arena-tactics"><legend>Deine Kampftaktik</legend><?php foreach($arenaTactics as $arenaTacticKey=>$arenaTactic): $arenaTacticId=$arenaOpponentId.'-'.$arenaTacticKey; ?><label class="pet-tactic-option" for="<?=Security::e($arenaTacticId)?>"><input id="<?=Security::e($arenaTacticId)?>" type="radio" name="tactic" value="<?=Security::e($arenaTacticKey)?>"<?=$arenaTacticKey==='tricky'?' checked':''?> required><span><strong><span aria-hidden="true"><?=Security::e($arenaTactic['icon'])?></span> <?=Security::e($arenaTactic['label'])?></strong><small><?=Security::e($arenaTactic['description'])?></small></span></label><?php endforeach;?></fieldset>
        <?php if(!$arenaCanFight):?><p class="pet-form-blocker"><?=Security::e((string)($arenaOpponent['locked_reason']??($arenaCoolingDown?'Kampf-Cooldown aktiv.':($arenaOpponentTicketCost>0&&$arenaTickets<$arenaOpponentTicketCost?'Keine Arena-Tickets übrig.':'Dieser Gegner ist gerade nicht verfügbar.'))))?></p><?php endif;?>
        <button class="btn btn-primary" type="submit"<?=$arenaCanFight?'':' disabled'?>>Kampf starten<?=$arenaOpponentTicketCost>0?' · '.$arenaOpponentTicketCost.' 🎟️':' · Training'?></button>
      </form>
    </article>
    <?php endforeach; ?>
  </div>
  <?php endif; ?>
</section>

<section class="panel pet-arena-history" aria-labelledby="pet-arena-history-title">
  <div class="panel-header"><h2 id="pet-arena-history-title">Kampfverlauf &amp; Replays</h2><p>Die vollständigen Rundenprotokolle bleiben auch ohne JavaScript lesbar.</p></div>
  <?php if(!$arenaBattleList):?><div class="pet-game-empty-body"><span aria-hidden="true">📜</span><h3>Noch kein Kampf</h3><p>Wähle oben einen Trainings- oder Arena-Gegner für dein erstes Replay.</p></div><?php else:?><div class="pet-battle-list">
    <?php foreach($arenaBattleList as $arenaBattleIndex=>$arenaBattle): if(!is_array($arenaBattle))continue;
      $arenaBattleResult=(string)($arenaBattle['result']??'draw');if(!isset($arenaResultLabels[$arenaBattleResult]))$arenaBattleResult='draw';
      $arenaBattleLog=$arenaBattle['battle_log']??$arenaBattle['log']??$arenaBattle['round_log']??[];
      if(is_string($arenaBattleLog)){try{$arenaBattleDecoded=json_decode($arenaBattleLog,true,512,JSON_THROW_ON_ERROR);$arenaBattleLog=is_array($arenaBattleDecoded)?$arenaBattleDecoded:[$arenaBattleLog];}catch(\Throwable){$arenaBattleLog=[$arenaBattleLog];}}
      if(!is_array($arenaBattleLog))$arenaBattleLog=[];
      $arenaBattleRatingChange=(int)($arenaBattle['rating_change']??((int)($arenaBattle['rating_after']??0)-(int)($arenaBattle['rating_before']??0)));
      $arenaBattleId=max(0,(int)($arenaBattle['id']??0));
    ?>
    <details class="pet-battle-details pet-battle-result-<?=Security::e($arenaBattleResult)?>" data-pet-battle-details<?=$arenaBattleIndex===0?' open':''?>>
      <summary><span class="pet-battle-result-badge"><?=Security::e($arenaResultLabels[$arenaBattleResult])?></span><strong>gegen <?=Security::e((string)($arenaBattle['opponent_name']??$arenaBattle['opponent_label']??$arenaBattle['opponent_key']??'Rivale'))?></strong><span><?=max(1,(int)($arenaBattle['rounds']??count($arenaBattleLog)))?> Runden</span><span class="pet-rating-change <?=$arenaBattleRatingChange>=0?'is-positive':'is-negative'?>"><?=$arenaBattleRatingChange>=0?'+':''?><?=$arenaBattleRatingChange?> Wertung</span><time datetime="<?=Security::e((string)($arenaBattle['created_at']??''))?>"><?=Security::e((string)($arenaBattle['created_at_label']??$arenaBattle['created_at']??''))?></time></summary>
      <div class="pet-battle-replay" aria-label="Kampfreplay <?=($arenaBattleId>0?'Nummer '.$arenaBattleId:'')?>">
        <div class="pet-battle-rewards"><span>🪙 +<?=max(0,(int)($arenaBattle['reward_coins']??0))?></span><span>⭐ +<?=max(0,(int)($arenaBattle['reward_xp']??0))?> XP</span><?php if((int)($arenaBattle['reward_training_points']??0)>0):?><span>✦ +<?=(int)$arenaBattle['reward_training_points']?></span><?php endif;?></div>
        <?php if($arenaBattleLog):?><ol class="pet-battle-rounds"><?php foreach($arenaBattleLog as $arenaRoundIndex=>$arenaRound):
          if(is_array($arenaRound)){$arenaRoundNumber=max(1,(int)($arenaRound['round']??$arenaRoundIndex+1));$arenaRoundText=(string)($arenaRound['text']??$arenaRound['message']??$arenaRound['action']??'Die Runde wurde ausgewertet.');$arenaRoundActor=(string)($arenaRound['actor']??$arenaRound['source']??'');}else{$arenaRoundNumber=$arenaRoundIndex+1;$arenaRoundText=(string)$arenaRound;$arenaRoundActor='';}
        ?><li><span>Runde <?=$arenaRoundNumber?></span><p><?php if($arenaRoundActor!==''):?><strong><?=Security::e($arenaRoundActor)?>:</strong> <?php endif;?><?=Security::e($arenaRoundText)?></p><?php if(is_array($arenaRound)&&isset($arenaRound['player_hp'],$arenaRound['opponent_hp'])):?><small>Dein Pet: <?=max(0,(int)$arenaRound['player_hp'])?> HP · Gegner: <?=max(0,(int)$arenaRound['opponent_hp'])?> HP</small><?php endif;?></li><?php endforeach;?></ol><?php else:?><p class="small-muted">Für diesen älteren Kampf ist kein Rundenprotokoll gespeichert.</p><?php endif;?>
      </div>
    </details>
    <?php endforeach; ?>
  </div><?php endif;?>
</section>
<?php endif; ?>
