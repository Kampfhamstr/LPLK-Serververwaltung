<?php use App\Core\Security; use App\Core\Auth; ?>
<?php require __DIR__.'/panel_home.php'; ?>
<div class="stat-grid">
<?php if($stats['users']!==null):?><div class="stat-card"><div class="stat-label">Webpanel-Nutzer</div><div class="stat-value"><?=$stats['users']?></div></div><?php endif;?>
<div class="stat-card"><div class="stat-label">Spieler & Personen</div><div class="stat-value"><?=$stats['players']?></div><div class="muted"><?= (int)$stats['secondary_accounts'] ?> zusätzliche Steam-Zweitaccounts</div></div>
<?php if($stats['servers']!==null):?><div class="stat-card"><div class="stat-label">Server</div><div class="stat-value"><?=$stats['servers']?></div></div><?php endif;?>
<?php if($stats['pending']!==null):?><div class="stat-card"><div class="stat-label">Offene Einladungen</div><div class="stat-value"><?=$stats['pending']?></div></div><?php endif;?>
<?php if(!empty($pluginUpdateSummary['visible'])):?><a class="stat-card stat-card-link plugin-update-dashboard-card" href="/plugins"><div class="stat-label">Plugin-Updates</div><div class="stat-value"><?=(int)$pluginUpdateSummary['updates']?></div><div class="muted"><?=(int)$pluginUpdateSummary['servers']?> Live-Server geprüft</div></a><?php endif;?>
</div>

<section class="panel dashboard-live-panel" id="server-status">
    <div class="panel-header split-header">
        <div>
            <h2>Live-Serverstatus</h2>
            <p class="muted">Jeder Server als eigene Statuskachel. Live-Aktualisierung alle 5 Sekunden.</p>
        </div>
        <div class="dashboard-live-tools">
            <span class="small-muted" data-dashboard-live-refresh-time>—</span>
            <button class="btn btn-secondary btn-small" type="button" data-dashboard-live-refresh>Aktualisieren</button>
        </div>
    </div>

    <div class="dashboard-server-grid dashboard-server-grid-cards" data-dashboard-live-root>
        <?php foreach(($liveServers ?? []) as $live): ?>
        <article class="dashboard-server-tile <?=$live['bridge_online']?'is-online':'is-offline'?>" data-dashboard-server="<?= (int)$live['id'] ?>">
            <header class="dashboard-tile-head">
                <div class="dashboard-tile-title">
                    <a href="/servers/<?= (int)$live['id'] ?>"><?=Security::e($live['name'])?></a>
                    <span class="small-muted" data-dash-framework><?=Security::e($live['framework'])?></span>
                </div>
                <div class="dashboard-tile-head-controls">
                    <?php $power=$live['dashboard_power']??[]; if(!empty($power['visible'])): ?>
                    <div class="dashboard-power-shortcuts" aria-label="Pterodactyl-Steuerung für <?=Security::e($live['name'])?>">
                        <?php if(!empty($power['start'])): ?><form method="post" action="/dashboard/servers/<?=(int)$live['id']?>/power"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="start"><button class="btn btn-success btn-small dashboard-power-button" type="submit">Start</button></form><?php endif; ?>
                        <?php if(!empty($power['restart'])): ?><form method="post" action="/dashboard/servers/<?=(int)$live['id']?>/power" data-confirm="Server „<?=Security::e($live['name'])?>“ wirklich neu starten? Verbundene Spieler werden getrennt."><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="restart"><button class="btn btn-warning btn-small dashboard-power-button" type="submit">Neu starten</button></form><?php endif; ?>
                        <?php if(!empty($power['stop'])): ?><form method="post" action="/dashboard/servers/<?=(int)$live['id']?>/power" data-confirm="Server „<?=Security::e($live['name'])?>“ wirklich stoppen? Verbundene Spieler werden getrennt."><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="stop"><button class="btn btn-stop btn-small dashboard-power-button" type="submit">Stoppen</button></form><?php endif; ?>
                    </div>
                    <?php endif; ?>
                    <?php if($live['can_live']): ?>
                        <span class="status-pill <?=$live['bridge_online']?'status-online':'status-offline'?>" data-dash-bridge><?=$live['bridge_online']?'ONLINE':($live['bridge_available']?'STATUS VERALTET':'KEINE DATEN')?></span>
                    <?php else: ?>
                        <span class="status-pill status-warning">KEIN LIVE-ZUGRIFF</span>
                    <?php endif; ?>
                </div>
            </header>

            <div class="dashboard-tile-maprow">
                <div class="dashboard-map-block">
                    <span>Aktuelle Map</span>
                    <strong data-dash-map><?=Security::e($live['map'] ?: '—')?></strong>
                </div>
                <div class="dashboard-map-block">
                    <span>Nächste Map</span>
                    <strong data-dash-next-map><?=Security::e($live['next_map'] ?: 'nicht geplant')?></strong>
                    <em class="dashboard-switch-state <?=$live['map_switch_in_flight']?'':'hidden'?>" data-dash-switch-state>Mapwechsel läuft</em>
                </div>
            </div>

            <div class="dashboard-tile-metrics">
                <div><span>Spieler</span><strong data-dash-player-count><?= (int)$live['human_players'] ?><?= $live['max_players'] !== null ? ' / '.(int)$live['max_players'] : '' ?></strong></div>
                <div class="metric-t"><span>T</span><strong data-dash-t-count><?= (int)$live['t_count'] ?></strong></div>
                <div class="metric-ct"><span>CT</span><strong data-dash-ct-count><?= (int)$live['ct_count'] ?></strong></div>
                <div class="metric-spec"><span>Spec</span><strong data-dash-spec-count><?= (int)$live['spectator_count'] ?></strong></div>
                <div><span>Bots</span><strong data-dash-bot-count><?= (int)$live['bots'] ?></strong></div>
            </div>

            <?php if(!$live['can_live']): ?>
                <div class="dashboard-live-empty dashboard-tile-empty"><?=Security::e($live['reason'] ?: 'Live-Daten nicht freigegeben.')?></div>
            <?php else: ?>
                <div class="dashboard-tile-teams" data-dash-team-columns>
                    <?php foreach(['T'=>'Terrorists','CT'=>'Counter-Terrorists','Spectator'=>'Zuschauer'] as $teamKey=>$teamLabel): ?>
                    <?php $teamPlayers = $live['teams'][$teamKey] ?? []; ?>
                    <section class="dashboard-team-strip dashboard-team-strip-<?=strtolower($teamKey)==='spectator'?'spec':strtolower($teamKey)?>" data-dash-team="<?=Security::e($teamKey)?>">
                        <div class="dashboard-team-strip-head">
                            <strong><?=Security::e($teamLabel)?></strong>
                            <span><?=count($teamPlayers)?></span>
                        </div>
                        <div class="dashboard-player-chips" data-dash-player-list>
                            <?php if(empty($teamPlayers)): ?>
                                <span class="dashboard-team-empty">keine</span>
                            <?php else: ?>
                                <?php foreach(array_slice($teamPlayers, 0, 5) as $p): ?>
                                <div class="dashboard-player-row" data-player-status-host data-player-steamid="<?=Security::e((string)$p['steam_id64'])?>"><a class="dashboard-player-chip <?=$teamKey!=='Spectator' ? ($p['alive']?'is-alive':'is-dead') : 'is-spec'?>" href="/servers/<?= (int)$live['id'] ?>/live/player/<?=Security::e($p['steam_id64'])?>">
                                    <span class="dashboard-player-chip-name"><?=Security::e($p['name'])?></span>
                                    <span class="dashboard-player-chip-time"><?=Security::e($p['connected_human'] ?: '0s')?></span>
                                </a><?php if(!empty($p['steam_id64']) && (Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$p['steam_id64'])?>" data-player-name="<?=Security::e((string)$p['name'])?>" data-player-server-id="<?=(int)$live['id']?>">…</button><?php endif;?></div>
                                <?php endforeach; ?>
                                <?php if(count($teamPlayers) > 5): ?>
                                    <span class="dashboard-more-players">+<?=count($teamPlayers)-5?> weitere</span>
                                <?php endif; ?>
                            <?php endif; ?>
                        </div>
                    </section>
                    <?php endforeach; ?>
                </div>

                <?php if(!empty($live['unassigned_count'])): ?>
                <div class="dashboard-unassigned">Noch keinem Team zugeordnet: <strong><?= (int)$live['unassigned_count'] ?></strong></div>
                <?php endif; ?>

                <details class="dashboard-server-chat">
                  <summary>Serverchat <span class="status-pill" data-dash-chat-total><?= (int)($live['chat_total']??0) ?></span></summary>
                  <div class="dashboard-server-chat-body" data-dash-chat-list>
                    <?php if(empty($live['recent_chat'])):?><div class="muted dashboard-chat-empty">Noch keine Chatnachrichten gespeichert.</div><?php endif;?>
                    <?php foreach(($live['recent_chat']??[]) as $cm): $cr=strtolower((string)($cm['community_role']??'spieler'));?>
                      <div class="dashboard-game-chat-line game-chat-role-<?=Security::e($cr)?>">
                        <span class="dashboard-game-chat-time"><?=Security::e(date('H:i:s',strtotime((string)$cm['sent_at'])?:time()))?></span>
                        <strong class="game-role-text game-role-text-<?=Security::e($cr)?>"><?=Security::e((string)$cm['player_name'])?></strong>
                        <span class="dashboard-game-chat-text"><?=Security::e((string)$cm['message'])?></span>
                        <?php if(!empty($cm['steam_id64']) && (Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$cm['steam_id64'])?>" data-player-name="<?=Security::e((string)$cm['player_name'])?>" data-player-server-id="<?=(int)$live['id']?>" data-player-chat-id="<?=(int)($cm['id']??0)?>">Aktionen</button><?php endif;?>
                      </div>
                    <?php endforeach;?>
                    <a class="dashboard-chat-archive-link" href="/servers/<?=(int)$live['id']?>/chat">Chat-Archiv durchsuchen</a>
                  </div>
                </details>

                <div class="dashboard-live-reason <?=$live['bridge_online']?'hidden':''?>" data-dash-reason>
                    <?=Security::e($live['reason'] ?: 'Live-Bridge nicht erreichbar oder Status veraltet.')?>
                </div>
            <?php endif; ?>

            <footer class="dashboard-tile-foot">
                <span class="small-muted" data-dash-updated><?=Security::e($live['updated_at'] ?: '—')?></span>
                <div class="dashboard-tile-actions"><?php $joinServerId=(int)$live['id']; require __DIR__.'/server_join_button.php'; ?>
                    <a class="btn btn-secondary btn-small" href="/servers/<?= (int)$live['id'] ?>">Details</a>
                    <?php if($live['can_live']): ?>
                    <a class="btn btn-primary btn-small" href="/servers/<?= (int)$live['id'] ?>/live">Live-Verwaltung</a>
                    <?php endif; ?>
                </div>
            </footer>
        </article>
        <?php endforeach; ?>

        <?php if(empty($liveServers)): ?>
            <div class="dashboard-live-empty">Noch keine Server angelegt.</div>
        <?php endif; ?>
    </div>
</section>

<section class="panel" id="panel-users-online">
  <div class="panel-header split-header">
    <div>
      <h2>Webpanel online</h2>
      <p class="muted">Aktiv bedeutet: innerhalb der letzten 3 Minuten eine Webpanel-Anfrage.</p>
    </div>
    <strong><?=count($onlinePanelUsers??[])?> online</strong>
  </div>
  <div class="panel-user-online-list" data-panel-users-online>
    <?php if(empty($onlinePanelUsers)):?><span class="muted">Aktuell kein Webpanel-Nutzer aktiv.</span><?php endif;?>
    <?php foreach(($onlinePanelUsers??[]) as $u):?>
      <div class="panel-user-online-chip">
        <?php if($u['steam_verified']):?><img src="/users/<?=(int)$u['id']?>/steam-avatar" alt=""><?php endif;?>
        <div><strong><?=Security::e($u['display_name'])?></strong><span><?=Security::e($u['web_role_label'])?><?=$u['steam_verified']?' · Steam':''?></span></div>
      </div>
    <?php endforeach;?>
  </div>
</section>

<div class="dashboard-community-grid">
<?php if(Auth::can('community.chat.view')):?><section class="panel dashboard-chat-panel" id="dashboard-chat">
  <div class="panel-header split-header">
    <div>
      <h2>Webpanel-Chat</h2>
      <p class="muted">Kurzchat für alle angemeldeten Panelnutzer. Es werden ausschließlich die letzten 30 Nachrichten behalten.</p>
    </div>
    <span class="small-muted" data-chat-status>Live</span>
  </div>

  <div class="dashboard-chat-window" data-chat-list data-current-user-id="<?=(int)Auth::user()['id']?>" data-can-moderate="<?=Auth::can('community.chat.moderate')?'1':'0'?>">
    <?php foreach(($chatMessages??[]) as $m):?>
      <article class="chat-message" data-chat-id="<?=(int)$m['id']?>">
        <div class="chat-avatar-wrap"><?php if($m['steam_verified']):?><img class="chat-avatar" src="/users/<?=(int)$m['user_id']?>/steam-avatar" alt=""><?php else:?><span class="chat-avatar chat-avatar-fallback"><?=Security::e(strtoupper(substr($m['display_name'],0,1)))?></span><?php endif;?></div>
        <div class="chat-body">
          <div class="chat-meta"><strong><?=Security::e($m['display_name'])?></strong><span><?=Security::e($m['web_role_label'])?> · <?=Security::e($m['created_at'])?></span></div>
          <div class="chat-text"><?=nl2br(Security::e($m['message']))?></div>
        </div>
        <?php if(Auth::can('community.chat.moderate')):?>
          <form method="post" action="/dashboard/chat/<?=(int)$m['id']?>/delete" class="chat-delete-form" data-chat-delete>
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
            <button class="link-button" title="Nachricht löschen">Löschen</button>
          </form>
        <?php endif;?>
      </article>
    <?php endforeach;?>
    <?php if(empty($chatMessages)):?><div class="chat-empty muted">Noch keine Chatnachrichten.</div><?php endif;?>
  </div>

  <?php if(Auth::can('community.chat.post')):?><form method="post" action="/dashboard/chat" class="dashboard-chat-form" data-chat-form>
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <input type="text" name="message" maxlength="500" autocomplete="off" placeholder="Nachricht schreiben..." required>
    <button class="btn btn-primary btn-small" type="submit">Senden</button>
  </form><?php endif;?>
</section><?php endif;?>

<?php if(Auth::can('community.forum.view')):?><section class="panel dashboard-forum-preview">
  <div class="panel-header split-header">
    <div><h2>Forum</h2><p class="muted">Dauerhafte Themen und Diskussionen.</p></div>
    <a class="btn btn-secondary btn-small" href="/forum">Forum öffnen</a>
  </div>
  <div class="forum-preview-list">
    <?php if(empty($latestForumThreads)):?><div class="muted">Noch keine Forum-Themen.</div><?php endif;?>
    <?php foreach(($latestForumThreads??[]) as $thread):?>
      <a class="forum-preview-item" href="/forum/<?=(int)$thread['id']?>">
        <div><strong><?=Security::e($thread['title'])?></strong><span><?=Security::e($thread['category_label'])?> · <?=Security::e($thread['author_display_name'])?></span></div>
        <div class="forum-preview-meta"><?=$thread['is_pinned']?'Angeheftet · ':''?><?=$thread['is_locked']?'Gesperrt · ':''?><?=max(0,(int)$thread['post_count']-1)?> Antworten</div>
      </a>
    <?php endforeach;?>
  </div>
  <?php if(Auth::can('community.forum.post')):?><a class="btn btn-primary btn-small" href="/forum#new-thread">Neues Thema</a><?php endif;?>
</section><?php endif;?>
</div>

<section class="panel" id="announcements">
  <div class="panel-header split-header">
    <div><h2>Mitteilungen</h2><p class="muted">Aktive Hinweise für alle Webpanel-Nutzer.</p></div>
    <span class="notification-summary"><strong data-announcement-unread><?= (int)($unreadAnnouncements??0) ?></strong> ungelesen</span>
  </div>

  <div class="announcement-list">
    <?php if(empty($announcements)):?><div class="muted">Keine aktiven Mitteilungen.</div><?php endif;?>
    <?php foreach(($announcements??[]) as $a):?>
      <article class="announcement-card announcement-<?=Security::e((string)$a['level'])?> <?=$a['is_read']?'is-read':'is-unread'?>">
        <div class="announcement-head">
          <div><strong><?=Security::e((string)$a['title'])?></strong><span><?=Security::e((string)$a['creator_display_name'])?> · <?=Security::e((string)$a['created_at'])?></span></div>
          <span class="status-pill"><?=strtoupper(Security::e((string)$a['level']))?></span>
        </div>
        <div class="announcement-message"><?=nl2br(Security::e((string)$a['message']))?></div>
        <div class="announcement-actions">
          <?php if(!$a['is_read']):?>
          <form method="post" action="/dashboard/announcements/<?=(int)$a['id']?>/read"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-secondary btn-small">Als gelesen markieren</button></form>
          <?php endif;?>
          <?php if(Auth::can('dashboard.announce')):?><form method="post" action="/dashboard/announcements/<?=(int)$a['id']?>/revoke" data-confirm="Mitteilung wirklich beenden?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-danger btn-small">Beenden</button></form><?php endif;?>
        </div>
      </article>
    <?php endforeach;?>
  </div>

  <?php if(Auth::can('dashboard.announce')):?>
  <form method="post" action="/dashboard/announcements" class="announcement-compose form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <h3>Neue Webpanel-Mitteilung</h3>
    <div class="form-grid">
      <label>Titel<input type="text" name="title" maxlength="100" required></label>
      <label>Priorität<select name="level"><option value="info">Information</option><option value="warning">Warnung</option><option value="critical">Kritisch</option></select></label>
      <label>Gültigkeit<select name="duration_minutes"><option value="60">1 Stunde</option><option value="360">6 Stunden</option><option value="1440" selected>24 Stunden</option><option value="10080">7 Tage</option><option value="0">Bis manuell beendet</option></select></label>
    </div>
    <label>Mitteilung<textarea name="message" rows="3" maxlength="1200" required></textarea></label>
    <button class="btn btn-primary">Mitteilung veröffentlichen</button>
  </form>
  <?php endif;?>
</section>

<section class="panel">
<div class="panel-header"><h2>Verwaltungsstatus</h2></div>
<div class="table-wrap"><table data-ui-adaptive="dashboard-server-summary"><thead><tr><th>Server</th><th>Framework</th><th>Letzter Scan</th><th>Letzte Synchronisation</th><th>Status</th></tr></thead><tbody>
<?php if(!$servers):?><tr><td colspan="5" class="muted">Noch keine Server angelegt.</td></tr><?php endif;?>
<?php foreach($servers as $s):?><tr><td><a href="/servers/<?=$s['id']?>"><?=Security::e($s['name'])?></a></td><td><?=Security::e($s['framework']?:'Nicht gescannt')?></td><td><?=Security::e($s['last_scan_at']?:'—')?></td><td><?=Security::e($s['last_sync_at']?:'—')?></td><td><?=Security::e($s['last_error']?'Fehler':'Bereit')?></td></tr><?php endforeach;?>
</tbody></table></div>
</section>
