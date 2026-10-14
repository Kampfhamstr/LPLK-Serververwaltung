<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Audit;
use App\Core\Config;
use App\Core\Database;
use App\Core\Security;
use App\Core\View;
use App\Services\SteamOpenIdService;
use App\Services\SteamService;

final class SteamIdentityController
{
    public function connect(): void
    {
        Auth::requireLogin();

        session_regenerate_id(true);
        $startedAt = time();
        $_SESSION['_created'] = $startedAt;
        $state = bin2hex(random_bytes(24));
        $_SESSION['steam_openid_state'] = $state;
        $_SESSION['steam_openid_started_at'] = $startedAt;

        $base = rtrim((string)Config::get('app_url', ''), '/');
        if (!str_starts_with($base, 'https://')) {
            throw new \RuntimeException('Steam-Verifizierung benötigt eine HTTPS app_url.');
        }

        $returnTo = $base . '/profile/steam/callback?state=' . rawurlencode($state);
        Security::issueSteamOpenIdFlowCookie($state);
        header('Location: ' . SteamOpenIdService::loginUrl($returnTo, $base));
    }

    public function callback(): void
    {
        Auth::requireLogin();

        $state = (string)($_GET['state'] ?? '');
        $expectedState = (string)($_SESSION['steam_openid_state'] ?? '');
        $started = (int)($_SESSION['steam_openid_started_at'] ?? 0);

        unset($_SESSION['steam_openid_state'], $_SESSION['steam_openid_started_at']);
        Security::clearSteamOpenIdFlowCookie($state);

        if ($state === '' || $expectedState === '' || !hash_equals($expectedState, $state) || $started < time() - 600) {
            View::flash('error', 'Steam-Verifizierung ist abgelaufen oder gehört nicht zu dieser Sitzung.');
            header('Location: /profile');
            return;
        }

        $base = rtrim((string)Config::get('app_url', ''), '/');
        $returnTo = $base . '/profile/steam/callback?state=' . rawurlencode($state);

        try {
            $steamId = SteamOpenIdService::verify($_GET, $returnTo);
            $user = Auth::user();
            $pdo = Database::connection();

            $dup = $pdo->prepare('SELECT id FROM users WHERE steam_id64=? AND id<>? LIMIT 1');
            $dup->execute([$steamId, (int)$user['id']]);
            if ($dup->fetchColumn()) {
                throw new \RuntimeException('Dieser Steam-Account ist bereits mit einem anderen Webpanel-Nutzer verifiziert.');
            }

            $profile = $this->profileForSteamId($steamId);
            $name = trim((string)($profile['personaname'] ?? ''));
            $profileUrl = $this->httpsUrl($profile['profileurl'] ?? null);
            $avatar = $this->httpsUrl($profile['avatarfull'] ?? ($profile['avatarmedium'] ?? null));

            if ($name === '') {
                throw new \RuntimeException('Der echte Steam-Anzeigename konnte nicht geladen werden. Bitte den Steam Web API-Key prüfen und die Verifizierung erneut starten.');
            }

            $verifiedAt = date('c');
            $pdo->prepare(
                'UPDATE users SET steam_id64=?,steam_personaname=?,steam_profile_url=?,steam_avatar_url=?,steam_verified_at=?,steam_profile_synced_at=?,steam_profile_refresh_attempted_epoch=?,updated_at=? WHERE id=?'
            )->execute([
                $steamId, $name, $profileUrl, $avatar, $verifiedAt, $verifiedAt, time(), $verifiedAt, (int)$user['id']
            ]);

            if ($avatar) SteamService::ensureAvatarCached($steamId, $avatar, true);

            session_regenerate_id(true);
            Audit::log('auth.steam.verified', 'user', (string)$user['id'], ['steam_id64'=>$steamId]);
            View::flash('success', 'Steam-Account wurde verifiziert. Im Webpanel wird künftig der verifizierte Steamname angezeigt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Steam-Verifizierung fehlgeschlagen: ' . $e->getMessage());
        }

        header('Location: /profile');
    }

    public function refresh(): void
    {
        Auth::requireLogin();
        Security::verifyCsrf();

        $user = Auth::user();
        $steamId = trim((string)($user['steam_id64'] ?? ''));
        if (!preg_match('/^7656119\d{10}$/', $steamId) || empty($user['steam_verified_at'])) {
            View::flash('error', 'Dieses Webpanel-Konto ist noch nicht mit Steam verifiziert.');
            header('Location: /profile');
            return;
        }

        try {
            $attemptedAt = time();
            $reserve = Database::connection()->prepare(
                'UPDATE users SET steam_profile_refresh_attempted_epoch=?,updated_at=? WHERE id=? AND COALESCE(steam_profile_refresh_attempted_epoch,0)<=CAST(? AS INTEGER)'
            );
            $reserve->execute([$attemptedAt, date('c', $attemptedAt), (int)$user['id'], $attemptedAt - 120]);
            if ($reserve->rowCount() !== 1) {
                View::flash('info', 'Das Steam-Profil wurde gerade erst abgerufen. Bitte warte zwei Minuten bis zum nächsten Versuch.');
                header('Location: /profile');
                return;
            }

            $profile = $this->profileForSteamId($steamId);
            $name = trim((string)($profile['personaname'] ?? ''));
            $profileUrl = $this->httpsUrl($profile['profileurl'] ?? null);
            $avatar = $this->httpsUrl($profile['avatarfull'] ?? ($profile['avatarmedium'] ?? null));

            if ($name === '') {
                throw new \RuntimeException('Der echte Steam-Anzeigename konnte nicht geladen werden. Bitte den Steam Web API-Key prüfen und die Aktualisierung erneut starten.');
            }

            Database::connection()->prepare(
                'UPDATE users SET steam_personaname=?,steam_profile_url=?,steam_avatar_url=?,steam_profile_synced_at=?,updated_at=? WHERE id=?'
            )->execute([
                $name,
                $profileUrl,
                $avatar,
                date('c'),
                date('c'),
                (int)$user['id'],
            ]);

            if ($avatar) SteamService::ensureAvatarCached($steamId, $avatar, true);
            Audit::log('auth.steam.profile_refreshed', 'user', (string)$user['id'], ['steam_id64'=>$steamId]);
            View::flash('success', 'Steam-Anzeigename und Avatar wurden aktualisiert.');
        } catch (\Throwable $e) {
            View::flash('error', 'Steam-Profil konnte nicht aktualisiert werden: ' . $e->getMessage());
        }

        header('Location: /profile');
    }

    public function avatar(string $id): void
    {
        Auth::requireLogin();

        $uid = (int)$id;
        $stmt = Database::connection()->prepare('SELECT steam_id64,steam_avatar_url FROM users WHERE id=? AND active=1');
        $stmt->execute([$uid]);
        $row = $stmt->fetch();

        if (!$row || !preg_match('/^7656119\d{10}$/', (string)($row['steam_id64'] ?? ''))) {
            http_response_code(404);
            return;
        }

        $steamId = (string)$row['steam_id64'];
        $path = SteamService::avatarPath($steamId)
            ?: SteamService::ensureAvatarCached($steamId, (string)($row['steam_avatar_url'] ?? ''));

        if (!$path || !is_file($path)) {
            http_response_code(404);
            return;
        }

        header('Content-Type: ' . SteamService::avatarMimeType($path));
        header('Cache-Control: private, max-age=3600');
        header('X-Content-Type-Options: nosniff');
        readfile($path);
    }

    private function profileForSteamId(string $steamId): array
    {
        $stmt = Database::connection()->prepare(
            'SELECT steam_personaname,steam_profile_url,steam_avatar_full_url,steam_avatar_medium_url FROM game_players WHERE steam_id64=? LIMIT 1'
        );
        $stmt->execute([$steamId]);
        $existing = $stmt->fetch();

        if (SteamService::hasApiKey()) {
            try {
                return SteamService::fetchUserProfile($steamId);
            } catch (\Throwable) {
                // Vorhandene lokale Steamdaten dürfen als Fallback dienen.
            }
        }

        $existingName = trim((string)($existing['steam_personaname'] ?? ''));
        if ($existing && $existingName !== '' && !$this->isLegacySyntheticSteamName(
            $steamId,
            $existingName,
            $existing['steam_profile_url'] ?? null,
            $existing['steam_avatar_full_url'] ?? null,
            $existing['steam_avatar_medium_url'] ?? null
        )) {
            return [
                'steamid' => $steamId,
                'personaname' => $existingName,
                'profileurl' => $existing['steam_profile_url'] ?? null,
                'avatarfull' => $existing['steam_avatar_full_url'] ?? null,
                'avatarmedium' => $existing['steam_avatar_medium_url'] ?? null,
            ];
        }

        // OpenID bestätigt die SteamID64, liefert aber keinen Anzeigenamen.
        // Ohne API- oder lokalen Profildatensatz darf deshalb kein künstlicher
        // Besitzername als vermeintlicher Steamname veröffentlicht werden.
        return ['steamid'=>$steamId, 'personaname'=>''];
    }

    private function isLegacySyntheticSteamName(string $steamId, string $name, mixed ...$profileEvidence): bool
    {
        foreach ($profileEvidence as $value) if (trim((string)$value) !== '') return false;
        return preg_match('/^7656119\d{10}$/', $steamId) === 1
            && hash_equals('Steam ' . substr($steamId, -6), trim($name));
    }

    private function httpsUrl(mixed $value): ?string
    {
        $url = trim((string)$value);
        return $url !== '' && str_starts_with($url, 'https://') && filter_var($url, FILTER_VALIDATE_URL)
            ? $url
            : null;
    }
}
