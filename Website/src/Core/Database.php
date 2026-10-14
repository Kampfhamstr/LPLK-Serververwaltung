<?php
namespace App\Core;

use PDO;

final class Database
{
    private static ?PDO $pdo = null;

    public static function connection(): PDO
    {
        if (self::$pdo) {
            return self::$pdo;
        }
        $driver = Config::get('database.driver', 'sqlite');
        if ($driver !== 'sqlite') {
            throw new \RuntimeException('Diese Version unterstützt standardmäßig SQLite.');
        }
        $path = (string) Config::get('database.path');
        $dir = dirname($path);
        if (!is_dir($dir)) {
            mkdir($dir, 0770, true);
        }
        self::$pdo = new PDO('sqlite:' . $path, null, null, [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES => false,
            // Ergänzt PRAGMA busy_timeout bereits auf PDO-Ebene.
            PDO::ATTR_TIMEOUT => 15,
        ]);

        // Busy-Timeout absichtlich VOR journal_mode setzen: Wenn mehrere Fast-Live-
        // Requests gleichzeitig eintreffen, wartet SQLite kurz auf einen laufenden
        // Writer, statt sofort mit "database is locked" abzubrechen.
        self::$pdo->exec('PRAGMA busy_timeout = 15000');
        self::$pdo->exec('PRAGMA foreign_keys = ON');
        self::$pdo->exec('PRAGMA journal_mode = WAL');

        // WAL + NORMAL ist für dieses Webpanel ein guter Kompromiss aus Haltbarkeit
        // und deutlich weniger Synchronisations-I/O bei den häufigen kurzen Pushes.
        self::$pdo->exec('PRAGMA synchronous = NORMAL');
        self::$pdo->exec('PRAGMA temp_store = MEMORY');
        self::$pdo->exec('PRAGMA wal_autocheckpoint = 1000');

        return self::$pdo;
    }

    /**
     * Führt kurze SQLite-Schreiboperationen bei SQLITE_BUSY/LOCKED erneut aus.
     * Damit führen parallele Live-/Chat-/Presence-Requests nicht sofort zu
     * einem sichtbaren "database is locked"-Fehler.
     */
    public static function withBusyRetry(callable $callback, int $attempts = 4): mixed
    {
        $attempts = max(1, min(8, $attempts));
        $delaysUs = [50000, 120000, 250000, 500000, 750000, 1000000, 1500000];

        for ($attempt = 0; ; $attempt++) {
            try {
                return $callback();
            } catch (\PDOException $e) {
                $message = strtolower($e->getMessage());
                $busy = str_contains($message, 'database is locked')
                    || str_contains($message, 'database table is locked')
                    || str_contains($message, 'database is busy')
                    || str_contains($message, 'sqlite_busy')
                    || str_contains($message, 'sqlite_locked');

                if (!$busy || $attempt + 1 >= $attempts) {
                    throw $e;
                }

                usleep($delaysUs[min($attempt, count($delaysUs) - 1)]);
            }
        }
    }
}
