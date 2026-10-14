<?php
$root=dirname(__DIR__);
function ok170(bool $condition,string $label): void { if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);} echo "OK  {$label}\n"; }

$version=trim((string)file_get_contents($root.'/VERSION'));
$layout=(string)file_get_contents($root.'/templates/layout.php');
$css=(string)file_get_contents($root.'/public/assets/app.css');
$js=(string)file_get_contents($root.'/public/assets/app.js');
$live=(string)file_get_contents($root.'/templates/server_live.php');
$players=(string)file_get_contents($root.'/templates/players.php');
$servers=(string)file_get_contents($root.'/templates/servers.php');
$users=(string)file_get_contents($root.'/templates/users.php');
$settings=(string)file_get_contents($root.'/templates/settings.php');
$fast=(string)file_get_contents($root.'/src/Services/FastLiveService.php');
$audit=(string)file_get_contents($root.'/templates/audit.php');

ok170(version_compare($version,'1.7.0','>='),'VERSION ist 1.7.0 oder neuer');
ok170(str_contains($layout,'data-ui-view-switch'),'globaler Ansichtsumschalter im Kopfbereich vorhanden');
ok170(str_contains($layout,'data-ui-view-mode="cards"')&&str_contains($layout,'data-ui-view-mode="compact"'),'Kachel- und Kompaktmodus vorhanden');
ok170(str_contains($layout,'class="ui-mode-cards"'),'Kachelansicht ist Standard');
ok170(str_contains($js,'serververwaltung.ui.viewMode'),'globale Ansicht wird browserweit persistent gespeichert');
ok170(str_contains($js,"body.classList.toggle('ui-mode-cards'")&&str_contains($js,"body.classList.toggle('ui-mode-compact'"),'Ansichtsmodus wird panelweit umgeschaltet');
ok170(str_contains($js,"table[data-ui-adaptive]")&&str_contains($js,'MutationObserver'),'adaptive Tabellen funktionieren auch bei Live-Refresh');
ok170(str_contains($css,'.live-section-grid')&&str_contains($css,'.settings-layout'),'lange Seiten sind in responsive Raster gegliedert');
ok170(str_contains($live,'data-ui-adaptive="live-players"'),'Live-Spielerliste nutzt adaptive Karten');
ok170(str_contains($players,'data-ui-adaptive="people"'),'Personenverwaltung nutzt adaptive Karten');
ok170(str_contains($servers,'data-ui-adaptive="server-list"'),'Serverübersicht nutzt adaptive Karten');
ok170(str_contains($users,'permission-user-card')&&str_contains($users,'role-template-form'),'Webpanel-Nutzer und Rollen nutzen die adaptive Rechte-Kartenansicht');
ok170(str_contains($settings,'class="settings-layout"'),'Einstellungen sind als Kartenraster gruppiert');
ok170(str_contains($audit,'data-audit-filter'),'Audit-Log besitzt Schnellfilter');
ok170(str_contains($fast,"public const REMOTE_CONFIG_PATH = 'game/csgo/addons/swiftlys2/data/serververwaltung_bridge/fastlive.json'"),'Fast-Live-Provisioning verwendet korrekten game/csgo-Pfad');

echo "\nv1.7.0 AdaptiveUI-Test erfolgreich.\n";
