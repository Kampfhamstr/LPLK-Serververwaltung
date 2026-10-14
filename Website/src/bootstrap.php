<?php
spl_autoload_register(function (string $class): void {
    if (!str_starts_with($class, 'App\\')) return;
    $path = __DIR__ . '/' . str_replace('App\\', '', $class) . '.php';
    $path = str_replace('\\', '/', $path);
    if (is_file($path)) require $path;
});

use App\Core\Config;
use App\Core\Security;

Config::load(dirname(__DIR__) . '/config/app.php');

// v1.6.8: Die Bridge ruft den Fast-Live-Endpunkt alle ~2 Sekunden auf und
// benötigt keine Browser-Session. Ohne diese Ausnahme würde jeder signierte
// Bridge-Push unnötig eine neue PHP-Session/Sessiondatei erzeugen.
$requestPath = (string)(parse_url((string)($_SERVER['REQUEST_URI'] ?? ''), PHP_URL_PATH) ?? '');
if (!str_starts_with($requestPath, '/bridge-api/')) {
    Security::startSession();
}
