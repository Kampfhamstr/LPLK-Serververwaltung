<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Database;
use App\Core\View;
use App\Core\Security;
use App\Core\Crypto;
use App\Core\Audit;
use App\Services\SyncService;
use App\Services\SteamService;
use App\Services\FastLiveService;

final class ServersController
{
    public function index(): void
    {
        Auth::requirePermission('servers.view');
        $all = Database::connection()->query('SELECT * FROM game_servers ORDER BY name')->fetchAll();
        $servers = array_values(array_filter($all, fn($server) => Auth::canServer((int)$server['id'], 'view')));
        View::render('servers', ['title' => 'Server', 'servers' => $servers]);
    }

    public function createForm(): void
    {
        Auth::requirePermission('servers.manage');
        View::render('server_form', ['title' => 'Server hinzufügen']);
    }

    public function create(): void
    {
        Auth::requirePermission('servers.manage');
        Security::verifyCsrf();

        $type = (string)($_POST['connector_type'] ?? 'pterodactyl');
        if (!in_array($type, ['pterodactyl', 'sftp', 'ftps', 'ftp'], true)) $type = 'pterodactyl';
        $name = trim((string)($_POST['name'] ?? ''));
        $secret = (string)($type === 'pterodactyl' ? ($_POST['secret_ptero'] ?? '') : ($type === 'sftp' ? ($_POST['secret_sftp'] ?? '') : ($_POST['secret_ftp'] ?? '')));
        $baseUrl = rtrim(trim((string)($_POST['base_url'] ?? '')), '/');
        $identifier = trim((string)($_POST['server_identifier'] ?? ''));
        $host = trim((string)($_POST['host'] ?? ''));
        $port = (int)($_POST['port'] ?? 22);
        $username = trim((string)($_POST['username'] ?? ''));
        $rootPath = trim((string)($_POST['root_path'] ?? '/')) ?: '/';

        $invalid = $name === '' || $secret === '';
        $invalid = $invalid || ($type === 'pterodactyl' && ($baseUrl === '' || $identifier === ''));
        $invalid = $invalid || (in_array($type, ['sftp','ftps','ftp'], true) && ($host === '' || $username === '' || $port < 1 || $port > 65535));
        if ($invalid) {
            View::flash('error', 'Bitte alle für die gewählte Verbindung erforderlichen Felder ausfüllen.');
            header('Location: /servers/new');
            exit;
        }

        $pdo = Database::connection();
        $pdo->prepare('INSERT INTO game_servers(name,connector_type,base_url,server_identifier,host,port,username,secret_enc,root_path,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?)')
            ->execute([$name, $type, $baseUrl, $identifier, $host, $port, $username, Crypto::encrypt($secret), $rootPath, date('c'), date('c')]);
        $id = (int)$pdo->lastInsertId();
        FastLiveService::ensureEnabled($id);

        foreach (['spieler'=>[], 'vip'=>[], 'member'=>[], 'admin'=>['wl.admin'], 'founder'=>['*']] as $role => $perms) {
            $pdo->prepare('INSERT INTO server_role_permissions(server_id,role_name,permissions_json,updated_at) VALUES(?,?,?,?)')
                ->execute([$id, $role, json_encode($perms), date('c')]);
        }

        Audit::log('server.create', 'server', (string)$id, ['name' => $name, 'type' => $type]);
        View::flash('success', 'Server wurde angelegt. Fast Live ist standardmäßig aktiviert und wird beim ersten erfolgreichen Scan automatisch auf der Bridge bereitgestellt.');
        header('Location: /servers/' . $id);
    }

    public function view(string $id): void
    {
        Auth::requirePermission('servers.view');
        Auth::requireServer((int)$id, 'view');
        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT * FROM game_servers WHERE id=?');
        $stmt->execute([(int)$id]);
        $server = $stmt->fetch();
        if (!$server) {
            http_response_code(404);
            View::render('error', ['title' => 'Server nicht gefunden', 'message' => 'Der angeforderte Server existiert nicht.']);
            return;
        }

        $p = $pdo->prepare('SELECT * FROM detected_plugins WHERE server_id=? ORDER BY framework,plugin_name');
        $p->execute([(int)$id]);
        $r = $pdo->prepare('SELECT role_name,permissions_json FROM server_role_permissions WHERE server_id=?');
        $r->execute([(int)$id]);
        $roles = [];
        foreach ($r->fetchAll() as $row) $roles[$row['role_name']] = json_decode($row['permissions_json'], true) ?: [];
        $a = $pdo->prepare('SELECT gp.display_name,gp.steam_id64,spg.role_name FROM server_player_groups spg JOIN game_players gp ON gp.id=spg.player_id WHERE spg.server_id=? ORDER BY gp.display_name');
        $a->execute([(int)$id]);
        $b = $pdo->prepare('SELECT id,framework,remote_path,content_hash,created_at FROM sync_snapshots WHERE server_id=? ORDER BY id DESC LIMIT 20');
        $b->execute([(int)$id]);
        $pr = $pdo->prepare('SELECT * FROM plugin_permission_rules WHERE server_id=? ORDER BY plugin_name,permission_key');
        $pr->execute([(int)$id]);

        View::render('server_view', [
            'title' => $server['name'],
            'server' => $server,
            'plugins' => $p->fetchAll(),
            'roles' => $roles,
            'assignments' => $a->fetchAll(),
            'backups' => $b->fetchAll(),
            'pluginRules' => $pr->fetchAll(),
            'fastLive' => [
                'enabled' => FastLiveService::enabled((int)$id),
                'recent' => FastLiveService::isRecent((int)$id),
                'last_seen_at' => FastLiveService::lastSeenAt((int)$id),
                'config' => FastLiveService::enabled((int)$id) ? FastLiveService::configSnippet((int)$id) : null,
            ],
        ]);
    }

    public function updateConnection(string $id): void
    {
        Auth::requirePermission('servers.manage');
        Auth::requireServer((int)$id, 'permissions');
        Security::verifyCsrf();
        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT * FROM game_servers WHERE id=?');
        $stmt->execute([(int)$id]);
        $server = $stmt->fetch();
        if (!$server) throw new \RuntimeException('Server nicht gefunden.');

        $type = (string)($_POST['connector_type'] ?? $server['connector_type']);
        if (!in_array($type, ['pterodactyl', 'sftp', 'ftps', 'ftp'], true)) throw new \RuntimeException('Ungültiger Verbindungstyp.');
        $secret = (string)($type === 'pterodactyl' ? ($_POST['secret_ptero'] ?? '') : ($type === 'sftp' ? ($_POST['secret_sftp'] ?? '') : ($_POST['secret_ftp'] ?? '')));
        $secretEnc = $secret !== '' ? Crypto::encrypt($secret) : $server['secret_enc'];

        $pdo->prepare('UPDATE game_servers SET name=?,connector_type=?,base_url=?,server_identifier=?,host=?,port=?,username=?,secret_enc=?,root_path=?,framework=NULL,last_scan_at=NULL,updated_at=? WHERE id=?')
            ->execute([
                trim((string)($_POST['name'] ?? $server['name'])),
                $type,
                rtrim(trim((string)($_POST['base_url'] ?? '')), '/'),
                trim((string)($_POST['server_identifier'] ?? '')),
                trim((string)($_POST['host'] ?? '')),
                (int)($_POST['port'] ?? 22),
                trim((string)($_POST['username'] ?? '')),
                $secretEnc,
                trim((string)($_POST['root_path'] ?? '/')) ?: '/',
                date('c'),
                (int)$id,
            ]);
        $pdo->prepare('DELETE FROM server_file_state WHERE server_id=?')->execute([(int)$id]);
        Audit::log('server.connection.update', 'server', $id, ['type' => $type]);
        View::flash('success', 'Serververbindung wurde gespeichert. Bitte den Server erneut scannen.');
        header('Location: /servers/' . $id);
    }

    public function rotateFastLive(string $id): void
    {
        Auth::requirePermission('servers.manage');
        Auth::requireServer((int)$id, 'permissions');
        Security::verifyCsrf();
        $sid=(int)$id;
        try {
            FastLiveService::rotateSecret($sid);
            $result=FastLiveService::provision($sid);
            Audit::log('server.fast_live.rotate','server',(string)$sid,['connector'=>$result['connector']??'']);
            View::flash('success','Fast-Live-Schlüssel wurde erneuert und automatisch auf dem Gameserver bereitgestellt.');
        } catch (\Throwable $e) {
            View::flash('error','Fast Live wurde vorbereitet, konnte aber nicht automatisch auf den Gameserver geschrieben werden: '.$e->getMessage());
        }
        header('Location: /servers/'.$sid.'#fast-live');
    }

    public function provisionFastLive(string $id): void
    {
        Auth::requirePermission('servers.manage');
        Auth::requireServer((int)$id, 'permissions');
        Security::verifyCsrf();
        $sid=(int)$id;
        try {
            $result=FastLiveService::provision($sid);
            Audit::log('server.fast_live.provision','server',(string)$sid,['connector'=>$result['connector']??'','path'=>$result['path']??'']);
            View::flash('success','Fast Live wurde automatisch auf dem Gameserver bereitgestellt. Bridge v1.4.9 erkennt die Konfiguration ohne Neustart.');
        } catch (\Throwable $e) {
            View::flash('error','Automatische Fast-Live-Bereitstellung fehlgeschlagen: '.$e->getMessage());
        }
        header('Location: /servers/'.$sid.'#fast-live');
    }

    public function disableFastLive(string $id): void
    {
        Auth::requirePermission('servers.manage');
        Auth::requireServer((int)$id, 'permissions');
        Security::verifyCsrf();
        $sid=(int)$id;
        FastLiveService::disable($sid);
        Audit::log('server.fast_live.disable','server',(string)$sid);
        View::flash('success','Fast Live wurde deaktiviert. Live-Zugriffe verwenden wieder den Connector-Fallback.');
        header('Location: /servers/'.$sid.'#fast-live');
    }

    public function scan(string $id): void
    {
        Auth::requirePermission('servers.manage');
        Auth::requireServer((int)$id, 'permissions');
        Security::verifyCsrf();
        try {
            $result = SyncService::scan((int)$id);
            $fastLiveText = '';
            try {
                $provision = FastLiveService::provision((int)$id);
                $fastLiveText = ' Fast Live wurde automatisch über ' . ($provision['connector'] ?? 'den Server-Connector') . ' bereitgestellt.';
                Audit::log('server.fast_live.auto_provision','server',(string)(int)$id,['connector'=>$provision['connector']??'','path'=>$provision['path']??'']);
            } catch (\Throwable $fastLiveError) {
                $fastLiveText = ' Fast Live ist aktiviert, konnte beim Scan aber noch nicht automatisch bereitgestellt werden: ' . $fastLiveError->getMessage();
            }
            $steamText = '';
            if (Auth::can('players.steam.sync') && SteamService::hasApiKey()) {
                $stmt = Database::connection()->prepare('SELECT player_id FROM server_managed_players WHERE server_id=?');
                $stmt->execute([(int)$id]);
                $ids = array_map('intval', $stmt->fetchAll(\PDO::FETCH_COLUMN));
                if ($ids) {
                    $steam = SteamService::syncPlayers($ids);
                    $steamText = ' ' . $steam['updated'] . ' Steam-Profile aktualisiert.';
                }
            }
            View::flash('success', 'Scan abgeschlossen: ' . $result['framework'] . ', ' . count($result['plugins']) . ' Plugins erkannt. Vorhandene verwaltete Gruppen wurden importiert.' . $steamText . $fastLiveText);
        } catch (\Throwable $e) {
            Database::connection()->prepare('UPDATE game_servers SET last_error=?,updated_at=? WHERE id=?')->execute([$e->getMessage(), date('c'), (int)$id]);
            View::flash('error', 'Scan fehlgeschlagen: ' . $e->getMessage());
        }
        header('Location: /servers/' . $id);
    }

    public function permissions(string $id): void
    {
        Auth::requirePermission('servers.manage');
        Auth::requireServer((int)$id, 'permissions');
        Security::verifyCsrf();
        $pdo = Database::connection();
        $up = $pdo->prepare('INSERT INTO server_role_permissions(server_id,role_name,permissions_json,updated_at) VALUES(?,?,?,?) ON CONFLICT(server_id,role_name) DO UPDATE SET permissions_json=excluded.permissions_json,updated_at=excluded.updated_at');
        foreach (['spieler','vip','member','admin','founder'] as $role) {
            $lines = preg_split('/\r\n|\n|\r/', (string)($_POST['perm_' . $role] ?? ''));
            $perms = [];
            foreach ($lines as $line) {
                $value = trim($line);
                if ($value !== '' && !in_array($value, $perms, true)) $perms[] = $value;
            }
            if ($role === 'founder' && !in_array('*', $perms, true)) $perms[] = '*';
            $up->execute([(int)$id, $role, json_encode($perms, JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE), date('c')]);
        }
        Audit::log('server.permissions.update', 'server', $id);
        View::flash('success', 'Gruppenrechte wurden gespeichert.');
        header('Location: /servers/' . $id);
    }

    public function addPluginPermission(string $id): void
    {
        Auth::requirePermission('servers.manage');
        Auth::requireServer((int)$id, 'permissions');
        Security::verifyCsrf();
        $pluginId = (int)($_POST['plugin_id'] ?? 0);
        $permission = trim((string)($_POST['permission_key'] ?? ''));
        $role = (string)($_POST['min_role'] ?? 'admin');
        $pdo = Database::connection();
        $pluginStmt = $pdo->prepare('SELECT framework,plugin_name FROM detected_plugins WHERE id=? AND server_id=?');
        $pluginStmt->execute([$pluginId, (int)$id]);
        $detectedPlugin = $pluginStmt->fetch();
        if (!$detectedPlugin || $permission === '' || !in_array($role, ['spieler','vip','member','admin','founder'], true)) {
            View::flash('error', 'Plugin, Permission und Mindestgruppe vollständig angeben.');
            header('Location: /servers/' . $id);
            exit;
        }
        $plugin = (string)$detectedPlugin['plugin_name'];
        $framework = (string)$detectedPlugin['framework'];
        try {
            $pdo->prepare('INSERT INTO plugin_permission_rules(server_id,framework,plugin_name,permission_key,min_role,created_at,updated_at) VALUES(?,?,?,?,?,?,?) ON CONFLICT(server_id,framework,plugin_name,permission_key) DO UPDATE SET min_role=excluded.min_role,updated_at=excluded.updated_at')
                ->execute([(int)$id, $framework, $plugin, $permission, $role, date('c'), date('c')]);
            Audit::log('server.plugin_permission.save', 'server', $id, ['plugin'=>$plugin,'permission'=>$permission,'min_role'=>$role]);
            View::flash('success', 'Plugin-Permission wurde gespeichert.');
        } catch (\Throwable $e) {
            View::flash('error', 'Plugin-Permission konnte nicht gespeichert werden: ' . $e->getMessage());
        }
        header('Location: /servers/' . $id);
    }

    public function deletePluginPermission(string $id, string $rule): void
    {
        Auth::requirePermission('servers.manage');
        Auth::requireServer((int)$id, 'permissions');
        Security::verifyCsrf();
        Database::connection()->prepare('DELETE FROM plugin_permission_rules WHERE id=? AND server_id=?')->execute([(int)$rule, (int)$id]);
        Audit::log('server.plugin_permission.delete', 'server', $id, ['rule_id'=>(int)$rule]);
        View::flash('success', 'Plugin-Permission wurde entfernt.');
        header('Location: /servers/' . $id);
    }

    public function preview(string $id): void
    {
        Auth::requirePermission('servers.sync');
        Auth::requireServer((int)$id, 'sync');
        Security::verifyCsrf();
        try {
            $changes = SyncService::preview((int)$id);
            View::render('sync_preview', ['title' => 'Synchronisationsvorschau', 'server_id' => (int)$id, 'changes' => $changes]);
        } catch (\Throwable $e) {
            View::flash('error', 'Vorschau fehlgeschlagen: ' . $e->getMessage());
            header('Location: /servers/' . $id);
        }
    }

    public function sync(string $id): void
    {
        Auth::requirePermission('servers.sync');
        Auth::requireServer((int)$id, 'sync');
        Security::verifyCsrf();
        try {
            $changes = SyncService::synchronize((int)$id);
            $count = count(array_filter($changes, fn($c) => $c['changed']));
            View::flash('success', 'Synchronisation abgeschlossen. ' . $count . ' Datei(en) geändert, gesichert und verifiziert.');
        } catch (\Throwable $e) {
            Database::connection()->prepare('UPDATE game_servers SET last_error=?,updated_at=? WHERE id=?')->execute([$e->getMessage(), date('c'), (int)$id]);
            View::flash('error', 'Synchronisation fehlgeschlagen: ' . $e->getMessage());
        }
        header('Location: /servers/' . $id);
    }

    public function restore(string $id, string $snapshot): void
    {
        Auth::requirePermission('servers.sync');
        Auth::requireServer((int)$id, 'sync');
        Security::verifyCsrf();
        try {
            SyncService::restore((int)$id, (int)$snapshot);
            View::flash('success', 'Backup wurde wiederhergestellt und verifiziert. Vorher wurde zusätzlich ein Sicherungsstand der aktuellen Datei erstellt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Wiederherstellung fehlgeschlagen: ' . $e->getMessage());
        }
        header('Location: /servers/' . $id);
    }
}
