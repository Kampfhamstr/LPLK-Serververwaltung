<?php
namespace App\Core;

final class TOTP
{
    private const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
    private const RECOVERY_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

    public static function generateSecret(int $bytes = 20): string
    {
        return self::base32Encode(random_bytes($bytes));
    }

    public static function provisioningUri(
        string $secret,
        string $account,
        string $issuer
    ): string {
        $secret = strtoupper(preg_replace('/[^A-Z2-7]/i', '', $secret) ?? '');
        $account = trim($account);
        $issuer = trim($issuer) !== '' ? trim($issuer) : 'Serververwaltung';

        $label = rawurlencode($issuer . ':' . $account);

        return 'otpauth://totp/' . $label
            . '?secret=' . rawurlencode($secret)
            . '&issuer=' . rawurlencode($issuer)
            . '&algorithm=SHA1'
            . '&digits=6'
            . '&period=30';
    }

    public static function formatSecret(string $secret): string
    {
        $secret = strtoupper(preg_replace('/[^A-Z2-7]/i', '', $secret) ?? '');
        return trim(chunk_split($secret, 4, ' '));
    }

    public static function verify(string $secret, string $code, int $window = 1): bool
    {
        if (!preg_match('/^\d{6}$/', $code)) return false;
        $counter = intdiv(time(), 30);
        for ($i = -$window; $i <= $window; $i++) {
            if (hash_equals(self::code($secret, $counter + $i), $code)) return true;
        }
        return false;
    }

    public static function code(string $secret, ?int $counter = null): string
    {
        $counter ??= intdiv(time(), 30);
        $key = self::base32Decode($secret);
        $binCounter = pack('N*', 0) . pack('N*', $counter);
        $hash = hash_hmac('sha1', $binCounter, $key, true);
        $offset = ord($hash[19]) & 0xf;
        $binary = ((ord($hash[$offset]) & 0x7f) << 24) |
                  ((ord($hash[$offset+1]) & 0xff) << 16) |
                  ((ord($hash[$offset+2]) & 0xff) << 8) |
                  (ord($hash[$offset+3]) & 0xff);
        return str_pad((string)($binary % 1000000), 6, '0', STR_PAD_LEFT);
    }

    /** @return list<string> */
    public static function generateRecoveryCodes(int $count = 10): array
    {
        $count = max(1, min(20, $count));
        $codes = [];
        while (count($codes) < $count) {
            $raw = '';
            for ($i = 0; $i < 12; $i++) {
                $raw .= self::RECOVERY_ALPHABET[random_int(0, strlen(self::RECOVERY_ALPHABET)-1)];
            }
            $code = substr($raw,0,4).'-'.substr($raw,4,4).'-'.substr($raw,8,4);
            $codes[$code] = true;
        }
        return array_keys($codes);
    }

    public static function encodeRecoveryHashes(array $codes): string
    {
        $hashes = [];
        foreach ($codes as $code) {
            $normalized = self::normalizeRecoveryCode((string)$code);
            if ($normalized === '') continue;
            $hashes[] = password_hash($normalized, PASSWORD_ARGON2ID);
        }
        return json_encode($hashes, JSON_UNESCAPED_SLASHES) ?: '[]';
    }

    public static function recoveryCount(?string $json): int
    {
        if (!$json) return 0;
        $hashes = json_decode($json, true);
        return is_array($hashes) ? count(array_filter($hashes, 'is_string')) : 0;
    }

    /**
     * Prüft einen Recovery-Code und entfernt ihn bei Erfolg sofort aus der Liste.
     * @return array{valid:bool,json:string,remaining:int}
     */
    public static function consumeRecoveryCode(?string $json, string $code): array
    {
        $normalized = self::normalizeRecoveryCode($code);
        $hashes = $json ? json_decode($json, true) : [];
        if ($normalized === '' || !is_array($hashes)) {
            return ['valid'=>false,'json'=>is_string($json)?$json:'[]','remaining'=>is_array($hashes)?count($hashes):0];
        }
        foreach ($hashes as $index => $hash) {
            if (!is_string($hash) || !password_verify($normalized, $hash)) continue;
            unset($hashes[$index]);
            $hashes = array_values($hashes);
            return [
                'valid'=>true,
                'json'=>json_encode($hashes, JSON_UNESCAPED_SLASHES) ?: '[]',
                'remaining'=>count($hashes),
            ];
        }
        return ['valid'=>false,'json'=>json_encode(array_values($hashes), JSON_UNESCAPED_SLASHES) ?: '[]','remaining'=>count($hashes)];
    }

    public static function normalizeRecoveryCode(string $code): string
    {
        return strtoupper(preg_replace('/[^A-Z0-9]/i', '', trim($code)) ?? '');
    }

    private static function base32Encode(string $data): string
    {
        $bits = '';
        foreach (str_split($data) as $c) $bits .= str_pad(decbin(ord($c)), 8, '0', STR_PAD_LEFT);
        $out = '';
        foreach (str_split($bits, 5) as $chunk) {
            $chunk = str_pad($chunk, 5, '0');
            $out .= self::ALPHABET[bindec($chunk)];
        }
        return $out;
    }

    private static function base32Decode(string $data): string
    {
        $data = strtoupper(preg_replace('/[^A-Z2-7]/', '', $data));
        $bits = '';
        foreach (str_split($data) as $c) {
            $pos = strpos(self::ALPHABET, $c);
            if ($pos === false) continue;
            $bits .= str_pad(decbin($pos), 5, '0', STR_PAD_LEFT);
        }
        $out = '';
        foreach (str_split($bits, 8) as $chunk) {
            if (strlen($chunk) < 8) break;
            $out .= chr(bindec($chunk));
        }
        return $out;
    }
}
