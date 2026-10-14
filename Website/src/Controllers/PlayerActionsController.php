<?php
namespace App\Controllers;

use App\Core\Audit;
use App\Core\Auth;
use App\Core\Database;
use App\Core\Security;
use App\Core\View;
use App\Services\GlobalBanService;
use App\Services\LiveServerService;
use App\Services\ModerationService;
use App\Services\WhitelistLiveService;

final class PlayerActionsController
{
    private const ACTION_PERMISSIONS = [
        'kick' => 'live.kick',
        'mute' => 'live.mute',
        'unmute' => 'live.mute',
        'gag' => 'live.gag',
        'ungag' => 'live.gag',
        'private_message' => 'live.message',
        'whitelist_add' => 'live.whitelist',
    ];

    public function context(): void
    {
        Auth::requireLogin();
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');

        $steamId64 = trim((string)($_GET['steamid'] ?? ''));
        if (!$this->validSteamId($steamId64)) {
            http_response_code(400);
            echo json_encode(['ok'=>false,'error'=>'Ungültige SteamID64.'], JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
            return;
        }

        $preferredServer = max(0, (int)($_GET['server_id'] ?? 0));
        $servers = [];
        $stmt = Database::connection()->query('SELECT id,name FROM game_servers ORDER BY name COLLATE NOCASE,id');
        foreach ($stmt->fetchAll() as $row) {
            $serverId = (int)$row['id'];
            if (!Auth::canServer($serverId, 'live')) continue;
            try {
                $status = LiveServerService::statusQuick($serverId);
                if (empty($status['bridge_online'])) continue;
                foreach ((array)($status['players'] ?? []) as $player) {
                    if ((string)($player['steam_id64'] ?? '') !== $steamId64) continue;
                    $servers[] = [
                        'id' => $serverId,
                        'name' => (string)$row['name'],
                        'preferred' => $serverId === $preferredServer,
                        'player_name' => (string)($player['name'] ?? ''),
                        'team' => (string)($player['team'] ?? 'Unassigned'),
                        'voice_muted' => !empty($player['voice_muted']),
                        'gagged' => !empty($player['gagged']),
                    ];
                    break;
                }
            } catch (\Throwable) {
                // Eine einzelne nicht erreichbare Bridge darf die globale Spieleraktion nicht blockieren.
            }
        }

        // Whitelists sind absichtlich serverbezogen. Ein Spieler kann also
        // auf Atlas freigeschaltet sein und auf Horizon nicht. Für die universelle
        // Spieleraktionskachel liefern wir deshalb die einzeln wählbaren Server
        // unabhängig davon, ob der Spieler dort gerade online ist.
        $whitelistServers = [];
        if (Auth::can('live.whitelist')) {
            $ws = Database::connection()->query('SELECT id,name FROM game_servers ORDER BY name COLLATE NOCASE,id');
            foreach ($ws->fetchAll() as $row) {
                $serverId = (int)$row['id'];
                if (!Auth::canServer($serverId, 'live')) continue;
                $whitelistServers[] = [
                    'id'=>$serverId,
                    'name'=>(string)$row['name'],
                    'preferred'=>$serverId === $preferredServer,
                ];
            }
        }

        $can = [];
        foreach (self::ACTION_PERMISSIONS as $action => $permission) $can[$action] = Auth::can($permission);
        $can['ban'] = Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent']);
        $can['ban_permanent'] = Auth::can('moderation.ban.permanent');
        $can['unban'] = Auth::can('moderation.unban');

        echo json_encode([
            'ok' => true,
            'steam_id64' => $steamId64,
            'servers' => $servers,
            'whitelist_servers' => $whitelistServers,
            'active_ban' => GlobalBanService::activeForSteam($steamId64),
            'moderation_status' => ModerationService::statusMap([$steamId64])[$steamId64] ?? null,
            'can' => $can,
        ], JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
    }

    public function live(): void
    {
        Auth::requireLogin();
        Security::verifyCsrf();

        $steamId64 = trim((string)($_POST['steam_id64'] ?? ''));
        $serverId = max(0, (int)($_POST['server_id'] ?? 0));
        $action = trim((string)($_POST['action'] ?? ''));
        $return = $this->safeReturn((string)($_POST['_return'] ?? '/dashboard'));

        if (!$this->validSteamId($steamId64) || $serverId <= 0 || !isset(self::ACTION_PERMISSIONS[$action])) {
            View::flash('error', 'Ungültige Spieleraktion.');
            header('Location: ' . $return);
            return;
        }

        Auth::requirePermission(self::ACTION_PERMISSIONS[$action]);
        Auth::requireServer($serverId, 'live');

        try {
            if ($action === 'whitelist_add') {
                $result = WhitelistLiveService::addOne($serverId, $steamId64);
                Audit::log('live.whitelist_add', 'server_player', $steamId64, [
                    'server_id'=>$serverId,
                    'source'=>'global_player_actions',
                    'added'=>$result['added'] ?? [],
                    'command_ids'=>$result['commands'] ?? [],
                ]);
                $message = !empty($result['added'])
                    ? 'Spieler wurde für genau diesen Server zur Whitelist hinzugefügt.'
                    : 'Spieler ist auf der Whitelist dieses Servers bereits vorhanden oder die Änderung wurde bereits vorgemerkt.';
                View::flash('success', $message);
                header('Location: ' . $return);
                return;
            }

            $player = LiveServerService::player($serverId, $steamId64);
            if (!$player) throw new \RuntimeException('Der Spieler ist auf dem ausgewählten Server nicht mehr online.');

            $payload = [];
            switch ($action) {
                case 'kick':
                    $payload['reason'] = $this->cleanText((string)($_POST['reason'] ?? 'Von der Serververwaltung entfernt.'), 160);
                    if ($payload['reason'] === '') $payload['reason'] = 'Von der Serververwaltung entfernt.';
                    break;

                case 'mute':
                case 'gag':
                    $payload['minutes'] = $this->punishmentMinutes($_POST['minutes'] ?? 60);
                    $payload['reason'] = $this->cleanText((string)($_POST['reason'] ?? 'Kommunikation gesperrt'), 160);
                    if ($payload['reason'] === '') $payload['reason'] = 'Kommunikation gesperrt';
                    break;

                case 'private_message':
                    $type = trim((string)($_POST['type'] ?? 'chat'));
                    if (!in_array($type, ['chat','center','alert','notify'], true)) $type = 'chat';
                    $message = $this->cleanText((string)($_POST['message'] ?? ''), 500, true);
                    if ($message === '') throw new \RuntimeException('Die Nachricht darf nicht leer sein.');
                    $payload = ['type'=>$type,'message'=>$message];
                    break;
            }

            $commandId = LiveServerService::queue($serverId, $action, $payload, $steamId64);
            Audit::log('live.' . $action, 'server_player', $steamId64, [
                'server_id'=>$serverId,
                'source'=>'global_player_actions',
                'payload'=>$payload,
                'command_id'=>$commandId,
            ]);

            $labels = [
                'kick'=>'Kick', 'mute'=>'Mute', 'unmute'=>'Unmute', 'gag'=>'Gag', 'ungag'=>'Ungag',
                'private_message'=>'Persönliche Nachricht', 'whitelist_add'=>'Whitelist',
            ];
            View::flash('success', ($labels[$action] ?? 'Aktion') . ' wurde an den Gameserver übermittelt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Spieleraktion fehlgeschlagen: ' . $e->getMessage());
        }

        header('Location: ' . $return);
    }

    private function validSteamId(string $steamId): bool
    {
        return (bool)preg_match('/^7656119\d{10}$/', $steamId);
    }

    private function cleanText(string $value, int $maxLength, bool $allowNewlines = false): string
    {
        $value = trim($value);
        if (!$allowNewlines) $value = preg_replace('/[\r\n\t]+/', ' ', $value) ?? $value;
        $value = preg_replace('/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/u', '', $value) ?? $value;
        return function_exists('mb_substr') ? mb_substr($value, 0, $maxLength) : substr($value, 0, $maxLength);
    }

    private function punishmentMinutes(mixed $value): int
    {
        $minutes = (int)$value;
        return in_array($minutes, [10,30,60,360,1440,4320,10080,20160,43200,129600,0], true) ? $minutes : 60;
    }

    private function safeReturn(string $value): string
    {
        $value = trim($value);
        if ($value === '' || !str_starts_with($value, '/') || str_starts_with($value, '//')) return '/dashboard';
        if (preg_match('/[\r\n]/', $value)) return '/dashboard';
        return substr($value, 0, 1000);
    }
}
