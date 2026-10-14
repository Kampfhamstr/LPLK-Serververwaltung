<?php use App\Core\Security; ?>
<section class="auth-card">
  <div class="auth-heading">
    <h1>Passwort vergessen</h1>
    <p>Geben Sie die E-Mail-Adresse Ihres Webpanel-Kontos ein.</p>
  </div>

  <form method="post" action="/forgot-password" class="form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <label>E-Mail-Adresse
      <input type="email" name="email" autocomplete="email" required autofocus>
    </label>
    <button class="btn btn-primary" type="submit">Reset-Link anfordern</button>
    <div class="auth-links"><a href="/login">Zurück zur Anmeldung</a></div>
  </form>

  <p class="auth-security-note">
    Aus Sicherheitsgründen wird nicht angezeigt, ob für die eingegebene Adresse tatsächlich ein Konto existiert.
  </p>
</section>
