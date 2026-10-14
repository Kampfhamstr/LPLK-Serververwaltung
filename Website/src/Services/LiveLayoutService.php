<?php
namespace App\Services;

use App\Core\Database;

final class LiveLayoutService
{
    public const MODULES = [
        'pterodactyl-control',
        'active-rules',
        'server-actions',
        'players',
        'server-chat',
        'messages',
        'bots-rules',
        'weapon-modes',
        'group-actions',
        'presets',
        'whitelist',
        'admin-log',
        'team-weapons',
        'command-feedback',
        'advanced',
        'bans',
    ];

    /**
     * Standardbreiten im 12-Spalten-Raster. Erlaubt sind 3/4/6/8/12 Spalten.
     * Große Datenmodule bekommen mehr Platz, kompakte Aktionen bewusst weniger.
     */
    public const DEFAULT_SIZES = [
        'pterodactyl-control' => 12,
        'active-rules' => 12,
        'server-actions' => 4,
        'players' => 8,
        'server-chat' => 8,
        'messages' => 4,
        'bots-rules' => 4,
        'weapon-modes' => 8,
        'group-actions' => 6,
        'presets' => 6,
        'whitelist' => 8,
        'admin-log' => 4,
        'team-weapons' => 4,
        'command-feedback' => 4,
        'advanced' => 8,
        'bans' => 12,
    ];

    private const ALLOWED_SIZES = [3, 4, 6, 8, 12];

    public static function get(int $userId, int $serverId): array
    {
        $default = ['order'=>self::MODULES, 'collapsed'=>[], 'sizes'=>self::DEFAULT_SIZES, 'scope'=>'default'];
        if ($userId <= 0 || $serverId <= 0) return $default;

        try {
            $pdo = Database::connection();
            $stmt = $pdo->prepare(
                'SELECT scope_key,layout_json FROM user_live_layouts WHERE user_id=? AND scope_key IN (?,?) '
                . 'ORDER BY CASE WHEN scope_key=? THEN 0 ELSE 1 END LIMIT 1'
            );
            $serverScope = 'server:' . $serverId;
            $stmt->execute([$userId, $serverScope, 'global', $serverScope]);
            $row = $stmt->fetch();
        } catch (\Throwable) {
            // Ein vergessenes migrate darf die Live-Seite nicht komplett blockieren.
            return $default;
        }
        if (!$row) return $default;

        $decoded = json_decode((string)($row['layout_json'] ?? ''), true);
        if (!is_array($decoded)) return $default;
        $layout = self::sanitize($decoded);
        $layout['scope'] = (string)($row['scope_key'] ?? 'default');
        return $layout;
    }

    public static function save(int $userId, int $serverId, array $layout, bool $allServers): array
    {
        if ($userId <= 0 || $serverId <= 0) throw new \InvalidArgumentException('Ungültiger Benutzer oder Server.');
        $layout = self::sanitize($layout);
        unset($layout['scope']);
        $json = json_encode($layout, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
        $scope = $allServers ? 'global' : 'server:' . $serverId;
        $pdo = Database::connection();
        $pdo->beginTransaction();
        try {
            if ($allServers) {
                $delete = $pdo->prepare("DELETE FROM user_live_layouts WHERE user_id=? AND scope_key LIKE 'server:%'");
                $delete->execute([$userId]);
            }
            $stmt = $pdo->prepare(
                'INSERT INTO user_live_layouts(user_id,scope_key,layout_json,updated_at) VALUES(?,?,?,?) '
                . 'ON CONFLICT(user_id,scope_key) DO UPDATE SET layout_json=excluded.layout_json,updated_at=excluded.updated_at'
            );
            $stmt->execute([$userId, $scope, $json, date('c')]);
            $pdo->commit();
        } catch (\Throwable $e) {
            if ($pdo->inTransaction()) $pdo->rollBack();
            throw $e;
        }
        $layout['scope'] = $scope;
        return $layout;
    }

    public static function reset(int $userId, int $serverId, bool $allServers): void
    {
        if ($userId <= 0 || $serverId <= 0) throw new \InvalidArgumentException('Ungültiger Benutzer oder Server.');
        $pdo = Database::connection();
        if ($allServers) {
            $stmt = $pdo->prepare('DELETE FROM user_live_layouts WHERE user_id=?');
            $stmt->execute([$userId]);
            return;
        }
        $stmt = $pdo->prepare('DELETE FROM user_live_layouts WHERE user_id=? AND scope_key=?');
        $stmt->execute([$userId, 'server:' . $serverId]);
    }

    public static function sanitize(array $layout): array
    {
        $allowed = array_flip(self::MODULES);
        $order = [];
        foreach ((array)($layout['order'] ?? []) as $id) {
            $id = (string)$id;
            if (isset($allowed[$id]) && !in_array($id, $order, true)) $order[] = $id;
        }
        foreach (self::MODULES as $id) if (!in_array($id, $order, true)) $order[] = $id;

        $collapsed = [];
        foreach ((array)($layout['collapsed'] ?? []) as $id) {
            $id = (string)$id;
            if (isset($allowed[$id]) && !in_array($id, $collapsed, true)) $collapsed[] = $id;
        }

        $sizes = self::DEFAULT_SIZES;
        foreach ((array)($layout['sizes'] ?? []) as $id => $span) {
            $id = (string)$id;
            $span = (int)$span;
            if (isset($allowed[$id]) && in_array($span, self::ALLOWED_SIZES, true)) $sizes[$id] = $span;
        }
        foreach (self::MODULES as $id) {
            if (!isset($sizes[$id]) || !in_array((int)$sizes[$id], self::ALLOWED_SIZES, true)) $sizes[$id] = 12;
        }
        return ['order'=>$order, 'collapsed'=>$collapsed, 'sizes'=>$sizes];
    }
}
