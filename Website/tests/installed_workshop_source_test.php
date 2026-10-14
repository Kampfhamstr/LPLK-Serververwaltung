<?php
$root = dirname(__DIR__);
$service = file_get_contents($root . '/src/Services/LiveServerService.php');
$controller = file_get_contents($root . '/src/Controllers/LiveController.php');
$template = file_get_contents($root . '/templates/server_live.php');

function ok2(bool $condition, string $message): void {
    if (!$condition) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok2(str_contains($service, "installed_workshop_maps"), "Bridge-Status ist Quelle der Workshopliste");
ok2(!str_contains(substr($service, strpos($service, 'public static function mapOptions'), 2500), "MapChooser"), "mapOptions liest keine MapChooser-Datei");
ok2(str_contains($controller, "'source_folder'=>\$sourceFolder"), "source_folder wird an die Bridge gesendet");
ok2(str_contains($template, "tatsächlich installierten Steam-Workshopmaps"), "UI kennzeichnet installierte Workshopmaps als Quelle");

echo "\nInstalled-Workshop-Source-Test erfolgreich.\n";
