<?php
$root=dirname(__DIR__);
$need=[
 'src/Core/WebAuthn.php'=>'class WebAuthn',
 'src/Controllers/AuthController.php'=>'passkeyRegisterOptions',
 'public/index.php'=>'/login/2fa/passkey/options',
 'templates/profile.php'=>'data-passkey-register',
 'templates/login_2fa.php'=>'data-passkey-login',
 'migrations/schema.sql'=>'webauthn_credentials',
 'src/Services/AntiAdvertisementService.php'=>'global_advertising_identities',
 'src/Services/FastLiveService.php'=>'global_anti_advertisement',
];
foreach($need as $f=>$needle){$p="$root/$f";$s=@file_get_contents($p);if($s===false||!str_contains($s,$needle)){fwrite(STDERR,"FAIL $f $needle\n");exit(1);}}
echo "OK v1.9.2 Passkey + global AntiAdvertisement\n";
