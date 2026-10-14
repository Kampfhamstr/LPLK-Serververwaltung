<?php
namespace App\Services;

use App\Core\Audit;
use App\Core\Auth;
use App\Core\Database;
use App\Core\Jsonc;

final class LiveServerService
{
    public const BRIDGE_ROOT = 'game/csgo/addons/swiftlys2/data/serververwaltung_bridge';
    public const STATUS_PATH = self::BRIDGE_ROOT . '/status.json';
    public const COMMANDS_PATH = self::BRIDGE_ROOT . '/commands';
    public const RESULTS_PATH = self::BRIDGE_ROOT . '/results';

    public static function server(int $serverId): array
    {
        $stmt = Database::connection()->prepare('SELECT * FROM game_servers WHERE id=?');
        $stmt->execute([$serverId]);
        $server = $stmt->fetch();
        if (!$server) throw new \RuntimeException('Server nicht gefunden.');
        return $server;
    }

    private const STATUS_CACHE_TTL_SECONDS = 10.0;
    private const STATUS_CACHE_STALE_SECONDS = 60.0;

    public static function statusQuick(int $serverId): array
    {
        if (FastLiveService::enabled($serverId)) return self::status($serverId);
        $cached = self::readStatusCache($serverId, self::STATUS_CACHE_STALE_SECONDS);
        if ($cached !== null) return self::refreshCachedStatusAge($cached, true);
        $offline = self::offlineStatus(false, 'Schnellansicht: keine blockierende Remote-Abfrage. Fast Live aktivieren oder die Live-Verwaltung dieses Servers einmal direkt öffnen.');
        $offline['quick_status'] = true;
        return $offline;
    }

    public static function status(int $serverId): array
    {
        // v1.6.8: Sobald Fast Live für einen Server aktiviert ist, wird im normalen
        // Web-Request niemals mehr synchron über Pterodactyl nach status.json gefragt.
        // Dadurch kann ein langsames/ausgefallenes Panel das Webpanel nicht mehr in 504 laufen lassen.
        if (FastLiveService::enabled($serverId)) {
            $fast = FastLiveService::readStatus($serverId, 60);
            if ($fast !== null) {
                return self::prepareStatus($serverId, $fast, true);
            }
            $offline = self::offlineStatus(true, 'Fast Live ist aktiviert, aber die Bridge hat noch keinen Status an das Webpanel gesendet.');
            $offline['fast_live'] = true;
            return $offline;
        }

        $fresh = self::readStatusCache($serverId, self::STATUS_CACHE_TTL_SECONDS);
        if ($fresh !== null) {
            return self::refreshCachedStatusAge($fresh, false);
        }

        $dir = self::statusCacheDirectory();
        if (!is_dir($dir) && !@mkdir($dir, 0770, true) && !is_dir($dir)) {
            // Cache darf Live nicht blockieren.
            return self::fetchStatusUncached($serverId);
        }

        $lockPath = $dir . '/server-' . $serverId . '.lock';
        $lock = @fopen($lockPath, 'c');

        if ($lock === false) {
            $stale = self::readStatusCache($serverId, self::STATUS_CACHE_STALE_SECONDS);
            return $stale !== null
                ? self::refreshCachedStatusAge($stale, true)
                : self::fetchStatusUncached($serverId);
        }

        if (!@flock($lock, LOCK_EX | LOCK_NB)) {
            // Ein anderer PHP-Worker aktualisiert gerade denselben Gameserver.
            // Nicht ebenfalls Pterodactyl belasten, sondern sofort den letzten
            // brauchbaren Status liefern.
            $stale = self::readStatusCache($serverId, self::STATUS_CACHE_STALE_SECONDS);
            @fclose($lock);

            if ($stale !== null) {
                return self::refreshCachedStatusAge($stale, true);
            }

            return self::offlineStatus(
                false,
                'Live-Status wird gerade durch einen anderen Webpanel-Request aktualisiert.'
            );
        }

        try {
            // Nach Lock erneut prüfen: Ein anderer Worker könnte unmittelbar
            // vor uns aktualisiert haben.
            $fresh = self::readStatusCache($serverId, self::STATUS_CACHE_TTL_SECONDS);
            if ($fresh !== null) {
                return self::refreshCachedStatusAge($fresh, false);
            }

            $status = self::fetchStatusUncached($serverId);
            self::writeStatusCache($serverId, $status);
            return $status;
        } finally {
            @flock($lock, LOCK_UN);
            @fclose($lock);
        }
    }

    private static function statusCacheDirectory(): string
    {
        return dirname(__DIR__, 2) . '/storage/cache/live-status';
    }

    private static function statusCachePath(int $serverId): string
    {
        return self::statusCacheDirectory() . '/server-' . $serverId . '.json';
    }

    private static function readStatusCache(int $serverId, float $maxAgeSeconds): ?array
    {
        $path = self::statusCachePath($serverId);
        if (!is_file($path)) return null;

        $mtime = @filemtime($path);
        if (!$mtime || (microtime(true) - $mtime) > $maxAgeSeconds) return null;

        try {
            $decoded = json_decode((string)@file_get_contents($path), true, 512, JSON_THROW_ON_ERROR);
            if (!is_array($decoded) || !is_array($decoded['status'] ?? null)) return null;
            return $decoded['status'];
        } catch (\Throwable) {
            return null;
        }
    }

    private static function writeStatusCache(int $serverId, array $status): void
    {
        try {
            $dir = self::statusCacheDirectory();
            if (!is_dir($dir) && !@mkdir($dir, 0770, true) && !is_dir($dir)) return;

            $path = self::statusCachePath($serverId);
            $tmp = $path . '.' . bin2hex(random_bytes(4)) . '.tmp';
            $json = json_encode(
                ['cached_at'=>microtime(true),'status'=>$status],
                JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR
            );
            if (@file_put_contents($tmp, $json, LOCK_EX) === false) return;
            @rename($tmp, $path);
            @chmod($path, 0660);
        } catch (\Throwable) {
            // Cachefehler dürfen Live nicht beeinträchtigen.
        }
    }

    private static function refreshCachedStatusAge(array $status, bool $sharedStale): array
    {
        $updated = strtotime((string)($status['updated_at'] ?? '')) ?: 0;
        $age = $updated > 0 ? max(0, time() - $updated) : PHP_INT_MAX;

        if (!empty($status['bridge_available'])) {
            $status['bridge_online'] = $age <= 12;
            $status['status_age_seconds'] = $age === PHP_INT_MAX ? null : $age;

            if (!$status['bridge_online']) {
                $status['reason'] = $updated > 0
                    ? 'SerververwaltungBridge erkannt, aber Status ist ' . $age . ' Sekunden alt.'
                    : 'SerververwaltungBridge erkannt, aber status.json enthält keinen gültigen Zeitstempel.';
            } else {
                $status['reason'] = null;
            }
        }

        $status['served_from_cache'] = true;
        $status['shared_stale_cache'] = $sharedStale;
        return $status;
    }

    private static function fetchStatusUncached(int $serverId): array
    {
        $server = self::server($serverId);

        // v1.4.3:
        // Die LiveBridge wird direkt über ihre status.json erkannt.
        // Ein vorheriger Framework-Scan ist keine Voraussetzung mehr.
        try {
            $connector = ConnectorFactory::make($server);
        } catch (\Throwable $e) {
            return self::offlineStatus(
                false,
                'Serververbindung konnte nicht initialisiert werden: ' . $e->getMessage()
            );
        }

        $detectedFramework = self::frameworkWithSwiftly((string)($server['framework'] ?? ''));

        try {
            $raw = $connector->read(self::STATUS_PATH);
            $status = json_decode($raw, true, 512, JSON_THROW_ON_ERROR);
            if (!is_array($status)) throw new \RuntimeException('Ungültige Statusdatei.');

            // Erst nach erfolgreichem Lesen ist die Bridge tatsächlich erkannt.
            if ($detectedFramework !== (string)($server['framework'] ?? '')) {
                try {
                    Database::connection()->prepare(
                        'UPDATE game_servers SET framework=?,updated_at=? WHERE id=?'
                    )->execute([$detectedFramework, date('c'), $serverId]);

                    Audit::log('live.bridge_autodetected', 'server', (string)$serverId, [
                        'old_framework' => (string)($server['framework'] ?? ''),
                        'new_framework' => $detectedFramework,
                        'status_path' => self::STATUS_PATH,
                    ]);
                } catch (\Throwable) {
                    // Ein kosmetisches Framework-Update darf Live nicht blockieren.
                }
            }

            $status['framework_detected'] = $detectedFramework;
            $status['bridge_autodetected'] = !in_array(
                (string)($server['framework'] ?? ''),
                ['swiftlys2', 'both'],
                true
            );
            return self::prepareStatus($serverId, $status, false);
        } catch (\Throwable $e) {
            $message = $e->getMessage();
            $missing = str_contains($message, 'HTTP 404')
                || str_contains(strtolower($message), 'nicht gefunden')
                || str_contains(strtolower($message), 'not found');

            $offline = self::offlineStatus(
                !$missing,
                $missing
                    ? 'SerververwaltungBridge wurde unter ' . self::STATUS_PATH . ' nicht erkannt.'
                    : 'Bridge-Status konnte nicht gelesen werden: ' . $message
            );
            $offline['framework_detected'] = $detectedFramework;
            return $offline;
        }
    }

    private static function prepareStatus(int $serverId, array $status, bool $fastLive): array
    {
        $updated = strtotime((string)($status['updated_at'] ?? '')) ?: 0;
        $age = $updated > 0 ? max(0, time() - $updated) : PHP_INT_MAX;
        $status['bridge_available'] = true;
        $status['bridge_online'] = $age <= 12;
        $status['status_age_seconds'] = $age === PHP_INT_MAX ? null : $age;
        $status['fast_live'] = $fastLive;
        $status['players'] = is_array($status['players'] ?? null) ? $status['players'] : [];
        $status['bans'] = is_array($status['bans'] ?? null) ? $status['bans'] : [];
        $status['mutes'] = is_array($status['mutes'] ?? null) ? $status['mutes'] : [];
        $status['gags'] = is_array($status['gags'] ?? null) ? $status['gags'] : [];
        $status['chat_messages'] = is_array($status['chat_messages'] ?? null) ? $status['chat_messages'] : [];
        if (!$status['bridge_online']) {
            $status['reason'] = $updated > 0
                ? 'SerververwaltungBridge erkannt, aber Status ist ' . $age . ' Sekunden alt.'
                : 'SerververwaltungBridge erkannt, aber Status enthält keinen gültigen Zeitstempel.';
        } else {
            $status['reason'] = null;
        }
        self::adoptUnknownPlayers($serverId, $status['players']);
        ServerChatService::ingest($serverId, $status['chat_messages']);
        $status['players'] = self::sortPlayersAlphabetically(self::enrichPlayers($status['players']));
        $status['bans'] = self::enrichBans($status['bans']);
        $status['mutes'] = self::enrichBans($status['mutes']);
        $status['gags'] = self::enrichBans($status['gags']);
        return $status;
    }

    /**
     * v1.7.8: Eine stabile A-Z-Reihenfolge verhindert, dass Spieler beim Tod
     * oder Teamwechsel in der Live-Verwaltung springen. Lebend/Tot beeinflusst
     * nur die Darstellung, niemals die Sortierung.
     */
    private static function sortPlayersAlphabetically(array $players): array
    {
        usort($players, static function (array $a, array $b): int {
            $nameA = trim((string)($a['name'] ?? $a['display_name'] ?? $a['steam_id64'] ?? ''));
            $nameB = trim((string)($b['name'] ?? $b['display_name'] ?? $b['steam_id64'] ?? ''));
            $byName = strnatcasecmp($nameA, $nameB);
            if ($byName !== 0) return $byName;
            return strcmp((string)($a['steam_id64'] ?? ''), (string)($b['steam_id64'] ?? ''));
        });
        return array_values($players);
    }

    private static function frameworkWithSwiftly(string $framework): string
    {
        $framework = strtolower(trim($framework));

        return match ($framework) {
            'both' => 'both',
            'counterstrikesharp' => 'both',
            default => 'swiftlys2',
        };
    }

    private static function offlineStatus(bool $available, string $reason): array
    {
        return [
            'bridge_available' => $available,
            'bridge_online' => false,
            'reason' => $reason,
            'players' => [],
            'bans' => [],
            'mutes' => [],
            'gags' => [],
        ];
    }

    public static function player(int $serverId, string $steamId64): ?array
    {
        foreach (self::status($serverId)['players'] ?? [] as $player) {
            if ((string)($player['steam_id64'] ?? '') === $steamId64) return $player;
        }
        return null;
    }

    public static function newCommandId(): string
    {
        $now = microtime(true);
        $seconds = (int)floor($now);
        $micros = (int)floor(($now - $seconds) * 1000000);

        return gmdate('YmdHis', $seconds)
            . sprintf('%06d', $micros)
            . '-'
            . bin2hex(random_bytes(8));
    }

    public static function adminActionNotificationPolicy(): array
    {
        $stmt = Database::connection()->query(
            "SELECT setting_key,setting_value
             FROM app_settings
             WHERE setting_key IN (
                'live.admin_action_notify.scope',
                'live.admin_action_notify.always_target'
             )"
        );
        $values = [];
        foreach ($stmt->fetchAll() as $row) {
            $values[(string)$row['setting_key']] = (string)($row['setting_value'] ?? '');
        }
        $scope = strtolower(trim((string)($values['live.admin_action_notify.scope'] ?? 'admins')));
        if (!in_array($scope, ['admins','all'], true)) $scope = 'admins';
        $alwaysTarget = ((string)($values['live.admin_action_notify.always_target'] ?? '1')) !== '0';
        return ['scope'=>$scope,'always_target'=>$alwaysTarget];
    }

    public static function queue(int $serverId, string $action, array $payload = [], ?string $targetSteamId = null): string
    {
        $allowed = [
            'broadcast','private_message','kick','ban','unban','mute','unmute','gag','ungag','silence','unsilence',
            'slay','freeze','respawn','health','armor','team','noclip','goto_player','bring_player','teleport_player','separate_players',
            'give_weapon','give_weapon_group','remove_weapons','nickname_set','nickname_restore','server_command','cvar_set',
            'map_change','round_restart','workshop_rescan','plugin_update','plugin_rollback','plugin_rescan',
            'bot_kick','bot_add','bot_quota','bot_stop','bot_difficulty','bot_player_kick','bot_player_slay','bot_player_team',
            'server_rules','weapon_mode','headshot_only','rules_reset','preset_apply','round_control','warmup_control','scope_action',
            'player_speed','player_gravity','godmode',
        ];
        if (!in_array($action, $allowed, true)) throw new \InvalidArgumentException('Nicht unterstützte Live-Aktion.');

        $server = self::server($serverId);
        $status = self::status($serverId);
        if (empty($status['bridge_online'])) {
            throw new \RuntimeException((string)($status['reason'] ?? 'SerververwaltungBridge ist aktuell nicht online.'));
        }
        $id = self::newCommandId();
        $requester = Auth::user();
        $requesterName = trim((string)($requester['steam_personaname'] ?? ''));
        if ($requesterName === '') $requesterName = trim((string)($requester['name'] ?? ''));
        if ($requesterName === '') $requesterName = 'Webpanel/System';
        $requesterRole = strtolower(trim((string)($requester['web_role'] ?? 'system')));
        $notificationPolicy = self::adminActionNotificationPolicy();

        $command = [
            'id' => $id,
            'action' => $action,
            'target_steamid' => $targetSteamId,
            'requested_by' => $requesterName,
            'requested_by_role' => $requesterRole,
            'notify_scope' => $notificationPolicy['scope'],
            'notify_target' => $notificationPolicy['always_target'],
            // BridgeCommand.Payload ist ein Dictionary<string, JsonElement>.
            // Ein leeres PHP-Array würde als [] serialisiert; die Bridge erwartet {}.
            'payload' => $payload === [] ? new \stdClass() : $payload,
            'requested_at' => gmdate('c'),
        ];
        if (FastLiveService::enabled($serverId)) {
            // Die lokale Queue ist absichtlich unabhängig davon, ob die Bridge in genau
            // diesem Moment online ist. Fällt sie kurz aus, bleibt der Befehl erhalten
            // und wird beim nächsten signierten Exchange zugestellt – ohne Webrequest
            // auf Pterodactyl zu blockieren.
            return FastLiveService::queueCommand($serverId, $command);
        }
        $connector = ConnectorFactory::make($server);
        $connector->write(self::COMMANDS_PATH . '/' . $id . '.json', json_encode($command, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . "\n");
        return $id;
    }

    public static function result(int $serverId, string $commandId): ?array
    {
        if (!preg_match('/^[A-Za-z0-9_-]{1,100}$/', $commandId)) return null;
        $fast = FastLiveService::result($serverId, $commandId);
        if ($fast !== null) return $fast;
        // Bei aktiviertem Fast Live niemals auf den langsamen Pterodactyl-Dateipfad
        // zurückfallen, solange das Ergebnis noch aussteht. Sonst könnte bereits
        // das Polling eines laufenden Befehls wieder einen 504 provozieren.
        if (FastLiveService::enabled($serverId)) return null;

        $server=self::server($serverId);
        $connector=ConnectorFactory::make($server);
        $path=self::RESULTS_PATH.'/'.$commandId.'.json';

        try {
            // Bekannter Result-Pfad: direkt lesen. Ein vorheriges exists()
            // würde bei Pterodactyl einen zusätzlichen files/list-Request erzeugen.
            $data=json_decode($connector->read($path),true);
            return is_array($data)?$data:null;
        } catch (\Throwable $e) {
            $message=$e->getMessage();
            $missing=str_contains($message,'HTTP 404')
                || str_contains(strtolower($message),'nicht gefunden')
                || str_contains(strtolower($message),'not found');

            if ($missing) return null;
            throw $e;
        }
    }

    public static function restart(int $serverId, int $seconds, string $message, string $messageType = 'center'): string
    {
        $seconds = max(0, min(30, $seconds));
        $server = self::server($serverId);
        $connector = ConnectorFactory::make($server);
        if (!$connector instanceof PterodactylConnector) {
            throw new \RuntimeException('Ein vollständiger Serverneustart ist derzeit nur bei Pterodactyl-Verbindungen verfügbar.');
        }

        if ($seconds > 0) {
            self::queue($serverId, 'broadcast', [
                'message' => $message,
                'type' => $messageType,
                'countdown_seconds' => $seconds,
                'countdown_kind' => 'restart',
            ]);
            // Das Bridge-Plugin kündigt den Countdown an; nach Ablauf löst das Webpanel den Pterodactyl-Restart aus.
            // Kleine Toleranz, damit auch die letzte Countdown-Meldung sicher vor dem Restart verarbeitet wird.
            @set_time_limit(max(30, $seconds + 15));
            usleep(($seconds * 1000000) + 350000);
        }
        $connector->power('restart');
        return 'restart-' . gmdate('YmdHis');
    }

    /**
     * Liefert ausschließlich die von SerververwaltungBridge lokal erkannten,
     * tatsächlich installierten Workshopmaps. MapChooser wird absichtlich nicht
     * als Quelle verwendet.
     *
     * @return array<string,array{id:string,title:string,source_folder:string,label:string}>
     */
    public static function installedWorkshopMaps(int $serverId): array
    {
        $status = self::status($serverId);
        $rawMaps = $status['installed_workshop_maps'] ?? [];
        if (!is_array($rawMaps)) return [];

        $out = [];
        foreach ($rawMaps as $item) {
            if (!is_array($item)) continue;

            $id = trim((string)($item['id'] ?? ''));
            if (!preg_match('/^[0-9]{6,20}$/', $id)) continue;

            $title = trim((string)($item['title'] ?? ''));
            $sourceFolder = trim((string)($item['source_folder'] ?? ''));

            if ($title === '') {
                $title = $sourceFolder !== '' ? $sourceFolder : 'Workshop ' . $id;
            }

            $out[$id] = [
                'id' => $id,
                'title' => $title,
                'source_folder' => $sourceFolder,
                'label' => $title . ' · ' . $id,
            ];
        }

        uasort($out, static fn(array $a, array $b): int =>
            strnatcasecmp((string)$a['title'], (string)$b['title']));

        return $out;
    }

    public static function mapOptions(int $serverId): array
    {
        $out = [];
        foreach (self::installedWorkshopMaps($serverId) as $id => $map) {
            $out[$id] = (string)$map['label'];
        }
        return $out;
    }

    public static function workshopPath(int $serverId): string
    {
        $status = self::status($serverId);
        return trim((string)($status['workshop_path'] ?? ''));
    }



    private static function adoptUnknownPlayers(int $serverId, array $players): void
    {
        $steamIds = [];
        $liveNames = [];

        foreach ($players as $player) {
            if (!is_array($player) || !empty($player['is_bot'])) continue;
            $steam = trim((string)($player['steam_id64'] ?? ''));
            if (!preg_match('/^7656119\d{10}$/', $steam)) continue;
            $steamIds[$steam] = $steam;
            $liveNames[$steam] = trim((string)($player['original_name'] ?? $player['name'] ?? ''));
        }

        if (!$steamIds) return;

        $pdo = Database::connection();
        $marks = implode(',', array_fill(0, count($steamIds), '?'));
        $knownStmt = $pdo->prepare('SELECT id,steam_id64 FROM game_players WHERE steam_id64 IN (' . $marks . ')');
        $knownStmt->execute(array_values($steamIds));

        $known = [];
        foreach ($knownStmt->fetchAll() as $row) {
            $known[(string)$row['steam_id64']] = (int)$row['id'];
        }

        $newIds = [];
        $createdSteam = [];

        $pdo->beginTransaction();
        try {
            $insertPlayer = $pdo->prepare(
                'INSERT OR IGNORE INTO game_players(display_name,steam_id64,community_role,status,active,created_at,updated_at) ' .
                "VALUES(?,?,'spieler','active',1,?,?)"
            );
            $findPlayer = $pdo->prepare('SELECT id FROM game_players WHERE steam_id64=?');
            $managed = $pdo->prepare(
                'INSERT OR IGNORE INTO server_managed_players(server_id,player_id,created_at) VALUES(?,?,?)'
            );
            $group = $pdo->prepare(
                "INSERT OR IGNORE INTO server_player_groups(server_id,player_id,role_name,updated_at) VALUES(?,?,'spieler',?)"
            );

            foreach ($steamIds as $steam) {
                $playerId = $known[$steam] ?? 0;
                $wasCreated = false;

                if ($playerId <= 0) {
                    $name = $liveNames[$steam] !== '' ? $liveNames[$steam] : ('Steam ' . substr($steam, -6));
                    $now = date('c');
                    $insertPlayer->execute([$name, $steam, $now, $now]);
                    $wasCreated = $insertPlayer->rowCount() > 0;
                    $findPlayer->execute([$steam]);
                    $playerId = (int)($findPlayer->fetchColumn() ?: 0);
                }

                if ($playerId <= 0) continue;

                $managed->execute([$serverId, $playerId, date('c')]);

                // Nur bei einer wirklich neuen SteamID initial die Serverrolle Spieler
                // setzen. Bereits gepflegte Rollen werden niemals heruntergestuft.
                if ($wasCreated) {
                    $group->execute([$serverId, $playerId, date('c')]);
                    $newIds[] = $playerId;
                    $createdSteam[] = $steam;
                }
            }

            $pdo->commit();
        } catch (\Throwable $e) {
            if ($pdo->inTransaction()) $pdo->rollBack();
            throw $e;
        }

        if ($newIds) {
            Audit::log('live.player_auto_discovered', 'server', (string)$serverId, [
                'created' => count($newIds),
                'steam_ids' => $createdSteam,
                'default_role' => 'spieler',
            ]);

            // Best effort: neue Live-Spieler sofort mit Steam-Profilname/Avatar ergänzen.
            if (SteamService::hasApiKey()) {
                try { SteamService::syncPlayers($newIds); } catch (\Throwable) { }
            }
        }
    }

    private static function enrichPlayers(array $players): array
    {
        $steamIds = array_values(array_unique(array_filter(
            array_map(static fn(array $p): string => (string)($p['steam_id64'] ?? ''), $players),
            static fn(string $steamId): bool => preg_match('/^7656119\d{10}$/', $steamId) === 1
        )));
        if (!$steamIds) return $players;
        $marks = implode(',', array_fill(0, count($steamIds), '?'));
        $stmt = Database::connection()->prepare(
            'SELECT gp.id, gp.steam_id64, gp.display_name, gp.community_role, gp.main_player_id, ' .
            'main.display_name AS main_display_name, main.community_role AS main_community_role ' .
            'FROM game_players gp LEFT JOIN game_players main ON main.id=gp.main_player_id WHERE gp.steam_id64 IN (' . $marks . ')'
        );
        $stmt->execute($steamIds);
        $known = [];
        foreach ($stmt->fetchAll() as $row) $known[(string)$row['steam_id64']] = $row;
        foreach ($players as &$player) {
            $row = $known[(string)($player['steam_id64'] ?? '')] ?? null;
            if (!$row) continue;
            $player['person_id'] = (int)$row['id'];
            $player['community_role'] = (string)($row['main_community_role'] ?: $row['community_role'] ?: 'spieler');
            $player['community_display_name'] = (string)($row['main_display_name'] ?: $row['display_name'] ?: '');
            $player['is_secondary_account'] = !empty($row['main_player_id']);
        }
        unset($player);
        return $players;
    }

    private static function enrichBans(array $bans): array
    {
        $steamIds = array_values(array_unique(array_filter(array_map(static fn(array $b): string => (string)($b['steam_id64'] ?? ''), $bans))));
        if (!$steamIds) return $bans;
        $marks = implode(',', array_fill(0, count($steamIds), '?'));
        $stmt = Database::connection()->prepare('SELECT steam_id64, display_name FROM game_players WHERE steam_id64 IN (' . $marks . ')');
        $stmt->execute($steamIds);
        $names = [];
        foreach ($stmt->fetchAll() as $row) $names[(string)$row['steam_id64']] = (string)$row['display_name'];
        foreach ($bans as &$ban) $ban['display_name'] = $names[(string)($ban['steam_id64'] ?? '')] ?? null;
        unset($ban);
        return $bans;
    }

    public static function localPerson(string $steamId64): ?array
    {
        $stmt = Database::connection()->prepare('SELECT gp.*, main.display_name AS main_display_name FROM game_players gp LEFT JOIN game_players main ON main.id=gp.main_player_id WHERE gp.steam_id64=?');
        $stmt->execute([$steamId64]);
        $row = $stmt->fetch();
        return $row ?: null;
    }
}
