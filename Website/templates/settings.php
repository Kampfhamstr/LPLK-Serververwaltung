<?php use App\Core\Security; use App\Core\Auth; $canManageSettings=Auth::can('settings.manage'); ?>
<?php if(!$canManageSettings):?><div class="alert alert-info">Nur Leserechte: Änderungen und API-/SMTP-Tests sind für dieses Konto gesperrt.</div><?php endif;?>
<fieldset class="settings-permission-fieldset" <?=$canManageSettings?'':'disabled'?>>
<div class="settings-layout">
<section class="panel narrow" id="github"><div class="panel-header"><h2>GitHub API</h2><p>Optional für höhere GitHub-API-Limits. Öffentliche Releases können auch ohne Token geprüft werden. Der Token wird verschlüsselt gespeichert. Plugin-Deployment ist derzeit auf öffentliche GitHub Release-Assets beschränkt.</p></div><form method="post" action="/settings" class="form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="section" value="github"><label>GitHub API Token<input type="password" name="github_api_token" autocomplete="off" value="<?=Security::e($settings['github.api_token']??'')?>" placeholder="optional"></label><button class="btn btn-primary">GitHub-Einstellung speichern</button></form><form method="post" action="/settings/test-github" class="toolbar top-gap"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-secondary">GitHub API testen</button></form></section>

<section class="panel narrow" id="admin-action-notify">
  <div class="panel-header">
    <h2>Adminaktions-Meldungen im Spiel</h2>
    <p>Legt fest, wer Meldungen über erfolgreich ausgeführte Adminaktionen der Serververwaltung im CS2-Chat erhält. Der direkt betroffene Zielspieler kann unabhängig davon immer zusätzlich informiert werden.</p>
  </div>
  <?php $notifyScope=(string)($settings['live.admin_action_notify.scope']??'admins'); ?>
  <?php $notifyTarget=((string)($settings['live.admin_action_notify.always_target']??'1'))!=='0'; ?>
  <form method="post" action="/settings" class="form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <input type="hidden" name="section" value="admin_action_notify">
    <fieldset class="settings-choice-group">
      <legend>Empfängerkreis</legend>
      <label class="radio-line">
        <input type="radio" name="notify_scope" value="admins" <?=$notifyScope==='admins'?'checked':''?>>
        <span><strong>Nur Admin + Founder</strong><small>Nur aktuell verbundene Spieler mit SwiftlyS2-Rolle Admin oder Founder erhalten die Meldung.</small></span>
      </label>
      <label class="radio-line">
        <input type="radio" name="notify_scope" value="all" <?=$notifyScope==='all'?'checked':''?>>
        <span><strong>Alle Spieler</strong><small>Alle aktuell verbundenen menschlichen Spieler erhalten die Meldung.</small></span>
      </label>
    </fieldset>
    <label class="checkbox-line admin-notify-target-option">
      <input type="checkbox" name="notify_always_target" value="1" <?=$notifyTarget?'checked':''?>>
      <span><strong>Zielspieler immer zusätzlich benachrichtigen</strong><small>Der direkt betroffene Spieler erhält die Meldung auch bei „Nur Admin + Founder“. Bei Kick/Ban erfolgt die Zielmeldung unmittelbar vor dem Disconnect.</small></span>
    </label>
    <div class="help">Farben: Auslöser in seiner Webpanel-Rollenfarbe, Ziel in seiner CS2-/Community-Rollenfarbe, übriger Aktionstext weiß.</div>
    <button class="btn btn-primary">Meldungseinstellungen speichern</button>
  </form>
</section>

<section class="panel narrow"><div class="panel-header"><h2>Steam Web API</h2><p>Ergänzt SteamID64-Datensätze automatisch um aktuellen Steam-Namen, Profil-Link und Avatar. Der Schlüssel wird verschlüsselt gespeichert.</p></div><form method="post" action="/settings" class="form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="section" value="steam"><label>Steam Web API Key<input type="password" name="steam_api_key" autocomplete="off" value="<?=Security::e($settings['steam.api_key']??'')?>" placeholder="Steam Web API Key"></label><p class="help">Für die öffentliche Steam Web API genügt ein User Web API Key. Die Verwaltungssoftware sendet ihn ausschließlich serverseitig über HTTPS an Steam.</p><button class="btn btn-primary">Steam-Einstellung speichern</button></form><form method="post" action="/settings/test-steam" class="toolbar top-gap"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-secondary">Steam API testen</button></form></section>

<section class="panel narrow"><div class="panel-header"><h2>SMTP</h2><p>Wird für Einladungen verwendet. Das Passwort wird verschlüsselt gespeichert.</p></div><form method="post" action="/settings" class="form-stack"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="section" value="smtp"><div class="form-grid"><label>SMTP-Host<input type="text" name="smtp_host" value="<?=Security::e($settings['smtp.host']??'')?>"></label><label>Port<input type="number" name="smtp_port" value="<?=Security::e($settings['smtp.port']??'587')?>"></label></div><label>Verschlüsselung<select name="smtp_encryption"><?php foreach(['tls'=>'STARTTLS','ssl'=>'SSL/TLS','none'=>'Keine'] as $k=>$v):?><option value="<?=$k?>" <?=($settings['smtp.encryption']??'tls')===$k?'selected':''?>><?=$v?></option><?php endforeach;?></select></label><label>Benutzer<input type="text" name="smtp_username" value="<?=Security::e($settings['smtp.username']??'')?>"></label><label>Passwort<input type="password" name="smtp_password" value="<?=Security::e($settings['smtp.password']??'')?>"></label><div class="form-grid"><label>Absenderadresse<input type="email" name="smtp_from_email" value="<?=Security::e($settings['smtp.from_email']??'')?>"></label><label>Absendername<input type="text" name="smtp_from_name" value="<?=Security::e($settings['smtp.from_name']??'Serververwaltung')?>"></label></div><button class="btn btn-primary">SMTP-Einstellungen speichern</button></form><form method="post" action="/settings/test-mail" class="toolbar top-gap"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-secondary">Test-E-Mail an mich senden</button></form></section>

</div>
</fieldset>
