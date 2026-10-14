<?php
$root = dirname(__DIR__);

function ok196(bool $condition, string $label): void
{
    if (!$condition) {
        fwrite(STDERR, "FEHLER: {$label}\n");
        exit(1);
    }
    echo "OK  {$label}\n";
}

$controller = (string)file_get_contents($root . '/src/Controllers/DashboardController.php');
$template = (string)file_get_contents($root . '/templates/dashboard.php');
$routes = (string)file_get_contents($root . '/public/index.php');
$css = (string)file_get_contents($root . '/public/assets/app.css');

ok196(version_compare(trim((string)file_get_contents($root . '/VERSION')), '1.9.6', '>='), 'Webpanel-Version ist 1.9.6 oder neuer');
ok196(str_contains($routes, "'/dashboard/servers/{id}/power'") && str_contains($routes, 'dashboardPower'), 'Dashboard-Powerroute ist registriert');
ok196(str_contains($controller, 'public function dashboardPower') && str_contains($controller, "['start','restart','stop']"), 'Controller akzeptiert nur Start, Restart und Stop');
ok196(str_contains($controller, "['founder', 'admin']") && str_contains($controller, 'dashboardPowerRoleAllowed'), 'Dashboard-Schnelltasten sind auf Founder und Admin begrenzt');
ok196(str_contains($controller, "Auth::can('dashboard.view')") && str_contains($controller, 'Auth::canServer($serverId, \'live\')') && str_contains($controller, 'Auth::can($permission)') && str_contains($controller, 'PterodactylLiveService::available'), 'Dashboardrecht, Serverfreigabe, Einzelrecht und Pterodactyl werden serverseitig geprüft');
ok196(str_contains($controller, "'source' => 'dashboard-serverkarte'") && str_contains($controller, "Audit::log('server.power.'"), 'Poweraktionen erhalten einen nachvollziehbaren Audit-Eintrag');
ok196(str_contains($template, '$live[\'dashboard_power\']') && str_contains($template, 'dashboard-power-shortcuts'), 'Serverkarten enthalten die Power-Schnelltasten');
ok196(str_contains($template, 'value="start"') && str_contains($template, 'value="restart"') && str_contains($template, 'value="stop"'), 'Start, Restart und Stop sind als getrennte POST-Aktionen vorhanden');
ok196(str_contains($template, 'data-confirm="Server') && str_contains($template, 'Verbundene Spieler werden getrennt.'), 'Restart und Stop verlangen eine Bestätigung');
ok196(str_contains($css, '.dashboard-power-shortcuts') && str_contains($css, '.dashboard-tile-head-controls'), 'Kompaktes Layout der Titelaktionen ist vorhanden');

echo "\nv1.9.6 Dashboard-Power-Schnelltasten erfolgreich geprüft.\n";
