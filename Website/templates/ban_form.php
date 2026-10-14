<?php
use App\Core\Security;
$hasAdvertisingEvidence = !empty($chat) || !empty($advertisingEvent);
$selectedReason = $hasAdvertisingEvidence ? 'advertising' : 'server_rules';
$selectedMinutes = $hasAdvertisingEvidence ? 43200 : 1440;
if(empty($canTemporary) && !empty($canPermanent)) $selectedMinutes=0;
?>
<section class="panel ban-create-panel">
  <div class="panel-header split-header">
    <div>
      <h2>Globalen Ban erstellen</h2>
      <p>Der Ban wird zentral gespeichert und über Fast Live auf allen verbundenen Gameservern durchgesetzt.</p>
    </div>
    <a class="btn btn-secondary" href="/bans">Zur Banverwaltung</a>
  </div>

  <?php if(!empty($chat)):?>
    <div class="ban-evidence-card">
      <div class="ban-evidence-head">
        <strong>Chatnachricht als Beleg</strong>
        <span><?=Security::e((string)($chat['server_name']??('Server #'.(int)$chat['server_id'])) )?> · <?=Security::e(date('d.m.Y H:i:s',strtotime((string)$chat['sent_at'])?:time()))?></span>
      </div>
      <div class="ban-evidence-player"><?=Security::e((string)$chat['player_name'])?> <span class="mono"><?=Security::e((string)($chat['steam_id64']??''))?></span></div>
      <blockquote><?=nl2br(Security::e((string)$chat['message']))?></blockquote>
    </div>
  <?php endif;?>

  <?php if(!empty($advertisingEvent)):?>
    <div class="ban-evidence-card">
      <div class="ban-evidence-head">
        <strong>Werbeschutz-Ereignis #<?=(int)$advertisingEvent['id']?></strong>
        <span><?=Security::e((string)($advertisingEvent['server_name']??('Server #'.(int)$advertisingEvent['server_id'])))?> · <?=Security::e(date('d.m.Y H:i:s',strtotime((string)$advertisingEvent['observed_at'])?:time()))?></span>
      </div>
      <div class="ban-evidence-player"><?=Security::e((string)$advertisingEvent['player_name'])?> <span class="mono"><?=Security::e((string)($advertisingEvent['steam_id64']??''))?></span></div>
      <div><strong>Treffer:</strong> <?=Security::e((string)($advertisingEvent['match_value']??'Werbung / Spam'))?></div>
      <?php if(!empty($advertisingEvent['message_excerpt'])):?><blockquote><?=nl2br(Security::e((string)$advertisingEvent['message_excerpt']))?></blockquote><?php endif;?>
    </div>
  <?php endif;?>

  <form method="post" action="/bans" class="ban-form-grid" data-confirm="Globalen Ban wirklich auf allen Servern aktivieren?">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <input type="hidden" name="_return" value="<?=Security::e((string)($returnTo??'/bans'))?>">
    <?php if(!empty($chat)):?><input type="hidden" name="chat_message_id" value="<?=(int)$chat['id']?>"><?php endif;?>
    <?php if(!empty($advertisingEvent)):?><input type="hidden" name="advertising_event_id" value="<?=(int)$advertisingEvent['id']?>"><?php endif;?>

    <label>SteamID64
      <input type="text" name="steam_id64" value="<?=Security::e((string)$steamId64)?>" pattern="7656119[0-9]{10}" maxlength="17" required>
    </label>
    <label>Spielername
      <input type="text" name="player_name" value="<?=Security::e((string)$playerName)?>" maxlength="80" required>
    </label>

    <label>Bangrund
      <select name="reason_code" required>
        <?php foreach($reasons as $code=>$label):?>
          <option value="<?=Security::e($code)?>" <?=$code===$selectedReason?'selected':''?>><?=Security::e($label)?></option>
        <?php endforeach;?>
      </select>
    </label>

    <label>Dauer
      <select name="minutes" data-ban-duration-select>
        <?php foreach($durations as $minutes=>$label):$minutes=(int)$minutes;if($minutes===0&&!$canPermanent)continue;if($minutes!==0&&!$canTemporary)continue;?>
          <option value="<?=$minutes?>" <?=$minutes===$selectedMinutes?'selected':''?>><?=Security::e($label)?></option>
        <?php endforeach;?>
      </select>
    </label>

    <label class="ban-custom-duration">Benutzerdefiniert in Minuten
      <input type="number" name="custom_minutes" min="1" max="5256000" value="60">
    </label>

    <label class="ban-detail-field">Details / konkrete Begründung
      <textarea name="reason_detail" maxlength="500" rows="4" placeholder="Optional: genaue Beschreibung des Verstoßes."><?php if($hasAdvertisingEvidence):?>Werbung / Spam; Beleg ist mit der Moderationshistorie verknüpft.<?php endif;?></textarea>
    </label>

    <div class="ban-form-hint">
      <strong>Wirkung:</strong> global auf allen Servern. Bereits verbundene Spieler werden beim nächsten Fast-Live-Abgleich entfernt; neue Verbindungsversuche erhalten Grund, Ban-ID und Ablaufzeit.
    </div>

    <div class="ban-form-actions">
      <button class="btn btn-danger" type="submit">Global bannen</button>
      <a class="btn btn-secondary" href="/bans">Abbrechen</a>
    </div>
  </form>
</section>
