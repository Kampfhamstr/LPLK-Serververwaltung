#!/usr/bin/env php
<?php
if (PHP_SAPI !== 'cli') exit(1);
$root = dirname(__DIR__);
require $root . '/src/bootstrap.php';

use App\Core\Database;
use App\Services\FastLiveService;

$arg = $argv[1] ?? '--all';
if ($arg === '--all') {
    $ids = array_map('intval', Database::connection()->query('SELECT id FROM game_servers ORDER BY id')->fetchAll(PDO::FETCH_COLUMN));
} elseif (ctype_digit((string)$arg) && (int)$arg > 0) {
    $ids = [(int)$arg];
} else {
    fwrite(STDERR, "Verwendung: php bin/provision-fastlive.php --all | <server-id>\n");
    exit(2);
}

if (!$ids) {
    echo "Keine Server gefunden.\n";
    exit(0);
}

$ok = 0;
$failed = 0;
foreach ($ids as $id) {
    try {
        $result = FastLiveService::provision($id);
        echo "OK Server #{$id}: {$result['path']} über {$result['connector']} bereitgestellt.\n";
        $ok++;
    } catch (Throwable $e) {
        fwrite(STDERR, "FEHLER Server #{$id}: {$e->getMessage()}\n");
        $failed++;
    }
}

echo "Fertig: {$ok} erfolgreich, {$failed} fehlgeschlagen.\n";
exit($failed > 0 ? 1 : 0);
