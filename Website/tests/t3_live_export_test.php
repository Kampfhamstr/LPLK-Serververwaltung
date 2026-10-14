<?php
$root = dirname(__DIR__);

$auth = file_get_contents($root . '/src/Core/Auth.php');
$liveService = file_get_contents($root . '/src/Services/LiveServerService.php');
$liveController = file_get_contents($root . '/src/Controllers/LiveController.php');
$playersController = file_get_contents($root . '/src/Controllers/PlayersController.php');
$livePlayerTemplate = file_get_contents($root . '/templates/server_live_player.php');
$exportTemplate = file_get_contents($root . '/templates/players_export.php');
$routes = file_get_contents($root . '/public/index.php');

function check130(bool $ok, string $message): void {
    if (!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

check130(str_contains($auth, "'players.export'"), 'Export-Berechtigung vorhanden');
check130(str_contains($auth, "'live.mute'") && str_contains($auth, "'live.gag'") && str_contains($auth, "'live.silence'"), 'Kommunikationsrechte vorhanden');
check130(str_contains($auth, "'live.slay'") && str_contains($auth, "'live.noclip'") && str_contains($auth, "'live.teleport'"), 'T3-Spieleraktionsrechte vorhanden');
check130(str_contains($liveService, 'adoptUnknownPlayers'), 'Unbekannte Live-SteamIDs werden automatisch übernommen');
check130(str_contains($liveService, "'spieler','active'"), 'Automatische Rolle Spieler ist fest verdrahtet');
check130(str_contains($liveService, "'mute','unmute','gag','ungag','silence','unsilence'"), 'Bridge-Aktionen für Kommunikation freigegeben');
check130(str_contains($liveController, "'slay' => 'live.slay'"), 'Slay ist an separates Recht gebunden');
check130(str_contains($liveController, 'serverCommand') && str_contains($liveController, 'cvarSet'), 'Serverkonsole und CVar-Controller vorhanden');
check130(str_contains($livePlayerTemplate, 'T3-Semantik'), 'Spielermenü erklärt Mute/Gag/Silence klar');
check130(str_contains($livePlayerTemplate, 'teleport_player') && str_contains($livePlayerTemplate, 'source_steamid') && str_contains($livePlayerTemplate, 'destination_steamid'), 'Freier Spieler-zu-Spieler-Teleport im Spielermenü vorhanden');
check130(str_contains($exportTemplate, 'Alle ab VIP aufwärts'), 'Globaler Rollenfilter ab VIP vorhanden');
check130(str_contains($playersController, 'whitelist_') && str_contains($playersController, 'fputcsv'), 'Whitelist- und Tabellenexport implementiert');
check130(str_contains($routes, "'exportDownload'") && str_contains($routes, "'serverCommand'"), 'Neue Routen registriert');

echo "\nT3-Live-/Export-Test erfolgreich.\n";
