<?php
namespace App\Controllers;

use App\Core\Audit;
use App\Core\Auth;
use App\Core\Database;
use App\Core\Security;
use App\Core\View;
use App\Services\LiveServerService;
use App\Services\PterodactylLiveService;
use App\Services\ServerChatService;

final class DashboardController
{
    public function index(): void
    {
        Auth::requirePermission('dashboard.view');

        $pdo = Database::connection();
        $allServers = $pdo->query('SELECT * FROM game_servers ORDER BY name')->fetchAll();
        $servers = array_values(array_filter(
            $allServers,
            fn($server) => Auth::canServer((int)$server['id'], 'view')
        ));

        $stmt = $pdo->prepare(
            'SELECT COUNT(*) FROM invitations WHERE accepted_at IS NULL AND revoked_at IS NULL AND expires_at > ?'
        );
        $stmt->execute([date('c')]);

        $stats = [
            'users' => Auth::can('users.view') ? (int)$pdo->query('SELECT COUNT(*) FROM users WHERE active=1')->fetchColumn() : null,
            'players' => (int)$pdo->query('SELECT COUNT(*) FROM game_players WHERE active=1 AND main_player_id IS NULL')->fetchColumn(),
            'secondary_accounts' => (int)$pdo->query('SELECT COUNT(*) FROM game_players WHERE active=1 AND main_player_id IS NOT NULL')->fetchColumn(),
            'servers' => Auth::can('servers.view') ? count($servers) : null,
            'pending' => Auth::can('users.invite') ? (int)$stmt->fetchColumn() : null,
        ];

        $liveServers = [];
        foreach ($servers as $server) {
            $liveServers[] = $this->buildServerSummary($server);
        }

        View::render('dashboard', [
            'title' => 'Meine Übersicht',
            'stats' => $stats,
            'servers' => $servers,
            'liveServers' => $liveServers,
            'onlinePanelUsers' => $this->onlinePanelUsers(),
            'announcements' => $this->activeAnnouncements(),
            'unreadAnnouncements' => Auth::unreadAnnouncementCount(),
            'chatMessages' => \App\Services\CommunityService::latestChatMessages(30),
            'latestForumThreads' => \App\Services\CommunityService::latestThreads(6),
            'pluginUpdateSummary' => $this->pluginUpdateSummaryFromLive($liveServers),
        ]);
    }

    public function liveStatus(): void
    {
        Auth::requirePermission('dashboard.view');

        $pdo = Database::connection();
        $allServers = $pdo->query('SELECT * FROM game_servers ORDER BY name')->fetchAll();
        $servers = array_values(array_filter(
            $allServers,
            fn($server) => Auth::canServer((int)$server['id'], 'view')
        ));

        $result = [];
        foreach ($servers as $server) {
            $result[] = $this->buildServerSummary($server);
        }

        header('Content-Type: application/json; charset=UTF-8');
        header('Cache-Control: no-store, no-cache, must-revalidate');
        echo json_encode(
            [
                'servers' => $result,
                'panel_users' => $this->onlinePanelUsers(),
                'unread_announcements' => Auth::unreadAnnouncementCount(),
                'generated_at' => gmdate('c')
            ],
            JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR
        );
    }

    /**
     * v1.9.6: Die drei bewusst kompakten Power-Tasten auf einer Dashboardkarte.
     * Sie sind absichtlich nur für die Webrollen Founder und Admin vorgesehen;
     * die granularen Rechte und die Serverfreigabe werden trotzdem zusätzlich
     * für jeden einzelnen POST geprüft.
     */
    public function dashboardPower(string $id): void
    {
        Auth::requireLogin();
        Security::verifyCsrf();

        $serverId = (int)$id;
        $operation = strtolower(trim((string)($_POST['operation'] ?? '')));
        $allowedOperations = ['start','restart','stop'];

        if ($serverId <= 0 || !in_array($operation, $allowedOperations, true)) {
            View::flash('error', 'Ungültige Serveraktion.');
            header('Location: /dashboard');
            return;
        }

        if (!Auth::can('dashboard.view') || !self::dashboardPowerRoleAllowed() || !Auth::canServer($serverId, 'live')) {
            http_response_code(403);
            View::render('error', [
                'title' => 'Zugriff verweigert',
                'message' => 'Die Power-Schnelltasten im Dashboard sind ausschließlich für Founder und Admins mit Live-Zugriff verfügbar.',
            ]);
            return;
        }

        $permission = 'server.power.' . $operation;
        if (!Auth::can($permission)) {
            http_response_code(403);
            View::render('error', [
                'title' => 'Zugriff verweigert',
                'message' => 'Für diese Serveraktion fehlt die erforderliche Berechtigung.',
            ]);
            return;
        }

        if (!PterodactylLiveService::available($serverId)) {
            View::flash('error', 'Diese Schnelltasten sind nur für Server mit Pterodactyl-Connector verfügbar.');
            header('Location: /dashboard');
            return;
        }

        try {
            PterodactylLiveService::power($serverId, $operation);
            Audit::log('server.power.' . $operation, 'server', (string)$serverId, [
                'connector' => 'pterodactyl',
                'source' => 'dashboard-serverkarte',
            ]);
            $label = match ($operation) {
                'start' => 'Start',
                'restart' => 'Neustart',
                'stop' => 'Stop',
            };
            View::flash('success', $label . ' wurde an Pterodactyl übermittelt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Pterodactyl-Poweraktion fehlgeschlagen: ' . $e->getMessage());
        }

        header('Location: /dashboard');
    }

    public function createAnnouncement(): void
    {
        Auth::requirePermission('dashboard.announce');
        \App\Core\Security::verifyCsrf();

        $title = trim((string)($_POST['title'] ?? ''));
        $message = trim((string)($_POST['message'] ?? ''));
        $level = strtolower((string)($_POST['level'] ?? 'info'));
        $duration = (int)($_POST['duration_minutes'] ?? 1440);

        if (!in_array($level, ['info','warning','critical'], true)) $level = 'info';
        if (!in_array($duration, [60,360,1440,10080,0], true)) $duration = 1440;

        $title = function_exists('mb_substr') ? mb_substr($title,0,100) : substr($title,0,100);
        $message = function_exists('mb_substr') ? mb_substr($message,0,1200) : substr($message,0,1200);

        if ($title === '' || $message === '') {
            View::flash('error', 'Titel und Mitteilung dürfen nicht leer sein.');
            header('Location: /dashboard#announcements');
            return;
        }

        $expires = $duration > 0 ? date('c', time() + $duration * 60) : null;
        $pdo = Database::connection();
        $pdo->prepare('INSERT INTO webpanel_announcements(title,message,level,created_by,expires_at,created_at) VALUES(?,?,?,?,?,?)')
            ->execute([$title,$message,$level,(int)Auth::user()['id'],$expires,date('c')]);

        \App\Core\Audit::log('dashboard.announcement.created','announcement',(string)$pdo->lastInsertId(),[
            'level'=>$level,'expires_at'=>$expires
        ]);
        View::flash('success', 'Webpanel-Mitteilung wurde veröffentlicht.');
        header('Location: /dashboard#announcements');
    }

    public function markAnnouncementRead(string $id): void
    {
        Auth::requirePermission('dashboard.view');
        \App\Core\Security::verifyCsrf();

        $announcementId = (int)$id;
        Database::connection()->prepare(
            'INSERT INTO webpanel_announcement_reads(announcement_id,user_id,read_at) VALUES(?,?,?) ' .
            'ON CONFLICT(announcement_id,user_id) DO UPDATE SET read_at=excluded.read_at'
        )->execute([$announcementId,(int)Auth::user()['id'],date('c')]);

        header('Location: /dashboard#announcements');
    }

    public function revokeAnnouncement(string $id): void
    {
        Auth::requirePermission('dashboard.announce');
        \App\Core\Security::verifyCsrf();

        $announcementId = (int)$id;
        Database::connection()->prepare('UPDATE webpanel_announcements SET revoked_at=? WHERE id=?')
            ->execute([date('c'),$announcementId]);
        \App\Core\Audit::log('dashboard.announcement.revoked','announcement',(string)$announcementId);
        View::flash('success','Mitteilung wurde beendet.');
        header('Location: /dashboard#announcements');
    }

    private function onlinePanelUsers(): array
    {
        $stmt = Database::connection()->prepare(
            'SELECT id,name,web_role,steam_id64,steam_personaname,steam_verified_at,last_seen_at ' .
            'FROM users WHERE active=1 AND last_seen_at IS NOT NULL AND last_seen_at>=? ORDER BY last_seen_at DESC,name'
        );
        $stmt->execute([date('c', time() - 180)]);

        $out = [];
        foreach ($stmt->fetchAll() as $row) {
            $out[] = [
                'id' => (int)$row['id'],
                'display_name' => Auth::displayName($row),
                'web_role' => Auth::normalizeWebRole((string)$row['web_role']),
                'web_role_label' => Auth::webRoleLabel((string)$row['web_role']),
                'steam_verified' => !empty($row['steam_verified_at']) && !empty($row['steam_id64']),
                'last_seen_at' => $row['last_seen_at'],
            ];
        }
        return $out;
    }

    private function activeAnnouncements(): array
    {
        $stmt = Database::connection()->prepare(
            "SELECT a.*,u.name AS creator_name,u.steam_personaname AS creator_steam_name,u.steam_verified_at AS creator_verified,
                    CASE WHEN r.announcement_id IS NULL THEN 0 ELSE 1 END AS is_read
             FROM webpanel_announcements a
             JOIN users u ON u.id=a.created_by
             LEFT JOIN webpanel_announcement_reads r
               ON r.announcement_id=a.id AND r.user_id=?
             WHERE a.revoked_at IS NULL
               AND (a.expires_at IS NULL OR a.expires_at>?)
             ORDER BY a.created_at DESC
             LIMIT 25"
        );
        $stmt->execute([(int)Auth::user()['id'], date('c')]);

        $rows = $stmt->fetchAll();
        foreach ($rows as &$row) {
            $row['creator_display_name'] = !empty($row['creator_verified']) && trim((string)$row['creator_steam_name']) !== ''
                ? (string)$row['creator_steam_name']
                : (string)$row['creator_name'];
        }
        unset($row);
        return $rows;
    }

    private function pluginUpdateSummaryFromLive(array $liveServers): array
    {
        if (!Auth::can('plugins.view')) return ['visible'=>false,'updates'=>0,'servers'=>0];
        $updates=0;$servers=0;
        foreach($liveServers as $server){
            if(empty($server['bridge_online']))continue;
            $servers++;
            $updates+=(int)($server['plugin_updates']??0);
        }
        return ['visible'=>true,'updates'=>$updates,'servers'=>$servers];
    }

    private static function frameworkLabel(string $framework): string
    {
        return match (strtolower(trim($framework))) {
            'swiftlys2' => 'SwiftlyS2',
            'counterstrikesharp' => 'CounterStrikeSharp',
            'both' => 'SwiftlyS2 + CounterStrikeSharp',
            default => $framework !== '' ? $framework : 'Nicht gescannt',
        };
    }

    public function serverLiveStatus(string $id): void
    {
        Auth::requirePermission('dashboard.view');
        Auth::requirePermission('live.view');
        Auth::requireServer((int)$id, 'view');
        Auth::requireServer((int)$id, 'live');
        $server = LiveServerService::server((int)$id);
        session_write_close();
        header('Content-Type: application/json; charset=UTF-8');
        header('Cache-Control: no-store');
        echo json_encode($this->buildServerSummary($server, false), JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
    }

    private function buildServerSummary(array $server, bool $quick = true): array
    {
        $serverId = (int)$server['id'];

        $base = [
            'id' => $serverId,
            'name' => (string)$server['name'],
            'framework' => self::frameworkLabel((string)($server['framework'] ?? '')),
            'last_scan_at' => $server['last_scan_at'] ?: null,
            'last_sync_at' => $server['last_sync_at'] ?: null,
            'last_error' => $server['last_error'] ?: null,
            'can_live' => Auth::can('live.view') && Auth::canServer($serverId, 'live'),
            'dashboard_power' => $this->dashboardPowerCapabilities($serverId),
            'bridge_online' => false,
            'bridge_available' => false,
            'requires_live_refresh' => !\App\Services\FastLiveService::enabled($serverId),
            'map' => null,
            'next_map' => null,
            'map_switch_in_flight' => false,
            'max_players' => null,
            'human_players' => 0,
            'bots' => 0,
            'ct_count' => 0,
            't_count' => 0,
            'spectator_count' => 0,
            'unassigned_count' => 0,
            'teams' => [
                'CT' => [],
                'T' => [],
                'Spectator' => [],
                'Unassigned' => [],
            ],
            'updated_at' => null,
            'status_age_seconds' => null,
            'reason' => null,
            'plugin_updates' => 0,
            'recent_chat' => ServerChatService::latest($serverId, 5),
            'chat_total' => ServerChatService::count($serverId),
        ];

        if (!$base['can_live']) {
            $base['reason'] = Auth::can('live.view')
                ? 'Für diesen Server wurde kein Live-Zugriff freigegeben.'
                : 'Für Live-Daten fehlt die Webpanel-Berechtigung.';
            return $base;
        }

        try {
            $status = $quick ? LiveServerService::statusQuick($serverId) : LiveServerService::status($serverId);
            $base['bridge_online'] = !empty($status['bridge_online']);
            $base['bridge_available'] = !empty($status['bridge_available']);
            if (!empty($status['framework_detected'])) {
                $base['framework'] = self::frameworkLabel((string)$status['framework_detected']);
            }
            $base['map'] = trim((string)($status['map'] ?? '')) ?: null;
            $base['next_map'] = trim((string)($status['map_switch_target'] ?? '')) ?: null;
            $base['map_switch_in_flight'] = !empty($status['map_switch_in_flight']);
            $base['max_players'] = isset($status['max_players']) ? (int)$status['max_players'] : null;
            $base['updated_at'] = $status['updated_at'] ?? null;
            $base['status_age_seconds'] = isset($status['status_age_seconds'])
                ? (int)$status['status_age_seconds']
                : null;
            $base['reason'] = $status['reason'] ?? null;
            if (Auth::can('plugins.view') && is_array($status['plugins'] ?? null)) {
                $sources=\App\Services\PluginUpdateService::sourcesByPluginId();
                foreach($status['plugins'] as $plugin){
                    if(!is_array($plugin))continue;
                    $pid=(string)($plugin['id']??'');
                    if(isset($sources[$pid]) && \App\Services\GitHubReleaseService::isNewer((string)($plugin['version']??''),(string)($sources[$pid]['latest_version']??''))) $base['plugin_updates']++;
                }
            }

            $players = is_array($status['players'] ?? null) ? $status['players'] : [];

            foreach ($players as $player) {
                if (!is_array($player)) continue;

                if (!empty($player['is_bot'])) {
                    $base['bots']++;
                    continue;
                }

                $team = (string)($player['team'] ?? 'Unassigned');
                if (!array_key_exists($team, $base['teams'])) {
                    $team = 'Unassigned';
                }

                $entry = [
                    'steam_id64' => (string)($player['steam_id64'] ?? ''),
                    'name' => (string)($player['name'] ?? 'Unbekannt'),
                    'community_role' => (string)($player['community_role'] ?? ''),
                    'alive' => !empty($player['alive']),
                    'health' => isset($player['health']) ? (int)$player['health'] : null,
                    'armor' => isset($player['armor']) ? (int)$player['armor'] : null,
                    'connected_seconds' => isset($player['connected_seconds']) ? (int)$player['connected_seconds'] : 0,
                    'connected_human' => (string)($player['connected_human'] ?? ''),
                    'frozen' => !empty($player['frozen']),
                    'nickname_override' => $player['nickname_override'] ?? null,
                    'person_id' => isset($player['person_id']) ? (int)$player['person_id'] : null,
                ];

                $base['teams'][$team][] = $entry;
                $base['human_players']++;

                match ($team) {
                    'CT' => $base['ct_count']++,
                    'T' => $base['t_count']++,
                    'Spectator' => $base['spectator_count']++,
                    default => $base['unassigned_count']++,
                };
            }

            foreach ($base['teams'] as &$teamPlayers) {
                usort($teamPlayers, static function (array $a, array $b): int {
                    $time = ($b['connected_seconds'] ?? 0) <=> ($a['connected_seconds'] ?? 0);
                    if ($time !== 0) return $time;
                    return strnatcasecmp((string)$a['name'], (string)$b['name']);
                });
            }
            unset($teamPlayers);
        } catch (\Throwable $e) {
            $base['bridge_online'] = false;
            $base['reason'] = 'Live-Status konnte nicht geladen werden: ' . $e->getMessage();
        }

        return $base;
    }

    private static function dashboardPowerRoleAllowed(): bool
    {
        return in_array(Auth::effectiveWebRole(), ['founder', 'admin'], true);
    }

    private function dashboardPowerCapabilities(int $serverId): array
    {
        $visible = self::dashboardPowerRoleAllowed()
            && Auth::canServer($serverId, 'live')
            && PterodactylLiveService::available($serverId);

        return [
            'visible' => $visible,
            'start' => $visible && Auth::can('server.power.start'),
            'restart' => $visible && Auth::can('server.power.restart'),
            'stop' => $visible && Auth::can('server.power.stop'),
        ];
    }
}
