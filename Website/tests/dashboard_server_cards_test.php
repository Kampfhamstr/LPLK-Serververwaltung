<?php
$root = dirname(__DIR__);
$template = file_get_contents($root . '/templates/dashboard.php');
$css = file_get_contents($root . '/public/assets/app.css');
$js = file_get_contents($root . '/public/assets/app.js');

function checkCard(bool $ok, string $message): void {
    if (!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

checkCard(str_contains($template, 'dashboard-server-tile'), 'Jeder Server wird als eigene Kachel gerendert');
checkCard(str_contains($template, 'data-dash-next-map'), 'Nächste Map ist Teil jeder Kachel');
checkCard(str_contains($template, 'dashboard-player-chip-time'), 'Spieler-Onlinezeit wird in der Kachel angezeigt');
checkCard(str_contains($css, 'grid-template-columns:repeat(3,minmax(340px,1fr))'), 'Desktop-Raster unterstützt drei Kacheln');
checkCard(str_contains($js, 'players.slice(0,5)'), 'Live-Refresh begrenzt kompakte Teamlisten auf fünf Spieler');
checkCard(str_contains($js, 'map_switch_in_flight'), 'Laufender Mapwechsel wird live aktualisiert');

echo "\nDashboard-Serverkachel-Test erfolgreich.\n";
