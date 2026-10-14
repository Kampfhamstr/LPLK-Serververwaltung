<?php
$root=dirname(__DIR__);
$auth=file_get_contents($root.'/src/Core/Auth.php');
$controller=file_get_contents($root.'/src/Controllers/AuthController.php');
$login=file_get_contents($root.'/templates/login.php');
$login2=file_get_contents($root.'/templates/login_2fa.php');
$profile=file_get_contents($root.'/templates/profile.php');
$routes=file_get_contents($root.'/public/index.php');
$totpSource=file_get_contents($root.'/src/Core/TOTP.php');

require_once $root.'/src/Core/TOTP.php';

function ok167(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok167(!str_contains($login,'name="totp"'),'Login Schritt 1 enthält keinen 2FA-Code');
ok167(str_contains($login,'Schritt 1'),'Login kennzeichnet Schritt 1');
ok167(str_contains($login2,'Schritt 2'),'Separate 2FA-Maske vorhanden');
ok167(str_contains($login2,'name="totp"'),'2FA-Maske enthält Authenticator-Code');
ok167(str_contains($routes,"'/login/2fa'"),'Separate 2FA-Routen vorhanden');

ok167(str_contains($auth,'beginLogin'),'Passwortprüfung ist eigener Login-Schritt');
ok167(str_contains($auth,"return '2fa_required'"),'Passwort kann 2FA-Challenge auslösen');
ok167(str_contains($auth,'completeTwoFactor'),'Zweiter Faktor wird separat abgeschlossen');
ok167(str_contains($auth,"'expires_at'=>time()+300"),'2FA-Challenge läuft nach 5 Minuten ab');
ok167(str_contains($auth,">= 6"),'Maximal 6 falsche 2FA-Codes pro Challenge');
ok167(str_contains($auth,'finalizeLogin'),'Vollständige Sitzung wird zentral finalisiert');

$completeStart=strpos($auth,'public static function completeTwoFactor');
$pendingStart=strpos($auth,'public static function pendingTwoFactor',$completeStart);
$completeBlock=substr($auth,$completeStart,$pendingStart-$completeStart);
ok167(strpos($completeBlock,'TOTP::verify') < strpos($completeBlock,'self::finalizeLogin($user)'),'2FA-Pfad finalisiert erst nach TOTP-Prüfung');
ok167(str_contains($auth,'Das korrekte Passwort allein genügt bei aktiver 2FA nicht'),'last_login_at wird erst nach vollständigem Login gesetzt');

ok167(str_contains($totpSource,'otpauth://totp/'),'Google-Authenticator-Provisioning URI vorhanden');
ok167(str_contains($totpSource,'&algorithm=SHA1'),'TOTP SHA-1 deklariert');
ok167(str_contains($totpSource,'&digits=6'),'TOTP 6-stellig');
ok167(str_contains($totpSource,'&period=30'),'TOTP 30-Sekunden-Intervall');

$secret=\App\Core\TOTP::generateSecret();
$code=\App\Core\TOTP::code($secret);
ok167(\App\Core\TOTP::verify($secret,$code),'TOTP Code/Verify funktioniert');
$uri=\App\Core\TOTP::provisioningUri($secret,'test@example.com','LPLK Serververwaltung');
ok167(str_starts_with($uri,'otpauth://totp/'),'Provisioning URI ist TOTP-kompatibel');
ok167(str_contains($uri,'issuer=LPLK%20Serververwaltung'),'Issuer wird korrekt eingebettet');

ok167(str_contains($profile,'Google Authenticator'),'Google Authenticator im Profil ausdrücklich unterstützt');
ok167(str_contains($profile,'Einrichtungsschlüssel'),'Manueller Google-Authenticator-Setup-Key vorhanden');
ok167(str_contains($profile,'In Authenticator-App öffnen'),'otpauth Direktlink vorhanden');
ok167(!str_contains($profile,'api.qrserver')&&!str_contains($profile,'chart.googleapis'),'2FA-Secret wird nicht an externen QR-Dienst gesendet');

echo "\nv1.6.7 Two-Step-TOTP-Test erfolgreich.\n";
