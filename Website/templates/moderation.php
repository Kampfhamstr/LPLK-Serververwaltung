<?php
use App\Core\Auth;
use App\Core\Security;

$moderationData = is_array($moderationData ?? null) ? $moderationData : [];
$warnings = is_array($moderationData['warnings'] ?? null) ? $moderationData['warnings'] : [];
$sanctions = is_array($moderationData['sanctions'] ?? null) ? $moderationData['sanctions'] : [];
$appeals = is_array($moderationData['appeals'] ?? null) ? $moderationData['appeals'] : [];
$notes = is_array($moderationData['notes'] ?? null) ? $moderationData['notes'] : [];
$evidence = is_array($moderationData['evidence'] ?? null) ? $moderationData['evidence'] : [];
$bans = is_array($moderationData['bans'] ?? null) ? $moderationData['bans'] : [];
$advertisingCases = is_array($moderationData['advertising_cases'] ?? null) ? $moderationData['advertising_cases'] : [];
$advertisingCounts = is_array($moderationData['advertising_counts'] ?? null) ? $moderationData['advertising_counts'] : [];
$advertisingRules = is_array($moderationData['advertising_rules'] ?? null) ? $moderationData['advertising_rules'] : [];
$advertisingRuleGroups = [
  'blocked_domains'=>['title'=>'Blockierte Domains','description'=>'Diese Domains werden direkt und serverübergreifend unterdrückt.','empty'=>'Noch keine zusätzlichen Domains.'],
  'blocked_phrases'=>['title'=>'Direkt blockierte Phrasen','description'=>'Jeder Text, der eine aktive Phrase enthält, wird vollständig unterdrückt.','empty'=>'Noch keine eigenen Phrasen.'],
  'allowed_domains'=>['title'=>'Erlaubte Domains','description'=>'Ausnahmen für die allgemeine Link- und Werbeerkennung. Explizit blockierte Domains haben weiterhin Vorrang.','empty'=>'Keine zusätzlichen Ausnahmen.'],
  'marketing_indicators'=>['title'=>'System-Erkennungsphrasen','description'=>'Feste Indikatoren, die zusammen mit verdächtigen Links oder Domains Werbung erkennen.','empty'=>'Keine Systemphrasen.'],
];
$activeAdvertisingRules=0;
foreach($advertisingRules as $ruleRows)if(is_array($ruleRows))$activeAdvertisingRules+=count(array_filter($ruleRows,static fn(array $row):bool=>!empty($row['active'])));
$activeWarnings = count(array_filter($warnings, static fn(array $row): bool => empty($row['resolved_at'])));
$activeSanctions = count(array_filter($sanctions, static fn(array $row): bool => !empty($row['active'])));
$openAppeals = count(array_filter($appeals, static fn(array $row): bool => in_array((string)($row['status'] ?? ''), ['open','reviewing'], true)));
$activeBans = count(array_filter($bans, static fn(array $row): bool => !empty($row['active'])));
$pendingAdvertising = (int)($advertisingCounts['reported'] ?? 0);
$advertisingLabels = ['detected'=>'Automatisch erkannt','reported'=>'Prüfung offen','protected'=>'Werbeschutz aktiv','banned'=>'Global gebannt','unbanned'=>'Entbannt','false_positive'=>'Fehlalarm','resolved'=>'Abgeschlossen'];
?>
<div class="moderation-page">
  <div class="page-title-row moderation-title-row">
    <div>
      <h1>Moderation</h1>
      <p>Werbe-/Spamfälle, globale Bans und Entbannungen, Verwarnungen, Kommunikationssperren, Beweise und Appeals zentral verwalten.</p>
    </div>
  </div>

  <form method="get" class="panel moderation-toolbar">
    <label class="moderation-search-field">
      <span>Spieler / SteamID / Grund / Text</span>
      <input type="search" name="q" value="<?=Security::e($q ?? '')?>" placeholder="Spieler, SteamID, Grund oder Inhalt suchen …" autocomplete="off">
    </label>
    <label class="moderation-status-field">Werbeschutz-Status
      <select name="advertising_status">
        <option value="">Alle Status</option>
        <?php foreach($advertisingLabels as $statusCode=>$statusLabel):?><option value="<?=Security::e($statusCode)?>" <?=($advertisingStatus??'')===$statusCode?'selected':''?>><?=Security::e($statusLabel)?></option><?php endforeach;?>
      </select>
    </label>
    <div class="button-row moderation-search-actions">
      <button class="btn btn-primary">Suchen</button>
      <a class="btn btn-secondary" href="/moderation">Zurücksetzen</a>
    </div>
  </form>

  <div class="moderation-summary-grid">
    <article class="moderation-summary-card <?= $pendingAdvertising > 0 ? 'state-warning' : 'state-neutral' ?>">
      <span class="eyebrow">Werbemeldungen offen</span><strong><?=$pendingAdvertising?></strong><small>manuell gemeldet, noch ungeprüft</small>
    </article>
    <article class="moderation-summary-card <?= $activeBans > 0 ? 'state-danger' : 'state-neutral' ?>">
      <span class="eyebrow">Globale Bans</span><strong><?=$activeBans?></strong><small><?=count($bans)?> Treffer inkl. Historie</small>
    </article>
    <article class="moderation-summary-card <?= $activeWarnings > 0 ? 'state-warning' : 'state-neutral' ?>">
      <span class="eyebrow">Aktive Verwarnungen</span><strong><?=$activeWarnings?></strong><small><?=count($warnings)?> Treffer geladen</small>
    </article>
    <article class="moderation-summary-card <?= $activeSanctions > 0 ? 'state-danger' : 'state-neutral' ?>">
      <span class="eyebrow">Mutes / Gags / Silence</span><strong><?=$activeSanctions?></strong><small>aktive globale Sperren</small>
    </article>
    <article class="moderation-summary-card <?= $openAppeals > 0 ? 'state-info' : 'state-neutral' ?>">
      <span class="eyebrow">Offene Appeals</span><strong><?=$openAppeals?></strong><small>offen oder in Prüfung</small>
    </article>
    <article class="moderation-summary-card state-neutral">
      <span class="eyebrow">Interne Notizen</span><strong><?=count($notes)?></strong><small>letzte Treffer</small>
    </article>
    <article class="moderation-summary-card state-neutral">
      <span class="eyebrow">Beweise</span><strong><?=count($evidence)?></strong><small>gesicherte Treffer</small>
    </article>
  </div>

  <section class="panel moderation-section" id="werbeschutz-regeln">
    <div class="panel-header moderation-section-head"><div><h2>Globale Werbeschutzregeln</h2><p>Hier siehst du, was AntiAdvertisement blockiert, direkt zensiert oder ausdrücklich erlaubt. Aktive Änderungen werden beim nächsten Fast-Live-Abruf an alle verbundenen Gameserver verteilt.</p></div><span class="status-pill status-info"><?=$activeAdvertisingRules?> aktiv</span></div>
    <?php if(Auth::can('moderation.advertising.rules')):?>
    <form method="post" action="/moderation/advertising-rules" class="advertising-rule-create-form">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="/moderation#werbeschutz-regeln">
      <label>Regelart<select name="rule_type" required><option value="blocked_domain">Domain blockieren</option><option value="blocked_phrase">Phrase direkt blockieren</option><option value="allowed_domain">Domain erlauben</option></select></label>
      <label>Domain oder Phrase<input name="rule_value" maxlength="253" required placeholder="beispiel.xyz oder claim free cases"></label>
      <button class="btn btn-primary">Regel hinzufügen</button>
    </form>
    <?php endif;?>
    <div class="advertising-rule-grid">
      <?php foreach($advertisingRuleGroups as $groupKey=>$group):?>
      <?php $ruleRows=is_array($advertisingRules[$groupKey]??null)?$advertisingRules[$groupKey]:[];?>
      <article class="advertising-rule-group">
        <div class="advertising-rule-group-head"><div><h3><?=Security::e($group['title'])?></h3><p><?=Security::e($group['description'])?></p></div><span><?=count($ruleRows)?></span></div>
        <ul class="advertising-rule-list">
          <?php foreach($ruleRows as $rule):?>
          <?php $isActive=!empty($rule['active']);$isFixed=!empty($rule['fixed']);$source=(string)($rule['source']??'manual');$sourceLabel=$source==='system'?'Systemregel':($source==='learned'?'Automatisch gelernt':'Manuell');?>
          <li class="<?=$isActive?'is-active':'is-inactive'?>">
            <div><strong><?=Security::e((string)($rule['rule_value']??''))?></strong><small><?=Security::e($sourceLabel)?><?php if(!empty($rule['hit_count'])):?> · <?=(int)$rule['hit_count']?> Treffer<?php endif;?></small></div>
            <span class="status-pill <?=$isActive?'status-online':'status-neutral'?>"><?=$isActive?'AKTIV':'INAKTIV'?></span>
            <?php if(!$isFixed && Auth::can('moderation.advertising.rules')):?>
            <form method="post" action="/moderation/advertising-rules/toggle" data-confirm="Diese Werbeschutzregel wirklich <?=$isActive?'deaktivieren':'aktivieren'?>?">
              <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="/moderation#werbeschutz-regeln">
              <input type="hidden" name="rule_type" value="<?=Security::e((string)$rule['rule_type'])?>"><input type="hidden" name="rule_value" value="<?=Security::e((string)$rule['rule_value'])?>"><input type="hidden" name="active" value="<?=$isActive?'0':'1'?>">
              <button class="btn btn-secondary btn-small"><?=$isActive?'Deaktivieren':'Aktivieren'?></button>
            </form>
            <?php elseif($isFixed):?><span class="advertising-rule-fixed">fest geschützt</span><?php endif;?>
          </li>
          <?php endforeach;?>
          <?php if(!$ruleRows):?><li class="advertising-rule-empty"><?=Security::e($group['empty'])?></li><?php endif;?>
        </ul>
      </article>
      <?php endforeach;?>
    </div>
  </section>

  <section class="panel moderation-section" id="werbeschutz">
    <div class="panel-header moderation-section-head"><div><h2>Werbeschutz-Fälle</h2><p>Pro SteamID genau eine aktuelle Moderationskarte. Erkennungen, Prüfungen sowie Ban- und Entbannungsaktionen stehen gemeinsam in der Chronik.</p></div><span class="status-pill <?= $pendingAdvertising > 0 ? 'status-warning' : 'status-neutral' ?>"><?=$pendingAdvertising?> offen</span></div>
    <div class="moderation-card-grid">
      <?php foreach($advertisingCases as $event):?>
      <?php
        $eventStatus=(string)($event['case_status']??($event['review_status']??'detected'));
        $activeBanId=(int)($event['active_ban_id']??0);
        $eventStatusLabel=$advertisingLabels[$eventStatus]??$eventStatus;
        $eventClass=in_array($eventStatus,['reported'],true)?'is-active':(in_array($eventStatus,['banned'],true)?'is-danger':(in_array($eventStatus,['detected','protected'],true)?'is-info':''));
        $eventPill=in_array($eventStatus,['banned'],true)?'status-danger':(in_array($eventStatus,['reported'],true)?'status-warning':(in_array($eventStatus,['detected','protected'],true)?'status-info':'status-neutral'));
        $recentActions=is_array($event['recent_actions']??null)?$event['recent_actions']:[];
      ?>
      <article class="moderation-card advertising-event-card <?=$eventClass?>">
        <div class="moderation-card-head"><div><strong><?=Security::e($event['player_name'] ?: ($event['steam_id64']??'Unbekannt'))?></strong><div class="mono"><?=Security::e((string)($event['steam_id64']??'ohne SteamID'))?></div></div><span class="status-pill <?=$eventPill?>"><?=Security::e($eventStatusLabel)?></span></div>
        <div class="moderation-card-reason"><?=Security::e((string)($event['match_value']??'Werbung / Spam'))?></div>
        <?php if(!empty($event['message_excerpt'])):?><blockquote class="advertising-message"><?=nl2br(Security::e((string)$event['message_excerpt']))?></blockquote><?php endif;?>
        <div class="advertising-event-facts">
          <span><?=(int)($event['event_count']??1)?> Treffer</span>
          <span><?=(int)($event['server_count']??1)?> Server</span>
          <span><?=Security::e((string)($event['server_name']??('Server #'.(int)$event['server_id'])))?></span>
          <span>zuletzt <?=Security::e(date('d.m.Y H:i:s',strtotime((string)$event['observed_at'])?:time()))?></span>
        </div>
        <?php if($activeBanId>0):?><div class="ban-history-note advertising-current-ban"><strong>Aktiver Global-Ban #<?=$activeBanId?></strong><?php if(!empty($event['active_ban_reason_label'])):?> · <?=Security::e((string)$event['active_ban_reason_label'])?><?php endif;?><?php if(!empty($event['active_ban_starts_at'])):?><br>Seit <?=Security::e(date('d.m.Y H:i',strtotime((string)$event['active_ban_starts_at'])?:time()))?><?php endif;?><?php if(!empty($event['active_ban_created_by_name'])):?> · von <?=Security::e((string)$event['active_ban_created_by_name'])?><?php endif;?></div><?php endif;?>
        <?php if($recentActions):?>
        <div class="advertising-action-history">
          <div class="advertising-action-history-head"><strong>Letzte Aktionen</strong><span><?=count($recentActions)?> von <?=(int)($event['event_count']??1)?>+ Einträgen</span></div>
          <ol>
            <?php foreach($recentActions as $action):?>
            <li class="advertising-action-<?=Security::e((string)($action['kind']??'event'))?>">
              <time><?=Security::e(date('d.m.Y H:i:s',strtotime((string)($action['at']??''))?:time()))?></time>
              <div><strong><?=Security::e((string)($action['label']??'Aktion'))?></strong><?php if(!empty($action['detail'])):?><small><?=Security::e((string)$action['detail'])?></small><?php endif;?><?php if(!empty($action['server_name'])):?><small><?=Security::e((string)$action['server_name'])?></small><?php endif;?></div>
            </li>
            <?php endforeach;?>
          </ol>
        </div>
        <?php endif;?>
        <div class="advertising-event-actions">
          <?php if($activeBanId>0 && Auth::can('moderation.unban')):?><form method="post" action="/bans/<?=$activeBanId?>/unban" class="advertising-unban-form" data-confirm="Diese SteamID wirklich auf allen Servern entbannen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="/moderation"><input type="text" name="unban_reason" maxlength="300" required placeholder="Entbannungsgrund"><button class="btn btn-danger btn-small">Global entbannen</button></form><?php endif;?>
          <?php if($activeBanId===0 && !empty($event['steam_id64']) && Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent']) && $eventStatus!=='false_positive'):?><a class="btn btn-danger btn-small" href="/bans/new?advertising_event_id=<?=(int)$event['id']?>&amp;_return=%2Fmoderation">Global bannen</a><?php endif;?>
          <?php if($activeBanId===0 && !empty($event['steam_id64']) && Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent']) && in_array($eventStatus,['reported','resolved','unbanned'],true)):?><form method="post" action="/moderation/advertising/<?=(int)$event['id']?>/review" data-confirm="Diese SteamID serverübergreifend in den Werbeschutz übernehmen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="status" value="protected"><input type="hidden" name="note" value="Durch Moderation bestätigt"><input type="hidden" name="_return" value="/moderation"><button class="btn btn-secondary btn-small">Werbeschutz aktivieren</button></form><?php endif;?>
          <?php if($activeBanId===0 && Auth::can('moderation.evidence') && !in_array($eventStatus,['false_positive','unbanned'],true)):?><form method="post" action="/moderation/advertising/<?=(int)$event['id']?>/review" data-confirm="Den aktuellsten Treffer dieser SteamID wirklich als Fehlalarm markieren?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="status" value="false_positive"><input type="hidden" name="note" value="Von Moderation als Fehlalarm bewertet"><input type="hidden" name="_return" value="/moderation"><button class="btn btn-secondary btn-small">Fehlalarm</button></form><?php endif;?>
          <?php if($activeBanId===0 && Auth::can('moderation.evidence') && in_array($eventStatus,['reported','detected','protected'],true)):?><form method="post" action="/moderation/advertising/<?=(int)$event['id']?>/review"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="status" value="resolved"><input type="hidden" name="note" value="Prüfung abgeschlossen"><input type="hidden" name="_return" value="/moderation"><button class="btn btn-secondary btn-small">Abschließen</button></form><?php endif;?>
        </div>
      </article>
      <?php endforeach;?>
      <?php if(!$advertisingCases):?><div class="moderation-empty">Keine Werbeschutz-Fälle für diesen Filter gefunden.</div><?php endif;?>
    </div>
  </section>

  <section class="panel moderation-section" id="ban-historie">
    <div class="panel-header moderation-section-head"><div><h2>Ban- und Entbannungshistorie</h2><p>Globale Bans bleiben nach Entbannung oder Ablauf mit Verantwortlichem, Zeitpunkt und Grund erhalten.</p></div><div class="button-row"><span class="status-pill <?= $activeBans > 0 ? 'status-danger' : 'status-neutral' ?>"><?=$activeBans?> aktiv</span><a class="btn btn-secondary btn-small" href="/bans?status=all">Vollständige Banverwaltung</a></div></div>
    <div class="moderation-card-grid">
      <?php foreach($bans as $ban):?>
      <article class="moderation-card <?=!empty($ban['active'])?'is-danger':''?>">
        <div class="moderation-card-head"><div><strong><?=Security::e($ban['player_name'] ?: $ban['steam_id64'])?></strong><div class="mono"><?=Security::e((string)$ban['steam_id64'])?> · Ban #<?=(int)$ban['id']?></div></div><span class="status-pill <?=!empty($ban['active'])?'status-danger':'status-neutral'?>"><?=!empty($ban['active'])?'AKTIV':'HISTORIE'?></span></div>
        <div class="moderation-card-reason"><?=Security::e((string)($ban['reason_label']??'Globaler Ban'))?></div>
        <?php if(!empty($ban['reason_detail'])):?><p><?=Security::e((string)$ban['reason_detail'])?></p><?php endif;?>
        <div class="small-muted">Gebannt <?=Security::e(date('d.m.Y H:i',strtotime((string)$ban['starts_at'])?:time()))?> · von <?=Security::e((string)($ban['created_by_name']??'System'))?></div>
        <?php if(empty($ban['active'])):?><div class="ban-history-note"><?php if(!empty($ban['unbanned_at'])):?>Entbannt/abgelaufen <?=Security::e(date('d.m.Y H:i',strtotime((string)$ban['unbanned_at'])?:time()))?><?php endif;?><?php if(!empty($ban['unbanned_by_name'])):?> · von <?=Security::e((string)$ban['unbanned_by_name'])?><?php endif;?><?php if(!empty($ban['unban_reason'])):?><br>Grund: <?=Security::e((string)$ban['unban_reason'])?><?php endif;?></div><?php endif;?>
        <?php if(!empty($ban['active']) && Auth::can('moderation.unban')):?><form method="post" action="/bans/<?=(int)$ban['id']?>/unban" class="moderation-inline-form" data-confirm="Diesen Spieler wirklich global entbannen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="/moderation"><label>Entbannungsgrund<input name="unban_reason" maxlength="300" required placeholder="Grund dokumentieren"></label><button class="btn btn-danger btn-small">Global entbannen</button></form><?php endif;?>
      </article>
      <?php endforeach;?>
      <?php if(!$bans):?><div class="moderation-empty">Keine Bans oder Entbannungen für diesen Filter gefunden.</div><?php endif;?>
    </div>
  </section>

  <section class="panel moderation-section">
    <div class="panel-header moderation-section-head"><div><h2>Verwarnungen</h2><p>Aktive Verwarnungen sind gelb markiert. Erledigte Einträge bleiben als Historie erhalten.</p></div><span class="status-pill <?= $activeWarnings > 0 ? 'status-warning' : 'status-neutral' ?>"><?=$activeWarnings?> aktiv</span></div>
    <div class="moderation-card-grid">
      <?php foreach($warnings as $w):?>
      <article class="moderation-card <?=empty($w['resolved_at'])?'is-active':''?>">
        <div class="moderation-card-head"><div><strong><?=Security::e($w['player_name'] ?: $w['steam_id64'])?></strong><div class="mono"><?=Security::e($w['steam_id64'])?></div></div><span class="status-pill <?=empty($w['resolved_at'])?'status-warning':'status-neutral'?>"><?=empty($w['resolved_at'])?'AKTIV':'ERLEDIGT'?></span></div>
        <div class="moderation-card-reason"><?=Security::e($w['reason_label'] ?? 'Verwarnung')?></div>
        <?php if(!empty($w['detail'])):?><p><?=Security::e($w['detail'])?></p><?php endif;?>
        <div class="small-muted"><?=Security::e($w['created_at'] ?? '')?> · <?=Security::e($w['created_by_name'] ?? 'System')?></div>
        <?php if(empty($w['resolved_at']) && Auth::can('moderation.warn')):?>
        <form method="post" action="/moderation/warnings/<?=(int)$w['id']?>/resolve" class="inline-action-form moderation-inline-form">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="/moderation">
          <label>Erledigungsgrund<input name="reason" value="Erledigt" maxlength="300"></label><button class="btn btn-secondary btn-small">Erledigen</button>
        </form>
        <?php endif;?>
      </article>
      <?php endforeach;?>
      <?php if(!$warnings):?><div class="moderation-empty">Keine Verwarnungen gefunden.</div><?php endif;?>
    </div>
  </section>

  <section class="panel moderation-section">
    <div class="panel-header moderation-section-head"><div><h2>Globale Kommunikationssperren</h2><p>Mute, Gag und Silence gelten serverübergreifend und können zeitlich oder permanent sein.</p></div><span class="status-pill <?= $activeSanctions > 0 ? 'status-danger' : 'status-neutral' ?>"><?=$activeSanctions?> aktiv</span></div>
    <div class="moderation-card-grid">
      <?php foreach($sanctions as $s):?>
      <article class="moderation-card <?=!empty($s['active'])?'is-danger':''?>">
        <div class="moderation-card-head"><div><strong><?=Security::e($s['player_name'] ?: $s['steam_id64'])?></strong><div class="mono"><?=Security::e($s['steam_id64'])?></div></div><span class="status-pill <?=!empty($s['active'])?'status-danger':'status-neutral'?>"><?=Security::e(strtoupper((string)($s['sanction_type'] ?? 'Sperre')))?></span></div>
        <div class="moderation-card-reason"><?=Security::e($s['reason_label'] ?? 'Regelverstoß')?></div>
        <?php if(!empty($s['reason_detail'])):?><p><?=Security::e($s['reason_detail'])?></p><?php endif;?>
        <div class="small-muted"><?=Security::e($s['starts_at'] ?? '')?><?php if(!empty($s['expires_at'])):?> → <?=Security::e($s['expires_at'])?><?php else:?> · Permanent<?php endif;?></div>
        <?php if(!empty($s['active']) && Auth::can('moderation.global_comms')):?>
        <form method="post" action="/moderation/sanctions/<?=(int)$s['id']?>/revoke" class="inline-action-form moderation-inline-form">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="/moderation">
          <label>Aufhebungsgrund<input name="reason" value="Aufgehoben" maxlength="300"></label><button class="btn btn-secondary btn-small">Global aufheben</button>
        </form>
        <?php endif;?>
      </article>
      <?php endforeach;?>
      <?php if(!$sanctions):?><div class="moderation-empty">Keine Kommunikationssperren gefunden.</div><?php endif;?>
    </div>
  </section>

  <section class="panel moderation-section">
    <div class="panel-header moderation-section-head"><div><h2>Interne Moderator-Notizen</h2><p>Nicht öffentlich sichtbare Hinweise für das Moderationsteam.</p></div><span class="status-pill status-neutral"><?=count($notes)?> Treffer</span></div>
    <div class="moderation-card-grid">
      <?php foreach($notes as $n):?>
      <article class="moderation-card is-info">
        <div class="moderation-card-head"><div><strong><?=Security::e($n['player_name'] ?: $n['steam_id64'])?></strong><div class="mono"><?=Security::e($n['steam_id64'])?></div></div><span class="status-pill status-info">NOTIZ</span></div>
        <p class="moderation-note-text"><?=nl2br(Security::e($n['note'] ?? ''))?></p>
        <div class="small-muted"><?=Security::e($n['created_at'] ?? '')?> · <?=Security::e($n['created_by_name'] ?? 'System')?></div>
      </article>
      <?php endforeach;?>
      <?php if(!$notes):?><div class="moderation-empty">Keine Moderator-Notizen gefunden.</div><?php endif;?>
    </div>
  </section>

  <section class="panel moderation-section">
    <div class="panel-header moderation-section-head"><div><h2>Beweise</h2><p>Gesicherte Chatnachrichten, Notizen, Links und weitere Moderationsbelege.</p></div><span class="status-pill status-neutral"><?=count($evidence)?> Treffer</span></div>
    <div class="moderation-card-grid">
      <?php foreach($evidence as $e):?>
      <article class="moderation-card">
        <div class="moderation-card-head"><div><strong><?=Security::e($e['player_name'] ?: $e['steam_id64'])?></strong><div class="mono"><?=Security::e($e['steam_id64'])?></div></div><span class="status-pill status-neutral"><?=Security::e(strtoupper((string)($e['evidence_type'] ?? 'Beleg')))?></span></div>
        <div class="moderation-card-reason"><?=Security::e($e['label'] ?? 'Beweis')?></div>
        <?php if(!empty($e['value'])):?><p class="moderation-evidence-value"><?=nl2br(Security::e($e['value']))?></p><?php endif;?>
        <div class="small-muted"><?=Security::e($e['created_at'] ?? '')?> · <?=Security::e($e['created_by_name'] ?? 'System')?></div>
      </article>
      <?php endforeach;?>
      <?php if(!$evidence):?><div class="moderation-empty">Keine Beweise gefunden.</div><?php endif;?>
    </div>
  </section>

  <section class="panel moderation-section">
    <div class="panel-header moderation-section-head"><div><h2>Appeals</h2><p>Interner Prüfworkflow für Entbannungs- und Sanktionsanträge.</p></div><span class="status-pill <?= $openAppeals > 0 ? 'status-info' : 'status-neutral' ?>"><?=$openAppeals?> offen</span></div>
    <div class="moderation-card-grid">
      <?php foreach($appeals as $a):?>
      <article class="moderation-card <?=in_array((string)($a['status'] ?? ''),['open','reviewing'],true)?'is-info':''?>">
        <div class="moderation-card-head"><div><strong><?=Security::e($a['player_name'] ?: $a['steam_id64'])?></strong><div class="mono"><?=Security::e($a['steam_id64'])?></div></div><span class="status-pill status-neutral"><?=Security::e(strtoupper((string)($a['status'] ?? 'open')))?></span></div>
        <p><?=nl2br(Security::e($a['statement'] ?? ''))?></p>
        <?php if(Auth::can('moderation.appeals')):?>
        <form method="post" action="/moderation/appeals/<?=(int)$a['id']?>" class="form-stack moderation-appeal-form">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="/moderation">
          <label>Status<select name="status"><option value="open" <?=($a['status']??'')==='open'?'selected':''?>>Offen</option><option value="reviewing" <?=($a['status']??'')==='reviewing'?'selected':''?>>In Prüfung</option><option value="approved" <?=($a['status']??'')==='approved'?'selected':''?>>Angenommen</option><option value="denied" <?=($a['status']??'')==='denied'?'selected':''?>>Abgelehnt</option></select></label>
          <label>Entscheidung<textarea name="decision" rows="2" maxlength="3000"><?=Security::e($a['decision'] ?? '')?></textarea></label>
          <button class="btn btn-secondary btn-small">Speichern</button>
        </form>
        <?php endif;?>
      </article>
      <?php endforeach;?>
      <?php if(!$appeals):?><div class="moderation-empty">Keine Appeals gefunden.</div><?php endif;?>
    </div>
  </section>
</div>
