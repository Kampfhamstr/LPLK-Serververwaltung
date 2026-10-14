<?php use App\Core\Security; ?>
<?php if(!$pet): ?>
<section class="panel pets-welcome">
  <div><span class="pets-eyebrow">Kostenlose Adoption</span><h2>24 Begleiter warten auf dich</h2><p>Von süßen Klassikern bis zu legendären Wesen: Alle Tiere sind kostenlos und spielerisch gleich stark.</p></div>
  <form method="post" action="/pets/adopt" class="form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <fieldset class="pet-species-selection">
      <legend>Tierart auswählen</legend>
      <div class="pet-species-groups">
      <?php foreach($speciesTiers as $tierKey=>$tier): ?>
      <section class="pet-species-group pet-species-group-<?=Security::e($tierKey)?>" aria-labelledby="pet-tier-<?=Security::e($tierKey)?>">
        <div class="pet-species-group-title" id="pet-tier-<?=Security::e($tierKey)?>"><strong><?=Security::e($tier['label'])?></strong><span><?=Security::e($tier['description'])?></span></div>
        <div class="pet-species-grid">
          <?php foreach($species as $key=>$animal): if(($animal['tier']??'classic')!==$tierKey)continue; ?>
          <label class="pet-species-card pet-species-card-<?=Security::e($tierKey)?>" data-species-tier="<?=Security::e($tierKey)?>">
            <input type="radio" name="species" value="<?=Security::e($key)?>" required>
            <span class="pet-tier-badge"><?=Security::e($tierKey==='epic'?'Episch':($tierKey==='cool'?'Cool':'Klassisch'))?></span>
            <span class="pet-species-emoji" aria-hidden="true"><?=$animal['emoji']?></span>
            <strong><?=Security::e($animal['label'])?></strong><small><?=Security::e($animal['temperament'])?></small>
          </label>
          <?php endforeach; ?>
        </div>
      </section>
      <?php endforeach; ?>
      </div>
    </fieldset>
    <label>Pet-Name<input type="text" name="name" minlength="2" maxlength="24" pattern="[A-Za-zÄÖÜäöüß0-9 ._-]{2,24}" autocomplete="off" required></label>
    <button class="btn btn-primary" type="submit">Kostenlos adoptieren</button>
  </form>
</section>
<?php else:
$animal=$species[$pet['species_key']]??['label'=>'Pet','emoji'=>'🐾','tier'=>'classic'];
$tier=$speciesTiers[$animal['tier']]??$speciesTiers['classic'];
$actionPresentations=[
  'feed'=>['icon'=>'🥣','hint'=>'Knusperrunde'],
  'pet'=>['icon'=>'🪮','hint'=>'Fellpflege'],
  'play'=>['icon'=>'🎾','hint'=>'Ballspaß'],
  'sleep'=>['icon'=>'💤','hint'=>'Nickerchen'],
  'train'=>['icon'=>'🏋️','hint'=>'Krafttraining'],
  'heal'=>['icon'=>'🩹','hint'=>'Heilpflege'],
];
?>
<?php require __DIR__.'/pet_nav.php'; ?>
<div class="pets-dashboard">
  <section class="panel pet-hero pet-action-stage" data-pet-action-stage data-pet-id="<?=(int)$pet['id']?>" data-pet-name="<?=Security::e($pet['name'])?>">
    <div class="pet-avatar" data-pet-main-character role="img" aria-label="<?=Security::e($pet['name'].', '.$animal['label'])?>">
      <span class="pet-emoji-actor" aria-hidden="true"><?=Security::e($animal['emoji'])?></span>
      <span class="pet-mischief-mark" aria-hidden="true">💧</span>
      <span class="pet-action-aura" data-pet-action-aura aria-hidden="true"></span>
      <span class="pet-action-prop" data-pet-action-prop aria-hidden="true"></span>
      <span class="pet-action-particles" data-pet-action-particles aria-hidden="true"><i></i><i></i><i></i></span>
    </div>
    <div class="pet-identity"><span class="pets-eyebrow"><?=Security::e($animal['label'])?> · <?=Security::e($tier['label'])?></span><h2><?=Security::e($pet['name'])?></h2><div class="pet-level">Level <?=(int)$pet['level']?> · <?=number_format((int)$pet['xp'],0,',','.')?> XP</div></div>
    <div class="pet-wallet"><span>🪙 <strong><?=(int)$wallet['coins']?></strong> Münzen</span><span>🏋️ <strong><?=(int)$wallet['training_points']?></strong> Trainingspunkte</span></div>
    <p class="pet-action-feedback" data-pet-action-feedback role="status" aria-live="polite" hidden></p>
  </section>
  <section class="panel pet-game-intro">
    <div><span class="pets-eyebrow">Aktuell in v1.15.2.1</span><h2>Steam-Spielwiese &amp; eigene Animationen</h2><p>Lass <?=Security::e($pet['name'])?> mit Steam-verifizierten Community-Pets spielen. Als Besitzer erscheint nur der Steamname; jede Pflegeaktion besitzt jetzt eine eigene Szene.</p></div>
    <div class="pet-game-link-grid">
      <a class="pet-game-link" href="/pets/playground"><span aria-hidden="true">🐾</span><strong>Spielwiese</strong><small>Andere Pets treffen und zusammen spielen</small></a>
      <a class="pet-game-link" href="/pets/adventure"><span aria-hidden="true">🗺️</span><strong>Abenteuer</strong><small>Orte erkunden und Beute finden</small></a>
      <a class="pet-game-link" href="/pets/equipment"><span aria-hidden="true">🎒</span><strong>Ausrüstung</strong><small>Attribute vergleichen und verbessern</small></a>
      <a class="pet-game-link" href="/pets/skills"><span aria-hidden="true">🌳</span><strong>Skillbaum</strong><small>Einen eigenen Build entwickeln</small></a>
      <a class="pet-game-link pet-game-link-arena" href="/pets/arena"><span aria-hidden="true">⚔️</span><strong>Zur Arena</strong><small>Gegen acht PvE-Herausforderer kämpfen</small></a>
    </div>
  </section>
  <section class="panel pet-status-panel"><h2>Zustand</h2><div class="pet-status-grid">
  <?php foreach(['health'=>'Gesundheit','hunger'=>'Sättigung','energy'=>'Energie','happiness'=>'Glück','bond'=>'Bindung'] as $key=>$label): $value=(int)$pet[$key]; ?>
    <div class="pet-meter"><div><span><?=$label?></span><strong><?=$value?> %</strong></div><progress max="100" value="<?=$value?>" aria-label="<?=$label?>: <?=$value?> Prozent"></progress></div>
  <?php endforeach; ?></div><p class="small-muted">Die Werte werden anhand der vergangenen Serverzeit berechnet. Dein Pet kann nicht sterben.</p></section>
  <section class="panel pet-actions"><h2>Pflege</h2><div class="pet-action-grid">
  <?php foreach($actions as $key=>$action): $until=$cooldowns[$key]??null;$locked=$until&&strtotime($until)>time();$actionPresentation=$actionPresentations[$key]??['icon'=>'🐾','hint'=>'Pet-Moment']; ?>
    <form method="post" action="/pets/action" data-pet-action-form data-pet-action="<?=Security::e($key)?>"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="<?=Security::e($key)?>"><input type="hidden" name="idempotency_key" value="<?=bin2hex(random_bytes(16))?>"><button class="pet-action-button pet-action-button-<?=Security::e($key)?>" type="submit" <?=$locked?'disabled':''?>><span class="pet-action-button-icon" aria-hidden="true"><?=Security::e($actionPresentation['icon'])?></span><span class="pet-action-button-copy"><strong><?=Security::e($action['label'])?></strong><small><?=Security::e($actionPresentation['hint'])?></small><span><?=$locked?'Verfügbar '.Security::e(date('H:i',strtotime($until))):'+'.(int)$action['xp'].' XP'?></span></span></button></form>
  <?php endforeach; ?></div></section>
  <section class="panel pet-tasks" id="tasks"><h2>Heutige Aufgaben</h2>
  <?php foreach($tasks as $task): $done=(int)$task['progress']>=(int)$task['target']; ?><div class="pet-task"><div><strong><?=Security::e($task['title'])?></strong><span><?=(int)$task['progress']?> / <?=(int)$task['target']?> · +<?=(int)$task['reward_coins']?> 🪙 · +<?=(int)$task['reward_xp']?> XP</span></div><?php if($task['claimed_at']):?><span class="pet-task-done">Eingelöst</span><?php elseif($done):?><form method="post" action="/pets/tasks/<?=(int)$task['id']?>/claim"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-primary btn-small">Einlösen</button></form><?php else:?><progress max="<?=(int)$task['target']?>" value="<?=(int)$task['progress']?>" aria-label="Fortschritt <?=Security::e($task['title'])?>: <?=(int)$task['progress']?> von <?=(int)$task['target']?>"></progress><?php endif;?></div><?php endforeach; ?>
  </section>
</div>
<?php endif; ?>
<section class="panel pet-ranking"><h2>Pet-Rangliste</h2><p class="small-muted">In der öffentlichen Rangliste erscheinen nur Pets von Steam-verifizierten Besitzern.</p><?php if(!$leaderboard):?><p class="small-muted">Noch keine öffentlichen Pets vorhanden.</p><?php else:?><div class="table-wrap"><table><thead><tr><th>#</th><th>Pet</th><th>Besitzer</th><th>Level</th><th>Bindung</th></tr></thead><tbody><?php foreach($leaderboard as $i=>$entry):$a=$species[$entry['species_key']]??['emoji'=>'🐾'];?><tr><td><?=$i+1?></td><td><?=$a['emoji']?> <?=Security::e($entry['name'])?></td><td><?=Security::e($entry['owner_name'])?></td><td><?=(int)$entry['level']?></td><td><?=(int)$entry['bond']?> %</td></tr><?php endforeach;?></tbody></table></div><?php endif;?></section>
