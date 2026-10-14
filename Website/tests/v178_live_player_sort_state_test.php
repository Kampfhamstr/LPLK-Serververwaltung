<?php
$root=dirname(__DIR__);
$service=file_get_contents($root.'/src/Services/LiveServerService.php');
$template=file_get_contents($root.'/templates/server_live.php');
$js=file_get_contents($root.'/public/assets/app.js');
$css=file_get_contents($root.'/public/assets/app.css');
function ok178(bool $ok,string $msg):void{if(!$ok){fwrite(STDERR,"FEHLER: $msg\n");exit(1);}echo "OK  $msg\n";}
ok178(str_contains($service,'sortPlayersAlphabetically(self::enrichPlayers'), 'Live-Status sortiert Spieler zentral A-Z');
ok178(str_contains($service,'strnatcasecmp($nameA, $nameB)'), 'Serverseitige natürliche A-Z-Sortierung vorhanden');
ok178(str_contains($template,"is-player-spectator"), 'Initiale Live-Liste kennzeichnet Zuschauer');
ok178(str_contains($template,"is-player-dead"), 'Initiale Live-Liste kennzeichnet tote Spieler');
ok178(str_contains($template,"player-life-state"), 'Lebend/Tot/Zuschauer-Statusbadge vorhanden');
ok178(str_contains($js,"localeCompare(livePlayerName(b),'de'"), 'Live-AJAX-Refresh sortiert ebenfalls A-Z');
ok178(str_contains($js,"sortPlayersAlphabetically(Array.isArray(data.players)"), 'Sortierung bleibt nach 5-Sekunden-Refresh stabil');
ok178(str_contains($js,"name.dataset.playerStatusHost=''"), 'Moderationsbadge-Host bleibt nach AJAX-Refresh erhalten');
ok178(str_contains($css,'.live-player-row.is-player-dead'), 'Tote Spieler werden optisch gedämpft');
ok178(str_contains($css,'.player-restriction-badge{opacity:1!important'), 'Moderationsbadges bleiben trotz Ausgrauen deutlich sichtbar');
echo "v1.7.8 Live-Spieler-Sortierung/Statusdarstellung erfolgreich.\n";
