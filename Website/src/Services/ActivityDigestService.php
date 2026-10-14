<?php
namespace App\Services;

use App\Core\Auth;
use App\Core\Database;

final class ActivityDigestService
{
    private const FORUM_THREAD_LIMIT = 10;
    private const ADMIN_BREAKDOWN_LIMIT = 12;
    private const ADMIN_LATEST_LIMIT = 10;
    private const MESSAGE_SENDER_LIMIT = 10;
    private const ANNOUNCEMENT_LIMIT = 8;
    private const CHAT_PREVIEW_LIMIT = 6;

    public static function pending(): bool
    {
        return !empty($_SESSION['_activity_digest_pending']);
    }

    public static function buildForCurrentLogin(): array
    {
        $user = Auth::user();
        if (!$user) throw new \RuntimeException('Nicht angemeldet.');

        $since = trim((string)($_SESSION['_activity_digest_since'] ?? ''));
        $firstLogin = !empty($_SESSION['_activity_digest_first_login']);

        if ($since === '' || strtotime($since) === false) {
            $since = date('c', time() - 7 * 86400);
            $firstLogin = true;
        }

        $now = date('c');
        $userId = (int)$user['id'];

        $forum = self::forumActivity($since);
        $messages = self::directMessages($userId, $since);
        $adminActions = self::adminActions($since);
        $announcements = self::announcements($since, $now);
        $chat = self::chatActivity($since);

        $total = (int)$forum['post_count']
            + (int)$messages['count']
            + (int)$adminActions['count']
            + (int)$announcements['count']
            + (int)$chat['count'];

        return [
            'pending'=>true,
            'first_login'=>$firstLogin,
            'since'=>$since,
            'until'=>$now,
            'since_label'=>$firstLogin
                ? 'Aktivität der letzten 7 Tage'
                : 'Seit Ihrem letzten Login am ' . self::formatGermanDate($since),
            'total_activity'=>$total,
            'forum'=>$forum,
            'admin_actions'=>$adminActions,
            'messages'=>$messages,
            'announcements'=>$announcements,
            'chat'=>$chat,
        ];
    }

    public static function markConsumed(): void
    {
        unset(
            $_SESSION['_activity_digest_pending'],
            $_SESSION['_activity_digest_since'],
            $_SESSION['_activity_digest_first_login']
        );
    }

    private static function forumActivity(string $since): array
    {
        $pdo = Database::connection();

        $stmt = $pdo->prepare(
            "SELECT COUNT(*)
             FROM forum_posts p
             JOIN forum_threads t ON t.id=p.thread_id
             WHERE p.deleted_at IS NULL
               AND t.deleted_at IS NULL
               AND p.created_at>?"
        );
        $stmt->execute([$since]);
        $postCount = (int)$stmt->fetchColumn();

        $stmt = $pdo->prepare(
            "SELECT COUNT(*)
             FROM forum_threads
             WHERE deleted_at IS NULL AND created_at>?"
        );
        $stmt->execute([$since]);
        $threadCount = (int)$stmt->fetchColumn();

        $stmt = $pdo->prepare(
            "SELECT t.id,t.title,t.category,
                    COUNT(p.id) AS new_posts,
                    MAX(p.created_at) AS latest_at,
                    MAX(p.id) AS latest_post_id
             FROM forum_posts p
             JOIN forum_threads t ON t.id=p.thread_id
             WHERE p.deleted_at IS NULL
               AND t.deleted_at IS NULL
               AND p.created_at>?
             GROUP BY t.id,t.title,t.category
             ORDER BY latest_at DESC,t.id DESC
             LIMIT " . self::FORUM_THREAD_LIMIT
        );
        $stmt->execute([$since]);

        $threads = [];
        foreach ($stmt->fetchAll() as $row) {
            $threads[] = [
                'id'=>(int)$row['id'],
                'title'=>(string)$row['title'],
                'category'=>(string)$row['category'],
                'new_posts'=>(int)$row['new_posts'],
                'latest_at'=>(string)$row['latest_at'],
                'url'=>'/forum/' . (int)$row['id'] . '#post-' . (int)$row['latest_post_id'],
            ];
        }

        return [
            'post_count'=>$postCount,
            'thread_count'=>$threadCount,
            'threads'=>$threads,
            'url'=>'/forum',
        ];
    }

    private static function directMessages(int $userId, string $since): array
    {
        $pdo = Database::connection();

        $stmt = $pdo->prepare(
            "SELECT COUNT(*)
             FROM webpanel_direct_messages
             WHERE recipient_id=? AND created_at>?"
        );
        $stmt->execute([$userId,$since]);
        $count = (int)$stmt->fetchColumn();

        $stmt = $pdo->prepare(
            "SELECT dm.sender_id,
                    COUNT(*) AS message_count,
                    SUM(CASE WHEN dm.read_at IS NULL THEN 1 ELSE 0 END) AS unread_count,
                    MAX(dm.created_at) AS latest_at,
                    u.name,u.web_role,u.steam_personaname,u.steam_verified_at
             FROM webpanel_direct_messages dm
             JOIN users u ON u.id=dm.sender_id
             WHERE dm.recipient_id=? AND dm.created_at>?
             GROUP BY dm.sender_id,u.name,u.web_role,u.steam_personaname,u.steam_verified_at
             ORDER BY latest_at DESC
             LIMIT " . self::MESSAGE_SENDER_LIMIT
        );
        $stmt->execute([$userId,$since]);

        $senders = [];
        foreach ($stmt->fetchAll() as $row) {
            $senders[] = [
                'user_id'=>(int)$row['sender_id'],
                'display_name'=>Auth::displayName([
                    'id'=>(int)$row['sender_id'],
                    'name'=>$row['name'],
                    'steam_personaname'=>$row['steam_personaname'],
                    'steam_verified_at'=>$row['steam_verified_at'],
                ]),
                'role'=>Auth::normalizeWebRole((string)$row['web_role']),
                'role_label'=>Auth::webRoleLabel((string)$row['web_role']),
                'count'=>(int)$row['message_count'],
                'unread'=>(int)$row['unread_count'],
                'latest_at'=>(string)$row['latest_at'],
            ];
        }

        return [
            'count'=>$count,
            'unread_count'=>array_sum(array_column($senders,'unread')),
            'senders'=>$senders,
        ];
    }

    private static function adminActions(string $since): array
    {
        if (!Auth::can('live.view')) {
            return ['count'=>0,'breakdown'=>[],'latest'=>[]];
        }

        $pdo = Database::connection();
        $stmt = $pdo->prepare(
            "SELECT a.id,a.user_id,a.action,a.target_type,a.target_id,a.details_json,a.created_at,
                    u.name,u.web_role,u.steam_personaname,u.steam_verified_at
             FROM audit_log a
             LEFT JOIN users u ON u.id=a.user_id
             WHERE a.created_at>?
               AND (a.action LIKE 'live.%' OR a.action LIKE 'server.power.%' OR a.action='server.console.execute')
               AND a.action NOT IN ('live.restart.execute','server.power.restart.execute')
             ORDER BY a.id DESC
             LIMIT 1000"
        );
        $stmt->execute([$since]);
        $rows = $stmt->fetchAll();

        $servers = [];
        foreach ($pdo->query('SELECT id,name FROM game_servers')->fetchAll() as $server) {
            $servers[(int)$server['id']] = (string)$server['name'];
        }

        $steamIds = [];
        $prepared = [];

        foreach ($rows as $row) {
            $details = [];
            if (trim((string)($row['details_json'] ?? '')) !== '') {
                try {
                    $decoded = json_decode((string)$row['details_json'], true, 512, JSON_THROW_ON_ERROR);
                    if (is_array($decoded)) $details = $decoded;
                } catch (\Throwable) {}
            }

            $serverId = 0;
            if ((string)$row['target_type'] === 'server' && ctype_digit((string)$row['target_id'])) {
                $serverId = (int)$row['target_id'];
            } elseif (isset($details['server_id'])) {
                $serverId = (int)$details['server_id'];
            }

            if ($serverId <= 0 || !isset($servers[$serverId]) || !Auth::canServer($serverId,'live')) {
                continue;
            }

            $targetSteam = null;
            if ((string)$row['target_type'] === 'server_player'
                && preg_match('/^7656119\d{10}$/',(string)$row['target_id'])) {
                $targetSteam = (string)$row['target_id'];
                $steamIds[$targetSteam] = true;
            }

            $prepared[] = [
                'id'=>(int)$row['id'],
                'action'=>(string)$row['action'],
                'created_at'=>(string)$row['created_at'],
                'server_id'=>$serverId,
                'server_name'=>$servers[$serverId],
                'target_steamid'=>$targetSteam,
                'actor_name'=>$row['user_id'] === null
                    ? 'System'
                    : Auth::displayName([
                        'id'=>(int)$row['user_id'],
                        'name'=>$row['name'],
                        'steam_personaname'=>$row['steam_personaname'],
                        'steam_verified_at'=>$row['steam_verified_at'],
                    ]),
                'actor_role'=>$row['user_id'] === null
                    ? 'system'
                    : Auth::normalizeWebRole((string)$row['web_role']),
                'actor_role_label'=>$row['user_id'] === null
                    ? 'System'
                    : Auth::webRoleLabel((string)$row['web_role']),
            ];
        }

        $playerNames = [];
        if ($steamIds) {
            $ids = array_keys($steamIds);
            $placeholders = implode(',',array_fill(0,count($ids),'?'));
            $q = $pdo->prepare(
                "SELECT steam_id64,display_name,steam_personaname
                 FROM game_players
                 WHERE steam_id64 IN ({$placeholders})"
            );
            $q->execute($ids);
            foreach ($q->fetchAll() as $row) {
                $playerNames[(string)$row['steam_id64']] =
                    trim((string)$row['steam_personaname']) !== ''
                        ? (string)$row['steam_personaname']
                        : (string)$row['display_name'];
            }
        }

        $breakdownMap = [];
        foreach ($prepared as &$row) {
            $target = $row['target_steamid'];
            $row['target_name'] = $target
                ? ($playerNames[$target] ?? $target)
                : null;
            $row['action_label'] = self::adminActionLabel($row['action']);
            $row['url'] = '/servers/' . $row['server_id'] . '/live#admin-log';

            $key = $row['actor_name'] . "\0" . $row['actor_role'] . "\0" . $row['server_id'];
            if (!isset($breakdownMap[$key])) {
                $breakdownMap[$key] = [
                    'actor_name'=>$row['actor_name'],
                    'actor_role'=>$row['actor_role'],
                    'actor_role_label'=>$row['actor_role_label'],
                    'server_id'=>$row['server_id'],
                    'server_name'=>$row['server_name'],
                    'count'=>0,
                    'url'=>$row['url'],
                ];
            }
            $breakdownMap[$key]['count']++;
        }
        unset($row);

        $breakdown = array_values($breakdownMap);
        usort($breakdown, static function(array $a,array $b): int {
            $countCmp = $b['count'] <=> $a['count'];
            if ($countCmp !== 0) return $countCmp;
            $serverCmp = strcasecmp($a['server_name'],$b['server_name']);
            return $serverCmp !== 0 ? $serverCmp : strcasecmp($a['actor_name'],$b['actor_name']);
        });

        return [
            'count'=>count($prepared),
            'breakdown'=>array_slice($breakdown,0,self::ADMIN_BREAKDOWN_LIMIT),
            'latest'=>array_slice($prepared,0,self::ADMIN_LATEST_LIMIT),
        ];
    }

    private static function announcements(string $since, string $now): array
    {
        $stmt = Database::connection()->prepare(
            "SELECT a.id,a.title,a.level,a.created_at,
                    u.name,u.steam_personaname,u.steam_verified_at
             FROM webpanel_announcements a
             JOIN users u ON u.id=a.created_by
             WHERE a.created_at>?
               AND a.revoked_at IS NULL
               AND (a.expires_at IS NULL OR a.expires_at>?)
             ORDER BY a.created_at DESC
             LIMIT " . self::ANNOUNCEMENT_LIMIT
        );
        $stmt->execute([$since,$now]);
        $rows = $stmt->fetchAll();

        $items = [];
        foreach ($rows as $row) {
            $items[] = [
                'id'=>(int)$row['id'],
                'title'=>(string)$row['title'],
                'level'=>(string)$row['level'],
                'created_at'=>(string)$row['created_at'],
                'creator'=>Auth::displayName([
                    'name'=>$row['name'],
                    'steam_personaname'=>$row['steam_personaname'],
                    'steam_verified_at'=>$row['steam_verified_at'],
                ]),
                'url'=>'/dashboard#announcements',
            ];
        }

        return [
            'count'=>count($items),
            'items'=>$items,
            'url'=>'/dashboard#announcements',
        ];
    }

    private static function chatActivity(string $since): array
    {
        $pdo = Database::connection();
        $stmt = $pdo->prepare(
            "SELECT COUNT(*) FROM dashboard_chat_messages WHERE created_at>?"
        );
        $stmt->execute([$since]);
        $count = (int)$stmt->fetchColumn();

        $stmt = $pdo->prepare(
            "SELECT m.id,m.message,m.created_at,
                    u.id user_id,u.name,u.web_role,u.steam_personaname,u.steam_verified_at
             FROM dashboard_chat_messages m
             JOIN users u ON u.id=m.user_id
             WHERE m.created_at>?
             ORDER BY m.id DESC
             LIMIT " . self::CHAT_PREVIEW_LIMIT
        );
        $stmt->execute([$since]);

        $items = [];
        foreach ($stmt->fetchAll() as $row) {
            $message = trim((string)$row['message']);
            if (function_exists('mb_strlen') && mb_strlen($message) > 100) {
                $message = mb_substr($message,0,100) . '…';
            } elseif (strlen($message) > 100) {
                $message = substr($message,0,100) . '…';
            }

            $items[] = [
                'id'=>(int)$row['id'],
                'author'=>Auth::displayName([
                    'id'=>(int)$row['user_id'],
                    'name'=>$row['name'],
                    'steam_personaname'=>$row['steam_personaname'],
                    'steam_verified_at'=>$row['steam_verified_at'],
                ]),
                'role'=>Auth::normalizeWebRole((string)$row['web_role']),
                'message'=>$message,
                'created_at'=>(string)$row['created_at'],
                'url'=>'/dashboard#dashboard-chat',
            ];
        }

        return [
            'count'=>$count,
            'items'=>$items,
            'url'=>'/dashboard#dashboard-chat',
        ];
    }

    private static function adminActionLabel(string $action): string
    {
        return match ($action) {
            'live.kick'=>'Kick',
            'live.ban'=>'Ban',
            'live.unban'=>'Unban',
            'live.mute'=>'Mute',
            'live.unmute'=>'Unmute',
            'live.gag'=>'Gag',
            'live.ungag'=>'Ungag',
            'live.silence'=>'Silence',
            'live.unsilence'=>'Unsilence',
            'live.slay'=>'Slay',
            'live.freeze'=>'Freeze',
            'live.respawn'=>'Respawn',
            'live.health'=>'Health ändern',
            'live.armor'=>'Armor ändern',
            'live.team'=>'Team ändern',
            'live.noclip'=>'Noclip',
            'live.goto_player'=>'Goto',
            'live.bring_player'=>'Bring',
            'live.separate_players'=>'Spieler trennen',
            'live.give_weapon'=>'Waffe geben',
            'live.give_weapon_group'=>'Gruppenwaffe',
            'live.remove_weapons'=>'Waffen entfernen',
            'live.nickname_set'=>'Nickname setzen',
            'live.nickname_restore'=>'Nickname zurücksetzen',
            'live.private_message'=>'Private Servernachricht',
            'live.broadcast'=>'Servernachricht',
            'live.map_change'=>'Mapwechsel',
            'live.restart.request'=>'Serverneustart',
            'server.power.start'=>'Server starten',
            'server.power.restart'=>'Server sofort neu starten',
            'server.power.restart.request'=>'Serverneustart',
            'server.power.stop'=>'Server stoppen',
            'server.power.kill'=>'Server Hard Kill',
            'live.round_restart'=>'Rundenneustart',
            'live.console'=>'Serverkonsole',
            'server.console.execute'=>'Serverkonsole',
            'live.cvar_set'=>'ConVar ändern',
            default=>ucfirst(str_replace(['live.','_'],['',' '],$action)),
        };
    }

    private static function formatGermanDate(string $value): string
    {
        $ts = strtotime($value);
        return $ts ? date('d.m.y H:i:s',$ts) : $value;
    }
}
