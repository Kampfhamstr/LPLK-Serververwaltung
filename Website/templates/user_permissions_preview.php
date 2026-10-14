<?php
use App\Core\Security;
use App\Core\Auth;
$groups=[
 'Allgemein'=>'dashboard.','Community & Kommunikation'=>'community.','Panel-Nutzer & Rollen'=>'users.','Spieler & Personen'=>'players.','Serververwaltung'=>'servers.','Plugins'=>'plugins.','Live-Serververwaltung'=>'live.','Moderation'=>'moderation.','Audit'=>'audit.'
];
?>
<div class="permission-preview-page">
<section class="panel">
 <div class="panel-header"><div><h2><?=Security::e(Auth::displayName($target))?></h2><p>Simulation der tatsächlich wirksamen Rechte. Es wird keine Anmeldung als dieser Benutzer durchgeführt und keine Aktion ausgeführt.</p></div><a class="btn btn-secondary" href="/users#user-<?=(int)$target['id']?>">Zurück</a></div>
 <div class="permission-user-badges"><span class="role-pill role-<?=Security::e(Auth::normalizeWebRole((string)$target['web_role']))?>"><?=Security::e(Auth::webRoleLabel((string)$target['web_role']))?></span><span class="state-chip <?=$target['active']?'is-on':'is-off'?>"><?=$target['active']?'Aktiv':'Gesperrt'?></span><span class="state-chip <?=(int)$target['totp_enabled']===1?'is-on':'is-off'?>">2FA <?=(int)$target['totp_enabled']===1?'AN':'AUS'?></span></div>
</section>
<section class="panel"><div class="panel-header"><div><h2>Effektive Funktionsrechte</h2><p>„Warum?“ ist direkt über die Herkunftsangabe beantwortet.</p></div><input type="search" placeholder="Recht suchen …" data-preview-search></div>
 <div class="effective-permission-grid">
 <?php foreach(Auth::PERMISSIONS as $permission=>$label):$d=$decisions[$permission];?>
  <article class="effective-permission-card <?=$d['allowed']?'is-allowed':'is-denied'?> <?=Auth::isDangerousPermission($permission)?'is-dangerous':''?>" data-preview-item data-search="<?=Security::e(strtolower($label.' '.$permission.' '.$d['source']))?>">
   <header><strong><?=Security::e($label)?></strong><span class="state-chip <?=$d['allowed']?'is-on':'is-off'?>"><?=$d['allowed']?'ERLAUBT':'VERBOTEN'?></span></header>
   <code><?=Security::e($permission)?></code><p><strong>Quelle:</strong> <?=Security::e($d['source'])?></p>
  </article>
 <?php endforeach;?>
 </div>
</section>
<section class="panel"><div class="panel-header"><div><h2>Serverzugriffe</h2><p>Funktionsrecht und Serverfreigabe müssen bei Serveraktionen beide erfüllt sein.</p></div></div>
 <div class="server-decision-grid">
 <?php foreach($servers as $server):$sid=(int)$server['id'];?>
  <article class="server-decision-card"><header><strong><?=Security::e($server['name'])?></strong><span>Max. CS2-Rolle: <?=Security::e(ucfirst((string)$serverDecisions[$sid]['max_game_role']))?></span></header>
   <?php foreach(['view'=>'Ansehen','players'=>'Spieler','permissions'=>'Rechte','sync'=>'Sync','live'=>'Live'] as $cap=>$label):$d=$serverDecisions[$sid][$cap];?>
    <div class="server-decision-row"><span><?=Security::e($label)?></span><span class="state-chip <?=$d['allowed']?'is-on':'is-off'?>"><?=$d['allowed']?'JA':'NEIN'?></span><small><?=Security::e($d['source'])?></small></div>
   <?php endforeach;?>
  </article>
 <?php endforeach;?>
 </div>
</section>
</div>
