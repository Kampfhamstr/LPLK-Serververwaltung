<?php
namespace App\Services;

use App\Core\Auth;
use App\Core\Database;

final class CommunityService
{
    public const FORUM_CATEGORIES = [
        'allgemein' => 'Allgemein',
        'server' => 'Server',
        'technik' => 'Technik',
        'intern' => 'Intern',
    ];

    public static function latestChatMessages(int $limit = 30): array
    {
        $limit = max(1, min(30, $limit));
        $pdo = Database::connection();

        $sql = "
            SELECT c.id,c.user_id,c.message,c.created_at,
                   u.name,u.web_role,u.steam_id64,u.steam_personaname,u.steam_verified_at
            FROM dashboard_chat_messages c
            JOIN users u ON u.id=c.user_id
            ORDER BY c.id DESC
            LIMIT {$limit}
        ";
        $rows = $pdo->query($sql)->fetchAll();
        $rows = array_reverse($rows);

        return array_map([self::class, 'chatRow'], $rows);
    }

    public static function postChat(int $userId, string $message): array
    {
        $message = self::cleanText($message, 500);
        if ($message === '') {
            throw new \RuntimeException('Die Chatnachricht darf nicht leer sein.');
        }

        $pdo = Database::connection();

        $last = $pdo->prepare('SELECT created_at FROM dashboard_chat_messages WHERE user_id=? ORDER BY id DESC LIMIT 1');
        $last->execute([$userId]);
        $lastAt = $last->fetchColumn();
        if ($lastAt && strtotime((string)$lastAt) > time() - 2) {
            throw new \RuntimeException('Bitte kurz warten, bevor Sie die nächste Nachricht senden.');
        }

        $pdo->beginTransaction();
        try {
            $pdo->prepare('INSERT INTO dashboard_chat_messages(user_id,message,created_at) VALUES(?,?,?)')
                ->execute([$userId, $message, date('c')]);
            $id = (int)$pdo->lastInsertId();

            // Der Dashboard-Chat ist absichtlich flüchtig:
            // physisch werden nur die neuesten 30 Nachrichten behalten.
            $pdo->exec(
                'DELETE FROM dashboard_chat_messages
                 WHERE id NOT IN (
                    SELECT id FROM dashboard_chat_messages ORDER BY id DESC LIMIT 30
                 )'
            );

            $stmt = $pdo->prepare(
                'SELECT c.id,c.user_id,c.message,c.created_at,
                        u.name,u.web_role,u.steam_id64,u.steam_personaname,u.steam_verified_at
                 FROM dashboard_chat_messages c
                 JOIN users u ON u.id=c.user_id
                 WHERE c.id=?'
            );
            $stmt->execute([$id]);
            $row = $stmt->fetch();

            $pdo->commit();
            return self::chatRow($row ?: []);
        } catch (\Throwable $e) {
            if ($pdo->inTransaction()) $pdo->rollBack();
            throw $e;
        }
    }

    public static function deleteChat(int $messageId): void
    {
        Database::connection()->prepare('DELETE FROM dashboard_chat_messages WHERE id=?')->execute([$messageId]);
    }

    public static function latestThreads(int $limit = 6): array
    {
        $limit = max(1, min(20, $limit));
        $sql = "
            SELECT t.id,t.category,t.title,t.is_pinned,t.is_locked,t.created_at,t.updated_at,
                   u.name,u.steam_personaname,u.steam_verified_at,
                   (SELECT COUNT(*) FROM forum_posts p WHERE p.thread_id=t.id AND p.deleted_at IS NULL) AS post_count
            FROM forum_threads t
            JOIN users u ON u.id=t.user_id
            WHERE t.deleted_at IS NULL
            ORDER BY t.is_pinned DESC,t.updated_at DESC,t.id DESC
            LIMIT {$limit}
        ";
        $rows = Database::connection()->query($sql)->fetchAll();
        foreach ($rows as &$row) {
            $row['author_display_name'] = self::displayName($row);
            $row['category_label'] = self::FORUM_CATEGORIES[$row['category']] ?? ucfirst((string)$row['category']);
            $row['post_count'] = (int)$row['post_count'];
            $row['is_pinned'] = (bool)$row['is_pinned'];
            $row['is_locked'] = (bool)$row['is_locked'];
        }
        unset($row);
        return $rows;
    }

    public static function threads(?string $category = null): array
    {
        $pdo = Database::connection();
        $where = 't.deleted_at IS NULL';
        $params = [];
        if ($category !== null && isset(self::FORUM_CATEGORIES[$category])) {
            $where .= ' AND t.category=?';
            $params[] = $category;
        }

        $stmt = $pdo->prepare(
            "SELECT t.id,t.category,t.title,t.is_pinned,t.is_locked,t.created_at,t.updated_at,
                    u.name,u.steam_personaname,u.steam_verified_at,
                    (SELECT COUNT(*) FROM forum_posts p WHERE p.thread_id=t.id AND p.deleted_at IS NULL) AS post_count
             FROM forum_threads t
             JOIN users u ON u.id=t.user_id
             WHERE {$where}
             ORDER BY t.is_pinned DESC,t.updated_at DESC,t.id DESC
             LIMIT 200"
        );
        $stmt->execute($params);
        $rows = $stmt->fetchAll();

        foreach ($rows as &$row) {
            $row['author_display_name'] = self::displayName($row);
            $row['category_label'] = self::FORUM_CATEGORIES[$row['category']] ?? ucfirst((string)$row['category']);
            $row['post_count'] = (int)$row['post_count'];
            $row['is_pinned'] = (bool)$row['is_pinned'];
            $row['is_locked'] = (bool)$row['is_locked'];
        }
        unset($row);
        return $rows;
    }

    public static function thread(int $threadId): ?array
    {
        $stmt = Database::connection()->prepare(
            "SELECT t.*,u.name,u.steam_personaname,u.steam_verified_at
             FROM forum_threads t
             JOIN users u ON u.id=t.user_id
             WHERE t.id=? AND t.deleted_at IS NULL"
        );
        $stmt->execute([$threadId]);
        $row = $stmt->fetch();
        if (!$row) return null;

        $row['author_display_name'] = self::displayName($row);
        $row['category_label'] = self::FORUM_CATEGORIES[$row['category']] ?? ucfirst((string)$row['category']);
        $row['is_pinned'] = (bool)$row['is_pinned'];
        $row['is_locked'] = (bool)$row['is_locked'];
        return $row;
    }

    public static function posts(int $threadId): array
    {
        $stmt = Database::connection()->prepare(
            "SELECT p.id,p.user_id,p.body,p.created_at,
                    u.name,u.web_role,u.steam_id64,u.steam_personaname,u.steam_verified_at
             FROM forum_posts p
             JOIN users u ON u.id=p.user_id
             WHERE p.thread_id=? AND p.deleted_at IS NULL
             ORDER BY p.id ASC"
        );
        $stmt->execute([$threadId]);
        $rows = $stmt->fetchAll();

        foreach ($rows as &$row) {
            $row['author_display_name'] = self::displayName($row);
            $row['web_role_label'] = Auth::webRoleLabel((string)$row['web_role']);
            $row['steam_verified'] = !empty($row['steam_verified_at']) && !empty($row['steam_id64']);
        }
        unset($row);
        return $rows;
    }

    public static function createThread(int $userId, string $category, string $title, string $body): int
    {
        if (!isset(self::FORUM_CATEGORIES[$category])) $category = 'allgemein';
        $title = self::cleanText($title, 120);
        $body = self::cleanText($body, 5000);

        if ($title === '' || $body === '') {
            throw new \RuntimeException('Titel und erster Beitrag dürfen nicht leer sein.');
        }

        $pdo = Database::connection();

        $last = $pdo->prepare('SELECT created_at FROM forum_threads WHERE user_id=? ORDER BY id DESC LIMIT 1');
        $last->execute([$userId]);
        $lastAt = $last->fetchColumn();
        if ($lastAt && strtotime((string)$lastAt) > time() - 10) {
            throw new \RuntimeException('Bitte kurz warten, bevor Sie ein weiteres Thema erstellen.');
        }

        $now = date('c');
        $pdo->beginTransaction();
        try {
            $pdo->prepare('INSERT INTO forum_threads(user_id,category,title,created_at,updated_at) VALUES(?,?,?,?,?)')
                ->execute([$userId,$category,$title,$now,$now]);
            $threadId = (int)$pdo->lastInsertId();

            $pdo->prepare('INSERT INTO forum_posts(thread_id,user_id,body,created_at) VALUES(?,?,?,?)')
                ->execute([$threadId,$userId,$body,$now]);

            $pdo->commit();
            return $threadId;
        } catch (\Throwable $e) {
            if ($pdo->inTransaction()) $pdo->rollBack();
            throw $e;
        }
    }

    public static function reply(int $threadId, int $userId, string $body): int
    {
        $thread = self::thread($threadId);
        if (!$thread) throw new \RuntimeException('Thema nicht gefunden.');
        if ($thread['is_locked']) throw new \RuntimeException('Dieses Thema ist gesperrt.');

        $body = self::cleanText($body, 5000);
        if ($body === '') throw new \RuntimeException('Die Antwort darf nicht leer sein.');

        $pdo = Database::connection();
        $last = $pdo->prepare('SELECT created_at FROM forum_posts WHERE user_id=? ORDER BY id DESC LIMIT 1');
        $last->execute([$userId]);
        $lastAt = $last->fetchColumn();
        if ($lastAt && strtotime((string)$lastAt) > time() - 3) {
            throw new \RuntimeException('Bitte kurz warten, bevor Sie erneut antworten.');
        }

        $now = date('c');
        $pdo->beginTransaction();
        try {
            $pdo->prepare('INSERT INTO forum_posts(thread_id,user_id,body,created_at) VALUES(?,?,?,?)')
                ->execute([$threadId,$userId,$body,$now]);
            $postId = (int)$pdo->lastInsertId();
            $pdo->prepare('UPDATE forum_threads SET updated_at=? WHERE id=?')->execute([$now,$threadId]);
            $pdo->commit();
            return $postId;
        } catch (\Throwable $e) {
            if ($pdo->inTransaction()) $pdo->rollBack();
            throw $e;
        }
    }

    private static function chatRow(array $row): array
    {
        return [
            'id' => (int)($row['id'] ?? 0),
            'user_id' => (int)($row['user_id'] ?? 0),
            'message' => (string)($row['message'] ?? ''),
            'created_at' => (string)($row['created_at'] ?? ''),
            'display_name' => self::displayName($row),
            'web_role' => Auth::normalizeWebRole((string)($row['web_role'] ?? 'member')),
            'web_role_label' => Auth::webRoleLabel((string)($row['web_role'] ?? 'member')),
            'steam_verified' => !empty($row['steam_verified_at']) && !empty($row['steam_id64']),
        ];
    }

    private static function displayName(array $row): string
    {
        if (!empty($row['steam_verified_at']) && trim((string)($row['steam_personaname'] ?? '')) !== '') {
            return (string)$row['steam_personaname'];
        }
        return trim((string)($row['name'] ?? '')) ?: 'Unbekannt';
    }

    private static function cleanText(string $value, int $max): string
    {
        $value = str_replace("\0", '', trim($value));
        $value = preg_replace('/\R{3,}/u', "\n\n", $value) ?? $value;
        if (function_exists('mb_substr')) return mb_substr($value, 0, $max, 'UTF-8');
        return substr($value, 0, $max);
    }
}
