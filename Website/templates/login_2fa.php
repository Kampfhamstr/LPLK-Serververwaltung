<?php use App\Core\Security; ?>
<section class="auth-card">
  <div class="auth-step auth-step-active">Schritt 2</div>
  <div class="auth-heading">
    <h1>Zwei-Faktor-Authentifizierung</h1>
    <p>Das Passwort wurde bestätigt. Jetzt mit Passkey bestätigen oder einen Authenticator-/Recovery-Code verwenden.</p>
  </div>


  <?php if(!empty($passkeyAvailable)):?>
  <div class="passkey-login-box" data-passkey-login data-csrf="<?=Security::e(Security::csrfToken())?>">
    <button class="btn btn-primary passkey-login-button" type="button" data-passkey-login-button>Mit Passkey bestätigen</button>
    <span class="help">Windows Hello, Fingerabdruck, Face ID oder FIDO2-Sicherheitsschlüssel. Phishing-resistent und als vollwertiger zweiter Faktor anerkannt.</span>
    <div class="small-muted" data-passkey-login-status></div>
  </div>
  <div class="auth-method-divider"><span>oder</span></div>
  <?php endif;?>

  <form method="post" action="/login/2fa" class="form-stack" autocomplete="off">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <label>
      Authenticator-Code
      <input class="totp-login-input" type="text" name="totp" maxlength="20" autocomplete="one-time-code" placeholder="123456 oder ABCD-EFGH-IJKL" required autofocus>
      <span class="help">Google Authenticator, Microsoft Authenticator und andere TOTP-Apps werden unterstützt. Alternativ funktioniert ein noch unbenutzter Recovery-Code.</span>
    </label>
    <button class="btn btn-primary" type="submit">Anmelden</button>
  </form>

  <form method="post" action="/login/2fa/cancel" class="auth-cancel-form">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <button class="btn btn-secondary" type="submit">Mit anderem Konto anmelden</button>
  </form>

  <div class="auth-login-help">
    <span>Die 2FA-Anfrage läuft nach 5 Minuten ab.</span>
    <span>Nach 6 falschen Codes muss Schritt 1 erneut durchgeführt werden.</span>
  </div>
</section>
