<?php
namespace App\Services;

use App\Core\Auth;
use App\Core\Audit;
use App\Core\Database;

final class PanelUserDirectoryService
{
    private const ONLINE_SECONDS = 180;
    private const MESSAGE_LIMIT = 4000;
    private const CONVERSATION_LIMIT = 60;

    public static function sidebarUsers(): array
    {
        $viewer = Auth::user();
        if (!$viewer) return [];

        $viewerId = (int)$viewer['id'];
        $includeInactive = Auth::can('users.manage');

        $sql = "
            SELECT u.id,u.name,u.email,u.web_role,u.active,u.created_at,u.last_login_at,u.last_seen_at,
                   u.steam_id64,u.steam_personaname,u.steam_verified_at,
                   (
                       SELECT COUNT(*)
                       FROM webpanel_direct_messages dm
                       WHERE dm.sender_id=u.id
                         AND dm.recipient_id=?
                         AND dm.read_at IS NULL
                   ) AS unread_count
            FROM users u
        ";
        if (!$includeInactive) $sql .= " WHERE u.active=1";
        $stmt = Database::connection()->prepare($sql);
        $stmt->execute([$viewerId]);
        $rows = $stmt->fetchAll();

        $now = time();
        foreach ($rows as &$row) {
            $row['id'] = (int)$row['id'];
            $row['active'] = (bool)$row['active'];
            $row['web_role'] = Auth::normalizeWebRole((string)$row['web_role']);
            $row['role_label'] = Auth::webRoleLabel((string)$row['web_role']);
            $row['display_name'] = Auth::displayName($row);
            $row['online'] = self::isOnline($row, $now);
            $row['unread_count'] = (int)$row['unread_count'];
            $row['is_me'] = $row['id'] === $viewerId;
            $row['avatar_url'] = (!empty($row['steam_verified_at']) && !empty($row['steam_id64']) && $row['active'])
                ? '/users/' . $row['id'] . '/steam-avatar'
                : null;
            unset($row['email']);
        }
        unset($row);

        usort($rows, static function(array $a, array $b): int {
            $levels = ['founder'=>1,'admin'=>2,'member'=>3,'vip'=>4,'spieler'=>5];
            $roleCmp = ($levels[$a['web_role']] ?? 9) <=> ($levels[$b['web_role']] ?? 9);
            if ($roleCmp !== 0) return $roleCmp;

            if ($a['online'] !== $b['online']) return $a['online'] ? -1 : 1;
            if ($a['active'] !== $b['active']) return $a['active'] ? -1 : 1;

            return strcasecmp((string)$a['display_name'], (string)$b['display_name']);
        });

        return $rows;
    }

    public static function detail(int $targetId): array
    {
        $viewer = Auth::user();
        if (!$viewer) throw new \RuntimeException('Nicht angemeldet.');

        $viewerId = (int)$viewer['id'];
        $pdo = Database::connection();

        $stmt = $pdo->prepare(
            'SELECT id,name,email,web_role,active,created_at,last_login_at,last_seen_at,
                    steam_id64,steam_personaname,steam_profile_url,steam_verified_at
             FROM users WHERE id=?'
        );
        $stmt->execute([$targetId]);
        $target = $stmt->fetch();
        if (!$target) throw new \RuntimeException('Webpanel-Nutzer nicht gefunden.');

        if (!(bool)$target['active'] && !Auth::can('users.manage')) {
            throw new \RuntimeException('Webpanel-Nutzer nicht verfügbar.');
        }

        // Das 20-s-Polling des Nutzer-Drawers darf nicht bei jedem GET einen
        // SQLite-Write erzeugen. Nur dann als gelesen markieren, wenn wirklich
        // ungelesene Nachrichten vorhanden sind. Der Mark-Read-Write ist zudem
        // bewusst best effort, damit ein Busy-Lock den Chat nicht unbenutzbar macht.
        $unread = $pdo->prepare(
            'SELECT 1 FROM webpanel_direct_messages
             WHERE sender_id=? AND recipient_id=? AND read_at IS NULL LIMIT 1'
        );
        $unread->execute([$targetId, $viewerId]);
        if ($unread->fetchColumn()) {
            try {
                Database::withBusyRetry(static function () use ($pdo, $targetId, $viewerId): void {
                    $pdo->prepare(
                        'UPDATE webpanel_direct_messages
                         SET read_at=?
                         WHERE sender_id=? AND recipient_id=? AND read_at IS NULL'
                    )->execute([date('c'), $targetId, $viewerId]);
                }, 3);
            } catch (\Throwable) {
                // Lesen der Unterhaltung bleibt wichtiger als der Read-Receipt-Status.
            }
        }

        $msg = $pdo->prepare(
            'SELECT dm.id,dm.sender_id,dm.recipient_id,dm.message,dm.created_at,dm.read_at,
                    s.name sender_name,s.steam_personaname sender_steam_name,s.steam_verified_at sender_steam_verified,
                    s.web_role sender_role
             FROM webpanel_direct_messages dm
             JOIN users s ON s.id=dm.sender_id
             WHERE (dm.sender_id=? AND dm.recipient_id=?)
                OR (dm.sender_id=? AND dm.recipient_id=?)
             ORDER BY dm.id DESC
             LIMIT ' . self::CONVERSATION_LIMIT
        );
        $msg->execute([$viewerId,$targetId,$targetId,$viewerId]);
        $messages = array_reverse($msg->fetchAll());

        foreach ($messages as &$message) {
            $message['id'] = (int)$message['id'];
            $message['sender_id'] = (int)$message['sender_id'];
            $message['recipient_id'] = (int)$message['recipient_id'];
            $message['mine'] = $message['sender_id'] === $viewerId;
            $message['sender_display_name'] = Auth::displayName([
                'id'=>$message['sender_id'],
                'name'=>$message['sender_name'],
                'steam_personaname'=>$message['sender_steam_name'],
                'steam_verified_at'=>$message['sender_steam_verified'],
            ]);
            $message['sender_role'] = Auth::normalizeWebRole((string)$message['sender_role']);
        }
        unset($message);

        $now = time();
        $role = Auth::normalizeWebRole((string)$target['web_role']);
        $detail = [
            'id'=>(int)$target['id'],
            'display_name'=>Auth::displayName($target),
            'web_role'=>$role,
            'role_label'=>Auth::webRoleLabel($role),
            'active'=>(bool)$target['active'],
            'online'=>self::isOnline($target,$now),
            'last_seen_at'=>$target['last_seen_at'],
            'last_login_at'=>$target['last_login_at'],
            'created_at'=>$target['created_at'],
            'steam_id64'=>$target['steam_id64'],
            'steam_verified'=>!empty($target['steam_verified_at']),
            'steam_profile_url'=>$target['steam_profile_url'],
            'avatar_url'=>(!empty($target['steam_verified_at']) && !empty($target['steam_id64']) && $target['active'])
                ? '/users/' . (int)$target['id'] . '/steam-avatar'
                : null,
            'is_me'=>(int)$target['id'] === $viewerId,
            'can_manage'=>Auth::can('users.manage') && Auth::canManageWebRoleTarget($role) && (int)$target['id'] !== $viewerId,
            'can_message'=>(bool)$target['active'] && (int)$target['id'] !== $viewerId,
            'assignable_roles'=>[],
        ];

        if ($detail['can_manage']) {
            foreach (Auth::WEB_ROLES as $candidateRole=>$candidateLabel) {
                if ($candidateRole === $role || Auth::canAssignWebRole($candidateRole)) {
                    $detail['assignable_roles'][$candidateRole] = $candidateLabel;
                }
            }
        }

        if (Auth::can('users.view')) {
            $detail['email'] = (string)$target['email'];
        }

        return [
            'user'=>$detail,
            'messages'=>$messages,
        ];
    }

    public static function sendMessage(int $targetId, string $message): array
    {
        $viewer = Auth::user();
        if (!$viewer) throw new \RuntimeException('Nicht angemeldet.');

        $viewerId = (int)$viewer['id'];
        if ($targetId === $viewerId) {
            throw new \RuntimeException('Eine Direktnachricht an das eigene Konto ist nicht erforderlich.');
        }

        $message = self::cleanText($message, self::MESSAGE_LIMIT);
        if ($message === '') throw new \RuntimeException('Die Nachricht darf nicht leer sein.');

        $pdo = Database::connection();

        $targetStmt = $pdo->prepare('SELECT id,active FROM users WHERE id=?');
        $targetStmt->execute([$targetId]);
        $target = $targetStmt->fetch();
        if (!$target || !(bool)$target['active']) {
            throw new \RuntimeException('Empfänger ist nicht verfügbar.');
        }

        $last = $pdo->prepare(
            'SELECT created_at FROM webpanel_direct_messages WHERE sender_id=? ORDER BY id DESC LIMIT 1'
        );
        $last->execute([$viewerId]);
        $lastAt = $last->fetchColumn();
        if ($lastAt && strtotime((string)$lastAt) > time() - 1) {
            throw new \RuntimeException('Bitte kurz warten, bevor die nächste Nachricht gesendet wird.');
        }

        $now = date('c');
        Database::withBusyRetry(static function () use ($pdo, $viewerId, $targetId, $message, $now): void {
            $pdo->prepare(
                'INSERT INTO webpanel_direct_messages(sender_id,recipient_id,message,created_at,read_at)
                 VALUES(?,?,?,?,NULL)'
            )->execute([$viewerId,$targetId,$message,$now]);
        }, 5);

        $id = (int)$pdo->lastInsertId();
        Audit::log('webpanel.direct_message.sent','user',(string)$targetId,[
            'message_id'=>$id,
        ]);

        return [
            'id'=>$id,
            'sender_id'=>$viewerId,
            'recipient_id'=>$targetId,
            'message'=>$message,
            'created_at'=>$now,
            'read_at'=>null,
            'mine'=>true,
            'sender_display_name'=>Auth::displayName($viewer),
            'sender_role'=>Auth::normalizeWebRole((string)$viewer['web_role']),
        ];
    }

    public static function updateAccount(int $targetId, string $role, bool $active): array
    {
        Auth::requirePermission('users.manage');

        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT * FROM users WHERE id=?');
        $stmt->execute([$targetId]);
        $target = $stmt->fetch();
        if (!$target) throw new \RuntimeException('Webpanel-Nutzer nicht gefunden.');

        $role = Auth::normalizeWebRole($role);
        $viewerId = (int)Auth::user()['id'];
        $currentRole = Auth::normalizeWebRole((string)$target['web_role']);

        if ($targetId === $viewerId) {
            throw new \RuntimeException('Die eigene Rolle bzw. der eigene Kontostatus wird in der Nutzerverwaltung nicht selbst verändert.');
        }
        if (!Auth::canManageWebRoleTarget($currentRole)) {
            throw new \RuntimeException('Nutzer mit gleicher oder höherer Rolle dürfen nicht bearbeitet werden.');
        }
        if ($role !== $currentRole && !Auth::canAssignWebRole($role)) {
            throw new \RuntimeException('Es dürfen nur Rollen unterhalb der eigenen Rolle vergeben werden.');
        }

        if ($currentRole === 'founder' && ($role !== 'founder' || !$active)) {
            $count = $pdo->prepare(
                "SELECT COUNT(*) FROM users WHERE lower(web_role)='founder' AND active=1 AND id<>?"
            );
            $count->execute([$targetId]);
            if ((int)$count->fetchColumn() === 0) {
                throw new \RuntimeException('Der letzte aktive Founder kann weder herabgestuft noch gesperrt werden.');
            }
        }

        $newAuthVersion=max(1,(int)($target['auth_version']??1))+($role!==$currentRole?1:0);
        $pdo->prepare(
            'UPDATE users SET web_role=?,active=?,auth_version=?,updated_at=? WHERE id=?'
        )->execute([$role,$active?1:0,$newAuthVersion,date('c'),$targetId]);

        Audit::log('user.update.sidebar','user',(string)$targetId,[
            'role'=>$role,
            'active'=>$active,
            'granular_permissions_preserved'=>true,
        ]);

        return [
            'id'=>$targetId,
            'web_role'=>$role,
            'role_label'=>Auth::webRoleLabel($role),
            'active'=>$active,
        ];
    }

    public static function unreadTotal(): int
    {
        $viewer = Auth::user();
        if (!$viewer) return 0;

        $stmt = Database::connection()->prepare(
            'SELECT COUNT(*) FROM webpanel_direct_messages
             WHERE recipient_id=? AND read_at IS NULL'
        );
        $stmt->execute([(int)$viewer['id']]);
        return (int)$stmt->fetchColumn();
    }

    private static function isOnline(array $user, ?int $now = null): bool
    {
        if (empty($user['active']) || empty($user['last_seen_at'])) return false;
        $ts = strtotime((string)$user['last_seen_at']);
        if (!$ts) return false;
        return ($now ?? time()) - $ts <= self::ONLINE_SECONDS;
    }

    private static function cleanText(string $text, int $max): string
    {
        $text = trim(str_replace(["\r\n","\r"], "\n", $text));
        $text = preg_replace('/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/u', '', $text) ?? '';
        if (mb_strlen($text) > $max) $text = mb_substr($text,0,$max);
        return trim($text);
    }
}
