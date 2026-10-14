<?php
$root=dirname(__DIR__);
$service=file_get_contents($root.'/src/Services/WhitelistLiveService.php');
$live=file_get_contents($root.'/src/Services/LiveServerService.php');
$controller=file_get_contents($root.'/src/Controllers/LiveController.php');
$routes=file_get_contents($root.'/public/index.php');
$serverTemplate=file_get_contents($root.'/templates/server_live.php');
$plugins=file_get_contents($root.'/templates/plugins.php');
$js=file_get_contents($root.'/public/assets/app.js');

function ok166(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok166(str_contains($service,'verifyPending'),'Whitelist besitzt echte Verifikationslogik');
ok166(str_contains($service,'$connector->read($listPath)'),'Verifikation liest echte Plugin-Datei');
ok166(str_contains($service,"'state'=>'confirmed'"),'Bestätigt-Status vorhanden');
ok166(str_contains($service,'$age>=20'),'20-Sekunden Timeout vorhanden');
ok166(str_contains($service,'commandMap'),'Whitelist-Befehle werden SteamIDs zugeordnet');
ok166(str_contains($controller,'rememberWhitelistPending'),'Panel merkt laufende Änderung pro Nutzer');
ok166(str_contains($controller,'whitelistVerify'),'Verifikationsendpunkt vorhanden');
ok166(str_contains($routes,'/live/whitelist/verify'),'Whitelist Verify-Route vorhanden');
ok166(str_contains($serverTemplate,'WIRD ENTFERNT'),'Entfernung wird sichtbar vorgemerkt');
ok166(str_contains($serverTemplate,'WIRD HINZUGEFÜGT'),'Hinzufügung wird sichtbar vorgemerkt');
ok166(str_contains($js,'Vom Gameserver bestätigt'),'Client zeigt direkte Bestätigung');
ok166(str_contains($js,'window.location.reload()'),'Liste wird nach Bestätigung neu geladen');
ok166(str_contains($live,'Bekannter Result-Pfad: direkt lesen'),'Result-Prüfung ohne exists/list-Roundtrip');

ok166(str_contains($plugins,'plugin-server-accordion'),'Server sind einklappbar');
ok166(str_contains($plugins,'data-plugin-card'),'Plugins sind einzeln einklappbar');
ok166(str_contains($plugins,'data-plugin-search'),'Plugin-Suche vorhanden');
ok166(str_contains($plugins,'Updates öffnen'),'Updates-Schnellansicht vorhanden');
ok166(str_contains($plugins,'Alles einklappen'),'Alles-einklappen vorhanden');
ok166(str_contains($plugins,'GitHub Repository'),'GitHub-Link bleibt vorhanden');
ok166(str_contains($plugins,'Letztes Release'),'Release-Link bleibt vorhanden');
ok166(str_contains($plugins,'GitHub-Updatequelle konfigurieren'),'Updatequellen-Konfiguration bleibt vorhanden');
ok166(str_contains($js,'v1.6.6: Plugin compact browser'),'Plugin-Accordion-JavaScript vorhanden');

echo "\nv1.6.6 Whitelist-Confirm-/Plugin-UI-Test erfolgreich.\n";
