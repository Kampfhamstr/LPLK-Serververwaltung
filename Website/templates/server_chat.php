<?php use App\Core\Security; use App\Core\Auth; ?>
<section class="panel server-chat-archive-head">
  <div class="panel-header split-header">
    <div><h2><?=Security::e((string)$server['name'])?> · Serverchat</h2><p>Dauerhaft gespeicherte Chatnachrichten mit Zeitstempel, SteamID64 und Community-Rolle.</p></div>
    <div class="toolbar"><span class="status-pill"><?=number_format((int)$total,0,',','.')?> Nachrichten</span><a class="btn btn-secondary btn-small" href="/servers/<?=(int)$server['id']?>/live">Zur Live-Verwaltung</a></div>
  </div>
  <form method="get" action="/servers/<?=(int)$server['id']?>/chat" class="server-chat-search-form">
    <label>Spieler / SteamID<input type="search" name="player" list="server-chat-player-list" value="<?=Security::e((string)$filters['player'])?>" placeholder="Name oder 7656119..."></label>
    <datalist id="server-chat-player-list"><?php foreach(($players??[]) as $p):?><option value="<?=Security::e((string)$p['player_name'])?>"><?=Security::e((string)($p['steam_id64']??''))?></option><?php endforeach;?></datalist>
    <label>Nachricht enthält<input type="search" name="q" value="<?=Security::e((string)$filters['q'])?>" placeholder="Suchtext"></label>
    <label>Von<input type="date" name="from" value="<?=Security::e((string)$filters['from'])?>"></label>
    <label>Bis<input type="date" name="to" value="<?=Security::e((string)$filters['to'])?>"></label>
    <label>Anzahl<select name="limit"><?php foreach([50,100,150,250] as $n):?><option value="<?=$n?>" <?=$filters['limit']===$n?'selected':''?>><?=$n?></option><?php endforeach;?></select></label>
    <button class="btn btn-primary">Suchen</button>
    <a class="btn btn-secondary" href="/servers/<?=(int)$server['id']?>/chat">Filter zurücksetzen</a>
  </form>
</section>

<section class="panel server-chat-archive-panel">
  <div class="server-chat-list server-chat-list-archive">
    <?php if(empty($messages)):?><div class="muted server-chat-empty">Keine Chatnachrichten für diesen Filter gefunden.</div><?php endif;?>
    <?php $archiveDayPrevious=null; foreach($messages as $m):?>
      <?php $role=strtolower((string)($m['community_role']??'spieler')); $archiveTs=strtotime((string)($m['sent_at']??''))?:time(); $archiveDay=date('Y-m-d',$archiveTs); if($archiveDay!==$archiveDayPrevious): $today=date('Y-m-d'); $yesterday=date('Y-m-d',time()-86400); $archiveDayLabel=$archiveDay===$today?'Heute · '.date('d.m.Y',$archiveTs):($archiveDay===$yesterday?'Gestern · '.date('d.m.Y',$archiveTs):date('d.m.Y',$archiveTs)); ?>
      <div class="server-chat-day-separator" data-chat-day="<?=Security::e($archiveDay)?>"><span><?=Security::e($archiveDayLabel)?></span></div>
      <?php $archiveDayPrevious=$archiveDay; endif;?>
      <article class="server-chat-message game-chat-role-<?=Security::e($role)?>">
        <div class="server-chat-meta" data-player-status-host data-player-steamid="<?=Security::e((string)($m['steam_id64']??''))?>">
          <time title="<?=Security::e((string)$m['sent_at'])?>"><?=Security::e(date('H:i:s',strtotime((string)$m['sent_at'])?:time()))?></time>
          <strong class="game-role-text game-role-text-<?=Security::e($role)?>"><?=Security::e((string)$m['player_name'])?></strong>
          <span class="game-role game-role-<?=Security::e($role)?>"><?=Security::e(ucfirst($role))?></span>
          <span class="team-badge team-<?=Security::e(strtolower((string)$m['team']))?>"><?=Security::e((string)$m['team'])?></span>
          <?php if(!empty($m['team_only'])):?><span class="status-pill">TEAM</span><?php else:?><span class="status-pill">ALL</span><?php endif;?>
          <?php if(!empty($m['steam_id64'])):?><span class="mono server-chat-steamid"><?=Security::e((string)$m['steam_id64'])?></span><?php endif;?>
          <?php if(!empty($m['map_name'])):?><span class="small-muted"><?=Security::e((string)$m['map_name'])?></span><?php endif;?>
          <?php if(!empty($m['steam_id64']) && (Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$m['steam_id64'])?>" data-player-name="<?=Security::e((string)$m['player_name'])?>" data-player-server-id="<?=(int)$server['id']?>" data-player-chat-id="<?=(int)$m['id']?>">Aktionen</button><?php endif;?>
          <?php if(!empty($m['steam_id64']) && Auth::can('moderation.evidence')):?><form method="post" action="/moderation/advertising/report" class="server-chat-report-form" data-confirm="Diese Chatnachricht als Werbung oder Spam an die Moderation melden?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="chat_message_id" value="<?=(int)$m['id']?>"><input type="hidden" name="_return" value="/servers/<?=(int)$server['id']?>/chat"><button class="btn btn-secondary btn-small" type="submit">Werbung melden</button></form><?php endif;?>
        </div>
        <div class="server-chat-text"><?=nl2br(Security::e((string)$m['message']))?></div>
      </article>
    <?php endforeach;?>
  </div>
  <?php
    $baseQuery=[
      'player'=>(string)$filters['player'],'q'=>(string)$filters['q'],'from'=>(string)$filters['from'],'to'=>(string)$filters['to'],'limit'=>(int)$filters['limit']
    ];
    $baseQuery=array_filter($baseQuery,static fn($v)=>$v!==''&&$v!==0);
  ?>
  <div class="server-chat-pagination">
    <?php if(!empty($filters['before_id'])):?><a class="btn btn-secondary btn-small" href="/servers/<?=(int)$server['id']?>/chat?<?=Security::e(http_build_query($baseQuery))?>">Zu den neuesten Treffern</a><?php endif;?>
    <?php if(!empty($nextBefore)):?>
      <?php $olderQuery=$baseQuery;$olderQuery['before_id']=(int)$nextBefore;?>
      <a class="btn btn-secondary btn-small" href="/servers/<?=(int)$server['id']?>/chat?<?=Security::e(http_build_query($olderQuery))?>">Ältere Nachrichten →</a>
    <?php endif;?>
  </div>
</section>
