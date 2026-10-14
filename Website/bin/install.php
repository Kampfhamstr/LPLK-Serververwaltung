#!/usr/bin/env php
<?php
if (PHP_SAPI !== 'cli') { fwrite(STDERR, "Nur über die Kommandozeile ausführen.\n"); exit(1); }

$root = dirname(__DIR__);
$configFile = $root . '/config/app.php';
$exampleFile = $root . '/config/app.example.php';
$schemaFile = $root . '/migrations/schema.sql';

function out(string $text=''): void { fwrite(STDOUT, $text . PHP_EOL); }
function prompt(string $label, string $default=''): string {
    $suffix = $default !== '' ? " [$default]" : '';
    fwrite(STDOUT, $label . $suffix . ': ');
    $value = trim((string)fgets(STDIN));
    return $value !== '' ? $value : $default;
}
function hiddenPrompt(string $label): string {
    fwrite(STDOUT, $label . ': ');
    $stty = trim((string)@shell_exec('command -v stty 2>/dev/null'));
    if ($stty !== '') @shell_exec('stty -echo');
    $value = rtrim((string)fgets(STDIN), "\r\n");
    if ($stty !== '') { @shell_exec('stty echo'); fwrite(STDOUT, PHP_EOL); }
    return $value;
}

out('Serververwaltung v1.15.2.8 – Installation');
out(str_repeat('=', 42));

$requiredExtensions = ['pdo_sqlite', 'curl', 'sodium', 'openssl'];
$missing = [];
foreach ($requiredExtensions as $ext) if (!extension_loaded($ext)) $missing[] = $ext;
if ($missing) {
    out('Fehlende PHP-Erweiterungen: ' . implode(', ', $missing));
    out('Installation abgebrochen. Siehe INSTALLATION.md.');
    exit(1);
}
if (!is_file($exampleFile) || !is_file($schemaFile)) { out('Installationsdateien fehlen.'); exit(1); }
if (is_file($configFile)) {
    $answer = strtolower(prompt('config/app.php existiert bereits. Neu installieren? (nein/ja)', 'nein'));
    if (!in_array($answer, ['ja','j','yes','y'], true)) { out('Abgebrochen.'); exit(0); }
}

$appUrl = rtrim(prompt('Öffentliche HTTPS-URL', 'https://verwaltung.example.de'), '/');
if (!str_starts_with($appUrl, 'https://')) {
    out('Für den produktiven Betrieb muss die URL mit https:// beginnen.');
    exit(1);
}
$name = prompt('Founder-Anzeigename');
$email = strtolower(prompt('Founder-E-Mail-Adresse'));
if ($name === '' || !filter_var($email, FILTER_VALIDATE_EMAIL)) { out('Name oder E-Mail-Adresse ist ungültig.'); exit(1); }

$password = hiddenPrompt('Founder-Passwort (mindestens 12 Zeichen)');
$confirm = hiddenPrompt('Founder-Passwort wiederholen');
if (strlen($password) < 12 || !hash_equals($password, $confirm)) { out('Passwörter stimmen nicht überein oder sind kürzer als 12 Zeichen.'); exit(1); }

foreach ([$root.'/storage', $root.'/storage/database', $root.'/storage/backups', $root.'/storage/logs', $root.'/storage/steam-avatars'] as $dir) {
    if (!is_dir($dir) && !mkdir($dir, 0770, true)) { out('Verzeichnis konnte nicht erstellt werden: ' . $dir); exit(1); }
}

$key = base64_encode(random_bytes(SODIUM_CRYPTO_SECRETBOX_KEYBYTES));
$config = "<?php\nreturn [\n" .
    "    'app_name' => 'Serververwaltung',\n" .
    "    'app_url' => " . var_export($appUrl, true) . ",\n" .
    "    'app_key' => 'base64:" . $key . "',\n" .
    "    'timezone' => 'Europe/Berlin',\n" .
    "    'database' => [\n        'driver' => 'sqlite',\n        'path' => __DIR__ . '/../storage/database/app.sqlite',\n    ],\n" .
    "    'session' => [\n        'name' => 'serververwaltung_session',\n        'lifetime' => 3600,\n        'secure' => true,\n        'same_site' => 'Strict',\n    ],\n" .
    "];\n";
if (file_put_contents($configFile, $config, LOCK_EX) === false) { out('config/app.php konnte nicht geschrieben werden.'); exit(1); }
@chmod($configFile, 0640);

$dbPath = $root . '/storage/database/app.sqlite';
$pdo = new PDO('sqlite:' . $dbPath, null, null, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$pdo->exec('PRAGMA foreign_keys = ON');
$pdo->exec((string)file_get_contents($schemaFile));
$pdo->exec('CREATE INDEX IF NOT EXISTS idx_global_ad_events_review ON global_advertising_events(review_status,received_at DESC,id DESC)');
$pdo->exec('CREATE INDEX IF NOT EXISTS idx_global_ad_events_ban ON global_advertising_events(global_ban_id,id DESC)');

$count = (int)$pdo->query("SELECT COUNT(*) FROM users WHERE web_role='founder'")->fetchColumn();
if ($count > 0) {
    out('Es existiert bereits mindestens ein Founder. Es wurde kein weiterer Account angelegt.');
} else {
    $stmt = $pdo->prepare('INSERT INTO users(name,email,password_hash,web_role,permissions_json,active,created_at,updated_at) VALUES(?,?,?,\'founder\',NULL,1,?,?)');
    $stmt->execute([$name, $email, password_hash($password, PASSWORD_ARGON2ID), date('c'), date('c')]);
    out('Founder-Account wurde angelegt: ' . $email);
}
@chmod($dbPath, 0660);

out();
out('Installation abgeschlossen.');
out('Nächster Schritt: Webserver-Document-Root auf ' . $root . '/public setzen.');
out('Danach HTTPS aktivieren und dem Webserver-Benutzer Schreibrechte auf storage/ geben.');
out('Details: ' . $root . '/INSTALLATION.md');
