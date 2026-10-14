<?php use App\Core\Security; ?>
<section class="panel narrow">
  <div class="panel-header">
    <h2>Serververbindung</h2>
    <p>Pterodactyl Client API wird empfohlen. SFTP und FTPS sind sichere direkte Alternativen; unverschlüsseltes FTP sollte nur in abgeschotteten Netzen verwendet werden.</p>
  </div>
  <form method="post" action="/servers" class="form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <label>Name<input type="text" name="name" required></label>
    <label>Verbindung
      <select name="connector_type" id="connector_type">
        <option value="pterodactyl">Pterodactyl Client API</option>
        <option value="sftp">SFTP</option>
        <option value="ftps">FTPS (FTP über TLS)</option>
        <option value="ftp">FTP (unverschlüsselt, nicht empfohlen)</option>
      </select>
    </label>
    <div data-connector="pterodactyl" class="form-stack">
      <label>Panel-URL<input type="url" name="base_url" placeholder="https://panel.example.de"></label>
      <label>Server-Identifier<input type="text" name="server_identifier" placeholder="z. B. 8 Zeichen oder UUID"></label>
      <label>Client API-Key<input type="password" name="secret_ptero" autocomplete="new-password"></label>
    </div>
    <div data-connector="sftp" class="form-stack hidden">
      <div class="form-grid">
        <label>Host<input type="text" name="host"></label>
        <label>Port<input type="number" name="port" value="2022" min="1" max="65535"></label>
      </div>
      <label>Benutzer<input type="text" name="username"></label>
      <label>SFTP-Passwort<input type="password" name="secret_sftp" autocomplete="new-password"></label>
    </div>
    <div data-connector="ftps" class="form-stack hidden">
      <div class="form-grid">
        <label>Host<input type="text" name="host"></label>
        <label>Port<input type="number" name="port" value="21" min="1" max="65535"></label>
      </div>
      <label>Benutzer<input type="text" name="username"></label>
      <label>FTPS-Passwort<input type="password" name="secret_ftp" autocomplete="new-password"></label>
    </div>
    <div data-connector="ftp" class="form-stack hidden">
      <div class="alert alert-warning">FTP überträgt Zugangsdaten und Daten unverschlüsselt. SFTP, FTPS oder die Pterodactyl API sind vorzuziehen.</div>
      <div class="form-grid">
        <label>Host<input type="text" name="host"></label>
        <label>Port<input type="number" name="port" value="21" min="1" max="65535"></label>
      </div>
      <label>Benutzer<input type="text" name="username"></label>
      <label>FTP-Passwort<input type="password" name="secret_ftp" autocomplete="new-password"></label>
    </div>
    <label>Root-Pfad<input type="text" name="root_path" value="/" placeholder="/"><span class="help">Bei Pterodactyl normalerweise /.</span></label>
    <button class="btn btn-primary" type="submit">Server anlegen</button>
  </form>
</section>
