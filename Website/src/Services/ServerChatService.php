<?php
namespace App\Services;

use App\Core\Database;

final class ServerChatService
{
    public static function ingest(int $serverId, array $messages): int
    {
        if (!$messages) return 0;
        $pdo = Database::connection();
        $batch = array_slice($messages, -500);

        // Die Bridge sendet einen Ringpuffer. Zuerst werden alle bereits bekannten
        // IDs in wenigen SELECTs ermittelt. Dadurch werden alte Ringpuffer-Einträge
        // nicht bei jedem 2-Sekunden-Push erneut als Schreiboperation behandelt.
        $uids = [];
        foreach ($batch as $m) {
            if (!is_array($m)) continue;
            $uid = trim((string)($m['id'] ?? $m['message_uid'] ?? ''));
            if ($uid !== '') $uids[substr($uid, 0, 120)] = true;
        }
        if (!$uids) return 0;

        $known = [];
        foreach (array_chunk(array_keys($uids), 400) as $chunk) {
            $sql = 'SELECT message_uid FROM server_chat_messages WHERE server_id=? AND message_uid IN (' .
                   implode(',', array_fill(0, count($chunk), '?')) . ')';
            $stmt = $pdo->prepare($sql);
            $stmt->execute(array_merge([$serverId], $chunk));
            foreach ($stmt->fetchAll(\PDO::FETCH_COLUMN) as $existing) $known[(string)$existing] = true;
        }

        // Erst die tatsächlich neuen Nachrichten vorbereiten. Der entscheidende
        // v1.7.7-Fix: Wenn nichts neu ist, wird KEINE SQLite-Write-Transaktion mehr
        // geöffnet. Bei mehreren Servern spart das permanent konkurrierende Writer.
        $pending = [];
        foreach ($batch as $m) {
            if (!is_array($m)) continue;
            $uid = trim((string)($m['id'] ?? $m['message_uid'] ?? ''));
            $steam = trim((string)($m['steam_id64'] ?? ''));
            $name = trim((string)($m['player_name'] ?? $m['name'] ?? 'Unbekannt'));
            $text = trim((string)($m['message'] ?? $m['text'] ?? ''));
            if ($uid === '' || $text === '') continue;
            if (strlen($uid) > 120) $uid = substr($uid, 0, 120);
            if (isset($known[$uid])) continue;
            if ($steam !== '' && !preg_match('/^7656119\d{10}$/', $steam)) $steam = '';
            $name = self::cut($name !== '' ? $name : 'Unbekannt', 80);
            $text = self::cut($text, 1000);
            $role = strtolower(trim((string)($m['community_role'] ?? 'spieler')));
            if (!in_array($role, ['founder','admin','member','vip','spieler'], true)) $role = 'spieler';
            $team = trim((string)($m['team'] ?? 'Unassigned')) ?: 'Unassigned';
            $sentAt = self::normalizeDate((string)($m['sent_at'] ?? $m['created_at'] ?? ''));
            $map = self::cut(trim((string)($m['map_name'] ?? $m['map'] ?? '')), 100);
            $pending[] = [
                $serverId,$uid,$steam !== '' ? $steam : null,$name,$role,self::cut($team,20),
                !empty($m['team_only']) ? 1 : 0,$text,$map !== '' ? $map : null,$sentAt,date('c')
            ];
            // Doppelte IDs innerhalb desselben Pushes ebenfalls überspringen.
            $known[$uid] = true;
        }

        if (!$pending) return 0;

        $insert = $pdo->prepare(
            'INSERT OR IGNORE INTO server_chat_messages(' .
            'server_id,message_uid,steam_id64,player_name,community_role_snapshot,team,team_only,message,map_name,sent_at,received_at' .
            ') VALUES(?,?,?,?,?,?,?,?,?,?,?)'
        );
        $count = 0;
        $ownTransaction = !$pdo->inTransaction();
        if ($ownTransaction) $pdo->beginTransaction();
        try {
            foreach ($pending as $row) {
                $insert->execute($row);
                if ($insert->rowCount() > 0) $count++;
            }
            if ($ownTransaction) $pdo->commit();
        } catch (\Throwable $e) {
            if ($ownTransaction && $pdo->inTransaction()) $pdo->rollBack();
            throw $e;
        }
        return $count;
    }

    public static function latest(int $serverId, int $limit = 20): array
    {
        return self::search($serverId, ['limit'=>$limit]);
    }

    public static function search(int $serverId, array $filters = []): array
    {
        $limit = max(1, min(250, (int)($filters['limit'] ?? 100)));
        $where = ['m.server_id=?'];
        $params = [$serverId];
        $player = trim((string)($filters['player'] ?? ''));
        $q = trim((string)($filters['q'] ?? ''));
        $from = trim((string)($filters['from'] ?? ''));
        $to = trim((string)($filters['to'] ?? ''));
        $beforeId = (int)($filters['before_id'] ?? 0);
        if ($player !== '') {
            $where[] = '(m.player_name LIKE ? COLLATE NOCASE OR m.steam_id64 LIKE ?)';
            $params[] = '%' . $player . '%';
            $params[] = '%' . $player . '%';
        }
        if ($q !== '') { $where[] = 'm.message LIKE ? COLLATE NOCASE'; $params[] = '%' . $q . '%'; }
        if ($from !== '') { $where[] = 'm.sent_at>=?'; $params[] = self::dateBoundary($from, false); }
        if ($to !== '') { $where[] = 'm.sent_at<=?'; $params[] = self::dateBoundary($to, true); }
        if ($beforeId > 0) { $where[] = 'm.id<?'; $params[] = $beforeId; }

        $sql = 'SELECT m.*, COALESCE(main.community_role,gp.community_role,m.community_role_snapshot,\'spieler\') AS community_role ' .
               'FROM server_chat_messages m ' .
               'LEFT JOIN game_players gp ON gp.steam_id64=m.steam_id64 ' .
               'LEFT JOIN game_players main ON main.id=gp.main_player_id ' .
               'WHERE ' . implode(' AND ', $where) . ' ORDER BY m.id DESC LIMIT ' . $limit;
        $stmt = Database::connection()->prepare($sql);
        $stmt->execute($params);
        return $stmt->fetchAll();
    }

    public static function count(int $serverId): int
    {
        $s = Database::connection()->prepare('SELECT COUNT(*) FROM server_chat_messages WHERE server_id=?');
        $s->execute([$serverId]);
        return (int)$s->fetchColumn();
    }

    public static function players(int $serverId, int $limit = 300): array
    {
        $limit = max(1, min(1000, $limit));
        $stmt = Database::connection()->prepare(
            'SELECT player_name,steam_id64,MAX(sent_at) AS last_message_at ' .
            'FROM server_chat_messages WHERE server_id=? ' .
            'GROUP BY player_name,steam_id64 ORDER BY last_message_at DESC LIMIT ' . $limit
        );
        $stmt->execute([$serverId]);
        return $stmt->fetchAll();
    }

    private static function normalizeDate(string $value): string
    {
        $ts = strtotime($value);
        return $ts ? date('c', $ts) : date('c');
    }

    private static function dateBoundary(string $value, bool $end): string
    {
        if (preg_match('/^\d{4}-\d{2}-\d{2}$/', $value)) $value .= $end ? ' 23:59:59' : ' 00:00:00';
        return self::normalizeDate($value);
    }

    private static function cut(string $value, int $max): string
    {
        return function_exists('mb_substr') ? mb_substr($value,0,$max) : substr($value,0,$max);
    }
}
