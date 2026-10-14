<?php use App\Core\Security; ?>
<div class="page-title-row">
  <div>
    <h1>Spielerliste exportieren</h1>
    <p class="muted">Exportiert die zentrale Personen-/SteamID-Liste nach Community-Rolle. Zweitaccounts können eingeschlossen werden, ohne die Gruppenstatistik zu verfälschen.</p>
  </div>
  <div class="toolbar"><a class="btn btn-secondary" href="/players">Zur Spielerübersicht</a></div>
</div>

<div class="admin-section-grid export-grid">
<section class="panel">
  <div class="panel-header">
    <h2>Whitelist.txt</h2>
    <p>Eine SteamID64 pro Zeile. Geeignet für Whitelists und externe Plugins.</p>
  </div>
  <form class="form-stack" method="get" action="/players/export/download">
    <input type="hidden" name="format" value="whitelist">
    <label>Mindestrolle
      <select name="min_role">
        <option value="spieler" selected>Alle ab Spieler aufwärts</option>
        <option value="vip">Alle ab VIP aufwärts</option>
        <option value="member">Alle ab Member aufwärts</option>
        <option value="admin">Alle ab Admin aufwärts</option>
        <option value="founder">Nur Founder</option>
      </select>
    </label>
    <input type="hidden" name="include_secondary" value="0"><label class="checkbox-line"><input type="checkbox" name="include_secondary" value="1" checked> Zweitaccounts mit exportieren</label>
    <input type="hidden" name="active_only" value="0"><label class="checkbox-line"><input type="checkbox" name="active_only" value="1" checked> Nur aktive Accounts</label>
    <button class="btn btn-primary" type="submit">whitelist.txt herunterladen</button>
  </form>
</section>

<section class="panel">
  <div class="panel-header">
    <h2>Komplette Tabelle</h2>
    <p>CSV mit Steamdaten, Accounttyp, Community-Rolle und einer Spalte je Gameserver.<?= $canContacts ? ' Kontaktdaten werden gemäß deiner Berechtigung ebenfalls exportiert.' : '' ?></p>
  </div>
  <form class="form-stack" method="get" action="/players/export/download">
    <input type="hidden" name="format" value="table">
    <label>Mindestrolle
      <select name="min_role">
        <option value="spieler" selected>Alle ab Spieler aufwärts</option>
        <option value="vip">Alle ab VIP aufwärts</option>
        <option value="member">Alle ab Member aufwärts</option>
        <option value="admin">Alle ab Admin aufwärts</option>
        <option value="founder">Nur Founder</option>
      </select>
    </label>
    <input type="hidden" name="include_secondary" value="0"><label class="checkbox-line"><input type="checkbox" name="include_secondary" value="1" checked> Zweitaccounts als eigene SteamID-Zeilen aufnehmen</label>
    <input type="hidden" name="active_only" value="0"><label class="checkbox-line"><input type="checkbox" name="active_only" value="1" checked> Nur aktive Accounts</label>
    <button class="btn btn-primary" type="submit">CSV-Tabelle herunterladen</button>
  </form>
</section>
</div>

<section class="panel">
  <div class="panel-header"><h2>Rollenfilter</h2><p>Der Filter arbeitet global nach der zentralen Community-Rolle.</p></div>
  <div class="export-role-help">
    <div><strong>Spieler</strong><span>Spieler, VIP, Member, Admin, Founder</span></div>
    <div><strong>VIP</strong><span>VIP, Member, Admin, Founder</span></div>
    <div><strong>Member</strong><span>Member, Admin, Founder</span></div>
    <div><strong>Admin</strong><span>Admin, Founder</span></div>
    <div><strong>Founder</strong><span>Founder</span></div>
  </div>
</section>
