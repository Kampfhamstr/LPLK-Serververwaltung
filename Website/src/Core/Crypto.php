<?php
namespace App\Core;

final class Crypto
{
    private static function key(): string
    {
        $raw = (string) Config::get('app_key', '');
        if (!str_starts_with($raw, 'base64:')) {
            throw new \RuntimeException('APP_KEY ist nicht korrekt konfiguriert.');
        }
        $key = base64_decode(substr($raw, 7), true);
        if ($key === false || strlen($key) !== SODIUM_CRYPTO_SECRETBOX_KEYBYTES) {
            throw new \RuntimeException('APP_KEY muss 32 Byte lang sein.');
        }
        return $key;
    }

    public static function encrypt(string $plain): string
    {
        $nonce = random_bytes(SODIUM_CRYPTO_SECRETBOX_NONCEBYTES);
        $cipher = sodium_crypto_secretbox($plain, $nonce, self::key());
        return base64_encode($nonce . $cipher);
    }

    public static function decrypt(?string $encoded): string
    {
        if (!$encoded) return '';
        $raw = base64_decode($encoded, true);
        if ($raw === false || strlen($raw) <= SODIUM_CRYPTO_SECRETBOX_NONCEBYTES) {
            throw new \RuntimeException('Verschlüsselter Wert ist ungültig.');
        }
        $nonce = substr($raw, 0, SODIUM_CRYPTO_SECRETBOX_NONCEBYTES);
        $cipher = substr($raw, SODIUM_CRYPTO_SECRETBOX_NONCEBYTES);
        $plain = sodium_crypto_secretbox_open($cipher, $nonce, self::key());
        if ($plain === false) throw new \RuntimeException('Entschlüsselung fehlgeschlagen.');
        return $plain;
    }
}
