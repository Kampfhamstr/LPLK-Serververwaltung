<?php use App\Core\Security; ?>
<section class="auth-card">
  <div class="auth-step">Schritt 1</div>
  <div class="auth-heading">
    <h1>Serververwaltung</h1>
    <p>E-Mail-Adresse und Passwort bestätigen.</p>
  </div>

  <form method="post" action="/login" class="form-stack" autocomplete="off">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <label>E-Mail-Adresse<input type="email" name="email" autocomplete="off" autocapitalize="none" spellcheck="false" required autofocus></label>
    <label>Passwort<input type="password" name="password" autocomplete="current-password" required></label>
    <button class="btn btn-primary" type="submit">Weiter</button>

    <div class="auth-login-help">
      <span>Ohne aktivierte 2FA erfolgt die Anmeldung direkt.</span>
      <span>Nur bei aktivierter 2FA erscheint anschließend Schritt 2.</span>
    </div>

    <div class="auth-links"><a href="/forgot-password">Passwort vergessen?</a></div>
  </form>
</section>
