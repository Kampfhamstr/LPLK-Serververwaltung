<?php
use App\Core\Security;
use App\Core\Auth;
$sid=(int)$server['id'];
$steam=(string)$player['steam_id64'];
$role=(string)($person['community_role']??'spieler');
$punishmentOptions=[
    10=>'10 Minuten',30=>'30 Minuten',60=>'1 Stunde',360=>'6 Stunden',
    1440=>'1 Tag',10080=>'7 Tage',43200=>'30 Tage',0=>'Permanent'
];
?>
<div class="page-title-row">
  <div>
    <h1><?=Security::e((string)$player['name'])?></h1>
    <p class="mono muted"><?=Security::e($steam)?> · <?=Security::e($server['name'])?></p>
  </div>
  <div class="toolbar">
    <a class="btn btn-secondary" href="/servers/<?=$sid?>/live">Zur Live-Übersicht</a>
    <?php if($person):?><a class="btn btn-secondary" href="/players/<?=$person['id']?>">Personenakte</a><?php endif;?>
  </div>
</div>

<div class="player-live-header">
  <div class="player-live-avatar">
    <?php if($person):?><img src="/players/<?=$person['id']?>/avatar" alt="" data-avatar>
    <?php else:?><span><?=Security::e(strtoupper(substr((string)$player['name'],0,1)))?></span><?php endif;?>
  </div>
  <div class="player-live-main" data-player-status-host data-player-steamid="<?=Security::e($steam)?>">
    <strong><?=Security::e((string)$player['name'])?></strong>
    <span><?=Security::e($steam)?></span>
    <?php if($person):?><div><span class="game-role game-role-<?=Security::e($role)?>"><?=Security::e(ucfirst($role))?></span></div><?php endif;?>
    <div class="live-state-badges">
      <?php if(!empty($player['nickname_override'])):?><span class="status-pill status-warning">Nickname überschrieben</span><?php endif;?>
      <?php if(!empty($player['frozen'])):?><span class="status-pill status-warning">Freeze</span><?php endif;?>
      <?php if(!empty($player['noclip'])):?><span class="status-pill status-warning">Noclip</span><?php endif;?>
      <?php if(!empty($player['godmode'])):?><span class="status-pill status-danger">Godmode</span><?php endif;?>
      <?php if(abs((float)($player['speed_multiplier']??1)-1.0)>0.001):?><span class="status-pill status-info">Speed <?=Security::e(number_format((float)$player['speed_multiplier'],2,',','.'))?>x</span><?php endif;?>
      <?php if(abs((float)($player['gravity_scale']??1)-1.0)>0.001):?><span class="status-pill status-info">Gravity <?=Security::e(number_format((float)$player['gravity_scale'],2,',','.'))?>x</span><?php endif;?>
      <?php if(!empty($player['voice_muted'])):?><span class="status-pill status-warning">Voice Mute</span><?php endif;?>
      <?php if(!empty($player['gagged'])):?><span class="status-pill status-warning">Text Gag</span><?php endif;?>
    </div>
  </div>
  <div class="player-live-stats player-live-stats-wide">
    <div><span>Team</span><strong><?=Security::e((string)($player['team']??'—'))?></strong></div>
    <div><span>Status</span><strong><?=!empty($player['alive'])?'Lebend':'Tot'?></strong></div>
    <div><span>HP</span><strong><?=Security::e((string)($player['health']??'—'))?></strong></div>
    <div><span>Armor</span><strong><?=Security::e((string)($player['armor']??'—'))?></strong></div>
    <div><span>Ping</span><strong><?=Security::e((string)($player['ping']??'—'))?> ms</strong></div>
    <div><span>UserID</span><strong><?=Security::e((string)($player['user_id']??'—'))?></strong></div>
    <div><span>Online</span><strong><?=Security::e((string)($player['connected_human']??'—'))?></strong></div>
    <div><span>IP</span><strong class="mono"><?=Security::e((string)($player['ip_address']??'—'))?></strong></div>
  </div>
</div>

<nav class="subnav" aria-label="Spieleradministration">
  <a href="#moderation">Moderation</a>
  <a href="#communication">Kommunikation</a>
  <a href="#status">Status</a>
  <a href="#movement">Team & Bewegung</a>
  <a href="#position">Position</a>
  <a href="#weapons">Waffen</a>
  <a href="#nickname">Nickname</a>
  <a href="#message">Nachricht</a>
</nav>

<div class="admin-section-grid">

<section class="panel" id="moderation">
  <div class="panel-header"><h2>Moderation</h2><p>Kick, Ban, Slay und Freeze. Die Funktionen entsprechen den relevanten T3-Admin-Aktionen.</p></div>
  <div class="form-stack">
    <?php if(Auth::can('live.kick')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form" data-confirm="<?=Security::e((string)$player['name'])?> wirklich kicken?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="kick">
      <label>Kick-Grund<input type="text" name="reason" value="Von der Serververwaltung entfernt." maxlength="160"></label>
      <button class="btn btn-danger" type="submit">Kicken</button>
    </form><?php endif;?>

    <?php if(Auth::can('live.ban')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form" data-confirm="Ban wirklich ausführen?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="ban">
      <label>Dauer<select name="minutes"><?php foreach($punishmentOptions as $minutes=>$label):?><option value="<?=$minutes?>" <?=$minutes===60?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select></label>
      <label>Grund<input type="text" name="reason" value="Serverban" maxlength="160"></label>
      <button class="btn btn-secondary" type="submit">Nur diesen Server bannen</button>
      <a class="btn btn-danger" href="/bans/new?steamid=<?=rawurlencode($steam)?>&name=<?=rawurlencode((string)$player['name'])?>">Global bannen</a>
    </form><?php endif;?>

    <?php if(Auth::can('live.slay')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" data-confirm="Spieler wirklich slayen?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="slay">
      <button class="btn btn-danger" type="submit">Slay</button>
    </form><?php endif;?>

    <?php if(Auth::can('live.freeze')):?><div class="form-stack compact-stack">
      <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="freeze"><input type="hidden" name="frozen" value="1">
        <label>Freeze-Dauer<select name="seconds"><option value="0" selected>Bis Unfreeze</option><option value="5">5 Sekunden</option><option value="10">10 Sekunden</option><option value="30">30 Sekunden</option><option value="60">1 Minute</option><option value="300">5 Minuten</option></select></label>
        <button class="btn btn-secondary">Freeze</button>
      </form>
      <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="freeze"><input type="hidden" name="frozen" value="0">
        <button class="btn btn-secondary">Unfreeze</button>
      </form>
    </div><?php endif;?>
  </div>
</section>

<section class="panel" id="communication">
  <div class="panel-header"><h2>Kommunikation</h2><p>T3-Semantik: Mute sperrt Voice, Gag sperrt Textchat, Silence sperrt beides.</p></div>
  <div class="form-stack">
    <?php if(Auth::can('live.mute')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form" data-confirm="Voicechat-Sperre wirklich ändern?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="mute">
      <label>Dauer<select name="minutes"><?php foreach($punishmentOptions as $minutes=>$label):?><option value="<?=$minutes?>" <?=$minutes===60?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select></label>
      <label>Grund<input type="text" name="reason" value="Voicechat gesperrt" maxlength="160"></label>
      <button class="btn btn-secondary">Mute</button>
    </form>
    <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="unmute"><button class="btn btn-secondary">Unmute</button></form><?php endif;?>

    <?php if(Auth::can('live.gag')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form" data-confirm="Textchat-Sperre wirklich ändern?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="gag">
      <label>Dauer<select name="minutes"><?php foreach($punishmentOptions as $minutes=>$label):?><option value="<?=$minutes?>" <?=$minutes===60?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select></label>
      <label>Grund<input type="text" name="reason" value="Textchat gesperrt" maxlength="160"></label>
      <button class="btn btn-secondary">Gag</button>
    </form>
    <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="ungag"><button class="btn btn-secondary">Ungag</button></form><?php endif;?>

    <?php if(Auth::can('live.silence')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form" data-confirm="Voice und Textchat wirklich gemeinsam sperren?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="silence">
      <label>Dauer<select name="minutes"><?php foreach($punishmentOptions as $minutes=>$label):?><option value="<?=$minutes?>" <?=$minutes===60?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select></label>
      <label>Grund<input type="text" name="reason" value="Kommunikation gesperrt" maxlength="160"></label>
      <button class="btn btn-danger">Silence</button>
    </form>
    <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="unsilence"><button class="btn btn-secondary">Unsilence</button></form><?php endif;?>
  </div>
  <?php if(!empty($player['voice_muted']) || !empty($player['gagged'])):?><div class="live-punishment-summary">
    <?php if(!empty($player['voice_muted'])):?><div><strong>Voice:</strong> <?=Security::e((string)($player['mute_reason']??'gesperrt'))?> · <?=empty($player['mute_expires_at'])?'Permanent':Security::e((string)$player['mute_expires_at'])?></div><?php endif;?>
    <?php if(!empty($player['gagged'])):?><div><strong>Text:</strong> <?=Security::e((string)($player['gag_reason']??'gesperrt'))?> · <?=empty($player['gag_expires_at'])?'Permanent':Security::e((string)$player['gag_expires_at'])?></div><?php endif;?>
  </div><?php endif;?>
</section>

<section class="panel" id="status">
  <div class="panel-header"><h2>Spielerstatus</h2><p>Respawn, Health und Armor.</p></div>
  <div class="form-stack">
    <?php if(Auth::can('live.respawn')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" data-confirm="Spieler wirklich respawnen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="respawn"><button class="btn btn-secondary">Respawnen</button></form><?php endif;?>
    <?php if(Auth::can('live.health')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="health"><label>Health<input type="number" name="value" min="1" max="500" value="100"></label><button class="btn btn-secondary">Health setzen</button></form><?php endif;?>
    <?php if(Auth::can('live.armor')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="armor"><label>Armor<input type="number" name="value" min="0" max="500" value="100"></label><button class="btn btn-secondary">Armor setzen</button></form><?php endif;?>
  </div>
</section>

<section class="panel" id="movement">
  <div class="panel-header"><h2>Team & Bewegung</h2><p>Teamzuordnung und Noclip.</p></div>
  <div class="form-stack">
    <?php if(Auth::can('live.team')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form" data-confirm="Teamzuordnung wirklich ändern?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="team"><label>Team<select name="team"><option value="ct">Counter-Terrorists</option><option value="t">Terrorists</option><option value="spectator">Zuschauer</option></select></label><button class="btn btn-primary">Team setzen</button></form><?php endif;?>
    <?php if(Auth::can('live.noclip')):?><div class="button-row">
      <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="noclip"><input type="hidden" name="enabled" value="1"><button class="btn btn-secondary">Noclip ein</button></form>
      <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="noclip"><input type="hidden" name="enabled" value="0"><button class="btn btn-secondary">Noclip aus</button></form>
    </div><?php endif;?>
    <?php if(Auth::can('live.godmode')):?><div class="button-row live-special-row">
      <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" data-confirm="Godmode für diesen Spieler aktivieren?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="godmode"><input type="hidden" name="enabled" value="1"><button class="btn btn-danger">Godmode ein</button></form>
      <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="godmode"><input type="hidden" name="enabled" value="0"><button class="btn btn-secondary">Godmode aus</button></form>
    </div><?php endif;?>
    <?php if(Auth::can('live.speed')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="player_speed">
      <label>Speed<select name="value"><option value="0.5">0,50x</option><option value="0.75">0,75x</option><option value="1" selected>1,00x</option><option value="1.25">1,25x</option><option value="1.5">1,50x</option><option value="2">2,00x</option><option value="3">3,00x</option></select></label><button class="btn btn-secondary">Speed setzen</button>
    </form><?php endif;?>
    <?php if(Auth::can('live.gravity')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="player_gravity">
      <label>Eigene Gravity<select name="value"><option value="0.25">0,25x</option><option value="0.5">0,50x</option><option value="0.75">0,75x</option><option value="1" selected>1,00x</option><option value="1.25">1,25x</option><option value="1.5">1,50x</option><option value="2">2,00x</option></select></label><button class="btn btn-secondary">Gravity setzen</button>
    </form><?php endif;?>
  </div>
</section>

<section class="panel" id="position">
  <div class="panel-header">
    <h2>Position / Teleport</h2>
    <p>Quelle und Ziel können frei aus allen aktuell verbundenen menschlichen Spielern gewählt werden. Ist dein verifiziertes Steam-Konto auf diesem Server online, wird es automatisch als Quelle vorausgewählt.</p>
  </div>

  <?php if(Auth::can('live.teleport') && count($teleportPlayers??[]) >= 2):?>
  <?php
    $defaultSource=(string)($ownSteamId??'');
    if($defaultSource==='') $defaultSource=$steam;
    $defaultDestination=$steam!==$defaultSource?$steam:'';
    if($defaultDestination===''){
      foreach(($teleportPlayers??[]) as $tp){
        if((string)($tp['steam_id64']??'')!==$defaultSource){$defaultDestination=(string)$tp['steam_id64'];break;}
      }
    }
  ?>
  <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="form-stack" data-confirm="Ausgewählten Spieler wirklich zur Zielposition teleportieren?">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <input type="hidden" name="action" value="teleport_player">
    <div class="form-grid">
      <label>Quelle – dieser Spieler wird bewegt
        <select name="source_steamid" required>
          <?php foreach(($teleportPlayers??[]) as $tp): $ts=(string)$tp['steam_id64']; ?>
          <option value="<?=Security::e($ts)?>" <?=$ts===$defaultSource?'selected':''?>><?=Security::e((string)$tp['name'])?> · <?=Security::e(ucfirst((string)($tp['community_role']??'spieler')))?> · <?=Security::e((string)($tp['team']??'—'))?></option>
          <?php endforeach;?>
        </select>
      </label>
      <label>Ziel – Position dieses Spielers
        <select name="destination_steamid" required>
          <?php foreach(($teleportPlayers??[]) as $tp): $ts=(string)$tp['steam_id64']; ?>
          <option value="<?=Security::e($ts)?>" <?=$ts===$defaultDestination?'selected':''?>><?=Security::e((string)$tp['name'])?> · <?=Security::e(ucfirst((string)($tp['community_role']??'spieler')))?> · <?=Security::e((string)($tp['team']??'—'))?></option>
          <?php endforeach;?>
        </select>
      </label>
    </div>
    <?php if(!empty($ownSteamId)):?><div class="status-pill status-info">Eigener Steam-Spieler ist online und als Quelle vorausgewählt.</div><?php endif;?>
    <button class="btn btn-primary">Quelle zum Ziel teleportieren</button>
  </form>
  <?php elseif(Auth::can('live.teleport')):?>
    <p class="muted">Für einen Teleport müssen mindestens zwei menschliche Spieler auf diesem Gameserver online sein.</p>
  <?php else:?><p class="muted">Keine Berechtigung.</p><?php endif;?>
</section>

<section class="panel" id="weapons">
  <div class="panel-header"><h2>Waffen</h2><p>Waffe aus einer festen, serverseitig erlaubten Liste geben oder Inventar entfernen.</p></div>
  <?php if(Auth::can('live.weapons')):?><div class="form-stack">
    <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="give_weapon"><label>Waffe<select name="weapon"><?php foreach($weapons as $category=>$items):?><optgroup label="<?=Security::e($category)?>"><?php foreach($items as $key=>$label):?><option value="<?=Security::e($key)?>"><?=Security::e($label)?></option><?php endforeach;?></optgroup><?php endforeach;?></select></label><button class="btn btn-primary">Waffe geben</button></form>
    <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" data-confirm="Alle Waffen dieses Spielers entfernen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="remove_weapons"><button class="btn btn-secondary">Alle Waffen entfernen</button></form>
  </div><?php else:?><p class="muted">Keine Berechtigung.</p><?php endif;?>
</section>

<section class="panel" id="nickname">
  <div class="panel-header">
    <h2>Server-Nickname</h2>
    <p>Ändert ausschließlich den Namen auf diesem Gameserver. Das Steam-Profil selbst wird niemals verändert.</p>
    <dl class="definition-list compact-definition">
      <dt>Aktuell auf Server</dt><dd><?=Security::e((string)($player['name']??'—'))?></dd>
      <dt>Steam-Profilname</dt><dd><?=Security::e((string)($person['steam_personaname']??$player['original_name']??'Noch nicht synchronisiert'))?></dd>
      <dt>Aktiver Override</dt><dd><?=Security::e((string)($player['nickname_override']??'kein Override'))?></dd>
    </dl>
  </div>

  <?php if(Auth::can('live.nickname')):?>
  <div class="form-stack">
    <?php if(!empty($nicknameChoices)):?>
    <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form" data-confirm="Ausgewählten gespeicherten Namen wirklich auf diesem Server setzen?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <input type="hidden" name="action" value="nickname_set">
      <label>Gespeicherte Namen
        <select name="nickname" required>
          <?php foreach($nicknameChoices as $choice):?>
          <option value="<?=Security::e((string)$choice['name'])?>"><?=Security::e((string)$choice['name'])?> · <?=Security::e((string)$choice['source'])?></option>
          <?php endforeach;?>
        </select>
      </label>
      <button class="btn btn-primary">Ausgewählten Namen setzen</button>
    </form>
    <?php endif;?>

    <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" class="inline-action-form" data-confirm="Neuen Server-Nickname wirklich setzen?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <input type="hidden" name="action" value="nickname_set">
      <label>Neuer Server-Nickname<input type="text" name="nickname" maxlength="48" placeholder="Neuer Server-Nickname" required></label>
      <button class="btn btn-primary">Neuen Nickname setzen</button>
    </form>

    <form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/action" data-confirm="Servernamen wirklich auf den aktuell gespeicherten Steam-Profilnamen setzen?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <input type="hidden" name="action" value="nickname_restore">
      <button class="btn btn-secondary" <?=empty($person['steam_personaname'])?'disabled':''?>>Servername auf Steam-Profilnamen setzen</button>
    </form>

    <p class="small-muted">Vor jedem Wechsel wird der vorher sichtbare Servername gespeichert. Das Dropdown kombiniert Server-Namenshistorie und die aus Steam synchronisierte Namenshistorie.</p>
  </div>
  <?php else:?><p class="muted">Keine Berechtigung.</p><?php endif;?>
</section>

<section class="panel" id="message">
  <div class="panel-header"><h2>Persönliche Nachricht</h2><p>Nur dieser Spieler erhält die Nachricht.</p></div>
  <?php if(Auth::can('live.message')):?><form method="post" action="/servers/<?=$sid?>/live/player/<?=rawurlencode($steam)?>/message" class="form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><label>Darstellung<select name="type"><option value="chat" selected>Chat</option><option value="center">Bildschirmmitte</option><option value="alert">Alert</option><option value="notify">Notify</option></select></label><label>Nachricht<textarea name="message" rows="4" maxlength="500" required></textarea></label><button class="btn btn-primary">Persönliche Nachricht senden</button></form><?php else:?><p class="muted">Keine Berechtigung.</p><?php endif;?>
</section>

</div>
