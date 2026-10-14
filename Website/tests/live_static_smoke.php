<?php
$root = dirname(__DIR__);
if (!function_exists('check')) {
    function check(bool $condition, string $message): void {
        if (!$condition) throw new RuntimeException($message);
        echo "OK  $message\n";
    }
}

$index = file_get_contents($root . '/public/index.php');
$auth = file_get_contents($root . '/src/Core/Auth.php');
$controller = file_get_contents($root . '/src/Controllers/LiveController.php');
$service = file_get_contents($root . '/src/Services/LiveServerService.php');
$liveTemplate = file_get_contents($root . '/templates/server_live.php');
$playerTemplate = file_get_contents($root . '/templates/server_live_player.php');
$bridgeCandidates = [
    $root . '/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs',
    ...glob(dirname($root,2) . '/bridge/SerververwaltungBridge*_Source/src/Plugin.cs'),
];
$bridgePath = current(array_values(array_filter($bridgeCandidates,'is_file'))) ?: null;
$bridge = $bridgePath ? (string)file_get_contents($bridgePath) : '';
$schema = file_get_contents($root . '/migrations/schema.sql');

foreach ([
    '/servers/{id}/live',
    '/servers/{id}/live/status',
    '/servers/{id}/live/player/{steamid}',
    '/servers/{id}/live/message',
    '/servers/{id}/live/map',
    '/servers/{id}/live/restart',
    '/servers/{id}/live/round-restart',
] as $route) {
    check(str_contains($index, $route), 'Live-Route vorhanden: ' . $route);
}

foreach (['live.view','live.kick','live.ban','live.freeze','live.respawn','live.health','live.armor','live.weapons','live.team','live.nickname','live.message','live.map','live.restart'] as $permission) {
    check(str_contains($auth, "'{$permission}'"), 'Live-Recht vorhanden: ' . $permission);
}
check(str_contains($auth, 'user_server_access') && str_contains($auth, "'live'") && str_contains($auth, 'user_server_group_access'), 'Serverbezogene Live-Freigabe ist direkt und über Servergruppen verdrahtet');
check(str_contains($schema, 'can_live_admin INTEGER NOT NULL DEFAULT 0'), 'Live-Freigabe ist im Schema enthalten');

check(str_contains($controller, "\$_POST['seconds'] ?? 5"), '5 Sekunden sind Controller-Voreinstellung');
check(str_contains($liveTemplate, '<option value="5" selected>5 Sekunden</option>'), '5 Sekunden sind in der Oberfläche vorausgewählt');
check(str_contains($liveTemplate, 'server.power.restart') && str_contains($liveTemplate, 'btn-warning ptero-power-button') && !str_contains($liveTemplate, '<h3>Server neu starten</h3>'), 'Pterodactyl-Restart ist eindeutig und ohne doppelte alte Neustart-Kachel vorhanden');
check(str_contains($liveTemplate, 'Mapwechsel zu {map} in {seconds} Sekunden.'), 'Mapwechsel-Ankündigung ist konfigurierbar');
check(str_contains($playerTemplate, 'Persönliche Nachricht'), 'Persönliche Spielernachrichten sind vorhanden');
check(str_contains($playerTemplate, 'Servername auf Steam-Profilnamen setzen'), 'Nickname kann auf Steam-Profilnamen zurückgesetzt werden');

foreach (['broadcast','private_message','kick','ban','unban','freeze','respawn','health','armor','team','give_weapon','remove_weapons','nickname_set','nickname_restore','map_change','round_restart'] as $action) {
    check(str_contains($service, "'{$action}'"), 'Webpanel-Aktion allowlisted: ' . $action);
    if($bridge!=='') check(str_contains($bridge, 'case "' . $action . '"'), 'Bridge-Aktion allowlisted: ' . $action);
}
if($bridge!==''){
    check(str_contains($bridge, 'Enumerable.Range(1, seconds).Reverse()'), '5-Sekunden-Countdown läuft sekündlich 5..1');
    check(str_contains($bridge, 'Core.Engine.IsMapValid(map)'), 'Map-/Workshop-Angabe wird über die Engine validiert');
    check(str_contains($bridge, 'map.All(char.IsDigit)') && str_contains($bridge, 'host_workshop_map '), 'Workshop-ID wird für host_workshop_map erkannt');
    check(str_contains($bridge, 'host_workshop_map '), 'Workshop-Mapwechsel ist implementiert');
    check(str_contains($bridge, 'changelevel '), 'Normaler Mapwechsel ist implementiert');
}else{
    echo "INFO Bridge-Quellcode wird als separates Paket geführt; reine Bridge-Prüfungen übersprungen.\n";
}

$liveBundle = strtolower($controller . $service . $liveTemplate . $playerTemplate . $bridge);
check(!str_contains($liveBundle, 'betbank'), 'Live-Modul enthält keine BetBank-Bearbeitung');
check(!str_contains($liveBundle, 'lotto'), 'Live-Modul enthält keine Lotto-Bearbeitung');

echo "\nLive-Static-Smoke-Test erfolgreich.\n";
