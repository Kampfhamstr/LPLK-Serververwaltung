<?php
namespace App\Services;

use App\Core\Database;
use App\Core\Audit;
use App\Core\Jsonc;

final class SyncService
{
    public static function scan(int $serverId): array
    {
        $server = self::server($serverId);
        $connector = ConnectorFactory::make($server);
        $detected = FrameworkDetector::detect($connector);
        $pdo = Database::connection();

        $pdo->beginTransaction();
        try {
            $pdo->prepare('UPDATE game_servers SET framework=?, last_scan_at=?, last_error=NULL, updated_at=? WHERE id=?')
                ->execute([$detected['framework'], date('c'), date('c'), $serverId]);
            $pdo->prepare('DELETE FROM detected_plugins WHERE server_id=?')->execute([$serverId]);
            $stmt = $pdo->prepare('INSERT INTO detected_plugins(server_id,framework,plugin_name,plugin_path,detected_at) VALUES(?,?,?,?,?)');
            foreach ($detected['plugins'] as $p) {
                $stmt->execute([$serverId, $p['framework'], $p['name'], $p['path'], date('c')]);
            }
            $pdo->commit();
        } catch (\Throwable $e) {
            $pdo->rollBack();
            throw $e;
        }

        if (in_array($detected['framework'], ['swiftlys2', 'both'], true) && $connector->exists(SwiftlyAdapter::PATH)) {
            $content = $connector->read(SwiftlyAdapter::PATH);
            self::recordState($serverId, SwiftlyAdapter::PATH, $content);
            self::importSwiftly($serverId, $content);
        }

        if (in_array($detected['framework'], ['counterstrikesharp', 'both'], true)) {
            if ($connector->exists(CounterStrikeSharpAdapter::ADMINS_PATH)) {
                $content = $connector->read(CounterStrikeSharpAdapter::ADMINS_PATH);
                self::recordState($serverId, CounterStrikeSharpAdapter::ADMINS_PATH, $content);
                self::importCssAdmins($serverId, $content);
            }
            if ($connector->exists(CounterStrikeSharpAdapter::GROUPS_PATH)) {
                $content = $connector->read(CounterStrikeSharpAdapter::GROUPS_PATH);
                self::recordState($serverId, CounterStrikeSharpAdapter::GROUPS_PATH, $content);
                self::importCssGroups($serverId, $content);
            }
        }

        self::reconcileLinkedAccounts($serverId);

        Audit::log('server.scan', 'server', (string)$serverId, [
            'framework' => $detected['framework'],
            'plugins' => count($detected['plugins']),
        ]);
        return $detected;
    }

    public static function preview(int $serverId): array
    {
        return self::generate($serverId, false);
    }

    public static function synchronize(int $serverId): array
    {
        return self::generate($serverId, true);
    }

    public static function restore(int $serverId, int $snapshotId): void
    {
        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT * FROM sync_snapshots WHERE id=? AND server_id=?');
        $stmt->execute([$snapshotId, $serverId]);
        $snapshot = $stmt->fetch();
        if (!$snapshot) throw new \RuntimeException('Backup nicht gefunden.');

        $server = self::server($serverId);
        $connector = ConnectorFactory::make($server);
        $current = $connector->exists($snapshot['remote_path']) ? $connector->read($snapshot['remote_path']) : '';
        self::backup($serverId, (string)$snapshot['framework'], (string)$snapshot['remote_path'], $current);
        $connector->write((string)$snapshot['remote_path'], (string)$snapshot['content']);
        $verify = $connector->read((string)$snapshot['remote_path']);
        if (hash('sha256', $verify) !== hash('sha256', (string)$snapshot['content'])) {
            throw new \RuntimeException('Wiederherstellung konnte nicht verifiziert werden.');
        }
        self::recordState($serverId, (string)$snapshot['remote_path'], $verify);
        Audit::log('server.restore', 'server', (string)$serverId, [
            'snapshot_id' => $snapshotId,
            'path' => $snapshot['remote_path'],
        ]);
    }

    private static function generate(int $serverId, bool $write): array
    {
        $server = self::server($serverId);
        if (empty($server['last_scan_at'])) {
            throw new \RuntimeException('Vor Vorschau oder Synchronisation muss der Server mindestens einmal gescannt werden.');
        }
        $connector = ConnectorFactory::make($server);
        $framework = $server['framework'] ?: FrameworkDetector::detect($connector)['framework'];
        $assignments = self::assignments($serverId);
        $managedSteamIds = self::managedSteamIds($serverId);
        $rolePermissions = self::rolePermissions($serverId);
        $changes = [];

        if (in_array($framework, ['swiftlys2', 'both'], true)) {
            $path = SwiftlyAdapter::PATH;
            if (!$connector->exists($path)) throw new \RuntimeException('SwiftlyS2 permissions.jsonc wurde nicht gefunden.');
            $old = $connector->read($path);
            self::assertNoExternalChange($serverId, $path, $old);
            $new = SwiftlyAdapter::build($old, $assignments, $rolePermissions, $managedSteamIds);
            $changes[] = self::change($serverId, 'swiftlys2', $path, $old, $new, $connector, $write);
        }

        if (in_array($framework, ['counterstrikesharp', 'both'], true)) {
            $adminsPath = CounterStrikeSharpAdapter::ADMINS_PATH;
            $groupsPath = CounterStrikeSharpAdapter::GROUPS_PATH;
            $oldAdmins = $connector->exists($adminsPath) ? $connector->read($adminsPath) : "{}\n";
            $oldGroups = $connector->exists($groupsPath) ? $connector->read($groupsPath) : "{}\n";
            self::assertNoExternalChange($serverId, $adminsPath, $oldAdmins);
            self::assertNoExternalChange($serverId, $groupsPath, $oldGroups);
            $newAdmins = CounterStrikeSharpAdapter::buildAdmins($oldAdmins, $assignments, $managedSteamIds);
            $newGroups = CounterStrikeSharpAdapter::buildGroups($oldGroups, $rolePermissions);
            $changes[] = self::change($serverId, 'counterstrikesharp', $adminsPath, $oldAdmins, $newAdmins, $connector, $write);
            $changes[] = self::change($serverId, 'counterstrikesharp', $groupsPath, $oldGroups, $newGroups, $connector, $write);
        }

        if ($framework === 'unknown' || !$changes) {
            throw new \RuntimeException('Kein unterstütztes Framework erkannt. Bitte Server zuerst scannen.');
        }

        if ($write) {
            Database::connection()->prepare('UPDATE game_servers SET last_sync_at=?, last_error=NULL, updated_at=? WHERE id=?')
                ->execute([date('c'), date('c'), $serverId]);
            Audit::log('server.sync', 'server', (string)$serverId, ['files' => array_column($changes, 'path')]);
        }
        return $changes;
    }

    private static function change(int $serverId, string $framework, string $path, string $old, string $new, ServerConnectorInterface $connector, bool $write): array
    {
        $changed = hash('sha256', $old) !== hash('sha256', $new);
        if ($write && $changed) {
            self::backup($serverId, $framework, $path, $old);
            $connector->write($path, $new);
            $verify = $connector->read($path);
            if (hash('sha256', $verify) !== hash('sha256', $new)) {
                throw new \RuntimeException('Verifikation nach Schreiben fehlgeschlagen: ' . $path);
            }
            self::recordState($serverId, $path, $verify);
        } elseif ($write) {
            self::recordState($serverId, $path, $old);
        }
        return [
            'framework' => $framework,
            'path' => $path,
            'changed' => $changed,
            'old' => $old,
            'new' => $new,
        ];
    }

    private static function assertNoExternalChange(int $serverId, string $path, string $current): void
    {
        $stmt = Database::connection()->prepare('SELECT content_hash FROM server_file_state WHERE server_id=? AND remote_path=?');
        $stmt->execute([$serverId, $path]);
        $expected = $stmt->fetchColumn();
        if ($expected !== false && !hash_equals((string)$expected, hash('sha256', $current))) {
            throw new \RuntimeException('Die Datei ' . $path . ' wurde seit dem letzten Scan/Sync außerhalb der Verwaltungssoftware geändert. Bitte den Server erneut scannen, die Änderungen prüfen und danach erneut synchronisieren.');
        }
    }

    private static function recordState(int $serverId, string $path, string $content): void
    {
        Database::connection()->prepare('INSERT INTO server_file_state(server_id,remote_path,content_hash,updated_at) VALUES(?,?,?,?) ON CONFLICT(server_id,remote_path) DO UPDATE SET content_hash=excluded.content_hash,updated_at=excluded.updated_at')
            ->execute([$serverId, $path, hash('sha256', $content), date('c')]);
    }

    private static function importSwiftly(int $serverId, string $content): void
    {
        $data = Jsonc::decode($content);
        $permissions = $data['Permissions'] ?? [];
        $players = is_array($permissions['Players'] ?? null) ? $permissions['Players'] : [];
        $groups = is_array($permissions['PermissionGroups'] ?? null) ? $permissions['PermissionGroups'] : [];
        $pdo = Database::connection();

        foreach (SwiftlyAdapter::ROLES as $role) {
            if (!array_key_exists($role, $groups) || !is_array($groups[$role])) continue;
            self::storeRolePermissions($serverId, $role, array_values(array_filter($groups[$role], 'is_string')));
        }

        foreach ($players as $steamId => $groupList) {
            if (!preg_match('/^7656119\d{10}$/', (string)$steamId) || !is_array($groupList)) continue;
            $role = null;
            foreach ($groupList as $group) {
                if (in_array($group, SwiftlyAdapter::ROLES, true)) {
                    $role = $group;
                    break;
                }
            }
            if ($role === null) continue;
            $playerId = self::ensurePlayer((string)$steamId);
            $pdo->prepare('INSERT OR IGNORE INTO server_managed_players(server_id,player_id,created_at) VALUES(?,?,?)')->execute([$serverId,$playerId,date('c')]);
            $pdo->prepare('INSERT INTO server_player_groups(server_id,player_id,role_name,updated_at) VALUES(?,?,?,?) ON CONFLICT(server_id,player_id) DO UPDATE SET role_name=excluded.role_name,updated_at=excluded.updated_at')
                ->execute([$serverId, $playerId, $role, date('c')]);
        }
    }

    private static function importCssGroups(int $serverId, string $content): void
    {
        $data = json_decode($content, true, 512, JSON_THROW_ON_ERROR);
        if (!is_array($data)) return;
        foreach (CounterStrikeSharpAdapter::ROLES as $role) {
            $key = '#serververwaltung/' . $role;
            if (!isset($data[$key]['flags']) || !is_array($data[$key]['flags'])) continue;
            $perms = [];
            foreach ($data[$key]['flags'] as $flag) {
                if (!is_string($flag)) continue;
                $perms[] = ltrim($flag, '@');
            }
            if ($role === 'founder' && !in_array('*', $perms, true)) $perms[] = '*';
            self::storeRolePermissions($serverId, $role, $perms);
        }
    }

    private static function importCssAdmins(int $serverId, string $content): void
    {
        $data = json_decode($content, true, 512, JSON_THROW_ON_ERROR);
        if (!is_array($data)) return;
        $pdo = Database::connection();
        foreach ($data as $name => $entry) {
            $steam = (string)($entry['identity'] ?? '');
            if (!preg_match('/^7656119\d{10}$/', $steam)) continue;
            $groups = is_array($entry['groups'] ?? null) ? $entry['groups'] : [];
            $role = null;
            foreach ($groups as $group) {
                if (!is_string($group) || !str_starts_with($group, '#serververwaltung/')) continue;
                $candidate = substr($group, strlen('#serververwaltung/'));
                if (in_array($candidate, CounterStrikeSharpAdapter::ROLES, true)) {
                    $role = $candidate;
                    break;
                }
            }
            if ($role === null) continue;
            $playerId = self::ensurePlayer($steam, (string)$name);
            $pdo->prepare('INSERT OR IGNORE INTO server_managed_players(server_id,player_id,created_at) VALUES(?,?,?)')->execute([$serverId,$playerId,date('c')]);
            $pdo->prepare('INSERT INTO server_player_groups(server_id,player_id,role_name,updated_at) VALUES(?,?,?,?) ON CONFLICT(server_id,player_id) DO UPDATE SET role_name=excluded.role_name,updated_at=excluded.updated_at')
                ->execute([$serverId, $playerId, $role, date('c')]);
        }
    }

    private static function ensurePlayer(string $steamId, ?string $name = null): int
    {
        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT id FROM game_players WHERE steam_id64=?');
        $stmt->execute([$steamId]);
        $id = $stmt->fetchColumn();
        if ($id !== false) return (int)$id;
        $display = trim((string)$name);
        if ($display === '') $display = 'Steam ' . substr($steamId, -6);
        $pdo->prepare('INSERT INTO game_players(display_name,steam_id64,active,created_at,updated_at) VALUES(?,?,1,?,?)')
            ->execute([$display, $steamId, date('c'), date('c')]);
        return (int)$pdo->lastInsertId();
    }


    private static function reconcileLinkedAccounts(int $serverId): void
    {
        $pdo = Database::connection();
        $mains = $pdo->query('SELECT DISTINCT parent.id,parent.community_role FROM game_players parent JOIN game_players child ON child.main_player_id=parent.id')->fetchAll();
        foreach ($mains as $main) {
            $mainId=(int)$main['id'];
            // Community-Rolle des Hauptaccounts ist immer maßgeblich.
            PlayerAccountService::propagateCommunityRole($mainId,(string)$main['community_role']);
            $stmt=$pdo->prepare('SELECT role_name FROM server_player_groups WHERE server_id=? AND player_id=?');
            $stmt->execute([$serverId,$mainId]);
            $role=$stmt->fetchColumn();
            if($role!==false){
                PlayerAccountService::propagateServerRole($mainId,$serverId,(string)$role);
                continue;
            }
            // Hat der Hauptaccount auf diesem Server keine verwaltete Rolle, dürfen seine Zweitaccounts keine abweichende Rolle behalten.
            foreach(PlayerAccountService::familyIds($mainId) as $familyId){
                if($familyId===$mainId)continue;
                $pdo->prepare('DELETE FROM server_player_groups WHERE server_id=? AND player_id=?')->execute([$serverId,$familyId]);
            }
        }
    }

    private static function storeRolePermissions(int $serverId, string $role, array $permissions): void
    {
        $permissions = array_values(array_unique(array_filter(array_map('trim', $permissions), fn($v) => $v !== '')));
        Database::connection()->prepare('INSERT INTO server_role_permissions(server_id,role_name,permissions_json,updated_at) VALUES(?,?,?,?) ON CONFLICT(server_id,role_name) DO UPDATE SET permissions_json=excluded.permissions_json,updated_at=excluded.updated_at')
            ->execute([$serverId, $role, json_encode($permissions, JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE), date('c')]);
    }

    private static function backup(int $serverId, string $framework, string $path, string $content): void
    {
        $hash = hash('sha256', $content);
        Database::connection()->prepare('INSERT INTO sync_snapshots(server_id,framework,remote_path,content_hash,content,created_at) VALUES(?,?,?,?,?,?)')
            ->execute([$serverId, $framework, $path, $hash, $content, date('c')]);
        $dir = dirname(__DIR__, 2) . '/storage/backups/' . $serverId . '/' . date('Ymd_His');
        if (!is_dir($dir)) mkdir($dir, 0770, true);
        $file = $dir . '/' . preg_replace('/[^A-Za-z0-9._-]+/', '_', basename($path));
        file_put_contents($file, $content, LOCK_EX);
        @chmod($file, 0660);
    }

    private static function server(int $id): array
    {
        $stmt = Database::connection()->prepare('SELECT * FROM game_servers WHERE id=?');
        $stmt->execute([$id]);
        $server = $stmt->fetch();
        if (!$server) throw new \RuntimeException('Server nicht gefunden.');
        return $server;
    }

    private static function assignments(int $id): array
    {
        $stmt = Database::connection()->prepare('SELECT gp.display_name,gp.steam_id64,spg.role_name FROM server_player_groups spg JOIN game_players gp ON gp.id=spg.player_id WHERE spg.server_id=? AND gp.active=1 ORDER BY gp.display_name');
        $stmt->execute([$id]);
        return $stmt->fetchAll();
    }

    private static function managedSteamIds(int $serverId): array
    {
        $stmt = Database::connection()->prepare('SELECT gp.steam_id64 FROM server_managed_players smp JOIN game_players gp ON gp.id=smp.player_id WHERE smp.server_id=?');
        $stmt->execute([$serverId]);
        return array_map('strval', $stmt->fetchAll(\PDO::FETCH_COLUMN));
    }

    private static function rolePermissions(int $id): array
    {
        $out = ['spieler'=>[], 'vip'=>[], 'member'=>[], 'admin'=>[], 'founder'=>['*']];
        $stmt = Database::connection()->prepare('SELECT role_name,permissions_json FROM server_role_permissions WHERE server_id=?');
        $stmt->execute([$id]);
        foreach ($stmt->fetchAll() as $row) {
            $value = json_decode($row['permissions_json'], true);
            $out[$row['role_name']] = is_array($value) ? $value : [];
        }
        $rules = Database::connection()->prepare('SELECT permission_key,min_role FROM plugin_permission_rules WHERE server_id=? ORDER BY id');
        $rules->execute([$id]);
        foreach ($rules->fetchAll() as $rule) {
            $role = (string)$rule['min_role'];
            $permission = trim((string)$rule['permission_key']);
            if (isset($out[$role]) && $permission !== '' && !in_array($permission, $out[$role], true)) {
                $out[$role][] = $permission;
            }
        }
        if (!in_array('*', $out['founder'], true)) $out['founder'][] = '*';
        return $out;
    }
}
