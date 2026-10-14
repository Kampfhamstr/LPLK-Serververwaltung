<?php
namespace App\Services;

use App\Core\Crypto;
use App\Core\Database;

final class SteamService
{
    public static function hasApiKey(): bool
    {
        $stmt = Database::connection()->prepare("SELECT setting_value FROM app_settings WHERE setting_key='steam.api_key'");
        $stmt->execute();
        return trim((string)($stmt->fetchColumn() ?: '')) !== '';
    }

    public static function syncPlayers(array $playerIds): array
    {
        $playerIds = array_values(array_unique(array_map('intval', $playerIds)));
        if (!$playerIds) return ['updated'=>0,'missing'=>0,'errors'=>[]];
        $pdo = Database::connection();
        $ph = implode(',', array_fill(0, count($playerIds), '?'));
        $stmt = $pdo->prepare("SELECT id,steam_id64,steam_personaname FROM game_players WHERE id IN ($ph)");
        $stmt->execute($playerIds);
        $rows = $stmt->fetchAll();
        return self::syncRows($rows);
    }

    public static function syncAll(): array
    {
        $rows = Database::connection()->query('SELECT id,steam_id64,steam_personaname FROM game_players WHERE active=1 ORDER BY id')->fetchAll();
        return self::syncRows($rows);
    }

    public static function fetchUserProfile(string $steamId64): array
    {
        $steamId64 = trim($steamId64);
        if (!preg_match('/^7656119\d{10}$/', $steamId64)) {
            throw new \InvalidArgumentException('Ungültige SteamID64.');
        }

        $profiles = self::fetchProfiles(self::apiKey(), [$steamId64]);
        foreach ($profiles as $profile) {
            if ((string)($profile['steamid'] ?? '') === $steamId64) return $profile;
        }

        throw new \RuntimeException('Steam Web API hat für diese SteamID kein Profil geliefert.');
    }

    public static function testKey(): array
    {
        $key = self::apiKey();
        $ch = curl_init('https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?steamids=76561197960435530');
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_CONNECTTIMEOUT => 8,
            CURLOPT_TIMEOUT => 15,
            CURLOPT_HTTPHEADER => ['Accept: application/json', 'x-webapi-key: ' . $key],
            CURLOPT_USERAGENT => 'Serververwaltung/1.15.1',
        ]);
        $body = curl_exec($ch);
        if ($body === false) throw new \RuntimeException('Steam-Verbindung fehlgeschlagen: ' . curl_error($ch));
        $status = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);
        if ($status !== 200) throw new \RuntimeException('Steam Web API antwortete mit HTTP ' . $status . '.');
        $data = json_decode((string)$body, true);
        if (!is_array($data) || !isset($data['response']['players'])) throw new \RuntimeException('Steam Web API lieferte eine unerwartete Antwort.');
        return ['ok'=>true,'players'=>count((array)$data['response']['players'])];
    }

    public static function avatarPath(string $steamId): ?string
    {
        $base = dirname(__DIR__, 2) . '/storage/steam-avatars/' . preg_replace('/\D+/', '', $steamId);
        foreach ([$base . '.img', $base . '.jpg'] as $path) {
            if (is_file($path) && filesize($path) > 0) return $path;
        }
        return null;
    }

    public static function ensureAvatarCached(string $steamId, ?string $url, bool $refresh = false): ?string
    {
        $existing = self::avatarPath($steamId);
        if ($existing && !$refresh) return $existing;

        $url = self::httpsUrl($url);
        if (!$url) return $existing;

        self::cacheAvatar($steamId, $url);
        return self::avatarPath($steamId) ?: $existing;
    }

    public static function avatarMimeType(string $path): string
    {
        $type = '';
        if (function_exists('finfo_open')) {
            $finfo = @finfo_open(FILEINFO_MIME_TYPE);
            if ($finfo) {
                $type = strtolower((string)@finfo_file($finfo, $path));
                @finfo_close($finfo);
            }
        }
        if ($type === '' && function_exists('getimagesize')) {
            $info = @getimagesize($path);
            $type = strtolower((string)($info['mime'] ?? ''));
        }
        return in_array($type, ['image/jpeg', 'image/png', 'image/webp'], true)
            ? $type
            : 'application/octet-stream';
    }

    private static function syncRows(array $rows): array
    {
        @set_time_limit(180);
        if (!$rows) return ['updated'=>0,'missing'=>0,'errors'=>[]];
        $key = self::apiKey();
        $pdo = Database::connection();
        $bySteam = [];
        foreach ($rows as $row) $bySteam[(string)$row['steam_id64']] = $row;
        $updated = 0; $missing = 0; $errors = [];

        foreach (array_chunk(array_keys($bySteam), 100) as $steamIds) {
            try {
                $profiles = self::fetchProfiles($key, $steamIds);
                $returned = [];
                foreach ($profiles as $profile) {
                    $steamId = (string)($profile['steamid'] ?? '');
                    if (!isset($bySteam[$steamId])) continue;
                    $returned[$steamId] = true;
                    $row = $bySteam[$steamId];
                    self::storeProfile((int)$row['id'], $row, $profile);
                    $updated++;
                }
                foreach ($steamIds as $steamId) {
                    if (isset($returned[$steamId])) continue;
                    $missing++;
                    $pdo->prepare('UPDATE game_players SET steam_sync_error=?,steam_last_synced_at=?,updated_at=? WHERE steam_id64=?')
                        ->execute(['Kein Steam-Profil von der API zurückgegeben.', date('c'), date('c'), $steamId]);
                }
            } catch (\Throwable $e) {
                $errors[] = $e->getMessage();
                foreach ($steamIds as $steamId) {
                    $pdo->prepare('UPDATE game_players SET steam_sync_error=?,updated_at=? WHERE steam_id64=?')
                        ->execute([$e->getMessage(), date('c'), $steamId]);
                }
            }
        }
        return ['updated'=>$updated,'missing'=>$missing,'errors'=>$errors];
    }

    private static function fetchProfiles(string $key, array $steamIds): array
    {
        $url = 'https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?steamids=' . rawurlencode(implode(',', $steamIds));
        $ch = curl_init($url);
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_CONNECTTIMEOUT => 8,
            CURLOPT_TIMEOUT => 20,
            CURLOPT_HTTPHEADER => ['Accept: application/json', 'x-webapi-key: ' . $key],
            CURLOPT_USERAGENT => 'Serververwaltung/1.15.1',
        ]);
        $body = curl_exec($ch);
        if ($body === false) throw new \RuntimeException('Steam-Verbindung fehlgeschlagen: ' . curl_error($ch));
        $status = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);
        if ($status !== 200) throw new \RuntimeException('Steam Web API antwortete mit HTTP ' . $status . '.');
        $data = json_decode((string)$body, true, 512, JSON_THROW_ON_ERROR);
        return is_array($data['response']['players'] ?? null) ? $data['response']['players'] : [];
    }

    private static function storeProfile(int $playerId, array $old, array $profile): void
    {
        $pdo = Database::connection();
        $name = trim((string)($profile['personaname'] ?? ''));
        if ($name !== '') {
            $history = $pdo->prepare('INSERT INTO steam_name_history(player_id,steam_name,first_seen_at,last_seen_at) VALUES(?,?,?,?) ON CONFLICT(player_id,steam_name) DO UPDATE SET last_seen_at=excluded.last_seen_at');
            $history->execute([$playerId, $name, date('c'), date('c')]);
        }
        $pdo->prepare('UPDATE game_players SET display_name=?,steam_personaname=?,steam_profile_url=?,steam_avatar_url=?,steam_avatar_medium_url=?,steam_avatar_full_url=?,steam_visibility=?,steam_personastate=?,steam_lastlogoff=?,steam_last_synced_at=?,steam_sync_error=NULL,updated_at=? WHERE id=?')
            ->execute([
                $name !== '' ? $name : (string)($old['steam_personaname'] ?: ('Steam ' . substr((string)$old['steam_id64'], -6))),
                $name !== '' ? $name : null,
                self::httpsUrl($profile['profileurl'] ?? null),
                self::httpsUrl($profile['avatar'] ?? null),
                self::httpsUrl($profile['avatarmedium'] ?? null),
                self::httpsUrl($profile['avatarfull'] ?? null),
                isset($profile['communityvisibilitystate']) ? (int)$profile['communityvisibilitystate'] : null,
                isset($profile['personastate']) ? (int)$profile['personastate'] : null,
                isset($profile['lastlogoff']) ? (int)$profile['lastlogoff'] : null,
                date('c'), date('c'), $playerId,
            ]);
        $avatar = self::httpsUrl($profile['avatarfull'] ?? ($profile['avatarmedium'] ?? null));
        if ($avatar) self::cacheAvatar((string)$old['steam_id64'], $avatar);
    }

    private static function cacheAvatar(string $steamId, string $url): void
    {
        if (!str_starts_with($url, 'https://')) return;

        $host = strtolower((string)parse_url($url, PHP_URL_HOST));
        if ($host === '' || !(
            $host === 'avatars.steamstatic.com'
            || $host === 'avatars.akamai.steamstatic.com'
            || str_ends_with($host, '.steamstatic.com')
        )) return;

        $ch = curl_init($url);
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => true,
            CURLOPT_MAXREDIRS => 3,
            CURLOPT_CONNECTTIMEOUT => 6,
            CURLOPT_TIMEOUT => 12,
            CURLOPT_HTTPHEADER => ['Accept: image/jpeg,image/png,image/webp;q=0.9,*/*;q=0.1'],
            CURLOPT_USERAGENT => 'Serververwaltung/1.15.1',
        ]);
        $body = curl_exec($ch);
        if ($body === false) { curl_close($ch); return; }
        $status = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);

        if ($status !== 200 || $body === '' || strlen((string)$body) > 2_000_000) return;

        $imageInfo = @getimagesizefromstring((string)$body);
        $mime = strtolower((string)($imageInfo['mime'] ?? ''));
        if (!in_array($mime, ['image/jpeg', 'image/png', 'image/webp'], true)) return;

        $dir = dirname(__DIR__, 2) . '/storage/steam-avatars';
        if (!is_dir($dir) && !@mkdir($dir, 0770, true) && !is_dir($dir)) return;

        $path = $dir . '/' . preg_replace('/\D+/', '', $steamId) . '.img';
        $written = @file_put_contents($path, $body, LOCK_EX);
        if ($written === false) return;
        @chmod($path, 0660);
    }

    private static function apiKey(): string
    {
        $stmt = Database::connection()->prepare("SELECT setting_value,encrypted FROM app_settings WHERE setting_key='steam.api_key'");
        $stmt->execute();
        $row = $stmt->fetch();
        if (!$row || trim((string)$row['setting_value']) === '') throw new \RuntimeException('Kein Steam Web API Key hinterlegt. Bitte unter Einstellungen → Steam eintragen.');
        $value = (string)$row['setting_value'];
        return (int)$row['encrypted'] === 1 ? Crypto::decrypt($value) : $value;
    }

    private static function httpsUrl(mixed $value): ?string
    {
        $url = trim((string)$value);
        if ($url === '' || !str_starts_with($url, 'https://')) return null;
        return filter_var($url, FILTER_VALIDATE_URL) ? $url : null;
    }
}
