<?php
namespace App\Services;

/**
 * v1.8.0: Pterodactyl-Livezustand fuer die Web-Liveverwaltung.
 *
 * Die normale Swiftly/Fast-Live-Spielerabfrage bleibt davon getrennt. Ressourcen
 * werden nur bei geoeffneter Liveverwaltung und mit kurzer Dateicache-Sperre
 * abgefragt, damit mehrere Browser/PHP-Worker nicht gleichzeitig das Panel pollen.
 */
final class PterodactylLiveService
{
    private const CACHE_TTL_SECONDS = 4.0;
    private const STALE_TTL_SECONDS = 30.0;

    public static function available(int $serverId): bool
    {
        try {
            $server = LiveServerService::server($serverId);
            return strtolower((string)($server['connector_type'] ?? '')) === 'pterodactyl';
        } catch (\Throwable) {
            return false;
        }
    }

    public static function power(int $serverId, string $signal): void
    {
        $server = LiveServerService::server($serverId);
        $connector = ConnectorFactory::make($server);
        if (!$connector instanceof PterodactylConnector) {
            throw new \RuntimeException('Diese Serveraktion ist nur mit einem Pterodactyl-Connector verfuegbar.');
        }
        $connector->power($signal);
        self::invalidateCache($serverId);
    }

    public static function snapshot(int $serverId, bool $force = false): array
    {
        $server = LiveServerService::server($serverId);
        if (strtolower((string)($server['connector_type'] ?? '')) !== 'pterodactyl') {
            return self::unavailable('Kein Pterodactyl-Connector konfiguriert.');
        }

        if (!$force) {
            $fresh = self::readCache($serverId, self::CACHE_TTL_SECONDS);
            if ($fresh !== null) return $fresh;
        }

        $dir = self::cacheDirectory();
        if (!is_dir($dir) && !@mkdir($dir, 0770, true) && !is_dir($dir)) {
            return self::fetch($serverId, $server);
        }

        $lock = @fopen($dir . '/server-' . $serverId . '.lock', 'c');
        if ($lock === false) {
            $stale = self::readCache($serverId, self::STALE_TTL_SECONDS);
            return $stale !== null ? self::markStale($stale) : self::fetch($serverId, $server);
        }

        if (!@flock($lock, LOCK_EX | LOCK_NB)) {
            $stale = self::readCache($serverId, self::STALE_TTL_SECONDS);
            @fclose($lock);
            return $stale !== null
                ? self::markStale($stale)
                : self::unavailable('Pterodactyl-Ressourcen werden gerade durch einen anderen Request aktualisiert.');
        }

        try {
            if (!$force) {
                $fresh = self::readCache($serverId, self::CACHE_TTL_SECONDS);
                if ($fresh !== null) return $fresh;
            }
            $snapshot = self::fetch($serverId, $server);
            if (!empty($snapshot['available'])) self::writeCache($serverId, $snapshot);
            return $snapshot;
        } finally {
            @flock($lock, LOCK_UN);
            @fclose($lock);
        }
    }

    private static function fetch(int $serverId, array $server): array
    {
        try {
            $connector = ConnectorFactory::make($server);
            if (!$connector instanceof PterodactylConnector) {
                return self::unavailable('Kein Pterodactyl-Connector konfiguriert.');
            }

            $attributes = $connector->resources();

            // Limits ändern sich nur selten. Deshalb werden die langsameren
            // Serverdetails maximal alle 5 Minuten erneut abgefragt. So bleibt
            // das Live-Monitoring bei mehreren offenen Browsern leichtgewichtig.
            $previous = self::readCache($serverId, 3600.0);
            $limitsFetchedAt = trim((string)($previous['limits_fetched_at'] ?? ''));
            $limitsAge = $limitsFetchedAt !== '' ? time() - (strtotime($limitsFetchedAt) ?: 0) : PHP_INT_MAX;
            $memoryLimitBytes = (int)($previous['memory_limit_bytes'] ?? 0);
            $diskLimitBytes = (int)($previous['disk_limit_bytes'] ?? 0);
            $cpuLimit = (float)($previous['cpu_limit_percent'] ?? 0.0);

            if ($limitsAge > 300 || ($memoryLimitBytes <= 0 && $diskLimitBytes <= 0 && $cpuLimit <= 0)) {
                try {
                    $details = $connector->serverDetails();
                    $limits = is_array($details['limits'] ?? null) ? $details['limits'] : [];
                    $memoryLimitMb = max(0, (int)($limits['memory'] ?? 0));
                    $diskLimitMb = max(0, (int)($limits['disk'] ?? 0));
                    $cpuLimit = max(0.0, (float)($limits['cpu'] ?? 0.0));
                    $memoryLimitBytes = $memoryLimitMb > 0 ? $memoryLimitMb * 1024 * 1024 : 0;
                    $diskLimitBytes = $diskLimitMb > 0 ? $diskLimitMb * 1024 * 1024 : 0;
                    $limitsFetchedAt = date('c');
                } catch (\Throwable) {
                    // Ressourcen bleiben auch ohne Zugriff auf Serverdetails nutzbar.
                }
            }

            $resources = is_array($attributes['resources'] ?? null) ? $attributes['resources'] : [];

            $memoryBytes = max(0, (int)($resources['memory_bytes'] ?? 0));
            $diskBytes = max(0, (int)($resources['disk_bytes'] ?? 0));
            $cpu = max(0.0, (float)($resources['cpu_absolute'] ?? 0.0));
            $networkRx = max(0, (int)($resources['network_rx_bytes'] ?? 0));
            $networkTx = max(0, (int)($resources['network_tx_bytes'] ?? 0));
            $uptimeMs = max(0, (int)($resources['uptime'] ?? $attributes['uptime'] ?? 0));
            $uptimeSeconds = (int)floor($uptimeMs / 1000);
            $state = self::normalizeState((string)($attributes['current_state'] ?? 'unknown'));
            $uptimeHuman = $state === 'running' ? self::humanDuration($uptimeSeconds) : ($state === 'starting' ? 'startet gerade' : '—');

            return [
                'available' => true,
                'server_id' => $serverId,
                'state' => $state,
                'state_label' => self::stateLabel($state),
                'suspended' => !empty($attributes['is_suspended']),
                'cpu_percent' => round($cpu, 2),
                'cpu_limit_percent' => $cpuLimit > 0 ? round($cpuLimit, 2) : null,
                'limits_fetched_at' => $limitsFetchedAt !== '' ? $limitsFetchedAt : null,
                'cpu_of_limit_percent' => $cpuLimit > 0 ? round(min(999.0, ($cpu / $cpuLimit) * 100), 2) : null,
                'memory_bytes' => $memoryBytes,
                'memory_limit_bytes' => $memoryLimitBytes > 0 ? $memoryLimitBytes : null,
                'memory_percent' => $memoryLimitBytes > 0 ? round(min(999.0, ($memoryBytes / $memoryLimitBytes) * 100), 2) : null,
                'disk_bytes' => $diskBytes,
                'disk_limit_bytes' => $diskLimitBytes > 0 ? $diskLimitBytes : null,
                'disk_percent' => $diskLimitBytes > 0 ? round(min(999.0, ($diskBytes / $diskLimitBytes) * 100), 2) : null,
                'network_rx_bytes' => $networkRx,
                'network_tx_bytes' => $networkTx,
                'uptime_ms' => $uptimeMs,
                'uptime_seconds' => $uptimeSeconds,
                'uptime_human' => $uptimeHuman,
                'fetched_at' => date('c'),
                'stale' => false,
                'error' => null,
            ];
        } catch (\Throwable $e) {
            $stale = self::readCache($serverId, self::STALE_TTL_SECONDS);
            if ($stale !== null) {
                $stale = self::markStale($stale);
                $stale['error'] = 'Aktualisierung fehlgeschlagen: ' . $e->getMessage();
                return $stale;
            }
            return self::unavailable('Pterodactyl-Ressourcen konnten nicht gelesen werden: ' . $e->getMessage());
        }
    }

    private static function normalizeState(string $state): string
    {
        $state = strtolower(trim($state));
        return in_array($state, ['running','starting','stopping','offline'], true) ? $state : 'unknown';
    }

    private static function stateLabel(string $state): string
    {
        return match (self::normalizeState($state)) {
            'running' => 'LÄUFT',
            'starting' => 'STARTET',
            'stopping' => 'STOPPT',
            'offline' => 'AUS',
            default => 'UNBEKANNT',
        };
    }

    public static function humanDuration(int $seconds): string
    {
        $seconds = max(0, $seconds);
        $days = intdiv($seconds, 86400);
        $hours = intdiv($seconds % 86400, 3600);
        $minutes = intdiv($seconds % 3600, 60);
        $secs = $seconds % 60;
        $parts = [];
        if ($days > 0) $parts[] = $days . ' T';
        if ($hours > 0 || $days > 0) $parts[] = $hours . ' Std';
        if ($minutes > 0 || $hours > 0 || $days > 0) $parts[] = $minutes . ' Min';
        if ($days === 0 && $hours === 0) $parts[] = $secs . ' Sek';
        return implode(' ', $parts) ?: '0 Sek';
    }

    private static function unavailable(string $error): array
    {
        return [
            'available'=>false,'state'=>'unknown','state_label'=>'NICHT VERFUEGBAR','suspended'=>false,
            'cpu_percent'=>null,'cpu_limit_percent'=>null,'cpu_of_limit_percent'=>null,'limits_fetched_at'=>null,
            'memory_bytes'=>null,'memory_limit_bytes'=>null,'memory_percent'=>null,
            'disk_bytes'=>null,'disk_limit_bytes'=>null,'disk_percent'=>null,
            'network_rx_bytes'=>null,'network_tx_bytes'=>null,
            'uptime_ms'=>null,'uptime_seconds'=>null,'uptime_human'=>'—',
            'fetched_at'=>date('c'),'stale'=>false,'error'=>$error,
        ];
    }

    private static function cacheDirectory(): string
    {
        return dirname(__DIR__, 2) . '/storage/cache/pterodactyl-live';
    }

    private static function cachePath(int $serverId): string
    {
        return self::cacheDirectory() . '/server-' . $serverId . '.json';
    }

    private static function readCache(int $serverId, float $maxAge): ?array
    {
        $path = self::cachePath($serverId);
        if (!is_file($path)) return null;
        $mtime = @filemtime($path);
        if ($mtime === false || microtime(true) - (float)$mtime > $maxAge) return null;
        $raw = @file_get_contents($path);
        if (!is_string($raw) || $raw === '') return null;
        $decoded = json_decode($raw, true);
        return is_array($decoded) ? $decoded : null;
    }

    private static function writeCache(int $serverId, array $snapshot): void
    {
        $dir = self::cacheDirectory();
        if (!is_dir($dir) && !@mkdir($dir, 0770, true) && !is_dir($dir)) return;
        $path = self::cachePath($serverId);
        $tmp = $path . '.tmp-' . bin2hex(random_bytes(4));
        $json = json_encode($snapshot, JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
        if (!is_string($json)) return;
        if (@file_put_contents($tmp, $json, LOCK_EX) === false) return;
        @chmod($tmp, 0660);
        @rename($tmp, $path);
    }

    private static function markStale(array $snapshot): array
    {
        $snapshot['stale'] = true;
        return $snapshot;
    }

    private static function invalidateCache(int $serverId): void
    {
        @unlink(self::cachePath($serverId));
    }
}
