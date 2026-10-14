<?php
$root=dirname(__DIR__);
$version=trim(file_get_contents($root.'/VERSION'));
$auth=file_get_contents($root.'/src/Core/Auth.php');
$authController=file_get_contents($root.'/src/Controllers/AuthController.php');
$totp=file_get_contents($root.'/src/Core/TOTP.php');
$profile=file_get_contents($root.'/templates/profile.php');
$login2fa=file_get_contents($root.'/templates/login_2fa.php');
$layout=file_get_contents($root.'/templates/layout.php');
$live=file_get_contents($root.'/templates/server_live.php');
$css=file_get_contents($root.'/public/assets/app.css');
$js=file_get_contents($root.'/public/assets/app.js');
$qr=file_get_contents($root.'/public/assets/qrcode-local.js');
$schema=file_get_contents($root.'/migrations/schema.sql');
$migrate=file_get_contents($root.'/bin/migrate.php');
function ok181(bool $ok,string $msg):void{if(!$ok){fwrite(STDERR,"FEHLER: $msg\n");exit(1);}echo "OK  $msg\n";}

ok181(version_compare($version,'1.8.1','>='),'VERSION ist 1.8.1 oder neuer');
ok181(str_contains($profile,'data-totp-qr')&&str_contains($profile,'In Authenticator-App öffnen')&&str_contains($profile,'Manuellen Einrichtungsschlüssel anzeigen'),'2FA bietet QR-Code, App-Link und manuellen Fallback');
ok181(str_contains($layout,'qrcode-local.js')&&str_contains($qr,'SerververwaltungQRCode')&&!preg_match('~https?://[^\s"\']+~',$profile),'QR-Code wird aus lokal gebündeltem Code erzeugt, ohne externen QR-Dienst');
ok181(str_contains($js,'SerververwaltungQRCode?.createSvg')&&str_contains($js,'data-copy-recovery-codes'),'Browser rendert QR lokal und unterstützt Copy-Buttons');
ok181(str_contains($schema,'totp_recovery_codes_json')&&str_contains($migrate,'ALTER TABLE users ADD COLUMN totp_recovery_codes_json'),'Recovery-Code-Speicher wird migrationssicher ergänzt');
ok181(str_contains($totp,'generateRecoveryCodes')&&str_contains($totp,'PASSWORD_ARGON2ID')&&str_contains($totp,'consumeRecoveryCode'),'Recovery-Codes werden erzeugt, gehasht und einmalig verbraucht');
ok181(str_contains($auth,'auth.2fa.recovery_used')&&str_contains($login2fa,'Recovery-Code'),'Recovery-Code kann als Login-Fallback verwendet werden und wird auditiert');
ok181(str_contains($auth,'founderTwoFactorLocked')&&str_contains($auth,'return self::founderTwoFactorLocked($user) ? \'admin\' : $role')&&str_contains($auth,'Founder-Sicherheitsmodus: Admin-Vorlage'),'Founder ohne 2FA wird effektiv auf Admin-Vorlage begrenzt');
ok181(str_contains($layout,'Admin-Modus · Founder gesperrt')&&str_contains($profile,'Founder durch 2FA freischalten'),'Founder-Sicherheitsmodus wird verständlich erklärt');
ok181(str_contains($authController,'founder_unlocked')&&str_contains($authController,'Founder-Vollzugriff ist jetzt freigeschaltet'),'Founder wird nach erfolgreicher 2FA automatisch freigeschaltet');
ok181(substr_count($live,'<h3>Server neu starten</h3>')===0,'Doppelte alte Server-Neustart-Kachel wurde entfernt');
ok181(str_contains($live,'btn-success ptero-power-button')&&str_contains($live,'btn-warning ptero-power-button')&&str_contains($live,'btn-stop ptero-power-button'),'Start, Restart und Stop sind eindeutige farbige Power-Buttons');
ok181(str_contains($live,'ptero-emergency-control')&&str_contains($live,'Hard Kill ausführen'),'Kill ist als separate Notfallaktion gekapselt');
ok181(str_contains($css,'.nav-accent-blue')&&str_contains($css,'.nav-accent-green')&&str_contains($layout,'nav-accent-'),'Hauptnavigation besitzt professionelle Bereichs-Farbakzente');
ok181(str_contains($authController,'auth.2fa.enabled')&&str_contains($authController,'auth.2fa.disabled')&&str_contains($authController,'auth.2fa.recovery_regenerated'),'2FA-Sicherheitsänderungen werden auditiert');

require_once $root.'/src/Core/TOTP.php';
$secret=\App\Core\TOTP::generateSecret();
$code=\App\Core\TOTP::code($secret);
ok181(\App\Core\TOTP::verify($secret,$code),'TOTP-Generator und Verifikation funktionieren');
$recovery=\App\Core\TOTP::generateRecoveryCodes(10);
ok181(count($recovery)===10&&count(array_unique($recovery))===10,'10 eindeutige Recovery-Codes werden erzeugt');
$encoded=\App\Core\TOTP::encodeRecoveryHashes($recovery);
ok181(!str_contains($encoded,$recovery[0]),'Recovery-Codes liegen nicht im Klartext in der Hashliste');
$consume=\App\Core\TOTP::consumeRecoveryCode($encoded,$recovery[0]);
ok181($consume['valid']===true&&$consume['remaining']===9,'Recovery-Code wird genau einmal verbraucht');
$consumeAgain=\App\Core\TOTP::consumeRecoveryCode($consume['json'],$recovery[0]);
ok181($consumeAgain['valid']===false,'Verbrauchter Recovery-Code kann nicht erneut verwendet werden');

echo "v1.8.1 Professional 2FA & Live UI Test erfolgreich.\n";
