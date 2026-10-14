<?php use App\Core\Security; ?>
<section class="panel audit-overview-panel">
  <div class="panel-header split-header">
    <div><h2>Letzte 500 Ereignisse</h2><p>Lokaler Schnellfilter für Benutzer, Aktion, Ziel, IP und Details.</p></div>
    <div class="audit-filter-tools">
      <label>Filter<input type="search" placeholder="z. B. login, Atlas, SteamID..." autocomplete="off" data-audit-filter></label>
      <span class="status-pill" data-audit-visible-count><?=count($rows)?> sichtbar</span>
    </div>
  </div>
  <div class="table-wrap audit-table-wrap">
    <table class="audit-table" data-audit-table>
      <thead><tr><th>Zeit</th><th>Benutzer</th><th>Aktion</th><th>Ziel</th><th>IP</th><th>Details</th></tr></thead>
      <tbody>
      <?php if(empty($rows)):?><tr><td colspan="6" class="muted">Noch keine Audit-Ereignisse vorhanden.</td></tr><?php endif;?>
      <?php foreach($rows as $r):?><tr><td><?=Security::e($r['created_at'])?></td><td><?=Security::e($r['user_name']?:'System')?></td><td class="mono"><?=Security::e($r['action'])?></td><td><?=Security::e(trim(($r['target_type']??'').' '.($r['target_id']??'')))?></td><td class="mono"><?=Security::e($r['ip_address'])?></td><td><code><?=Security::e($r['details_json']?:'—')?></code></td></tr><?php endforeach;?>
      </tbody>
    </table>
  </div>
  <div class="muted audit-filter-empty" data-audit-filter-empty hidden>Keine Ereignisse passen zum Filter.</div>
</section>
