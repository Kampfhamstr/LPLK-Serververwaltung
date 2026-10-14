<?php
return [
    'app_name' => 'Serververwaltung',
    'app_url' => 'https://verwaltung.example.de',
    'app_key' => 'base64:CHANGE_ME',
    'timezone' => 'Europe/Berlin',
    'pets' => [
        // Globaler Not-Aus; bestehende Pet-Daten bleiben beim Deaktivieren erhalten.
        'enabled' => true,
    ],
    // Passkeys/WebAuthn: leer lassen = automatisch aus app_url ableiten.
    'webauthn' => [
        'rp_id' => '',
        'origin' => '',
        'rp_name' => 'Serververwaltung',
    ],
    'database' => [
        'driver' => 'sqlite',
        'path' => __DIR__ . '/../storage/database/app.sqlite',
    ],
    'session' => [
        'name' => 'serververwaltung_session',
        'lifetime' => 3600,
        'secure' => true,
        'same_site' => 'Strict',
    ],
];
