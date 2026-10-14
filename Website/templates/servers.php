<?php use App\Core\Security; use App\Core\Auth; ?>
<div class="page-toolbar-row">
  <div>
    <strong><?=count($servers)?> Server</strong>
    <span class="muted">Verbindungen, Framework-Status und Live-Zugriff auf einen Blick.</span>
  </div>
  <div class="toolbar">
    <?php if(Auth::can('servers.manage')):?><a class="btn btn-primary" href="/servers/new">Server hinzufügen</a><?php endif;?>
  </div>
</div>
<section class="panel server-overview-panel">
  <div class="panel-header split-header">
    <div><h2>Serverübersicht</h2><p>In der Kachelansicht wird jeder Server als eigene kompakte Karte dargestellt. Die globale Ansicht oben rechts kann jederzeit auf „Kompakt“ umgestellt werden.</p></div>
  </div>
  <div class="table-wrap">
    <table data-ui-adaptive="server-list">
      <thead><tr><th>Name</th><th>Verbindung</th><th>Framework</th><th>Letzter Scan</th><th>Letzter Sync</th><th>Status</th><th>Aktion</th></tr></thead>
      <tbody>
      <?php if(empty($servers)):?><tr><td colspan="7" class="muted">Noch keine Server angelegt.</td></tr><?php endif;?>
      <?php foreach($servers as $s):?>
        <?php $canQuick = Auth::can('servers.view') && Auth::can('live.view') && Auth::canServer((int)$s['id'],'view') && Auth::canServer((int)$s['id'],'live'); ?>
        <tr class="server-list-row <?=$s['last_error']?'has-error':'is-ready'?>"<?= $canQuick ? ' data-server-quick data-server-id="'.(int)$s['id'].'" data-server-name="'.Security::e($s['name']).'"' : '' ?>>
          <td><strong><a href="/servers/<?=$s['id']?>"><?=Security::e($s['name'])?></a></strong></td>
          <td><?=Security::e($s['connector_type'])?></td>
          <td><?=Security::e($s['framework']?:'Nicht gescannt')?></td>
          <td><?=Security::e($s['last_scan_at']?:'—')?></td>
          <td><?=Security::e($s['last_sync_at']?:'—')?></td>
          <td><span class="status-pill <?=$s['last_error']?'status-offline':'status-online'?>"><?=$s['last_error']?'FEHLER':'BEREIT'?></span></td>
          <td><div class="table-actions"><?php $joinServerId=(int)$s['id']; require __DIR__.'/server_join_button.php'; ?><a class="btn btn-secondary btn-small" href="/servers/<?=$s['id']?>">Details</a><?php if($canQuick):?><button type="button" class="btn btn-secondary btn-small" data-server-quick-open aria-label="Schnellverwaltung für <?=Security::e($s['name'])?> öffnen">Schnellverwaltung</button><a class="btn btn-primary btn-small" href="/servers/<?=$s['id']?>/live">Live</a><?php endif;?></div></td>
        </tr>
      <?php endforeach;?>
      </tbody>
    </table>
  </div>
</section>


<?php if(Auth::can('live.view')):?>
<div class="quick-manage-overlay" data-quick-manage-overlay hidden>
  <section class="quick-manage-dialog" role="dialog" aria-modal="true" aria-labelledby="quick-manage-title">
    <header class="quick-manage-head">
      <div><span class="eyebrow">Schnellverwaltung</span><h2 id="quick-manage-title" data-qm-title>Server</h2><div class="quick-manage-status muted" data-qm-status>Live-Status wird geladen …</div></div>
      <button type="button" class="btn btn-secondary btn-small" data-qm-close>Schließen</button>
    </header>
    <nav class="quick-manage-breadcrumb" data-qm-breadcrumb><button type="button" class="link-button" data-qm-home>Hauptmenü</button><span data-qm-crumb></span></nav>
    <div class="quick-manage-body" data-qm-body><div class="quick-manage-loading">Schnellverwaltung wird geladen …</div></div>
    <footer class="quick-manage-foot"><span class="muted" data-qm-feedback>Button „Schnellverwaltung“ oder Rechtsklick auf eine Serverzeile öffnet dieses Fenster.</span><a class="btn btn-secondary btn-small" data-qm-full href="#">Volle Liveverwaltung</a></footer>
  </section>
</div>
<?php endif;?>
