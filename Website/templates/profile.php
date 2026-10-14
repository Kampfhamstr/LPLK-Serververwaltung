<?php use App\Core\Security; use App\Core\Auth; ?>
<?php $founderLocked=Auth::founderTwoFactorLocked($user); $effectiveRole=Auth::effectiveWebRoleForUser($user); ?>
<div class="two-column profile-overview-grid">
<section class="panel panel-accent panel-accent-blue"><div class="panel-header"><h2>Kontodaten</h2></div><dl class="definition-list"><dt>Anzeige</dt><dd><?=Security::e(Auth::displayName($user))?></dd><dt>Interner Kontoname</dt><dd><?=Security::e($user['name'])?></dd><dt>E-Mail</dt><dd><?=Security::e($user['email'])?></dd><dt>Gespeicherte Rolle</dt><dd><span class="role-pill role-<?=Security::e(Auth::normalizeWebRole((string)$user['web_role']))?>"><?=Security::e(Auth::webRoleLabel((string)$user['web_role']))?></span></dd><dt>Aktuell wirksam</dt><dd><span class="role-pill role-<?=Security::e($effectiveRole)?>"><?=Security::e(Auth::webRoleLabel($effectiveRole))?></span><?php if($founderLocked):?> <span class="state-chip is-custom">Founder wartet auf 2FA</span><?php endif;?></dd><dt>2FA</dt><dd><?=Auth::secondFactorEnabledForUser($user)?'<span class="state-chip is-on">Aktiv</span>':'<span class="state-chip is-off">Nicht aktiviert</span>'?></dd></dl></section>
<section class="panel panel-accent panel-accent-violet"><div class="panel-header"><h2>Passwort ändern</h2></div><form method="post" action="/profile/password" class="form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><label>Aktuelles Passwort<input type="password" name="current_password" autocomplete="current-password" required></label><label>Neues Passwort<input type="password" name="new_password" minlength="12" autocomplete="new-password" required><span class="help">Mindestens 12 Zeichen.</span></label><label>Neues Passwort wiederholen<input type="password" name="new_password_confirm" minlength="12" autocomplete="new-password" required></label><button class="btn btn-primary">Passwort ändern</button></form></section>
</div>

<?php if($founderLocked):?>
<section class="panel founder-unlock-panel">
  <div class="founder-unlock-content">
    <div><span class="eyebrow">Sicherheitsmodus</span><h2>Founder ist noch nicht freigeschaltet</h2><p>Das Konto bleibt als <strong>Founder</strong> gespeichert, arbeitet bis zur 2FA-Aktivierung jedoch ausschließlich mit den Rechten der <strong>Admin-Rollenvorlage</strong>. Nach erfolgreicher Einrichtung wird der Founder-Vollzugriff automatisch freigeschaltet.</p></div>
    <a class="btn btn-warning" href="#twofactor">Founder durch 2FA freischalten</a>
  </div>
</section>
<?php endif;?>

<section class="panel panel-accent panel-accent-green" id="twofactor">
  <div class="panel-header split-header">
    <div>
      <h2>Authenticator (TOTP)</h2>
      <p>Optionaler zweiter Faktor mit QR-Code. Unterstützt unter anderem Google Authenticator, Microsoft Authenticator, Authy, 1Password und Bitwarden.</p>
    </div>
    <span class="status-pill <?=$user['totp_enabled']?'status-online':'status-warning'?>"><?=$user['totp_enabled']?'AKTIV':'NICHT AKTIV'?></span>
  </div>

  <?php if($user['totp_enabled']):?>
    <div class="twofactor-status-grid">
      <article class="security-status-card is-ok"><span>Authenticator</span><strong>Aktiv</strong><small>TOTP · 6 Stellen · 30 Sekunden</small></article>
      <article class="security-status-card <?=$recoveryCount>3?'is-ok':'is-warning'?>"><span>Recovery-Codes</span><strong><?=(int)$recoveryCount?> verfügbar</strong><small>Jeder Code ist nur einmal verwendbar.</small></article>
      <article class="security-status-card"><span>Wirksame Rolle</span><strong><?=Security::e(Auth::webRoleLabel($effectiveRole))?></strong><small><?=$founderLocked?'Founder wird nach 2FA freigeschaltet.':'2FA-Sicherheitsanforderung erfüllt.'?></small></article>
    </div>

    <?php if(!empty($newRecoveryCodes)):?>
      <section class="recovery-code-reveal" data-recovery-codes>
        <div class="recovery-code-head"><div><span class="eyebrow">Nur jetzt sichtbar</span><h3>Recovery-Codes sicher speichern</h3><p>Diese Klartextcodes werden nur kurz in deiner Sitzung gehalten. Jeder Code funktioniert genau einmal als Ersatz für den Authenticator-Code.</p></div><span class="status-pill status-warning">VERTRAULICH</span></div>
        <div class="recovery-code-grid"><?php foreach($newRecoveryCodes as $code):?><code><?=Security::e((string)$code)?></code><?php endforeach;?></div>
        <div class="button-row"><button type="button" class="btn btn-secondary" data-copy-recovery-codes>Alle kopieren</button><a class="btn btn-secondary" href="/profile/2fa/recovery/download">Als TXT speichern</a></div>
      </section>
    <?php endif;?>

    <div class="security-action-grid">
      <details class="security-action-card">
        <summary>Neue Recovery-Codes erzeugen</summary>
        <form method="post" action="/profile/2fa/recovery/regenerate" class="form-stack top-gap-small" data-confirm="Alle bisherigen Recovery-Codes werden sofort ungültig. Neue Codes erzeugen?">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
          <label>Aktuelles Passwort<input type="password" name="current_password" autocomplete="current-password" required></label>
          <label>Authenticator- oder Recovery-Code<input type="text" name="second_factor" autocomplete="one-time-code" maxlength="20" required></label>
          <button class="btn btn-warning" type="submit">Recovery-Codes erneuern</button>
        </form>
      </details>

      <details class="security-action-card danger-card">
        <summary>2FA deaktivieren</summary>
        <form method="post" action="/profile/2fa/disable" class="form-stack top-gap-small" data-confirm="Zwei-Faktor-Authentifizierung wirklich deaktivieren?">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
          <label>Aktuelles Passwort<input type="password" name="current_password" autocomplete="current-password" required></label>
          <label>Authenticator- oder Recovery-Code<input type="text" name="second_factor" autocomplete="one-time-code" maxlength="20" required></label>
          <?php if(Auth::isFounderAccount($user)):?><div class="alert alert-warning">Beim Deaktivieren verliert dieses Founder-Konto sofort den Founder-Vollzugriff und arbeitet nur noch mit der Admin-Rollenvorlage.</div><?php endif;?>
          <button class="btn btn-danger" type="submit">2FA deaktivieren</button>
        </form>
      </details>
    </div>

  <?php elseif(!empty($_SESSION['pending_totp'])):?>
    <div class="totp-setup-layout">
      <section class="totp-primary-method">
        <div class="setup-method-head"><span class="setup-step-number">1</span><div><h3>QR-Code scannen</h3><p>Empfohlene Einrichtung am PC: Authenticator-App öffnen, neues Konto hinzufügen und den QR-Code scannen.</p></div></div>
        <?php if(!empty($totpProvisioningUri)):?>
          <div class="totp-qr-shell"><div class="totp-qr-code" data-totp-qr data-totp-uri="<?=Security::e((string)$totpProvisioningUri)?>"><span>QR-Code wird lokal erzeugt …</span></div><small>Der QR-Code wird ausschließlich im Browser aus dem lokalen TOTP-Link erzeugt. Kein externer QR-Dienst erhält das Secret.</small></div>
        <?php endif;?>
        <div class="totp-app-support"><span>Google Authenticator</span><span>Microsoft Authenticator</span><span>Authy</span><span>1Password</span><span>Bitwarden</span><span>andere TOTP-App</span></div>
      </section>

      <section class="totp-alternative-methods">
        <div class="setup-method-head"><span class="setup-step-number">2</span><div><h3>Alternative Einrichtung</h3><p>Falls du das Panel auf dem Smartphone geöffnet hast oder die Kamera nicht verwenden möchtest.</p></div></div>
        <?php if(!empty($totpProvisioningUri)):?>
          <a class="btn btn-primary" href="<?=Security::e((string)$totpProvisioningUri)?>">In Authenticator-App öffnen</a>
          <button class="btn btn-secondary" type="button" data-copy-value="<?=Security::e((string)$totpProvisioningUri)?>">TOTP-Link kopieren</button>
        <?php endif;?>

        <details class="totp-manual-details">
          <summary>Manuellen Einrichtungsschlüssel anzeigen</summary>
          <div class="totp-setup-key">
            <span>Einrichtungsschlüssel</span>
            <strong class="mono" data-totp-secret><?=Security::e((string)($totpSetupKey??$_SESSION['pending_totp']))?></strong>
            <button class="btn btn-secondary btn-small" type="button" data-copy-totp-secret>Schlüssel kopieren</button>
          </div>
          <div class="totp-standard-info"><span>TOTP</span><span>6 Stellen</span><span>30 Sekunden</span><span>SHA-1</span></div>
        </details>
      </section>
    </div>

    <form method="post" action="/profile/2fa/enable" class="totp-confirm-form">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <div class="setup-method-head"><span class="setup-step-number">3</span><div><h3>Einrichtung bestätigen</h3><p>Zum Abschluss den aktuell angezeigten 6-stelligen Code eingeben. Erst danach wird 2FA aktiviert.</p></div></div>
      <div class="totp-confirm-row"><label>Bestätigungscode<input type="text" name="confirm_code" inputmode="numeric" pattern="[0-9 ]{6,7}" maxlength="7" autocomplete="one-time-code" placeholder="123456" required></label><button class="btn btn-success" type="submit">2FA aktivieren<?=$founderLocked?' & Founder freischalten':''?></button></div>
    </form>

  <?php else:?>
    <div class="twofactor-start-card">
      <div><h3>2FA in wenigen Sekunden einrichten</h3><p>Nach dem Start erhältst du einen QR-Code, einen direkten App-Link und einen manuellen Schlüssel als Fallback. Nach erfolgreicher Bestätigung werden zusätzlich 10 einmalige Recovery-Codes erzeugt.</p></div>
      <form method="post" action="/profile/2fa/enable"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn <?=$founderLocked?'btn-warning':'btn-primary'?>"><?=$founderLocked?'2FA einrichten & Founder freischalten':'2FA einrichten'?></button></form>
    </div>
  <?php endif;?>
</section>


<section class="panel panel-accent panel-accent-blue" id="passkeys" data-passkey-profile data-csrf="<?=Security::e(Security::csrfToken())?>">
  <div class="panel-header split-header">
    <div><h2>Passkeys & Sicherheitsschlüssel</h2><p>Phishing-resistente Zwei-Faktor-Authentifizierung mit Windows Hello, Fingerabdruck, Face ID oder FIDO2-Hardware-Key. Der private Schlüssel bleibt auf deinem Gerät.</p></div>
    <span class="status-pill <?=!empty($passkeys)?'status-online':'status-warning'?>"><?=!empty($passkeys)?count($passkeys).' AKTIV':'NICHT EINGERICHTET'?></span>
  </div>
  <?php if(empty($passkeySupported)):?><div class="alert alert-warning">Passkeys benötigen HTTPS und eine korrekte <code>app_url</code>. Auf dieser Verbindung ist WebAuthn derzeit nicht verfügbar.</div><?php endif;?>
  <?php if(!empty($passkeys)):?>
    <div class="passkey-list">
      <?php foreach($passkeys as $pk):?><article class="security-status-card is-ok"><span>Passkey</span><strong><?=Security::e((string)$pk['label'])?></strong><small>Registriert <?=Security::e((string)$pk['created_at'])?> · zuletzt <?=Security::e((string)($pk['last_used_at']?:'noch nie'))?></small><details><summary>Entfernen</summary><form method="post" action="/profile/passkeys/<?=(int)$pk['id']?>/delete" class="form-stack top-gap-small" data-confirm="Diesen Passkey wirklich entfernen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><label>Aktuelles Passwort<input type="password" name="current_password" autocomplete="current-password" required></label><button class="btn btn-danger btn-small">Passkey entfernen</button></form></details></article><?php endforeach;?>
    </div>
  <?php endif;?>
  <?php if(!empty($passkeySupported)):?>
  <div class="passkey-enroll-card">
    <div><h3>Neuen Passkey registrieren</h3><p>Empfohlen für Founder/Admin: mindestens zwei Passkeys, z. B. Windows Hello plus Hardware-Sicherheitsschlüssel als Backup.</p></div>
    <div class="form-stack passkey-enroll-form"><label>Bezeichnung<input type="text" maxlength="80" value="Windows Hello / Sicherheitsschlüssel" data-passkey-label></label><label>Aktuelles Passwort<input type="password" autocomplete="current-password" data-passkey-password></label><button type="button" class="btn btn-primary" data-passkey-register>Passkey registrieren<?=$founderLocked?' & Founder freischalten':''?></button><div class="small-muted" data-passkey-status></div></div>
  </div>
  <?php endif;?>
</section>

<section class="panel" id="steam-verification">
  <div class="panel-header">
    <h2>Steam-Verifizierung</h2>
    <p>Die Verknüpfung bestätigt die SteamID64 direkt über Steam OpenID. Das Steam-Passwort wird niemals an dieses Webpanel übermittelt. Auf der öffentlichen Pet-Spielwiese wird anschließend ausschließlich dein echter Steam-Anzeigename als Besitzername gezeigt.</p>
  </div>

  <?php if(!empty($user['steam_verified_at']) && !empty($user['steam_id64'])):?>
    <div class="steam-identity-card">
      <img class="steam-identity-avatar" src="/users/<?=(int)$user['id']?>/steam-avatar" alt="">
      <div>
        <strong><?=Security::e(trim((string)($user['steam_personaname']??''))!==''?(string)$user['steam_personaname']:'Steamname muss aktualisiert werden')?></strong>
        <div class="mono small-muted"><?=Security::e((string)$user['steam_id64'])?></div>
        <div class="small-muted">Verifiziert: <?=Security::e((string)$user['steam_verified_at'])?></div>
        <div class="small-muted">Profil zuletzt synchronisiert: <?=Security::e((string)($user['steam_profile_synced_at']?:'—'))?></div>
      </div>
      <span class="status-pill status-online">STEAM VERIFIZIERT</span>
    </div>
    <div class="button-row">
      <form method="post" action="/profile/steam/refresh">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
        <button class="btn btn-secondary" type="submit">Steam-Anzeige aktualisieren</button>
      </form>
      <a class="btn btn-secondary" href="/profile/steam/connect">Steam-Account erneut verifizieren</a>
    </div>
  <?php else:?>
    <p>Verbinde dein Konto, um die öffentliche Pet-Spielwiese zu betreten. Der Steam-Anzeigename wird über die konfigurierte Steam Web API oder ein bereits synchronisiertes lokales Profil geladen; dein interner Webpanel-Name wird dort nicht veröffentlicht.</p>
    <a class="btn btn-primary" href="/profile/steam/connect">Mit Steam verifizieren</a>
  <?php endif;?>
</section>
