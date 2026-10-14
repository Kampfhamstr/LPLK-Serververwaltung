#!/usr/bin/env php
<?php
if (PHP_SAPI !== 'cli') exit(1);
$root = dirname(__DIR__);
require $root . '/src/bootstrap.php';
use App\Core\Database;
function p(string $label): string { fwrite(STDOUT,$label.': '); return trim((string)fgets(STDIN)); }
function hp(string $label): string { fwrite(STDOUT,$label.': '); @shell_exec('stty -echo'); $v=rtrim((string)fgets(STDIN),"\r\n"); @shell_exec('stty echo'); fwrite(STDOUT,PHP_EOL); return $v; }
$name=p('Anzeigename');$email=strtolower(p('E-Mail'));$password=hp('Passwort (mindestens 12 Zeichen)');$confirm=hp('Passwort wiederholen');
if($name===''||!filter_var($email,FILTER_VALIDATE_EMAIL)||strlen($password)<12||!hash_equals($password,$confirm)){fwrite(STDERR,"Ungültige Eingabe.\n");exit(1);}
try{Database::connection()->prepare("INSERT INTO users(name,email,password_hash,web_role,permissions_json,active,created_at,updated_at) VALUES(?,?,?,'founder',NULL,1,?,?)")->execute([$name,$email,password_hash($password,PASSWORD_ARGON2ID),date('c'),date('c')]);echo "Founder wurde erstellt.\n";}catch(Throwable $e){fwrite(STDERR,"Fehler: ".$e->getMessage()."\n");exit(1);}
