<?php use App\Core\Security; ?>
<section class="auth-card">
  <div class="auth-heading">
    <h1>Neues Passwort festlegen</h1>
    <p>Der Reset-Link ist einmal verwendbar. Eine vorhandene Zwei-Faktor-Authentifizierung bleibt aktiv.</p>
  </div>

  <form method="post" action="/reset-password/<?=Security::e($token)?>" class="form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <label>Neues Passwort
      <input type="password" name="password" minlength="12" autocomplete="new-password" required autofocus>
    </label>
    <label>Neues Passwort bestätigen
      <input type="password" name="password_confirm" minlength="12" autocomplete="new-password" required>
    </label>
    <button class="btn btn-primary" type="submit">Passwort speichern</button>
    <div class="auth-links"><a href="/login">Zurück zur Anmeldung</a></div>
  </form>

  <p class="auth-security-note">
    Nach erfolgreichem Zurücksetzen werden alle bisherigen Webpanel-Sitzungen dieses Kontos beendet.
  </p>
</section>
