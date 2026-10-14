<?php
namespace App\Services;

use App\Core\Database;

/**
 * Verknüpft mehrere Steam-Accounts zu einer Person.
 * main_player_id = NULL bedeutet Hauptaccount; Zweitaccounts verweisen direkt auf den Hauptaccount.
 * Community- und Serverrollen werden immer als Familie synchron gehalten.
 */
final class PlayerAccountService
{
    public static function primaryId(int $playerId): int
    {
        $stmt = Database::connection()->prepare('SELECT id,main_player_id FROM game_players WHERE id=?');
        $stmt->execute([$playerId]);
        $row = $stmt->fetch();
        if (!$row) throw new \RuntimeException('Person nicht gefunden.');
        return (int)($row['main_player_id'] ?: $row['id']);
    }

    public static function familyIds(int $playerId): array
    {
        $primaryId = self::primaryId($playerId);
        $stmt = Database::connection()->prepare('SELECT id FROM game_players WHERE id=? OR main_player_id=? ORDER BY id');
        $stmt->execute([$primaryId, $primaryId]);
        return array_map('intval', $stmt->fetchAll(\PDO::FETCH_COLUMN));
    }

    public static function normalizePrimaryIds(array $playerIds): array
    {
        $result = [];
        foreach ($playerIds as $playerId) {
            $id = (int)$playerId;
            if ($id < 1) continue;
            $primary = self::primaryId($id);
            $result[$primary] = $primary;
        }
        return array_values($result);
    }

    public static function setMainAccount(int $playerId, ?int $mainPlayerId): array
    {
        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT * FROM game_players WHERE id=?');
        $stmt->execute([$playerId]);
        $player = $stmt->fetch();
        if (!$player) throw new \RuntimeException('Person nicht gefunden.');

        if ($mainPlayerId === null || $mainPlayerId < 1) {
            if (empty($player['main_player_id'])) return ['primary_id'=>$playerId,'family_ids'=>[$playerId],'unlinked'=>false];
            $oldPrimaryId = (int)$player['main_player_id'];
            $pdo->beginTransaction();
            try {
                $pdo->prepare('UPDATE game_players SET main_player_id=NULL,updated_at=? WHERE id=?')->execute([date('c'), $playerId]);
                $pdo->commit();
            } catch (\Throwable $e) {
                $pdo->rollBack();
                throw $e;
            }
            return ['primary_id'=>$playerId,'family_ids'=>[$playerId],'unlinked'=>true,'old_primary_id'=>$oldPrimaryId];
        }

        if ($mainPlayerId === $playerId) throw new \RuntimeException('Ein Account kann nicht sein eigener Hauptaccount sein.');
        $mainStmt = $pdo->prepare('SELECT * FROM game_players WHERE id=?');
        $mainStmt->execute([$mainPlayerId]);
        $main = $mainStmt->fetch();
        if (!$main) throw new \RuntimeException('Hauptaccount nicht gefunden.');
        if (!empty($main['main_player_id'])) throw new \RuntimeException('Als Hauptaccount kann nur ein eigenständiger Hauptaccount gewählt werden.');

        // Ein Hauptaccount mit eigenen Zweitaccounts darf nicht selbst Zweitaccount werden.
        $childStmt = $pdo->prepare('SELECT COUNT(*) FROM game_players WHERE main_player_id=?');
        $childStmt->execute([$playerId]);
        if ((int)$childStmt->fetchColumn() > 0) {
            throw new \RuntimeException('Dieser Account besitzt bereits Zweitaccounts. Bitte diese zuerst umhängen oder lösen.');
        }

        $pdo->beginTransaction();
        try {
            $pdo->prepare('UPDATE game_players SET main_player_id=?,community_role=?,updated_at=? WHERE id=?')
                ->execute([$mainPlayerId, $main['community_role'], date('c'), $playerId]);
            self::copyServerRolesInternal($pdo, $mainPlayerId, $playerId);
            $pdo->commit();
        } catch (\Throwable $e) {
            $pdo->rollBack();
            throw $e;
        }
        return ['primary_id'=>$mainPlayerId,'family_ids'=>self::familyIds($mainPlayerId),'unlinked'=>false];
    }

    public static function propagateCommunityRole(int $playerId, string $role): array
    {
        $pdo = Database::connection();
        $ids = self::familyIds($playerId);
        $stmt = $pdo->prepare('UPDATE game_players SET community_role=?,updated_at=? WHERE id=?');
        foreach ($ids as $id) $stmt->execute([$role, date('c'), $id]);
        return $ids;
    }

    public static function propagateServerRole(int $playerId, int $serverId, ?string $role): array
    {
        $pdo = Database::connection();
        $ids = self::familyIds($playerId);
        $managed = $pdo->prepare('INSERT OR IGNORE INTO server_managed_players(server_id,player_id,created_at) VALUES(?,?,?)');
        $up = $pdo->prepare('INSERT INTO server_player_groups(server_id,player_id,role_name,updated_at) VALUES(?,?,?,?) ON CONFLICT(server_id,player_id) DO UPDATE SET role_name=excluded.role_name,updated_at=excluded.updated_at');
        $del = $pdo->prepare('DELETE FROM server_player_groups WHERE server_id=? AND player_id=?');
        foreach ($ids as $id) {
            $managed->execute([$serverId, $id, date('c')]);
            if ($role === null || $role === '') $del->execute([$serverId, $id]);
            else $up->execute([$serverId, $id, $role, date('c')]);
        }
        return $ids;
    }

    public static function copyServerRoles(int $sourcePrimaryId): void
    {
        $pdo = Database::connection();
        $children = $pdo->prepare('SELECT id FROM game_players WHERE main_player_id=?');
        $children->execute([$sourcePrimaryId]);
        foreach ($children->fetchAll(\PDO::FETCH_COLUMN) as $childId) {
            self::copyServerRolesInternal($pdo, $sourcePrimaryId, (int)$childId);
        }
    }

    private static function copyServerRolesInternal(\PDO $pdo, int $sourcePlayerId, int $targetPlayerId): void
    {
        $pdo->prepare('DELETE FROM server_player_groups WHERE player_id=?')->execute([$targetPlayerId]);
        $pdo->prepare('DELETE FROM server_managed_players WHERE player_id=?')->execute([$targetPlayerId]);

        $managedRows = $pdo->prepare('SELECT server_id,created_at FROM server_managed_players WHERE player_id=?');
        $managedRows->execute([$sourcePlayerId]);
        $insManaged = $pdo->prepare('INSERT OR IGNORE INTO server_managed_players(server_id,player_id,created_at) VALUES(?,?,?)');
        foreach ($managedRows->fetchAll() as $row) {
            $insManaged->execute([(int)$row['server_id'], $targetPlayerId, (string)($row['created_at'] ?: date('c'))]);
        }

        $roles = $pdo->prepare('SELECT server_id,role_name FROM server_player_groups WHERE player_id=?');
        $roles->execute([$sourcePlayerId]);
        $up = $pdo->prepare('INSERT INTO server_player_groups(server_id,player_id,role_name,updated_at) VALUES(?,?,?,?) ON CONFLICT(server_id,player_id) DO UPDATE SET role_name=excluded.role_name,updated_at=excluded.updated_at');
        foreach ($roles->fetchAll() as $row) {
            $up->execute([(int)$row['server_id'], $targetPlayerId, (string)$row['role_name'], date('c')]);
        }
    }
}
