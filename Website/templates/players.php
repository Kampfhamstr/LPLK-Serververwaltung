<?php use App\Core\Security; use App\Core\Auth; ?>
<div class="group-overview">
<?php foreach(['founder','admin','member','vip','spieler'] as $r): ?>
<a class="group-card group-<?=$r?> <?=($roleFilter===$r?'is-active':'')?>" href="/players?role=<?=$r?><?= $q!==''?'&q='.rawurlencode($q):'' ?>">
<div><span class="group-card-name"><?=Security::e($roles[$r])?></span><span class="group-card-sub">Hauptaccounts</span></div><strong><?=$counts[$r]??0?></strong>
</a>
<?php endforeach; ?>
<a class="group-card group-all <?=($roleFilter===''?'is-active':'')?>" href="/players<?= $q!==''?'?q='.rawurlencode($q):'' ?>"><div><span class="group-card-name">Alle Personen</span><span class="group-card-sub"><?= (int)$secondaryCount ?> Zweitaccount(s)</span></div><strong><?=array_sum($counts)?></strong></a>
</div>

<section class="panel">
<div class="panel-header player-list-header"><div><h2>Spieler & Personen</h2><p>Eine Person kann einen Hauptaccount und mehrere Steam-Zweitaccounts besitzen. Gruppenstatistiken zählen nur Hauptaccounts; Zweitaccounts erben Community- und Serverrechte automatisch.</p></div>
<div class="toolbar player-actions">
<?php if($canImport):?><a class="btn btn-secondary" href="/players/import">Whitelist importieren</a><?php endif;?>
<?php if(!empty($canExport)):?><a class="btn btn-secondary" href="/players/export">Spielerliste exportieren</a><?php endif;?>
<?php if($canSteamSync && $steamConfigured):?><form method="post" action="/players/steam-sync-all" data-confirm="Alle aktiven Steam-Profile jetzt aktualisieren? Die Steam-Profilnamen werden als Anzeigenamen übernommen."><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-secondary">Steam-Profile aktualisieren</button></form><?php endif;?>
</div></div>

<form class="search-bar" method="get" action="/players">
<label>Suche<input type="search" name="q" value="<?=Security::e($q)?>" placeholder="Steamname, alter Steamname, Hauptaccount, SteamID64<?=Auth::can('players.contacts.view')?', Vorname, Nachname, E-Mail, Discord':''?>"></label>
<?php if($roleFilter!==''):?><input type="hidden" name="role" value="<?=Security::e($roleFilter)?>"><?php endif;?>
<button class="btn btn-primary">Suchen</button><?php if($q!==''||$roleFilter!==''):?><a class="btn btn-secondary" href="/players">Filter zurücksetzen</a><?php endif;?>
</form>

<?php if($canManage):?>
<form id="bulk-players-form" class="bulk-player-form" method="post" action="/players/bulk" data-bulk-players>
<input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
<input type="hidden" name="_return" value="<?=Security::e($_SERVER['REQUEST_URI']??'/players')?>">
<div class="bulk-player-head"><div><strong>Massenverwaltung</strong><span class="muted"><span data-selected-count>0</span> Accountzeilen ausgewählt</span></div><button class="btn btn-secondary btn-small" type="button" data-clear-selection>Auswahl aufheben</button></div>
<div class="bulk-hint">Wird ein Zweitaccount ausgewählt, gilt die Änderung automatisch für den zugehörigen Hauptaccount und alle weiteren Zweitaccounts dieser Person.</div>
<div class="bulk-player-grid">
<label>Community-Rolle
<select name="community_role" data-bulk-community-role data-role-color-select>
<option value="__unchanged">Nicht ändern</option>
<?php $maxLevel=Auth::GAME_ROLE_LEVELS[$maxCommunityRole]??1;foreach($roles as $rk=>$label):if((Auth::GAME_ROLE_LEVELS[$rk]??0)>$maxLevel)continue;?><option value="<?=$rk?>"><?=Security::e($label)?></option><?php endforeach;?>
</select></label>
<label>Serverrolle
<select name="server_role" data-bulk-server-role data-role-color-select>
<option value="__unchanged">Nicht ändern</option><option value="__none">Keine verwaltete Gruppe</option>
<?php $bulkMax=1;foreach($servers as $bs){$bid=(int)$bs['id'];if(!($manageable[$bid]??false))continue;$bulkMax=max($bulkMax,Auth::GAME_ROLE_LEVELS[$maxRoles[$bid]??'spieler']??1);}foreach(['spieler','vip','member','admin','founder'] as $rk):if((Auth::GAME_ROLE_LEVELS[$rk]??0)>$bulkMax)continue;?><option value="<?=$rk?>"><?=Security::e($roles[$rk])?></option><?php endforeach;?>
</select></label>
<label class="checkbox-line bulk-mirror"><input type="checkbox" name="mirror_community" value="1" data-mirror-community> Community-Rolle gleichzeitig als Serverrolle verwenden</label>
</div>
<?php if(array_filter($manageable)):?>
<div class="bulk-server-list"><span class="bulk-server-label">Serverauswahl</span><?php foreach($servers as $s):$sid=(int)$s['id'];if(!($manageable[$sid]??false))continue;?><label class="bulk-server-option"><input type="checkbox" name="server_ids[]" value="<?=$sid?>"><span><?=Security::e($s['name'])?></span></label><?php endforeach;?></div>
<div class="toolbar bulk-player-actions"><button class="btn btn-secondary" type="submit" name="scope" value="selected">Auf ausgewählte Server anwenden</button><button class="btn btn-primary" type="submit" name="scope" value="global">Auf alle verwaltbaren Server anwenden</button></div>
<?php else:?><div class="muted">Keine Server mit Berechtigung zur Rollenverwaltung vorhanden.</div><div class="toolbar bulk-player-actions"><button class="btn btn-primary" type="submit" name="scope" value="selected">Community-Rolle anwenden</button></div><?php endif;?>
</form>
<?php endif;?>

<div class="table-wrap"><table class="people-table" data-ui-adaptive="people"><thead><tr><?php if($canManage):?><th class="select-column"><input type="checkbox" data-select-all="bulk-player" aria-label="Alle sichtbaren Accounts auswählen"></th><?php endif;?><th>Person / Steam-Account</th><th>SteamID64</th><th>Community-Rolle</th><?php foreach($servers as $s):?><th><span class="server-column-title"><?=Security::e($s['name'])?></span></th><?php endforeach;?><th>Serverrechte setzen</th><th></th></tr></thead><tbody>
<?php if(!$players):?><tr><td colspan="<?=5+count($servers)+($canManage?1:0)?>" class="muted">Keine passenden Personen gefunden.</td></tr><?php endif;?>
<?php foreach($players as $p): $role=$p['community_role']?:'spieler'; $quickForm='quick-rights-'.(int)$p['id']; $isSecondary=!empty($p['main_player_id']); $roleManageable=$canManage&&Auth::canManageGameRoleTarget($role); ?>
<tr class="person-row person-row-<?=$role?> <?=$isSecondary?'secondary-account-row':'primary-account-row'?>">
<?php if($canManage):?><td class="select-column"><?php if($roleManageable):?><input type="checkbox" name="player_ids[]" value="<?=(int)$p['id']?>" form="bulk-players-form" data-select-item="bulk-player" aria-label="<?=Security::e($p['display_name'])?> auswählen"><?php else:?><span title="Admin-/Founder-Rolle geschützt">—</span><?php endif;?></td><?php endif;?>
<td><div class="person-cell <?=$isSecondary?'person-cell-secondary':''?>" data-player-status-host data-player-steamid="<?=Security::e((string)$p['steam_id64'])?>"><div class="avatar-box"><?php if($p['steam_last_synced_at']):?><img src="/players/<?=$p['id']?>/avatar" alt="" data-avatar><?php endif;?></div><div class="person-ident"><a class="person-name" href="/players/<?=$p['id']?>"><?=Security::e($p['display_name'])?></a><div class="account-meta"><?php if($isSecondary):?><span class="account-badge secondary-badge">Zweitaccount</span><span>von <a href="/players/<?=(int)$p['main_player_id']?>"><?=Security::e($p['main_steam_personaname']?:$p['main_display_name'])?></a></span><?php else:?><span class="account-badge primary-badge">Hauptaccount</span><?php if((int)$p['secondary_count']>0):?><span><?=(int)$p['secondary_count']?> Zweitaccount(s)</span><?php endif;?><?php endif;?></div></div></div></td>
<td class="mono"><?=Security::e($p['steam_id64'])?></td>
<td>
<?php if($isSecondary):?><div class="inherited-role"><span class="game-role game-role-<?=$role?>"><?=Security::e($roles[$role]??$role)?></span><small>vom Hauptaccount</small></div>
<?php elseif($roleManageable):?><form class="role-move-form role-auto-form" method="post" action="/players/<?=$p['id']?>/community-role"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="<?=Security::e($_SERVER['REQUEST_URI']??'/players')?>"><select class="role-select role-select-<?=$role?>" name="community_role" aria-label="Community-Rolle" data-community-role-select data-current="<?=Security::e($role)?>" data-player="<?=Security::e($p['display_name'])?>">
<?php $maxLevel=Auth::GAME_ROLE_LEVELS[$maxCommunityRole]??1; foreach($roles as $rk=>$label): if((Auth::GAME_ROLE_LEVELS[$rk]??0)>$maxLevel && $rk!==$role) continue;?><option value="<?=$rk?>" <?=$rk===$role?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select><label class="role-mirror-mini" title="Die neue Community-Rolle gleichzeitig als Serverrolle auf alle von dir verwaltbaren Server übernehmen"><input type="checkbox" name="mirror_to_servers" value="1"> auch Server</label></form>
<?php else:?><span class="game-role game-role-<?=$role?>"><?=Security::e($roles[$role]??$role)?></span><?php endif;?>
</td>
<?php foreach($servers as $s): $sid=(int)$s['id']; $sr=$assignments[$p['id']][$sid]??''; $serverRoleManageable=$roleManageable&&($sr===''||Auth::canManageGameRoleTarget($sr));?><td class="server-right-cell"><div class="server-right-inline">
<?php if($isSecondary):?><?php if($sr):?><div class="inherited-role compact"><span class="game-role game-role-<?=$sr?>"><?=Security::e($roles[$sr]??$sr)?></span><small>geerbt</small></div><?php else:?><span class="muted">—</span><?php endif;?>
<?php elseif($serverRoleManageable && ($manageable[$sid]??false)):?>
<form class="server-role-inline-form" method="post" action="/players/<?=$p['id']?>/server-role/<?=$sid?>"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="<?=Security::e($_SERVER['REQUEST_URI']??'/players')?>"><select name="server_role" class="server-role-select role-select <?= $sr?'role-select-'.Security::e($sr):'' ?>" data-server-role-select data-current="<?=Security::e($sr)?>" data-player="<?=Security::e($p['display_name'])?>" data-server="<?=Security::e($s['name'])?>"><option value="" <?=$sr===''?'selected':''?>>Keine Gruppe</option><?php $max=Auth::GAME_ROLE_LEVELS[$maxRoles[$sid]??'spieler']??1;foreach(['spieler','vip','member','admin','founder'] as $rk):if((Auth::GAME_ROLE_LEVELS[$rk]??0)>$max&&$rk!==$sr)continue;?><option value="<?=$rk?>" <?=$sr===$rk?'selected':''?>><?=Security::e($roles[$rk])?></option><?php endforeach;?></select></form>
<label class="server-check" title="<?=Security::e($s['name'])?> für Schnellzuweisung auswählen"><input type="checkbox" name="server_ids[]" value="<?=$sid?>" form="<?=$quickForm?>" aria-label="<?=Security::e($s['name'])?> auswählen"></label>
<?php else:?><?php if($sr):?><span class="game-role game-role-<?=$sr?>"><?=Security::e($roles[$sr]??$sr)?></span><?php else:?><span class="muted">—</span><?php endif;?><?php endif;?>
</div></td><?php endforeach;?>
<td><?php if($isSecondary):?><span class="muted">über Hauptaccount</span><?php elseif($roleManageable && array_filter($manageable)):?>
<form id="<?=$quickForm?>" class="quick-rights-form" method="post" action="/players/<?=$p['id']?>/quick-assign"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="_return" value="<?=Security::e($_SERVER['REQUEST_URI']??'/players')?>"><select name="server_role" class="role-select role-select-<?=$role?>" data-role-color-select aria-label="Servergruppe"><option value="">Keine Gruppe</option><?php $maxQuick=1; foreach($servers as $qs){$qid=(int)$qs['id'];if(!($manageable[$qid]??false))continue;$maxQuick=max($maxQuick,Auth::GAME_ROLE_LEVELS[$maxRoles[$qid]??'spieler']??1);} foreach(['spieler','vip','member','admin','founder'] as $rk):if((Auth::GAME_ROLE_LEVELS[$rk]??0)>$maxQuick)continue;?><option value="<?=$rk?>" <?=$rk===$role?'selected':''?>><?=Security::e($roles[$rk])?></option><?php endforeach;?></select><div class="quick-rights-buttons"><button class="btn btn-secondary btn-small" type="submit" name="scope" value="selected">Auswahl setzen</button><button class="btn btn-primary btn-small" type="submit" name="scope" value="global" data-global-confirm="Diese Servergruppe wirklich auf allen von dir verwaltbaren Servern für diese Person und alle verknüpften Zweitaccounts setzen?">Global setzen</button></div></form>
<?php else:?><span class="muted">Keine Bearbeitung</span><?php endif;?></td>
<td><div class="player-quick-actions"><a class="btn btn-secondary btn-small" href="/players/<?=$p['id']?>"><?=$roleManageable?'Bearbeiten':'Ansehen'?></a><?php if(!empty($p['steam_id64']) && (Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$p['steam_id64'])?>" data-player-name="<?=Security::e((string)$p['display_name'])?>">Aktionen</button><?php endif;?></div></td>
</tr>
<?php endforeach;?>
</tbody></table></div></section>

<?php if($canManage):?>
<section class="panel narrow"><div class="panel-header"><h2>Person manuell anlegen</h2><p>Der Steam-Profilname wird nach erfolgreicher Steam-Synchronisierung automatisch als Anzeigename verwendet. Bei manueller Anlage ist Spieler vorausgewählt. Neue Accounts sind zunächst eigenständige Hauptaccounts und können anschließend als Zweitaccount verknüpft werden.</p></div>
<form method="post" action="/players" class="form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><label>SteamID64<input class="mono" type="text" name="steam_id64" required pattern="7656119[0-9]{10}"></label><label>Community-Rolle<select name="community_role" class="role-select role-select-spieler" data-role-color-select><?php $maxLevel=Auth::GAME_ROLE_LEVELS[$maxCommunityRole]??1;foreach($roles as $rk=>$label):if((Auth::GAME_ROLE_LEVELS[$rk]??0)>$maxLevel)continue;?><option value="<?=$rk?>" <?=$rk==='spieler'?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select></label><button class="btn btn-primary">Person anlegen</button></form></section>
<?php endif;?>
