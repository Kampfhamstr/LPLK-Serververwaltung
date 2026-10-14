<?php use App\Core\Security; use App\Core\Auth; $antiAdPlugin=null; foreach((array)($status['plugins']??[]) as $pl){ $pid=strtolower((string)($pl['id']??'')); $pname=strtolower((string)($pl['name']??'')); if($pid==='lplk.serververwaltung.antiadvertisement' || str_contains($pname,'antiadvertisement')){ $antiAdPlugin=$pl; break; } } $rules=(array)($status['rules']??[]); $weaponTeamLimits=(array)($rules['weapon_team_limits']??[]); $currentWeaponMode=(string)($rules['weapon_mode']??'normal'); $botT=0; $botCt=0; foreach((array)($status['players']??[]) as $bp){ if(empty($bp['is_bot']))continue; $bt=strtoupper((string)($bp['team']??'')); if($bt==='T')$botT++; elseif($bt==='CT')$botCt++; } $layoutOrder=(array)($liveLayout['order']??[]); $layoutCollapsed=(array)($liveLayout['collapsed']??[]); $layoutSizes=(array)($liveLayout['sizes']??[]); $canBotKick=Auth::can('live.bot.kick'); $canBotSlay=Auth::can('live.bot.slay'); $canBotTeam=Auth::can('live.bot.team'); ?>
<div class="page-title-row">
  <div><h1><?=Security::e($server['name'])?> · Live-Verwaltung</h1><p class="muted">Online-Spieler, Serveraktionen und Nachrichten. Live-Daten werden über die SerververwaltungBridge bereitgestellt.</p></div>
  <div class="toolbar"><?php if(Auth::can('players.view')):?><a class="btn btn-secondary" href="/servers/<?=(int)$server['id']?>/ranking">Spielerranglisten</a><?php endif;?><?php $joinServerId=(int)$server['id']; require __DIR__.'/server_join_button.php'; ?><a class="btn btn-secondary" href="/servers/<?=$server['id']?>">Serverdetails</a><button class="btn btn-secondary" type="button" data-live-refresh>Jetzt aktualisieren</button></div>
</div>

<div class="live-summary" data-live-server-id="<?=$server['id']?>" data-ptero-resources-enabled="<?=((($server['connector_type']??'')==='pterodactyl' && Auth::can('server.resources.view'))?'1':'0')?>" data-live-csrf="<?=Security::e(Security::csrfToken())?>" data-can-bot-kick="<?=$canBotKick?'1':'0'?>" data-can-bot-slay="<?=$canBotSlay?'1':'0'?>" data-can-bot-team="<?=$canBotTeam?'1':'0'?>">
  <div class="live-status-card"><span class="summary-label">Bridge</span><strong data-live-bridge class="status-pill <?=$status['bridge_online']?'status-online':'status-offline'?>"><?=$status['bridge_online']?'ONLINE':'OFFLINE'?></strong></div>
  <div class="live-status-card"><span class="summary-label">Map</span><strong data-live-map><?=Security::e((string)($status['map']??'—'))?></strong></div>
  <div class="live-status-card"><span class="summary-label">Spieler</span><strong data-live-count><?=count($status['players']??[])?> / <?=Security::e((string)($status['max_players']??'—'))?></strong></div>
  <div class="live-status-card live-team-score-card"><span class="summary-label">Teampunktzahl</span><strong data-live-team-score><span class="score-ct">CT <?=Security::e((string)($status['ct_score']??0))?></span> : <span class="score-t"><?=Security::e((string)($status['t_score']??0))?> T</span></strong><small data-live-rounds><?=Security::e((string)($status['rounds_played']??0))?> Runden gespielt</small></div>
  <div class="live-status-card"><span class="summary-label">Status aktualisiert</span><strong data-live-updated><?=Security::e((string)($status['updated_at']??'—'))?></strong></div>
</div>

<div class="live-layout-toolbar" data-live-layout-toolbar data-layout-order="<?=Security::e(implode(',',array_map('strval',$layoutOrder)))?>" data-layout-collapsed="<?=Security::e(implode(',',array_map('strval',$layoutCollapsed)))?>" data-layout-sizes="<?=Security::e((string)json_encode($layoutSizes, JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE))?>" data-layout-endpoint="/servers/<?=(int)$server['id']?>/live/layout" data-layout-csrf="<?=Security::e(Security::csrfToken())?>">
  <div class="live-layout-toolbar-main">
    <strong>Live-Kacheln</strong><span class="small-muted" data-live-layout-note>Auf-/zuklappen, Breite wählen oder persönliche Anordnung bearbeiten.</span>
  </div>
  <div class="toolbar">
    <button class="btn btn-secondary btn-small" type="button" data-live-expand-all>Alle aufklappen</button>
    <button class="btn btn-secondary btn-small" type="button" data-live-collapse-all>Alle einklappen</button>
    <button class="btn btn-secondary btn-small" type="button" data-live-layout-edit>Layout bearbeiten</button>
    <button class="btn btn-primary btn-small" type="button" data-live-layout-save>Layout speichern</button>
    <button class="btn btn-secondary btn-small" type="button" data-live-layout-reset>Standardlayout</button>
    <label class="checkbox-line compact"><input type="checkbox" data-live-layout-all-servers> Dieses Layout für alle Server</label>
  </div>
</div>

<?php $isPterodactyl=(($server['connector_type']??'')==='pterodactyl'); $canPteroResources=$isPterodactyl&&Auth::can('server.resources.view'); $canAnyPower=$isPterodactyl&&Auth::canAny(['server.power.start','server.power.restart','server.power.stop','server.power.kill']); ?>
<?php if($isPterodactyl && ($canPteroResources || $canAnyPower)):?>
<section class="panel pterodactyl-control-panel ui-section-primary" id="pterodactyl-control">
  <div class="panel-header split-header">
    <div><h2>Pterodactyl · Serversteuerung & Ressourcen</h2><p>Powerstatus und Ressourcenauslastung kommen direkt aus der Pterodactyl Client API. Die Spieler-Livedaten bleiben weiterhin Fast Live/Bridge-basiert.</p></div>
    <?php if($canPteroResources):?><span class="status-pill" data-ptero-state>WIRD GELADEN</span><?php endif;?>
  </div>

  <?php if($canPteroResources):?>
  <div class="ptero-resource-grid" data-ptero-resource-grid>
    <article class="ptero-resource-card"><span>Status</span><strong data-ptero-state-text>Wird geladen</strong><small data-ptero-state-note>Pterodactyl wird abgefragt …</small></article>
    <article class="ptero-resource-card"><span>Laufzeit</span><strong data-ptero-uptime>—</strong><small>seit dem letzten Containerstart</small></article>
    <article class="ptero-resource-card"><span>CPU</span><strong data-ptero-cpu>—</strong><div class="ptero-meter"><i data-ptero-cpu-bar></i></div><small data-ptero-cpu-detail>—</small></article>
    <article class="ptero-resource-card"><span>RAM</span><strong data-ptero-memory>—</strong><div class="ptero-meter"><i data-ptero-memory-bar></i></div><small data-ptero-memory-detail>—</small></article>
    <article class="ptero-resource-card"><span>Festplatte</span><strong data-ptero-disk>—</strong><div class="ptero-meter"><i data-ptero-disk-bar></i></div><small data-ptero-disk-detail>—</small></article>
    <article class="ptero-resource-card"><span>Netzwerk</span><strong data-ptero-network>—</strong><small>Download / Upload seit Start</small></article>
  </div>
  <div class="small-muted ptero-resource-foot"><span data-ptero-fetched>noch nicht aktualisiert</span><span data-ptero-stale hidden> · zwischengespeicherte Werte</span><span data-ptero-error></span></div>
  <?php endif;?>

  <?php if($canAnyPower):?>
  <div class="ptero-power-toolbar" aria-label="Pterodactyl Serversteuerung">
    <div class="ptero-power-main">
      <?php if(Auth::can('server.power.start')):?><form method="post" action="/servers/<?=$server['id']?>/live/power" class="ptero-power-action" data-confirm="Gameserver wirklich starten?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="signal" value="start"><button class="btn btn-success ptero-power-button" type="submit"><strong>Start</strong><span>Server starten</span></button></form><?php endif;?>
      <?php if(Auth::can('server.power.restart')):?><form method="post" action="/servers/<?=$server['id']?>/live/power" class="ptero-power-action" data-confirm="Gameserver sofort neu starten? Verbundene Spieler werden getrennt."><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="signal" value="restart"><button class="btn btn-warning ptero-power-button" type="submit"><strong>Neu starten</strong><span>Server neu starten</span></button></form><?php endif;?>
      <?php if(Auth::can('server.power.stop')):?><form method="post" action="/servers/<?=$server['id']?>/live/power" class="ptero-power-action" data-confirm="Gameserver sauber stoppen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="signal" value="stop"><button class="btn btn-stop ptero-power-button" type="submit"><strong>Stoppen</strong><span>Sauber herunterfahren</span></button></form><?php endif;?>
    </div>
    <?php if(Auth::can('server.power.kill')):?><details class="ptero-emergency-control"><summary>Notfall: Kill</summary><form method="post" action="/servers/<?=$server['id']?>/live/power" class="ptero-power-action ptero-kill-action"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="signal" value="kill"><label>Zur Bestätigung <strong>KILL</strong> eingeben<input class="mono" type="text" name="confirm_kill" maxlength="4" placeholder="KILL" autocomplete="off" required></label><button class="btn btn-kill" type="submit">Hard Kill ausführen</button><small>Nur verwenden, wenn ein normaler Stop nicht mehr reagiert.</small></form></details><?php endif;?>
  </div>
  <?php endif;?>
</section>
<?php elseif(!$isPterodactyl):?>
<section class="panel ui-section-compact"><div class="panel-header"><h2>Pterodactyl-Steuerung</h2><p>CPU/RAM/Disk, Laufzeit sowie Start/Restart/Stop/Kill sind nur bei einem Pterodactyl-Connector verfügbar.</p></div></section>
<?php endif;?>

<section class="panel active-rules-panel ui-section-primary" id="active-rules" data-rule-dashboard>
  <?php
    $gravity=(int)($rules['gravity']??800);
    $globalSpeed=(float)($rules['global_speed']??1);
    $headshot=!empty($rules['headshot_only']);
    $friendly=!empty($rules['friendly_fire']);
    $infiniteAmmo=(int)($rules['infinite_ammo']??0);
    $weaponModeLabel=(string)($rules['weapon_mode_label']??$rules['weapon_mode']??'Normal');
    $activePreset=trim((string)($rules['active_preset']??''));
    $bridgeVersion=trim((string)($status['version']??''));
  ?>
  <div class="panel-header split-header">
    <div>
      <h2>Live-Regelstatus</h2>
      <p>Ist-Zustand direkt vom laufenden Server<?= $bridgeVersion!=='' ? ' · Bridge '.Security::e($bridgeVersion) : '' ?>. Kachel anklicken oder rechtsklicken, um Schnellaktionen zu öffnen.</p>
    </div>
    <?php if(Auth::can('live.rules')):?><form method="post" action="/servers/<?=$server['id']?>/live/rules-reset" data-confirm="Alle Live-Sonderregeln wirklich auf Standard zurücksetzen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="reset_players" value="1"><button class="btn btn-danger btn-small">Alles zurücksetzen</button></form><?php endif;?>
  </div>

  <div class="rule-status-legend" aria-label="Farblegende">
    <span class="rule-legend rule-legend-normal">Standard / aus</span>
    <span class="rule-legend rule-legend-active">Aktiv</span>
    <span class="rule-legend rule-legend-custom">Vom Standard abweichend</span>
    <span class="small-muted">Linksklick oder Rechtsklick öffnet die Schnellwahl.</span>
  </div>

  <div class="rule-status-grid">
    <div class="rule-status-item rule-status-interactive <?=$gravity===800?'rule-state-default':'rule-state-custom'?>" tabindex="0" data-rule-card data-rule-anchor="#bots-rules">
      <span>Gravity</span><strong><?=$gravity?></strong><small><?=$gravity===800?'Standard':'Sonderwert aktiv'?></small>
      <?php if(Auth::can('live.rules')&&Auth::can('live.gravity')):?><div class="rule-quick-menu" data-rule-menu hidden><strong>Gravity setzen</strong><div class="rule-quick-actions"><?php foreach([200,400,800,1200,1600] as $v):?><form method="post" action="/servers/<?=$server['id']?>/live/rule-quick"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="key" value="gravity"><input type="hidden" name="value" value="<?=$v?>"><button class="btn btn-small <?=$gravity===$v?'btn-active':'btn-secondary'?>"><?=$v?></button></form><?php endforeach;?></div></div><?php endif;?>
    </div>

    <div class="rule-status-item rule-status-interactive <?=abs($globalSpeed-1.0)<.001?'rule-state-default':'rule-state-custom'?>" tabindex="0" data-rule-card data-rule-anchor="#bots-rules">
      <span>Server-Speed</span><strong><?=Security::e(number_format($globalSpeed,2,',','.'))?>x</strong><small><?=abs($globalSpeed-1.0)<.001?'Standard':'Sonderwert aktiv'?></small>
      <?php if(Auth::can('live.rules')&&Auth::can('live.speed')):?><div class="rule-quick-menu" data-rule-menu hidden><strong>Speed setzen</strong><div class="rule-quick-actions"><?php foreach([0.5,0.75,1,1.25,1.5,2] as $v):?><form method="post" action="/servers/<?=$server['id']?>/live/rule-quick"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="key" value="global_speed"><input type="hidden" name="value" value="<?=$v?>"><button class="btn btn-small <?=abs($globalSpeed-$v)<.001?'btn-active':'btn-secondary'?>"><?=number_format($v,2,',','.')?>x</button></form><?php endforeach;?></div></div><?php endif;?>
    </div>

    <div class="rule-status-item rule-status-interactive <?=$headshot?'rule-state-active':'rule-state-default'?>" tabindex="0" data-rule-card data-rule-anchor="#weapon-modes">
      <span>Headshot Only</span><strong><?=$headshot?'AN':'AUS'?></strong><small><?=$headshot?'Aktiv':'Normaler Schaden'?></small>
      <?php if(Auth::can('live.weapon_mode')):?><div class="rule-quick-menu" data-rule-menu hidden><strong>Headshot Only</strong><div class="rule-quick-actions"><form method="post" action="/servers/<?=$server['id']?>/live/rule-quick"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="key" value="headshot_only"><input type="hidden" name="value" value="1"><button class="btn btn-small <?=$headshot?'btn-active':'btn-secondary'?>">AN</button></form><form method="post" action="/servers/<?=$server['id']?>/live/rule-quick"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="key" value="headshot_only"><input type="hidden" name="value" value="0"><button class="btn btn-small <?=!$headshot?'btn-active':'btn-secondary'?>">AUS</button></form></div></div><?php endif;?>
    </div>

    <div class="rule-status-item rule-status-interactive <?=$currentWeaponMode==='normal'?'rule-state-default':'rule-state-active'?>" tabindex="0" data-rule-card data-rule-anchor="#weapon-modes">
      <span>Waffenmodus</span><strong><?=Security::e($weaponModeLabel)?></strong><small><?=$currentWeaponMode==='normal'?'Normal':'Restrict aktiv'?></small>
      <?php if(Auth::can('live.weapon_mode')):?><div class="rule-quick-menu rule-quick-menu-wide" data-rule-menu hidden><strong>Waffenmodus</strong><div class="rule-quick-actions"><?php $quickModes=['normal'=>'Normal','pistols_only'=>'Pistols','deagle_only'=>'Deagle','awp_only'=>'AWP','scout_only'=>'Scout','rifles_only'=>'Rifles','smg_only'=>'SMG','shotguns_only'=>'Shotguns','snipers_only'=>'Snipers','knife_only'=>'Knife','zeus_only'=>'Zeus']; foreach($quickModes as $mk=>$ml):?><form method="post" action="/servers/<?=$server['id']?>/live/rule-quick"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="key" value="weapon_mode"><input type="hidden" name="value" value="<?=Security::e($mk)?>"><button class="btn btn-small <?=$currentWeaponMode===$mk?'btn-active':'btn-secondary'?>"><?=Security::e($ml)?></button></form><?php endforeach;?></div><a class="btn btn-secondary btn-small" href="#weapon-modes">Benutzerdefiniert / Teamlimits</a></div><?php endif;?>
    </div>

    <div class="rule-status-item rule-status-interactive <?=count($weaponTeamLimits)>0?'rule-state-custom':'rule-state-default'?>" tabindex="0" data-rule-card data-rule-anchor="#weapon-modes">
      <span>Waffen-Teamlimits</span><strong><?=count($weaponTeamLimits)?> aktiv</strong><small><?=count($weaponTeamLimits)>0?'Limits gesetzt':'Unbegrenzt'?></small>
      <div class="rule-quick-menu" data-rule-menu hidden><strong>Teamlimits</strong><a class="btn btn-secondary btn-small" href="#weapon-modes">Limits bearbeiten</a></div>
    </div>

    <div class="rule-status-item rule-status-interactive <?=$friendly?'rule-state-active':'rule-state-default'?>" tabindex="0" data-rule-card data-rule-anchor="#bots-rules">
      <span>Friendly Fire</span><strong><?=$friendly?'AN':'AUS'?></strong><small><?=$friendly?'Aktiv':'Deaktiviert'?></small>
      <?php if(Auth::can('live.rules')):?><div class="rule-quick-menu" data-rule-menu hidden><strong>Friendly Fire</strong><div class="rule-quick-actions"><form method="post" action="/servers/<?=$server['id']?>/live/rule-quick"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="key" value="friendly_fire"><input type="hidden" name="value" value="1"><button class="btn btn-small <?=$friendly?'btn-active':'btn-secondary'?>">AN</button></form><form method="post" action="/servers/<?=$server['id']?>/live/rule-quick"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="key" value="friendly_fire"><input type="hidden" name="value" value="0"><button class="btn btn-small <?=!$friendly?'btn-active':'btn-secondary'?>">AUS</button></form></div></div><?php endif;?>
    </div>

    <div class="rule-status-item rule-status-interactive <?=$infiniteAmmo>0?'rule-state-active':'rule-state-default'?>" tabindex="0" data-rule-card data-rule-anchor="#bots-rules">
      <span>Infinite Ammo</span><strong><?=$infiniteAmmo===0?'AUS':($infiniteAmmo===1?'OHNE RELOAD':'MIT RELOAD')?></strong><small><?=$infiniteAmmo>0?'Aktiv':'Deaktiviert'?></small>
      <?php if(Auth::can('live.rules')):?><div class="rule-quick-menu" data-rule-menu hidden><strong>Infinite Ammo</strong><div class="rule-quick-actions"><?php foreach([0=>'Aus',1=>'Ohne Reload',2=>'Mit Reload'] as $v=>$label):?><form method="post" action="/servers/<?=$server['id']?>/live/rule-quick"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="key" value="infinite_ammo"><input type="hidden" name="value" value="<?=$v?>"><button class="btn btn-small <?=$infiniteAmmo===$v?'btn-active':'btn-secondary'?>"><?=Security::e($label)?></button></form><?php endforeach;?></div></div><?php endif;?>
    </div>

    <div class="rule-status-item rule-status-interactive <?=($botT+$botCt)>0?'rule-state-active':'rule-state-default'?>" tabindex="0" data-rule-card data-rule-anchor="#bots-rules">
      <span>Bots</span><strong data-live-bot-count>T <?=$botT?> · CT <?=$botCt?></strong><small data-live-bot-note><?=($botT+$botCt)>0?'Bots vorhanden':'Keine Bots'?></small>
      <?php if(Auth::can('live.bots')):?><div class="rule-quick-menu" data-rule-menu hidden><strong>Bots</strong><div class="rule-quick-actions"><form method="post" action="/servers/<?=$server['id']?>/live/bots"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="add_t"><button class="btn btn-secondary btn-small">+ T</button></form><form method="post" action="/servers/<?=$server['id']?>/live/bots"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="add_ct"><button class="btn btn-secondary btn-small">+ CT</button></form><form method="post" action="/servers/<?=$server['id']?>/live/bots"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="kick"><button class="btn btn-danger btn-small">Alle kicken</button></form></div></div><?php endif;?>
    </div>

    <div class="rule-status-item rule-status-interactive <?=$activePreset!==''&&$activePreset!=='—'?'rule-state-active':'rule-state-default'?>" tabindex="0" data-rule-card data-rule-anchor="#presets">
      <span>Preset</span><strong><?=Security::e($activePreset!==''?$activePreset:'—')?></strong><small><?=$activePreset!==''&&$activePreset!=='—'?'Preset aktiv':'Kein Preset'?></small>
      <div class="rule-quick-menu" data-rule-menu hidden><strong>Presets</strong><a class="btn btn-secondary btn-small" href="#presets">Preset auswählen</a></div>
    </div>
  </div>

  <?php $specialPlayers=(array)($rules['special_players']??[]); if($specialPlayers):?><div class="active-special-list"><strong>Aktive Spieler-Sondermodi:</strong><?php foreach($specialPlayers as $sp):?><span class="status-pill status-warning"><?=Security::e((string)($sp['name']??$sp['steam_id64']??'Spieler'))?>: <?=Security::e(implode(', ',(array)($sp['modes']??[])))?></span><?php endforeach;?></div><?php endif;?>
</section>

<?php if(!$status['bridge_available'] || !$status['bridge_online']):?>
<section class="panel bridge-warning"><div class="panel-header"><h2>Live-Bridge nicht erreichbar</h2></div>
  <p><?=Security::e((string)($status['reason']??'Der Live-Status ist aktuell nicht verfügbar.'))?></p>
  <p class="muted">SwiftlyS2: <span class="mono">SerververwaltungBridge</span> v1.5.4 oder neuer nach <span class="mono">game/csgo/addons/swiftlys2/plugins/SerververwaltungBridge/</span> installieren. Danach erzeugt das Plugin automatisch den Status- und Befehlsbereich.</p>
</section>
<?php endif;?>

<nav class="subnav" aria-label="Live-Verwaltung">
  <?php if($isPterodactyl && ($canPteroResources || $canAnyPower)):?><a href="#pterodactyl-control">Pterodactyl</a><?php endif;?>
  <a href="#players">Online-Spieler</a>
  <a href="#server-chat">Serverchat</a>
  <a href="#anti-advertisement">Werbeschutz</a>
  <?php if(!empty($whitelistManager['detected']) && Auth::can('live.whitelist')):?><a href="#whitelist"><?=Security::e((string)$whitelistManager['mode_label'])?></a><?php endif;?>
  <a href="#active-rules">Aktive Regeln</a>
  <?php if(Auth::can('live.bots') || Auth::can('live.rules')):?><a href="#bots-rules">Bots & Regeln</a><?php endif;?>
  <?php if(Auth::can('live.weapon_mode')):?><a href="#weapon-modes">Waffenmodi</a><?php endif;?>
  <?php if(Auth::can('live.presets')):?><a href="#presets">Presets</a><?php endif;?>
  <a href="#server-actions">Serveraktionen</a>
  <?php if(Auth::can('server.console.view') || Auth::can('server.console.execute')):?><a href="#admin-log">Admin-Log</a><?php endif;?>
  <a href="#messages">Servernachrichten</a>
  <?php if(Auth::can('server.console.view') || Auth::can('server.console.execute') || Auth::can('live.cvar')):?><a href="#advanced">Erweitert</a><?php endif;?>
  <?php if(Auth::can('live.ban')):?><a href="#bans">Banliste</a><?php endif;?>
</nav>

<div class="live-section-grid live-modular-dashboard" data-live-layout-root>
<section class="panel ui-section-compact" id="anti-advertisement">
  <div class="panel-header split-header">
    <div><h2>Werbeschutz</h2><p>SwiftlyS2 AntiAdvertisement blockiert Werbe-/Spam-Nachrichten und typische Werbe-Bot-Namen direkt auf dem Gameserver.</p></div>
    <?php if($antiAdPlugin):?><span class="status-pill status-online">INSTALLIERT · v<?=Security::e((string)($antiAdPlugin['version']??'—'))?></span><?php else:?><span class="status-pill status-warning">NICHT INSTALLIERT</span><?php endif;?>
  </div>
  <?php if($antiAdPlugin):?>
    <div class="rule-control-grid">
      <div class="action-card"><h3>Aktiver Schutz</h3><p class="muted">Chatwerbung wird vor der normalen Ausgabe gestoppt. Verdächtige Werbenamen werden serverseitig erkannt und vor dem Kick auf einen neutralen Namen zensiert.</p><p><strong>Gebannte SteamIDs:</strong> kein Chat und keine öffentliche Bridge-/Werbeschutz-Erfolgsmeldung; der sichtbare Name wird vor dem Disconnect neutralisiert.</p></div>
      <div class="action-card"><h3>Konfiguration</h3><p class="muted">Die Plugin-Konfiguration liegt direkt auf dem Gameserver.</p><code class="mono">addons/swiftlys2/data/serververwaltung_antiadvertisement/config.json</code><p class="small-muted">Blacklist, Allowlist, Eskalation und Erfolgsmeldungen sind dort konfigurierbar.</p></div>
    </div>
  <?php else:?>
    <div class="alert alert-info">Installiere <strong>SerververwaltungAntiAdvertisement v1.0.12</strong> im SwiftlyS2-Pluginordner. Die Bridge erkennt das Plugin anschließend automatisch in der Plugin-Inventarliste.</div>
  <?php endif;?>
</section>
<section class="panel ui-section-primary" id="players">
  <div class="panel-header split-header"><div><h2>Online-Spieler</h2><p>Menschen und Bots gemeinsam alphabetisch A–Z. Aktuelle Kills, CS2-Punkte und Teamzugehörigkeit kommen direkt von der Bridge. Tote Spieler und Zuschauer bleiben sichtbar.</p></div><label class="checkbox-line compact"><input type="checkbox" checked data-live-auto> Automatisch aktualisieren</label></div>
  <div class="table-wrap"><table class="live-player-table" data-ui-adaptive="live-players"><thead><tr><th>Spieler</th><th>SteamID64</th><th>Rolle</th><th>Team</th><th>Kills</th><th>Punkte</th><th>Status</th><th>HP</th><th>Armor</th><th>Ping</th><th>Kommunikation</th><th>Verbindung</th><th>Aktion</th></tr></thead><tbody data-live-player-body>
  <?php if(empty($status['players'])):?><tr data-empty-row><td colspan="13" class="muted">Aktuell keine Spielerdaten verfügbar.</td></tr><?php endif;?>
  <?php foreach(($status['players']??[]) as $p):
    $isBot=!empty($p['is_bot']);
    $role=$isBot?'bot':(string)($p['community_role']??'');
    $teamNorm=strtolower(trim((string)($p['team']??'')));
    $isSpectator=in_array($teamNorm,['spectator','spectators','spec'],true);
    $isAlive=!$isSpectator && !empty($p['alive']);
    $lifeClass=$isSpectator?'is-player-spectator':($isAlive?'is-player-alive':'is-player-dead');
    $botUserId=(int)($p['user_id']??0); $botPlayerId=(int)($p['player_id']??-1);
  ?>
    <tr class="live-player-row <?=$lifeClass?> live-role-<?=Security::e(strtolower($role !== '' ? $role : 'spieler'))?>" data-player-life-state="<?=Security::e($isSpectator?'spectator':($isAlive?'alive':'dead'))?>">
      <td data-player-status-host data-player-steamid="<?=Security::e($isBot?'':(string)($p['steam_id64']??''))?>"><strong><?=Security::e((string)($p['name']??'Unbekannt'))?></strong><?php if($isBot):?><div class="small-muted">CS2-Bot</div><?php elseif(!empty($p['nickname_override'])):?><div class="small-muted">Server-Nickname aktiv</div><?php endif;?><?php if(!$isBot&&!empty($p['is_secondary_account'])):?><div class="small-muted">Zweitaccount</div><?php endif;?></td>
      <td class="mono"><?=$isBot?'<span class="bot-id-label">BOT</span>':Security::e((string)($p['steam_id64']??''))?></td>
      <td><?=$isBot?'<span class="game-role game-role-bot">BOT</span>':($role!==''?'<span class="game-role game-role-'.Security::e($role).'">'.Security::e(ucfirst($role)).'</span>':'<span class="muted">nicht erfasst</span>')?></td>
      <td><span class="team-badge team-<?=Security::e(strtolower((string)($p['team']??'spec')))?>"><?=Security::e((string)($p['team']??'—'))?></span></td>
      <td class="numeric-stat"><?=Security::e((string)($p['kills']??0))?></td>
      <td class="numeric-stat"><?=Security::e((string)($p['score']??0))?></td>
      <td><span class="player-life-state <?=$isSpectator?'player-life-spec':($isAlive?'player-life-alive':'player-life-dead')?>"><?=$isSpectator?'Zuschauer':($isAlive?'Lebend':'Tot')?></span><?php if(!$isBot&&!empty($p['frozen'])):?><span class="status-note"> · Gefreezt</span><?php endif;?></td>
      <td><?=Security::e((string)($p['health']??'—'))?></td>
      <td><?=Security::e((string)($p['armor']??'—'))?></td>
      <td><?=Security::e((string)($p['ping']??'—'))?> ms</td>
      <td><?php if($isBot):?><span class="muted">—</span><?php else:?><div class="compact-status-list"><?php if(!empty($p['voice_muted'])):?><span>Mute</span><?php endif;?><?php if(!empty($p['gagged'])):?><span>Gag</span><?php endif;?><?php if(!empty($p['noclip'])):?><span>Noclip</span><?php endif;?><?php if(empty($p['voice_muted'])&&empty($p['gagged'])&&empty($p['noclip'])):?><span class="muted">—</span><?php endif;?></div><?php endif;?></td>
      <td><?=Security::e((string)($p['connected_human']??'—'))?></td>
      <td>
        <?php if(Auth::canServer((int)$server['id'],'live')):?>
          <?php if($isBot):?>
            <div class="bot-player-actions">
              <?php if($canBotKick):?><form method="post" action="/servers/<?=$server['id']?>/live/bot-player" data-confirm="Diesen Bot wirklich kicken?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="kick"><input type="hidden" name="user_id" value="<?=$botUserId?>"><input type="hidden" name="player_id" value="<?=$botPlayerId?>"><button class="btn btn-danger btn-small" type="submit">Kicken</button></form><?php endif;?>
              <?php if($canBotSlay):?><form method="post" action="/servers/<?=$server['id']?>/live/bot-player" data-confirm="Diesen Bot wirklich töten?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="slay"><input type="hidden" name="user_id" value="<?=$botUserId?>"><input type="hidden" name="player_id" value="<?=$botPlayerId?>"><button class="btn btn-secondary btn-small" type="submit">Kill</button></form><?php endif;?>
              <?php if($canBotTeam):?><form method="post" action="/servers/<?=$server['id']?>/live/bot-player" class="bot-team-form"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="team"><input type="hidden" name="user_id" value="<?=$botUserId?>"><input type="hidden" name="player_id" value="<?=$botPlayerId?>"><select name="team" aria-label="Bot-Team"><option value="ct" <?=$teamNorm==='ct'?'selected':''?>>CT</option><option value="t" <?=$teamNorm==='t'?'selected':''?>>T</option><option value="spectator" <?=$isSpectator?'selected':''?>>Spec</option></select><button class="btn btn-secondary btn-small" type="submit">Setzen</button></form><?php endif;?>
              <?php if(!$canBotKick&&!$canBotSlay&&!$canBotTeam):?><span class="muted">—</span><?php endif;?>
            </div>
          <?php else:?><div class="player-quick-actions"><a class="btn btn-secondary btn-small" href="/servers/<?=$server['id']?>/live/player/<?=rawurlencode((string)($p['steam_id64']??''))?>">Verwalten</a><?php if(!empty($p['steam_id64']) && (Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$p['steam_id64'])?>" data-player-name="<?=Security::e((string)($p['name']??'Unbekannt'))?>" data-player-server-id="<?=(int)$server['id']?>">Aktionen</button><?php endif;?></div><?php endif;?>
        <?php else:?><span class="muted">—</span><?php endif;?>
      </td>
    </tr>
  <?php endforeach;?>
  </tbody></table></div>
</section>

<section class="panel live-server-chat-panel ui-section-primary" id="server-chat">
  <div class="panel-header split-header">
    <div><h2>Serverchat</h2><p>Dauerhaft im Webpanel archiviert. Namen werden nach der aktuellen Community-Rolle farbig dargestellt.</p></div>
    <div class="toolbar"><span class="status-pill" data-server-chat-total><?= (int)($serverChatTotal??0) ?> gesamt</span><a class="btn btn-secondary btn-small" href="/servers/<?=$server['id']?>/chat">Archiv & Suche</a></div>
  </div>
  <div class="server-chat-list" data-live-server-chat data-server-id="<?=$server['id']?>">
    <?php if(empty($serverChat)):?><div class="muted server-chat-empty">Noch keine Chatnachrichten gespeichert. Mit der aktuellen Bridge werden neue Nachrichten automatisch übernommen.</div><?php endif;?>
    <?php $chatDayPrevious=null; foreach(($serverChat??[]) as $m): $chatRole=strtolower((string)($m['community_role']??'spieler')); $chatTs=strtotime((string)($m['sent_at']??''))?:time(); $chatDay=date('Y-m-d',$chatTs); if($chatDay!==$chatDayPrevious): $today=date('Y-m-d'); $yesterday=date('Y-m-d',time()-86400); $chatDayLabel=$chatDay===$today?'Heute · '.date('d.m.Y',$chatTs):($chatDay===$yesterday?'Gestern · '.date('d.m.Y',$chatTs):date('d.m.Y',$chatTs)); ?>
      <div class="server-chat-day-separator" data-chat-day="<?=Security::e($chatDay)?>"><span><?=Security::e($chatDayLabel)?></span></div>
      <?php $chatDayPrevious=$chatDay; endif;?>
      <article class="server-chat-message game-chat-role-<?=Security::e($chatRole)?>" data-chat-message-id="<?=(int)$m['id']?>">
        <div class="server-chat-meta" data-player-status-host data-player-steamid="<?=Security::e((string)($m['steam_id64']??''))?>">
          <time title="<?=Security::e((string)$m['sent_at'])?>"><?=Security::e(date('d.m.Y H:i:s',strtotime((string)$m['sent_at'])?:time()))?></time>
          <strong class="game-role-text game-role-text-<?=Security::e($chatRole)?>"><?=Security::e((string)$m['player_name'])?></strong>
          <span class="game-role game-role-<?=Security::e($chatRole)?>"><?=Security::e(ucfirst($chatRole))?></span>
          <span class="team-badge team-<?=Security::e(strtolower((string)$m['team']))?>"><?=Security::e((string)$m['team'])?></span>
          <span class="status-pill"><?=!empty($m['team_only'])?'TEAM':'ALL'?></span>
          <?php if(!empty($m['steam_id64']) && (Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$m['steam_id64'])?>" data-player-name="<?=Security::e((string)$m['player_name'])?>" data-player-server-id="<?=(int)$server['id']?>" data-player-chat-id="<?=(int)$m['id']?>">Aktionen</button><?php endif;?>
        </div>
        <div class="server-chat-text"><?=nl2br(Security::e((string)$m['message']))?></div>
      </article>
    <?php endforeach;?>
  </div>
</section>

<?php if(Auth::can('live.bots') || Auth::can('live.rules')):?><section class="panel ui-section-primary" id="bots-rules">
  <div class="panel-header"><h2>Bots & Spielregeln</h2><p>Häufige Serverregeln als sichere, validierte Schnellsteuerung. Änderungen werden vollständig protokolliert.</p></div>
  <div class="rule-control-grid">
    <?php if(Auth::can('live.bots')):?><div class="action-card form-stack"><div><h3>Bots</h3><p class="muted" data-live-bot-count data-live-bot-prefix="Aktuell ">Aktuell T <?=$botT?> · CT <?=$botCt?></p></div><div class="button-row">
      <form method="post" action="/servers/<?=$server['id']?>/live/bots"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="add_t"><button class="btn btn-secondary">+ T-Bot</button></form>
      <form method="post" action="/servers/<?=$server['id']?>/live/bots"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="add_ct"><button class="btn btn-secondary">+ CT-Bot</button></form>
      <form method="post" action="/servers/<?=$server['id']?>/live/bots" data-confirm="Alle Bots wirklich kicken?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="kick"><button class="btn btn-danger">Alle Bots kicken</button></form>
    </div><form method="post" action="/servers/<?=$server['id']?>/live/bots" class="inline-action-form"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="quota"><label>Bot-Quota<input type="number" name="value" min="0" max="30" value="<?=Security::e((string)($rules['bot_quota']??0))?>"></label><button class="btn btn-secondary">Setzen</button></form><form method="post" action="/servers/<?=$server['id']?>/live/bots" class="inline-action-form"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="difficulty"><label>Schwierigkeit<select name="value"><?php for($i=0;$i<=5;$i++):?><option value="<?=$i?>" <?=((int)($rules['bot_difficulty']??2)===$i)?'selected':''?>><?=$i?></option><?php endfor;?></select></label><button class="btn btn-secondary">Setzen</button></form><div class="button-row"><form method="post" action="/servers/<?=$server['id']?>/live/bots"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="stop"><input type="hidden" name="enabled" value="1"><button class="btn btn-secondary">Bots stoppen</button></form><form method="post" action="/servers/<?=$server['id']?>/live/bots"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="stop"><input type="hidden" name="enabled" value="0"><button class="btn btn-secondary">Bots freigeben</button></form></div></div><?php endif;?>

    <?php if(Auth::can('live.rules') && Auth::can('live.gravity') && Auth::can('live.speed')):?><form method="post" action="/servers/<?=$server['id']?>/live/rules" class="action-card form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="group" value="physics"><div><h3>Physik & Speed</h3><p class="muted">Serverweite Werte; Spieler können zusätzlich individuell überschrieben werden.</p></div><label>Gravity<input type="number" name="gravity" min="100" max="2000" step="50" value="<?=Security::e((string)($rules['gravity']??800))?>"></label><div class="quick-value-row"><button type="button" class="btn btn-secondary btn-small" data-fill-value="200">200</button><button type="button" class="btn btn-secondary btn-small" data-fill-value="400">400</button><button type="button" class="btn btn-secondary btn-small" data-fill-value="800">800</button><button type="button" class="btn btn-secondary btn-small" data-fill-value="1200">1200</button></div><label>Server-Speed<select name="global_speed"><?php foreach([0.5,0.75,1,1.25,1.5,2,3] as $v):?><option value="<?=$v?>" <?=abs((float)($rules['global_speed']??1)-$v)<.001?'selected':''?>><?=number_format($v,2,',','.')?>x</option><?php endforeach;?></select></label><button class="btn btn-primary">Physik übernehmen</button></form><?php endif;?>

    <?php if(Auth::can('live.rules')):?><form method="post" action="/servers/<?=$server['id']?>/live/rules" class="action-card form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="group" value="gameplay"><div><h3>Gameplay</h3><p class="muted">Munition, Kaufregeln, Movement und Schadensskalierung.</p></div><label class="checkbox-line"><input type="checkbox" name="friendly_fire" value="1" <?=!empty($rules['friendly_fire'])?'checked':''?>> Friendly Fire</label><label>Infinite Ammo<select name="infinite_ammo"><option value="0">Aus</option><option value="1" <?=((int)($rules['infinite_ammo']??0)===1)?'selected':''?>>Unendlich ohne Nachladen</option><option value="2" <?=((int)($rules['infinite_ammo']??0)===2)?'selected':''?>>Unendlich mit Nachladen</option></select></label><label>Kaufen überall<select name="buy_anywhere"><option value="0">Aus</option><option value="1" <?=((int)($rules['buy_anywhere']??0)===1)?'selected':''?>>Beide Teams</option><option value="2" <?=((int)($rules['buy_anywhere']??0)===2)?'selected':''?>>T</option><option value="3" <?=((int)($rules['buy_anywhere']??0)===3)?'selected':''?>>CT</option></select></label><label class="checkbox-line"><input type="checkbox" name="bunnyhop" value="1" <?=!empty($rules['bunnyhop'])?'checked':''?>> Bunnyhop erlauben</label><label class="checkbox-line"><input type="checkbox" name="auto_bhop" value="1" <?=!empty($rules['auto_bhop'])?'checked':''?>> Auto-Bunnyhop</label><label>Airaccelerate<input type="number" name="airaccelerate" min="1" max="2000" step="1" value="<?=Security::e((string)($rules['airaccelerate']??12))?>"></label><label>Fallschaden-Skalierung<input type="number" name="fall_damage_scale" min="0" max="5" step="0.1" value="<?=Security::e((string)($rules['fall_damage_scale']??1))?>"></label><label>Damage-Multiplikator<select name="damage_scale"><?php foreach([0,0.5,1,2,5,10] as $v):?><option value="<?=$v?>" <?=abs((float)($rules['damage_scale']??1)-$v)<.001?'selected':''?>><?=$v?>x</option><?php endforeach;?></select></label><button class="btn btn-primary">Gameplay übernehmen</button></form>

    <form method="post" action="/servers/<?=$server['id']?>/live/rules" class="action-card form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="group" value="match"><div><h3>Match & Economy</h3><p class="muted">Rundenzeit, Kaufzeit, Geld und Teambalance.</p></div><label>Freeze-Time<input type="number" name="freeze_time" min="0" max="60" value="<?=Security::e((string)($rules['freeze_time']??15))?>"></label><label>Rundenzeit (Min.)<input type="number" name="round_time" min="0.5" max="60" step="0.1" value="<?=Security::e((string)($rules['round_time']??1.92))?>"></label><label>Buy-Time (Sek.)<input type="number" name="buy_time" min="0" max="9999" value="<?=Security::e((string)($rules['buy_time']??20))?>"></label><label>Startgeld<input type="number" name="start_money" min="0" max="65535" value="<?=Security::e((string)($rules['start_money']??800))?>"></label><label>Maxgeld<input type="number" name="max_money" min="0" max="65535" value="<?=Security::e((string)($rules['max_money']??16000))?>"></label><label>Maxrounds<input type="number" name="max_rounds" min="0" max="100" value="<?=Security::e((string)($rules['max_rounds']??24))?>"></label><label class="checkbox-line"><input type="checkbox" name="auto_team_balance" value="1" <?=!empty($rules['auto_team_balance'])?'checked':''?>> Auto-Team-Balance</label><label>Teamlimit<input type="number" name="limitteams" min="0" max="10" value="<?=Security::e((string)($rules['limitteams']??2))?>"></label><button class="btn btn-primary">Matchwerte übernehmen</button></form><?php endif;?>
  </div>
</section><?php endif;?>

<?php if(Auth::can('live.weapon_mode')):?><section class="panel ui-section-primary" id="weapon-modes"><div class="panel-header"><h2>Headshot & Weapon Restricted</h2><p>Vordefinierte Trainings-/Funmodi plus benutzerdefinierte Waffenliste.</p></div><div class="rule-control-grid">
  <div class="action-card form-stack"><h3>Headshot Only</h3><p class="muted">Nutzt den nativen CS2-Schadensmodus. Dein separates HeadshotOnly-Plugin wird dadurch nicht verändert.</p><div class="button-row"><form method="post" action="/servers/<?=$server['id']?>/live/rules"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="group" value="headshot"><input type="hidden" name="enabled" value="1"><button class="btn btn-primary">Headshot Only AN</button></form><form method="post" action="/servers/<?=$server['id']?>/live/rules"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="group" value="headshot"><input type="hidden" name="enabled" value="0"><button class="btn btn-secondary">AUS</button></form></div></div>
  <form method="post" action="/servers/<?=$server['id']?>/live/weapon-mode" class="action-card form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <h3>Waffenmodus & Teamlimits</h3>
    <label>Preset
      <select name="mode">
        <?php $modeLabels=['normal'=>'Normal','pistols_only'=>'Pistols Only','deagle_only'=>'Deagle Only','awp_only'=>'AWP Only','scout_only'=>'Scout Only','rifles_only'=>'Rifles Only','smg_only'=>'SMG Only','shotguns_only'=>'Shotguns Only','snipers_only'=>'Snipers Only','knife_only'=>'Knife Only','zeus_only'=>'Zeus Only','grenades_only'=>'Grenades Only','custom'=>'Benutzerdefiniert']; foreach($modeLabels as $modeKey=>$modeLabel):?>
          <option value="<?=Security::e($modeKey)?>" <?=$currentWeaponMode===$modeKey?'selected':''?>><?=Security::e($modeLabel)?></option>
        <?php endforeach;?>
      </select>
    </label>
    <details>
      <summary>Benutzerdefinierte Waffen auswählen</summary>
      <div class="weapon-checkbox-grid"><?php foreach($weapons as $cat=>$items):?><fieldset><legend><?=Security::e($cat)?></legend><?php foreach($items as $key=>$label):?><label class="checkbox-line"><input type="checkbox" name="allowed_weapons[]" value="<?=Security::e($key)?>"> <?=Security::e($label)?></label><?php endforeach;?></fieldset><?php endforeach;?></div>
    </details>
    <details class="weapon-limit-details" <?=!empty($weaponTeamLimits)?'open':''?>>
      <summary>Maximale Anzahl je Waffe und Team</summary>
      <p class="muted">0 = unbegrenzt. Beispiel: AWP = 1 erlaubt gleichzeitig höchstens einen AWP-Träger pro T- und CT-Team. Kauf und Pickup werden von der Bridge geprüft.</p>
      <div class="quick-value-row">
        <button type="button" class="btn btn-secondary btn-small" data-weapon-limit-preset="awp-one">AWP: 1 / Team</button>
        <button type="button" class="btn btn-secondary btn-small" data-weapon-limit-preset="snipers-one">Sniper: 1 / Team</button>
        <button type="button" class="btn btn-secondary btn-small" data-weapon-limit-preset="clear">Limits löschen</button>
      </div>
      <div class="weapon-limit-grid">
        <?php foreach($weapons as $cat=>$items): foreach($items as $key=>$label):?>
          <label><?=Security::e($label)?><input type="number" min="0" max="10" step="1" name="team_limits[<?=Security::e($key)?>]" data-weapon-limit="<?=Security::e($key)?>" value="<?=Security::e((string)($weaponTeamLimits[$key]??0))?>"></label>
        <?php endforeach; endforeach;?>
      </div>
      <label class="checkbox-line"><input type="checkbox" name="team_limits_include_bots" value="1" <?=!empty($rules['weapon_team_limits_include_bots'])?'checked':''?>> Bots bei Teamlimits mitzählen</label>
    </details>
    <label class="checkbox-line"><input type="checkbox" name="apply_now" value="1"> Aktuelle Waffen sofort ersetzen (bei Fun-/Training empfohlen)</label>
    <button class="btn btn-primary">Waffenmodus & Limits übernehmen</button>
  </form>
</div></section><?php endif;?>

<?php if(Auth::can('live.rules')):?><section class="panel ui-section-primary" id="group-actions"><div class="panel-header"><h2>Gruppenaktionen</h2><p>Aktionen für T, CT oder alle menschlichen Spieler.</p></div><div class="rule-control-grid"><form method="post" action="/servers/<?=$server['id']?>/live/scope" class="action-card form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><label>Ziel<select name="scope"><option value="all">Alle</option><option value="t">T</option><option value="ct">CT</option></select></label><label>Aktion<select name="operation"><option value="respawn">Respawn</option><option value="freeze">Freeze</option><option value="unfreeze">Unfreeze</option><option value="health">Health setzen</option><option value="armor">Armor setzen</option><option value="godmode_on">Godmode AN</option><option value="godmode_off">Godmode AUS</option></select></label><label>Wert (für HP/Armor)<input type="number" name="value" min="0" max="500" value="100"></label><button class="btn btn-primary">Gruppenaktion ausführen</button></form><div class="action-card form-stack" id="round-control"><h3>Runde & Teams</h3><div class="button-row"><form method="post" action="/servers/<?=$server['id']?>/live/round-control" data-confirm="Teams wirklich tauschen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="swap_teams"><button class="btn btn-secondary">Teams tauschen</button></form><form method="post" action="/servers/<?=$server['id']?>/live/round-control" data-confirm="Teams wirklich neu mischen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="scramble_teams"><button class="btn btn-secondary">Teams mischen</button></form></div><div class="button-row"><form method="post" action="/servers/<?=$server['id']?>/live/warmup"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="start"><button class="btn btn-secondary">Warmup starten</button></form><form method="post" action="/servers/<?=$server['id']?>/live/warmup"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="operation" value="end"><button class="btn btn-secondary">Warmup beenden</button></form></div></div></div></section><?php endif;?>

<?php if(Auth::can('live.presets')):?><section class="panel ui-section-primary" id="presets"><div class="panel-header"><h2>Regel-Presets</h2><p>Komplette Regelsets mit optionalem automatischem Reset nach Mapwechsel, Runden oder Minuten.</p></div><div class="rule-control-grid"><form method="post" action="/servers/<?=$server['id']?>/live/preset" class="action-card form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="preset_action" value="apply"><label>Preset<select name="preset"><?php foreach(($rulePresets??[]) as $preset):?><option value="<?=Security::e((string)$preset['key'])?>"><?=Security::e((string)$preset['name'])?><?=empty($preset['builtin'])?' · Eigen':''?></option><?php endforeach;?></select></label><label>Gültigkeit<select name="duration"><option value="manual">Bis manuell zurückgesetzt</option><option value="map">Bis Mapwechsel</option><option value="rounds">X Runden</option><option value="minutes">X Minuten</option></select></label><div class="preset-duration-grid"><label>Runden<input type="number" name="rounds" min="1" max="30" value="1"></label><label>Minuten<input type="number" name="minutes" min="1" max="240" value="10"></label></div><button class="btn btn-primary">Preset anwenden</button></form><form method="post" action="/servers/<?=$server['id']?>/live/preset" class="action-card form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="preset_action" value="save_current"><h3>Aktuellen Zustand speichern</h3><p class="muted">Speichert den von der Bridge gemeldeten Live-Regelstatus als eigenes Preset.</p><label>Name<input type="text" name="name" maxlength="48" placeholder="Mein Fun-Modus" required></label><button class="btn btn-secondary">Als Preset speichern</button></form><?php $custom=array_values(array_filter((array)($rulePresets??[]),static fn($p)=>empty($p['builtin']))); if($custom):?><form method="post" action="/servers/<?=$server['id']?>/live/preset" class="action-card form-stack" data-confirm="Eigenes Preset wirklich löschen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="preset_action" value="delete"><h3>Eigenes Preset löschen</h3><label>Preset<select name="preset"><?php foreach($custom as $preset):?><option value="<?=Security::e((string)$preset['key'])?>"><?=Security::e((string)$preset['name'])?></option><?php endforeach;?></select></label><button class="btn btn-danger">Löschen</button></form><?php endif;?></div></section><?php endif;?>

<?php if(!empty($whitelistManager['detected']) && Auth::can('live.whitelist')):?>
<section class="panel whitelist-live-panel ui-section-primary" id="whitelist">
  <?php
    $whitelistReadOnly=isset($whitelistManager['editable'])&&!$whitelistManager['editable'];
    $whitelistPendingAdd=[];
    $whitelistPendingRemove=[];
    foreach(($whitelistPending??[]) as $pendingChange){
      foreach((array)($pendingChange['added']??[]) as $sid)$whitelistPendingAdd[(string)$sid]=true;
      foreach((array)($pendingChange['removed']??[]) as $sid)$whitelistPendingRemove[(string)$sid]=true;
    }
  ?>
  <div class="panel-header split-header">
    <div>
      <h2><?=Security::e((string)$whitelistManager['mode_label'])?> verwalten</h2>
      <p>
        SwiftlyS2 Plugin <strong><?=Security::e((string)($whitelistManager['plugin']['name']??'Whitelist'))?></strong>
        <?=Security::e((string)($whitelistManager['plugin']['version']??''))?> erkannt.
        Änderungen laufen über <span class="mono"><?=Security::e((string)$whitelistManager['add_command'])?></span> /
        <span class="mono"><?=Security::e((string)$whitelistManager['remove_command'])?></span>; kein Plugin-Reload nötig.
      </p>
    </div>
    <div class="whitelist-summary"><strong><?=count($whitelistManager['entries']??[])?></strong><span>Einträge</span></div>
  </div>

  <?php if((int)$whitelistManager['mode']===2):?>
    <div class="alert alert-warning"><strong>Blacklist-Modus aktiv.</strong> Einträge in dieser Liste werden vom Server abgewiesen. Hinzufügen bedeutet hier also sperren.</div>
  <?php endif;?>

  <?php if(!empty($whitelistManager['read_error'])):?>
    <div class="alert alert-warning">Listendatei konnte nicht gelesen werden: <?=Security::e((string)$whitelistManager['read_error'])?></div>
  <?php elseif(empty($whitelistManager['config_loaded'])):?>
    <div class="alert alert-info">Plugin-Config wurde nicht gelesen. Verwendet werden die Standardwerte <span class="mono">wl</span>, <span class="mono">uwl</span> und Modus Whitelist.</div>
  <?php endif;?>

  <?php if(!empty($whitelistManager['lplk'])):?>
    <?php if(!empty($whitelistManager['state_error'])):?><div class="alert alert-warning"><?=Security::e((string)$whitelistManager['state_error'])?></div><?php endif;?>
    <?php if(($whitelistManager['enabled']??null)===false):?><div class="alert alert-info">Die Zugangskontrolle ist ausgeschaltet. Die gespeicherte Liste kann vorbereitet werden; Änderungen schalten das Addon nicht ein.</div><?php endif;?>
  <?php endif;?>
  <div class="whitelist-meta">
    <span><strong>Modus:</strong> <?=Security::e((string)$whitelistManager['mode_label'])?></span>
    <span><strong>Datei:</strong> <span class="mono"><?=Security::e((string)$whitelistManager['list_path'])?></span></span>
    <span><strong>Permission:</strong> <span class="mono"><?=Security::e((string)$whitelistManager['permission'])?></span></span>
  </div>

  <?php if(!empty($whitelistPending)):?>
  <div class="whitelist-confirmation-stack">
    <?php foreach($whitelistPending as $pendingChange):?>
      <?php
        $pendingAdded=count((array)($pendingChange['added']??[]));
        $pendingRemoved=count((array)($pendingChange['removed']??[]));
      ?>
      <div
        class="whitelist-confirmation"
        data-whitelist-pending
        data-server-id="<?=$server['id']?>"
        data-token="<?=Security::e((string)$pendingChange['token'])?>">
        <div class="whitelist-confirmation-indicator" data-whitelist-pending-indicator></div>
        <div class="whitelist-confirmation-content">
          <strong data-whitelist-pending-title>Änderung wird auf dem Gameserver bestätigt …</strong>
          <span data-whitelist-pending-text>
            <?php if($pendingAdded):?><?=$pendingAdded?> Hinzufügung<?=($pendingAdded===1?'':'en')?><?php endif;?>
            <?php if($pendingAdded&&$pendingRemoved):?> · <?php endif;?>
            <?php if($pendingRemoved):?><?=$pendingRemoved?> Entfernung<?=($pendingRemoved===1?'':'en')?><?php endif;?>
          </span>
        </div>
        <button type="button" class="btn btn-secondary btn-small" data-whitelist-pending-refresh hidden>Liste neu laden</button>
      </div>
    <?php endforeach;?>
  </div>
  <?php endif;?>

  <div class="whitelist-live-grid">
    <div class="whitelist-column">
      <h3>Aktuelle <?=Security::e((string)$whitelistManager['mode_label'])?></h3>
      <p class="muted">Die Spielerliste ist standardmäßig eingeklappt. Sortierung: Founder → Admin → Member → VIP → Spieler, innerhalb jeder Rolle alphabetisch A–Z.</p>

      <details class="whitelist-current-details">
        <summary>
          <?=Security::e((string)$whitelistManager['mode_label'])?>-Spieler anzeigen
          <span class="status-pill"><?=count($whitelistManager['entries']??[])?></span>
        </summary>

        <div class="whitelist-current-details-body">
          <div class="section-head">
            <div><p class="muted">Einträge markieren und gesammelt entfernen.</p></div>
            <input type="search" placeholder="Einträge filtern..." data-whitelist-current-search>
          </div>

          <form method="post" action="/servers/<?=$server['id']?>/live/whitelist" class="form-stack" data-confirm="Markierte Einträge wirklich entfernen?">
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
            <input type="hidden" name="operation" value="remove_selected">
            <div class="whitelist-scroll">
              <?php if(empty($whitelistManager['entries'])):?><div class="muted whitelist-empty">Die Liste ist aktuell leer.</div><?php endif;?>
              <?php $lastWhitelistRole=null; $roleLabels=['founder'=>'Founder','admin'=>'Admin','member'=>'Member','vip'=>'VIP','spieler'=>'Spieler']; ?>
              <?php foreach(($whitelistManager['entries']??[]) as $entry):?>
              <?php $entryRole=strtolower((string)($entry['role']??$entry['community_role']??'spieler')); if($entryRole!==$lastWhitelistRole): $lastWhitelistRole=$entryRole; ?>
                <div class="whitelist-role-group game-role-<?=Security::e($entryRole)?>"><?=Security::e($roleLabels[$entryRole]??ucfirst($entryRole))?></div>
              <?php endif;?>
              <label class="whitelist-row<?=isset($whitelistPendingRemove[(string)$entry['steam_id64']])?' whitelist-row-pending-remove':''?>" data-whitelist-current data-steamid="<?=Security::e((string)$entry['steam_id64'])?>" data-search="<?=Security::e(strtolower((string)$entry['display_name'].' '.(string)$entry['steam_id64'].' '.(string)$entry['community_role']))?>">
                <input type="checkbox" name="steam_ids[]" value="<?=Security::e((string)$entry['steam_id64'])?>">
                <span class="whitelist-player-main"><strong><?=Security::e((string)$entry['display_name'])?></strong><span class="mono"><?=Security::e((string)$entry['steam_id64'])?></span></span>
                <span class="whitelist-player-side">
                  <?php if(!empty($entry['community_role'])):?><span class="game-role game-role-<?=Security::e((string)$entry['community_role'])?>"><?=Security::e(ucfirst((string)$entry['community_role']))?></span><?php endif;?>
                  <?php if(isset($whitelistPendingRemove[(string)$entry['steam_id64']])):?><span class="status-pill whitelist-pending-pill">WIRD ENTFERNT</span><?php endif;?>
                  <?php if(!empty($entry['online'])):?><span class="status-pill status-online">ONLINE</span><?php endif;?>
                  <?php if(!empty($entry['steam_id64']) && (Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$entry['steam_id64'])?>" data-player-name="<?=Security::e((string)$entry['display_name'])?>" data-player-server-id="<?=(int)$server['id']?>">Aktionen</button><?php endif;?>
                </span>
              </label>
              <?php endforeach;?>
            </div>
            <button class="btn btn-danger btn-small" type="submit" <?=$whitelistReadOnly?'disabled':''?>>Markierte entfernen</button>
          </form>
        </div>
      </details>
    </div>

    <div class="whitelist-column">
      <h3>Schnell hinzufügen</h3>
      <form method="post" action="/servers/<?=$server['id']?>/live/whitelist" class="form-stack">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
        <input type="hidden" name="operation" value="add_one">
        <label>SteamID64
          <input class="mono" type="text" name="steam_id64" maxlength="17" pattern="7656119[0-9]{10}" placeholder="76561198000000000" required>
        </label>
        <button class="btn btn-primary" type="submit" <?=$whitelistReadOnly?'disabled':''?>>SteamID hinzufügen</button>
      </form>

      <div class="section-head whitelist-candidate-head">
        <div><h3>Panel-Spieler hinzufügen</h3><p class="muted">Dem Server zugeordnete Spieler, die noch nicht in der Liste stehen. Ebenfalls nach Rolle und innerhalb der Rolle A–Z sortiert.</p></div>
      </div>
      <form method="post" action="/servers/<?=$server['id']?>/live/whitelist" class="form-stack">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
        <input type="hidden" name="operation" value="add_selected">
        <div class="whitelist-select-toolbar">
          <input type="search" placeholder="Spieler oder SteamID suchen..." data-whitelist-player-search>
          <button class="btn btn-secondary btn-small" type="button" data-whitelist-select-visible>Sichtbare markieren</button>
        </div>
        <div class="whitelist-scroll whitelist-candidates">
          <?php if(empty($whitelistManager['candidates'])):?><div class="muted whitelist-empty">Keine weiteren zugeordneten Spieler verfügbar.</div><?php endif;?>
          <?php $lastCandidateRole=null; $roleLabels=['founder'=>'Founder','admin'=>'Admin','member'=>'Member','vip'=>'VIP','spieler'=>'Spieler']; ?>
          <?php foreach(($whitelistManager['candidates']??[]) as $candidate):?>
          <?php $candidateRole=strtolower((string)($candidate['role']??'spieler')); if($candidateRole!==$lastCandidateRole): $lastCandidateRole=$candidateRole; ?>
            <div class="whitelist-role-group game-role-<?=Security::e($candidateRole)?>"><?=Security::e($roleLabels[$candidateRole]??ucfirst($candidateRole))?></div>
          <?php endif;?>
          <label class="whitelist-row<?=isset($whitelistPendingAdd[(string)$candidate['steam_id64']])?' whitelist-row-pending-add':''?>" data-whitelist-candidate data-steamid="<?=Security::e((string)$candidate['steam_id64'])?>" data-search="<?=Security::e(strtolower((string)$candidate['display_name'].' '.(string)$candidate['steam_id64'].' '.(string)$candidate['role']))?>">
            <input type="checkbox" name="steam_ids[]" value="<?=Security::e((string)$candidate['steam_id64'])?>">
            <span class="whitelist-player-main"><strong><?=Security::e((string)$candidate['display_name'])?></strong><span class="mono"><?=Security::e((string)$candidate['steam_id64'])?></span></span>
            <span class="whitelist-player-side">
              <span class="game-role game-role-<?=Security::e((string)$candidate['role'])?>"><?=Security::e(ucfirst((string)$candidate['role']))?></span>
              <?php if(isset($whitelistPendingAdd[(string)$candidate['steam_id64']])):?><span class="status-pill whitelist-pending-pill">WIRD HINZUGEFÜGT</span><?php endif;?>
              <?php if(!empty($candidate['online'])):?><span class="status-pill status-online">ONLINE</span><?php endif;?>
              <?php if(!empty($candidate['steam_id64']) && (Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist'))):?><button type="button" class="btn btn-secondary btn-small player-action-button" data-player-actions data-player-steamid="<?=Security::e((string)$candidate['steam_id64'])?>" data-player-name="<?=Security::e((string)$candidate['display_name'])?>" data-player-server-id="<?=(int)$server['id']?>">Aktionen</button><?php endif;?>
            </span>
          </label>
          <?php endforeach;?>
        </div>
        <button class="btn btn-primary btn-small" type="submit" <?=$whitelistReadOnly?'disabled':''?>>Markierte hinzufügen</button>
      </form>
    </div>
  </div>

  <details class="whitelist-replace">
    <summary>Komplette Liste abgleichen</summary>
    <div class="whitelist-replace-body">
      <p class="muted">Die Datei wird nicht blind überschrieben. Das Panel berechnet nur Hinzufügungen und Entfernungen und führt die Plugin-Befehle aus. Vor dem Abgleich wird <span class="mono">whitelist.serververwaltung-last.bak</span> angelegt.</p>
      <form method="post" action="/servers/<?=$server['id']?>/live/whitelist" class="form-stack" data-confirm="Komplette Liste wirklich abgleichen? Änderungen werden direkt auf dem laufenden Server wirksam.">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
        <input type="hidden" name="operation" value="replace">
        <label>Gewünschte SteamIDs – eine pro Zeile
          <textarea class="mono" name="list_text" rows="10" spellcheck="false"><?php foreach(($whitelistManager['steam_ids']??[]) as $sid):?><?=Security::e((string)$sid)."\n"?><?php endforeach;?></textarea>
        </label>
        <button class="btn btn-danger" type="submit" <?=$whitelistReadOnly?'disabled':''?>>Liste abgleichen</button>
      </form>
    </div>
  </details>
</section>
<?php endif;?>

<?php if(Auth::can('server.console.view') || Auth::can('server.console.execute') || Auth::can('audit.view')):?>
<section class="panel admin-log-panel ui-section-primary" id="admin-log">
  <div class="panel-header split-header">
    <div>
      <h2>Admin-Log</h2>
      <p>Letzte Adminaktionen dieses Servers aus der Serververwaltung.</p>
    </div>
    <div class="admin-log-summary">
      <strong><?=count($adminActionLog['entries']??[])?></strong>
      <span>Einträge</span>
    </div>
  </div>

  <?php if(!empty($adminActionLog['error'])):?>
    <div class="alert alert-warning">Admin-Log konnte nicht gelesen werden: <?=Security::e((string)$adminActionLog['error'])?></div>
  <?php endif;?>

  <details class="admin-log-details">
    <summary>
      Admin-Log anzeigen
      <span class="status-pill"><?=count($adminActionLog['entries']??[])?></span>
    </summary>

    <div class="admin-log-body">
      <div class="admin-log-toolbar">
        <input type="search" placeholder="Name, Aktion, SteamID, Fehler suchen..." data-admin-log-search>
        <select data-admin-log-status>
          <option value="all">Alle Status</option>
          <option value="ok">Nur OK</option>
          <option value="fehler">Nur FEHLER</option>
        </select>
      </div>

      <div class="small-muted admin-log-meta">
        Datei: <span class="mono"><?=Security::e((string)($adminActionLog['path']??''))?></span>
        · Zeitformat: TT.MM.JJ HH:MM:ss
        · Neueste Einträge zuerst
        · 10 Einträge pro Seite
      </div>

      <div class="admin-log-table-wrap">
        <table class="admin-log-table">
          <thead>
            <tr>
              <th>Zeit</th>
              <th>Status</th>
              <th>Auslöser</th>
              <th>Rolle</th>
              <th>Aktion</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>
          <?php if(empty($adminActionLog['entries'])):?>
            <tr><td colspan="6" class="muted">Noch keine Adminaktionen protokolliert.</td></tr>
          <?php endif;?>
          <?php foreach(($adminActionLog['entries']??[]) as $entry):?>
            <tr
              data-admin-log-row
              data-status="<?=Security::e(strtolower((string)$entry['status']))?>"
              data-search="<?=Security::e((string)$entry['search'])?>">
              <td class="mono admin-log-time"><?=Security::e((string)$entry['timestamp'])?></td>
              <td>
                <?php if((string)$entry['status']==='FEHLER'):?>
                  <span class="status-pill status-error">FEHLER</span>
                <?php else:?>
                  <span class="status-pill status-online">OK</span>
                <?php endif;?>
              </td>
              <td><strong><?=Security::e((string)$entry['actor'])?></strong></td>
              <td><?=Security::e((string)$entry['role'])?></td>
              <td><?=Security::e((string)$entry['action'])?></td>
              <td>
                <?php if(!empty($entry['error'])):?>
                  <details class="admin-log-error">
                    <summary>Fehler anzeigen</summary>
                    <div><?=Security::e((string)$entry['error'])?></div>
                  </details>
                <?php elseif(!empty($entry['command_id'])):?>
                  <span class="mono small-muted"><?=Security::e((string)$entry['command_id'])?></span>
                <?php else:?>
                  <span class="muted">—</span>
                <?php endif;?>
              </td>
            </tr>
          <?php endforeach;?>
          </tbody>
        </table>
      </div>

      <div class="muted admin-log-empty-filter" data-admin-log-empty hidden>Keine Einträge passen zum Filter.</div>

      <div class="admin-log-pagination" data-admin-log-pagination>
        <button type="button" class="btn btn-secondary btn-small" data-admin-log-newer disabled>← Neuere</button>
        <span class="admin-log-page-info" data-admin-log-page-info>Seite 1 / 1</span>
        <button type="button" class="btn btn-secondary btn-small" data-admin-log-older>Ältere →</button>
      </div>
    </div>
  </details>
</section>
<?php endif;?>

<?php if(Auth::can('live.weapons')):?><section class="panel ui-section-compact" id="team-weapons">
  <div class="panel-header"><h2>Waffen an Team / alle</h2><p>Waffe gleichzeitig an alle menschlichen T-Spieler, CT-Spieler oder alle aktuell in T/CT befindlichen Spieler geben.</p></div>
  <form method="post" action="/servers/<?=$server['id']?>/live/weapons" class="message-form team-weapon-form" data-confirm="Waffe wirklich an die gewählte Gruppe ausgeben?">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <label>Zielgruppe<select name="scope"><option value="ct">CT-Team</option><option value="t">T-Team</option><option value="all" selected>Alle T + CT</option></select></label>
    <label>Waffe<select name="weapon"><?php foreach($weapons as $category=>$items):?><optgroup label="<?=Security::e($category)?>"><?php foreach($items as $key=>$label):?><option value="<?=Security::e($key)?>"><?=Security::e($label)?></option><?php endforeach;?></optgroup><?php endforeach;?></select></label>
    <label class="checkbox-line"><input type="checkbox" name="notify" value="1" checked> Betroffene Spieler benachrichtigen</label>
    <label>Benachrichtigung<select name="notify_type"><option value="chat">Chat</option><option value="center">Bildschirmmitte</option><option value="notify">Notify</option><option value="alert">Alert</option></select></label>
    <label class="message-grow">Text<input type="text" name="notify_message" maxlength="180" value="Serververwaltung: {weapon} wurde ausgegeben."></label>
    <button class="btn btn-primary">Waffe an Gruppe geben</button>
  </form>
</section><?php endif;?>

<section class="panel ui-section-compact" id="command-feedback">
  <div class="panel-header"><h2>Letzte Live-Aktionen</h2><p>Echte Fast-Live-Rückmeldung der Bridge: angefordert, zugestellt, erfolgreich oder fehlgeschlagen.</p></div>
  <div class="moderation-card-grid">
    <?php foreach(($recentCommands??[]) as $cmd): $st=(string)($cmd['status']??'queued'); $result=(array)($cmd['result']??[]); ?>
      <article class="moderation-card <?=$st==='failed'?'is-danger':($st==='success'?'is-active':'')?>">
        <div class="moderation-card-head"><strong><?=Security::e((string)($cmd['action']??'Aktion'))?></strong><span class="status-pill <?=$st==='failed'?'status-danger':($st==='success'?'status-online':($st==='delivered'?'status-info':'status-neutral'))?>"><?=Security::e(\App\Services\PanelNavigation::commandStatus($st))?></span></div>
        <?php if(!empty($cmd['target_steamid'])):?><div class="mono small-muted"><?=Security::e((string)$cmd['target_steamid'])?></div><?php endif;?>
        <div class="small-muted">Angefordert: <?=Security::e((string)($cmd['requested_at']??'—'))?><?php if(!empty($cmd['completed_at'])):?> · Bestätigt: <?=Security::e((string)$cmd['completed_at'])?><?php endif;?></div>
        <?php if(!empty($result['message'])):?><p><?=Security::e((string)$result['message'])?></p><?php endif;?>
        <div class="mono small-muted"><?=Security::e((string)($cmd['command_id']??''))?></div>
      </article>
    <?php endforeach;?>
    <?php if(empty($recentCommands)):?><div class="muted">Noch keine Fast-Live-Aktionen protokolliert.</div><?php endif;?>
  </div>
</section>

<section class="panel ui-section-primary live-actions-compact" id="server-actions">
  <div class="panel-header"><div><h2>Spiel- & Mapaktionen</h2><p>Kompakte Aktionen für Map, Runde und kollidierende Spieler. Die eigentliche Server-Powersteuerung bleibt separat.</p></div></div>
  <div class="live-action-accordion">
    <?php if(Auth::can('live.map')):?><details class="live-action-disclosure" open>
      <summary><span><strong>Map wechseln</strong><small>Workshopmap auswählen oder ID eingeben</small></span><span class="disclosure-state" aria-hidden="true"></span></summary>
      <form method="post" action="/servers/<?=$server['id']?>/live/map" class="form-stack compact-action-form" data-confirm="Mapwechsel wirklich ankündigen und ausführen?">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
        <?php if(!empty($mapOptions)):?>
          <div class="map-search-tools compact-map-search"><label class="map-search-field">Map suchen<input type="search" placeholder="Name oder Workshop-ID..." autocomplete="off" enterkeyhint="search" data-map-search></label><button class="btn btn-secondary btn-small map-search-reset" type="button" data-map-search-reset>Reset</button></div>
          <div class="small-muted map-search-help"><span data-map-search-count><?=count($mapOptions)?> Maps verfügbar</span> · Die Liste stammt aus den tatsächlich installierten Steam-Workshopmaps. · Enter filtert nur und startet keinen Mapwechsel. Esc setzt den Filter zurück.</div>
          <label>Map<select name="map_choice" data-map-select><option value="">Bitte auswählen</option><?php foreach(($mapOptions??[]) as $value=>$label):?><option value="<?=Security::e((string)$value)?>"><?=Security::e((string)$label)?></option><?php endforeach;?></select></label>
          <label>Oder Map / Workshop-ID<input type="text" name="map" placeholder="de_dust2 oder 3070596702"></label>
        <?php else:?>
          <label>Map / Workshop-ID<input type="text" name="map" placeholder="de_dust2 oder 3070596702" required></label>
        <?php endif;?>
        <div class="compact-two-columns"><label>Ankündigung<select name="seconds"><option value="0">Sofort</option><option value="5" selected>5 Sekunden</option><option value="10">10 Sekunden</option><option value="30">30 Sekunden</option><option value="60">60 Sekunden</option></select></label><label>Nachricht<input type="text" name="message" value="Mapwechsel zu {map} in {seconds} Sekunden." maxlength="240"></label></div>
        <button class="btn btn-primary" type="submit">Mapwechsel starten</button>
      </form>
    </details><?php endif;?>

    <?php if(Auth::can('live.map')):?><details class="live-action-disclosure">
      <summary><span><strong>Runde neu starten</strong><small>Aktuelle Map bleibt geladen</small></span><span class="disclosure-state" aria-hidden="true"></span></summary>
      <form method="post" action="/servers/<?=$server['id']?>/live/round-restart" class="form-stack compact-action-form" data-confirm="Runde wirklich neu starten?">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
        <label>Ankündigung<select name="seconds"><option value="0">Sofort</option><option value="5" selected>5 Sekunden</option><option value="10">10 Sekunden</option><option value="30">30 Sekunden</option></select></label>
        <button class="btn btn-secondary" type="submit">Runde neu starten</button>
      </form>
    </details><?php endif;?>

    <?php if(Auth::can('live.teleport')):?><details class="live-action-disclosure">
      <summary><span><strong>Spieler trennen</strong><small>Nur tatsächlich überlappende Spieler</small></span><span class="disclosure-state" aria-hidden="true"></span></summary>
      <form method="post" action="/servers/<?=$server['id']?>/live/separate-players" class="form-stack compact-action-form" data-confirm="Jetzt alle lebenden Spieler auf Positionsüberlappungen prüfen und gegebenenfalls auseinander setzen?">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
        <div class="small-muted">Lebende menschliche Spieler werden geprüft. Normale Spielerabstände bleiben unverändert; ohne sichere freie Position wird nichts erzwungen.</div>
        <button class="btn btn-secondary" type="submit">Überlappende Spieler trennen</button>
      </form>
    </details><?php endif;?>
  </div>
</section>

<?php if(Auth::can('live.message')):?><section class="panel ui-section-compact" id="messages"><div class="panel-header"><h2>Servernachricht</h2><p>Nachricht an alle aktuell verbundenen Spieler. Persönliche Nachrichten findest du in der jeweiligen Spieleransicht.</p></div><form method="post" action="/servers/<?=$server['id']?>/live/message" class="message-form server-message-form"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><label>Darstellung<select name="type"><option value="chat" selected>Chat</option><option value="center">Bildschirmmitte</option><option value="alert">Alert</option><option value="notify">Notify</option></select></label><label class="message-grow">Nachricht<textarea name="message" rows="3" maxlength="500" required placeholder="Nachricht an alle Spieler"></textarea></label><button class="btn btn-primary" type="submit">Nachricht senden</button></form></section><?php endif;?>

<?php if(Auth::can('server.console.view') || Auth::can('server.console.execute') || Auth::can('live.cvar')):?><section class="panel ui-section-compact" id="advanced">
  <div class="panel-header"><h2>Erweiterte Serveradministration</h2><p>Konsole und ConVars sind separat abgesichert. Admin erhält die Konsolenrechte standardmäßig über seine Rollenvorlage; individuelle Verbote und Serverfreigaben haben weiterhin Vorrang.</p></div>
  <div class="live-action-grid">
    <?php if(Auth::can('server.console.execute')):?><form method="post" action="/servers/<?=$server['id']?>/live/console" class="action-card form-stack" data-confirm="Diesen Serverbefehl wirklich ausführen?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <div><h3>Befehlskonsole</h3><p class="muted">Engine-Befehl sicher über das Webpanel/Bridge-Backend. Jeder Aufruf wird im Audit-Log protokolliert; Pterodactyl-WebSocket-Tokens werden nicht an den Browser ausgegeben.</p></div>
      <label>Befehl<input class="mono" type="text" name="command" maxlength="240" placeholder="status" required></label>
      <button class="btn btn-danger" type="submit">Befehl ausführen</button>
    </form><?php elseif(Auth::can('server.console.view')):?><div class="action-card form-stack"><div><h3>Serverkonsole</h3><p class="muted">Konsolenbereich darf angesehen werden, aber für dieses Konto ist das Ausführen von Befehlen ausdrücklich nicht freigegeben.</p></div><span class="status-pill status-offline">NUR LESERECHT</span></div><?php endif;?>

    <?php if(Auth::can('live.cvar')):?><form method="post" action="/servers/<?=$server['id']?>/live/cvar" class="action-card form-stack" data-confirm="Diese ConVar wirklich auf dem laufenden Server setzen?">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <div><h3>ConVar setzen</h3><p class="muted">Name und Wert werden serverseitig validiert und über die Bridge ausgeführt.</p></div>
      <label>ConVar<input class="mono" type="text" name="name" maxlength="80" pattern="[A-Za-z0-9_.-]{1,80}" placeholder="mp_friendlyfire" required></label>
      <label>Wert<input class="mono" type="text" name="value" maxlength="120" placeholder="0"></label>
      <button class="btn btn-danger" type="submit">ConVar setzen</button>
    </form><?php endif;?>
  </div>
</section><?php endif;?>

<?php if(Auth::can('live.ban')):?><section class="panel ui-section-compact" id="bans"><div class="panel-header"><h2>Aktive Live-Bans</h2><p>Von der SerververwaltungBridge verwaltete Bans. Andere Ban-Plugins bleiben davon getrennt.</p></div><div class="table-wrap"><table data-ui-adaptive="live-bans"><thead><tr><th>Spieler</th><th>SteamID64</th><th>Grund</th><th>Gültig bis</th><th></th></tr></thead><tbody><?php if(empty($status['bans'])):?><tr><td colspan="5" class="muted">Keine aktiven Live-Bans.</td></tr><?php endif;?><?php foreach(($status['bans']??[]) as $ban):?><tr><td><?=Security::e((string)($ban['display_name']??'Unbekannt'))?></td><td class="mono"><?=Security::e((string)($ban['steam_id64']??''))?></td><td><?=Security::e((string)($ban['reason']??'—'))?></td><td><?=empty($ban['expires_at'])?'Permanent':Security::e((string)$ban['expires_at'])?></td><td><form method="post" action="/servers/<?=$server['id']?>/live/player/<?=rawurlencode((string)($ban['steam_id64']??''))?>/action" data-confirm="Diesen Live-Ban wirklich aufheben?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="action" value="unban"><button class="btn btn-secondary btn-small" type="submit">Entbannen</button></form></td></tr><?php endforeach;?></tbody></table></div></section><?php endif;?>
</div>
