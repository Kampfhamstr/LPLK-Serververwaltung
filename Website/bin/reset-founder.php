#!/usr/bin/env php
<?php
if (PHP_SAPI !== 'cli') exit(1);
$root = dirname(__DIR__);
require $root . '/src/bootstrap.php';
use App\Core\Database;
function p(string $label): string { fwrite(STDOUT,$label.': '); return trim((string)fgets(STDIN)); }
function hp(string $label): string { fwrite(STDOUT,$label.': '); @shell_exec('stty -echo'); $v=rtrim((string)fgets(STDIN),"\r\n"); @shell_exec('stty echo'); fwrite(STDOUT,PHP_EOL); return $v; }
$email=strtolower(p('Founder-E-Mail'));$pdo=Database::connection();$stmt=$pdo->prepare("SELECT id,name FROM users WHERE lower(email)=lower(?) AND web_role='founder'");$stmt->execute([$email]);$u=$stmt->fetch();if(!$u){fwrite(STDERR,"Founder nicht gefunden.\n");exit(1);} $password=hp('Neues Passwort (mindestens 12 Zeichen)');$confirm=hp('Passwort wiederholen');if(strlen($password)<12||!hash_equals($password,$confirm)){fwrite(STDERR,"Passwörter ungültig.\n");exit(1);} $pdo->prepare('UPDATE users SET password_hash=?,totp_secret_enc=NULL,totp_recovery_codes_json=NULL,totp_enabled=0,active=1,updated_at=? WHERE id=?')->execute([password_hash($password,PASSWORD_ARGON2ID),date('c'),$u['id']]);echo "Founder-Passwort wurde zurückgesetzt; 2FA wurde deaktiviert.\n";
