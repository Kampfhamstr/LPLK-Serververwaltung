<?php
namespace App\Services;

final class SteamOpenIdService
{
    public const PROVIDER = 'https://steamcommunity.com/openid/login';
    private const NS = 'http://specs.openid.net/auth/2.0';
    private const IDENTIFIER_SELECT = 'http://specs.openid.net/auth/2.0/identifier_select';

    public static function loginUrl(string $returnTo, string $realm): string
    {
        $params = [
            'openid.ns' => self::NS,
            'openid.mode' => 'checkid_setup',
            'openid.return_to' => $returnTo,
            'openid.realm' => rtrim($realm, '/') . '/',
            'openid.identity' => self::IDENTIFIER_SELECT,
            'openid.claimed_id' => self::IDENTIFIER_SELECT,
        ];

        return self::PROVIDER . '?' . http_build_query($params, '', '&', PHP_QUERY_RFC3986);
    }

    public static function verify(array $query, string $expectedReturnTo): string
    {
        $stateReturn = (string)($query['openid_return_to'] ?? '');
        if ($stateReturn === '' || !hash_equals($expectedReturnTo, $stateReturn)) {
            throw new \RuntimeException('Steam OpenID return_to stimmt nicht mit dieser Sitzung überein.');
        }

        $opEndpoint = (string)($query['openid_op_endpoint'] ?? '');
        if ($opEndpoint !== '' && !hash_equals(self::PROVIDER, $opEndpoint)) {
            throw new \RuntimeException('Unerwarteter Steam OpenID Provider.');
        }

        $claimed = (string)($query['openid_claimed_id'] ?? '');
        if (!preg_match('#^https?://steamcommunity\.com/openid/id/(7656119\d{10})$#', $claimed, $m)) {
            throw new \RuntimeException('Steam hat keine gültige SteamID64 zurückgegeben.');
        }

        $map = [
            'openid_ns' => 'openid.ns',
            'openid_op_endpoint' => 'openid.op_endpoint',
            'openid_claimed_id' => 'openid.claimed_id',
            'openid_identity' => 'openid.identity',
            'openid_return_to' => 'openid.return_to',
            'openid_response_nonce' => 'openid.response_nonce',
            'openid_assoc_handle' => 'openid.assoc_handle',
            'openid_signed' => 'openid.signed',
            'openid_sig' => 'openid.sig',
        ];

        $verify = [];
        foreach ($map as $phpKey => $openidKey) {
            if (isset($query[$phpKey]) && is_string($query[$phpKey])) {
                $verify[$openidKey] = $query[$phpKey];
            }
        }
        $verify['openid.mode'] = 'check_authentication';

        $ch = curl_init(self::PROVIDER);
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_POST => true,
            CURLOPT_POSTFIELDS => http_build_query($verify, '', '&', PHP_QUERY_RFC3986),
            CURLOPT_CONNECTTIMEOUT => 8,
            CURLOPT_TIMEOUT => 15,
            CURLOPT_HTTPHEADER => ['Content-Type: application/x-www-form-urlencoded', 'Accept: text/plain'],
            CURLOPT_USERAGENT => 'Serververwaltung/1.15.1',
        ]);
        $body = curl_exec($ch);
        if ($body === false) {
            $error = curl_error($ch);
            curl_close($ch);
            throw new \RuntimeException('Steam OpenID konnte nicht bestätigt werden: ' . $error);
        }
        $status = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);

        if ($status !== 200 || !preg_match('/(?:^|\R)is_valid\s*:\s*true(?:\R|$)/i', (string)$body)) {
            throw new \RuntimeException('Steam OpenID Bestätigung war nicht gültig.');
        }

        return $m[1];
    }
}
