<?php
$root=dirname(__DIR__);
$controller=file_get_contents($root.'/src/Controllers/AuthController.php');
$auth=file_get_contents($root.'/src/Core/Auth.php');
$routes=file_get_contents($root.'/public/index.php');
$schema=file_get_contents($root.'/migrations/schema.sql');
$migrate=file_get_contents($root.'/bin/migrate.php');
$login=file_get_contents($root.'/templates/login.php');
$forgot=file_get_contents($root.'/templates/forgot_password.php');
$reset=file_get_contents($root.'/templates/reset_password.php');

function ok133(bool $ok,string $msg):void{
    if(!$ok) throw new RuntimeException($msg);
    echo "OK  {$msg}\n";
}

ok133(str_contains($routes,'/forgot-password'),'Forgot-Password-Routen vorhanden');
ok133(str_contains($routes,'/reset-password/{token}'),'Reset-Token-Routen vorhanden');
ok133(str_contains($login,'Passwort vergessen?'),'Login verlinkt Passwort vergessen');
ok133(str_contains($schema,'CREATE TABLE IF NOT EXISTS password_reset_requests'),'Reset-Tabelle im Schema');
ok133(str_contains($schema,'auth_version INTEGER NOT NULL DEFAULT 1'),'auth_version im Users-Schema');
ok133(str_contains($migrate,'ALTER TABLE users ADD COLUMN auth_version'),'Migration ergänzt auth_version bei Altinstallationen');
ok133(str_contains($controller,'hash(\'sha256\',$token)'),'Reset-Token wird nur als SHA-256-Hash verwendet');
ok133(str_contains($controller,'time()+3600'),'Reset-Link läuft nach 60 Minuten ab');
ok133(str_contains($controller,'>=5'),'IP-Rate-Limit vorhanden');
ok133(str_contains($controller,'>=3'),'E-Mail-Rate-Limit vorhanden');
ok133(str_contains($controller,'PASSWORD_ARGON2ID'),'Reset speichert Passwort mit Argon2id');
ok133(str_contains($controller,'auth_version=?,updated_at=?'),'Passwortänderung erhöht auth_version');
ok133(str_contains($auth,'$_SESSION[\'auth_version\']'),'Sessions sind an auth_version gebunden');
ok133(str_contains($controller,'all_sessions_invalidated'),'Reset protokolliert Session-Ungültigmachung');
ok133(str_contains($forgot,'nicht angezeigt, ob'),'Formular erklärt neutrale Account-Antwort');
ok133(str_contains($reset,'Zwei-Faktor-Authentifizierung bleibt aktiv'),'Reset-Formular weist auf erhaltene 2FA hin');

echo "\nPassword-Recovery-Test erfolgreich.\n";
