<?php
namespace App\Core;

final class Security
{
    public static function startSession(): void
    {
        if (session_status() === PHP_SESSION_ACTIVE) return;
        session_name((string)Config::get('session.name', 'serververwaltung_session'));
        session_set_cookie_params([
            'lifetime' => (int)Config::get('session.lifetime', 3600),
            'path' => '/',
            'secure' => (bool)Config::get('session.secure', true),
            'httponly' => true,
            'samesite' => (string)Config::get('session.same_site', 'Strict'),
        ]);
        ini_set('session.use_strict_mode', '1');
        ini_set('session.use_only_cookies', '1');

        // Das normale Panel-Cookie bleibt auch während Steam OpenID strikt.
        // Nur auf dem Callback darf ein kurzlebiges, signiertes Hilfscookie
        // die bereits bestehende Session wieder aufnehmen.
        $steamFlowSessionId = self::steamOpenIdFlowSessionId();
        if ($steamFlowSessionId !== null) session_id($steamFlowSessionId);

        session_start();
        $now = time();
        $lifetime = (int)Config::get('session.lifetime', 3600);
        if (isset($_SESSION['_last_activity']) && $now - (int)$_SESSION['_last_activity'] > $lifetime) {
            $_SESSION = [];
            session_regenerate_id(true);
        }
        $_SESSION['_last_activity'] = $now;
        if (!isset($_SESSION['_created'])) {
            session_regenerate_id(true);
            $_SESSION['_created'] = $now;
        } elseif ($now - (int)$_SESSION['_created'] > 900) {
            session_regenerate_id(true);
            $_SESSION['_created'] = $now;
        }
    }

    /** Erstellt ausschließlich für den Steam-Callback eine 10 Minuten gültige Session-Brücke. */
    public static function issueSteamOpenIdFlowCookie(string $state): void
    {
        if (session_status() !== PHP_SESSION_ACTIVE || headers_sent()) return;
        if (!preg_match('/^[a-f0-9]{48}$/D', $state)) throw new \InvalidArgumentException('Ungültiger Steam-OpenID-State.');

        $now = time();
        $payload = json_encode([
            'sid'=>session_id(),
            'state'=>$state,
            'issued_at'=>$now,
            'expires_at'=>$now + 600,
        ], JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
        $encoded = self::base64UrlEncode($payload);
        $token = $encoded . '.' . self::base64UrlEncode(self::steamOpenIdMac($encoded));

        setcookie(self::steamOpenIdFlowCookieName($state), $token, [
            'expires' => $now + 600,
            'path' => '/profile/steam/callback',
            'secure' => (bool)Config::get('session.secure', true),
            'httponly' => true,
            'samesite' => 'Lax',
        ]);
    }

    /** Entfernt die einmalige Steam-Session-Brücke unabhängig vom Callback-Ergebnis. */
    public static function clearSteamOpenIdFlowCookie(string $state): void
    {
        if (headers_sent() || !preg_match('/^[a-f0-9]{48}$/D', $state)) return;
        setcookie(self::steamOpenIdFlowCookieName($state), '', [
            'expires' => time() - 3600,
            'path' => '/profile/steam/callback',
            'secure' => (bool)Config::get('session.secure', true),
            'httponly' => true,
            'samesite' => 'Lax',
        ]);
    }

    private static function steamOpenIdFlowSessionId(): ?string
    {
        $path = (string)(parse_url((string)($_SERVER['REQUEST_URI'] ?? ''), PHP_URL_PATH) ?? '');
        if ($path !== '/profile/steam/callback') return null;

        $state = trim((string)($_GET['state'] ?? ''));
        if (!preg_match('/^[a-f0-9]{48}$/D', $state)) return null;
        $token = (string)($_COOKIE[self::steamOpenIdFlowCookieName($state)] ?? '');
        $parts = explode('.', $token, 2);
        if (count($parts) !== 2 || $parts[0] === '' || $parts[1] === '') return null;

        $signature = self::base64UrlDecode($parts[1]);
        if ($signature === null || !hash_equals(self::steamOpenIdMac($parts[0]), $signature)) return null;
        $decoded = self::base64UrlDecode($parts[0]);
        if ($decoded === null) return null;
        $payload = json_decode($decoded, true);
        if (!is_array($payload)) return null;

        $issuedAt = (int)($payload['issued_at'] ?? 0);
        $expiresAt = (int)($payload['expires_at'] ?? 0);
        $sessionId = (string)($payload['sid'] ?? '');
        if (!hash_equals((string)($payload['state'] ?? ''), $state)) return null;
        if ($issuedAt < time() - 600 || $issuedAt > time() + 30 || $expiresAt < time() || $expiresAt > $issuedAt + 600) return null;
        if (!preg_match('/^[A-Za-z0-9,-]{16,128}$/D', $sessionId)) return null;
        return $sessionId;
    }

    private static function steamOpenIdFlowCookieName(string $state): string
    {
        return session_name() . '_steam_' . $state;
    }

    private static function steamOpenIdMac(string $encodedPayload): string
    {
        $appKey = (string)Config::get('app_key', '');
        if ($appKey === '') throw new \RuntimeException('Der Anwendungsschlüssel fehlt.');
        return hash_hmac('sha256', $encodedPayload, hash('sha256', $appKey, true), true);
    }

    private static function base64UrlEncode(string $value): string
    {
        return rtrim(strtr(base64_encode($value), '+/', '-_'), '=');
    }

    private static function base64UrlDecode(string $value): ?string
    {
        if ($value === '' || preg_match('/[^A-Za-z0-9_-]/', $value)) return null;
        $normalized = strtr($value, '-_', '+/');
        $remainder = strlen($normalized) % 4;
        if ($remainder !== 0) $normalized .= str_repeat('=', 4 - $remainder);
        $decoded = base64_decode($normalized, true);
        return is_string($decoded) ? $decoded : null;
    }

    public static function csrfToken(): string
    {
        if (empty($_SESSION['_csrf'])) {
            $_SESSION['_csrf'] = bin2hex(random_bytes(32));
        }
        return $_SESSION['_csrf'];
    }

    public static function verifyCsrf(): void
    {
        $token = $_POST['_csrf'] ?? $_SERVER['HTTP_X_CSRF_TOKEN'] ?? '';
        if (!is_string($token) || !hash_equals(self::csrfToken(), $token)) {
            http_response_code(419);
            exit('Ungültiges oder abgelaufenes Formular-Token.');
        }
    }

    public static function e(string|int|float|null $value): string
    {
        return htmlspecialchars((string)$value, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
    }

    public static function clientIp(): string
    {
        return substr((string)($_SERVER['REMOTE_ADDR'] ?? 'unknown'), 0, 64);
    }
}
