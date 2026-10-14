<?php
$root = dirname(__DIR__);
$controller = file_get_contents($root . '/src/Controllers/DashboardController.php');
$template = file_get_contents($root . '/templates/dashboard.php');
$js = file_get_contents($root . '/public/assets/app.js');
$css = file_get_contents($root . '/public/assets/app.css');
$index = file_get_contents($root . '/public/index.php');

function check_dashboard(bool $ok, string $message): void {
    if (!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

check_dashboard(str_contains($index, "/dashboard/live-status"), 'Dashboard Live-JSON-Route vorhanden');
check_dashboard(str_contains($controller, "LiveServerService::status"), 'Dashboard liest Bridge-Livestatus');
check_dashboard(str_contains($controller, "'Spectator' => []"), 'Zuschauer werden separat gruppiert');
check_dashboard(str_contains($controller, "'next_map'"), 'Nächste Map wird aus Bridge-Status bereitgestellt');
check_dashboard(str_contains($template, 'data-dashboard-live-root'), 'Live-Panel im Dashboard vorhanden');
check_dashboard(str_contains($template, 'Terrorists'), 'T-Teamspalte vorhanden');
check_dashboard(str_contains($template, 'Counter-Terrorists'), 'CT-Teamspalte vorhanden');
check_dashboard(str_contains($template, 'Zuschauer'), 'Zuschauerspalte vorhanden');
check_dashboard(str_contains($js, "setInterval(refresh,5000)"), 'Automatische 5-Sekunden-Aktualisierung vorhanden');
check_dashboard(str_contains($css, 'dashboard-server-grid'), 'Dashboard Live-CSS vorhanden');

echo "\nDashboard-Live-Test erfolgreich.\n";
