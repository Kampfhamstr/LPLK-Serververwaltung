<?php use App\Core\Security; use App\Core\Auth; ?>
<section class="panel global-ban-head">
  <div class="panel-header split-header">
    <div>
      <h2>Globale Banverwaltung</h2>
      <p>Zentrale Banliste für alle Gameserver mit Grund, Dauer, Historie und optionalem Chat-Beleg.</p>
    </div>
    <div class="toolbar">
      <span class="status-pill status-danger"><?=number_format((int)$activeCount,0,',','.')?> aktiv</span>
      <?php if(!empty($canTemporary)||!empty($canPermanent)):?><a class="btn btn-danger" href="/bans/new">Neuer globaler Ban</a><?php endif;?>
    </div>
  </div>

  <div class="ban-reason-legend">
    <?php foreach($reasons as $code=>$label):?><span><?=Security::e($label)?></span><?php endforeach;?>
  </div>

  <form method="get" action="/bans" class="ban-filter-grid">
    <label>Suche
      <input type="search" name="q" value="<?=Security::e((string)$filters['q'])?>" placeholder="Name, SteamID64, Ban-ID, Grund">
    </label>
    <label>Status
      <select name="status">
        <option value="active" <?=$filters['status']==='active'?'selected':''?>>Aktive Bans</option>
        <option value="history" <?=$filters['status']==='history'?'selected':''?>>Historie / Entbannt / Abgelaufen</option>
        <option value="all" <?=$filters['status']==='all'?'selected':''?>>Alle</option>
      </select>
    </label>
    <label>Grund
      <select name="reason"><option value="">Alle Gründe</option><?php foreach($reasons as $code=>$label):?><option value="<?=Security::e($code)?>" <?=$filters['reason']===$code?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select>
    </label>
    <button class="btn btn-primary" type="submit">Filtern</button>
    <a class="btn btn-secondary" href="/bans">Zurücksetzen</a>
  </form>
</section>

<section class="global-ban-grid">
  <?php if(empty($bans)):?><div class="panel"><div class="muted">Keine Bans für diesen Filter gefunden.</div></div><?php endif;?>
  <?php foreach($bans as $ban):?>
    <?php
      $active=!empty($ban['active']);
      $permanent=!empty($ban['permanent']);
      $expires=$ban['expires_at']?strtotime((string)$ban['expires_at']):false;
      $remaining=$active&&$expires?max(0,$expires-time()):null;
      $days=$remaining!==null?intdiv($remaining,86400):0;
      $hours=$remaining!==null?intdiv($remaining%86400,3600):0;
    ?>
    <article class="panel global-ban-card <?=$active?'is-active':'is-history'?>">
      <div class="global-ban-card-head">
        <div>
          <div class="global-ban-id">Ban #<?=(int)$ban['id']?></div>
          <h3><?=Security::e((string)$ban['player_name'])?></h3>
          <div class="mono small-muted"><?=Security::e((string)$ban['steam_id64'])?></div>
          <?php if(!empty($ban['steam_id64']) && (Auth::can('live.kick')||Auth::can('moderation.ban.temporary')||Auth::can('moderation.ban.permanent')||Auth::can('moderation.unban')||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$ban['steam_id64'])?>" data-player-name="<?=Security::e((string)$ban['player_name'])?>">Spieleraktionen</button><?php endif;?>
        </div>
        <span class="status-pill <?=$active?'status-danger':'status-neutral'?>"><?=$active?'GLOBAL GEBANNT':'HISTORIE'?></span>
      </div>

      <div class="global-ban-facts">
        <div><span>Grund</span><strong><?=Security::e((string)$ban['reason_label'])?></strong></div>
        <div><span>Dauer</span><strong><?=$permanent?'Permanent':($ban['expires_at']?Security::e(date('d.m.Y H:i',strtotime((string)$ban['expires_at'])?:time())):'—')?></strong></div>
        <div><span>Von</span><strong class="ban-actor-identity"><span><?=Security::e((string)($ban['created_by_name']??'System'))?></span><?php if(!empty($ban['created_by_steam_id64'])):?><small>Steam: <?=Security::e((string)$ban['created_by_steam_id64'])?><?php if(!empty($ban['created_by_panel_name']) && (string)$ban['created_by_panel_name']!==(string)($ban['created_by_name']??'')):?> · Panel: <?=Security::e((string)$ban['created_by_panel_name'])?><?php endif;?></small><?php endif;?></strong></div>
        <div><span>Seit</span><strong><?=Security::e(date('d.m.Y H:i',strtotime((string)$ban['starts_at'])?:time()))?></strong></div>
      </div>

      <?php if($active && !$permanent && $remaining!==null):?><div class="ban-remaining">Verbleibend: <?=$days?> Tag(e), <?=$hours?> Std.</div><?php endif;?>
      <?php if(trim((string)($ban['reason_detail']??''))!==''):?><div class="global-ban-detail"><?=nl2br(Security::e((string)$ban['reason_detail']))?></div><?php endif;?>

      <?php if(!empty($ban['evidence_message'])):?>
        <details class="ban-evidence-details">
          <summary>Chat-Beleg anzeigen</summary>
          <div><strong><?=Security::e((string)($ban['evidence_player_name']??$ban['player_name']))?></strong> · <?=Security::e(date('d.m.Y H:i:s',strtotime((string)$ban['evidence_sent_at'])?:time()))?></div>
          <blockquote><?=nl2br(Security::e((string)$ban['evidence_message']))?></blockquote>
        </details>
      <?php endif;?>

      <?php if($active):?>
        <?php if(!empty($canTemporary)||!empty($canPermanent)):?><details class="ban-edit-details">
          <summary>Ban bearbeiten</summary>
          <form method="post" action="/bans/<?=(int)$ban['id']?>/update" class="form-stack" data-confirm="Globalen Ban wirklich ändern?">
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
            <label>Grund<select name="reason_code"><?php foreach($reasons as $code=>$label):?><option value="<?=Security::e($code)?>" <?=$ban['reason_code']===$code?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select></label>
            <label>Details<textarea name="reason_detail" maxlength="500" rows="3"><?=Security::e((string)($ban['reason_detail']??''))?></textarea></label>
            <label>Neue Dauer ab jetzt<select name="minutes"><?php foreach($durations as $minutes=>$label):$minutes=(int)$minutes;if($minutes===0&&!$canPermanent)continue;if($minutes!==0&&!$canTemporary)continue;?><option value="<?=$minutes?>"><?=Security::e($label)?></option><?php endforeach;?></select></label>
            <label>Benutzerdefiniert in Minuten<input type="number" name="custom_minutes" min="1" max="5256000" value="60"></label>
            <button class="btn btn-secondary" type="submit">Ban aktualisieren</button>
          </form>
        </details><?php endif;?>
        <?php if(!empty($canUnban)):?><form method="post" action="/bans/<?=(int)$ban['id']?>/unban" class="global-unban-form" data-confirm="Diesen Spieler wirklich auf allen Servern entbannen?">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
          <input type="text" name="unban_reason" maxlength="300" placeholder="Entbannungsgrund" required>
          <button class="btn btn-danger" type="submit">Global entbannen</button>
        </form><?php endif;?>
      <?php else:?>
        <div class="ban-history-note">
          <?php if(!empty($ban['unbanned_at'])):?>Entbannt/abgelaufen: <?=Security::e(date('d.m.Y H:i',strtotime((string)$ban['unbanned_at'])?:time()))?><?php endif;?>
          <?php if(!empty($ban['unbanned_by_name'])):?> · von <?=Security::e((string)$ban['unbanned_by_name'])?><?php endif;?>
          <?php if(!empty($ban['unban_reason'])):?><br>Grund: <?=Security::e((string)$ban['unban_reason'])?><?php endif;?>
        </div>
      <?php endif;?>
    </article>
  <?php endforeach;?>
</section>
