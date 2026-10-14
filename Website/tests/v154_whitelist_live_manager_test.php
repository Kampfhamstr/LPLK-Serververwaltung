<?php
$root=dirname(__DIR__);
$auth=file_get_contents($root.'/src/Core/Auth.php');
$service=file_get_contents($root.'/src/Services/WhitelistLiveService.php');
$controller=file_get_contents($root.'/src/Controllers/LiveController.php');
$template=file_get_contents($root.'/templates/server_live.php');
$routes=file_get_contents($root.'/public/index.php');
$js=file_get_contents($root.'/public/assets/app.js');

function ok154(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok154(str_contains($auth,"'live.whitelist'"),'Live-Whitelist-Berechtigung vorhanden');
ok154(str_contains($auth,"'live.weapons','live.whitelist','live.team'"),'Admin erhält Live-Whitelist-Recht');
ok154(str_contains($service,"SUPPORTED_ID = 'Whitelist'"),'Manager erkennt Plugin-ID Whitelist');
ok154(str_contains($service,"/whitelist.txt"),'Manager liest whitelist.txt im Pluginpfad');
ok154(str_contains($service,"AddCommand"),'Konfigurierbarer AddCommand wird gelesen');
ok154(str_contains($service,"RemoveCommand"),'Konfigurierbarer RemoveCommand wird gelesen');
ok154(str_contains($service,"whitelist.serververwaltung-last.bak"),'Backup vor größeren Änderungen vorhanden');
ok154(str_contains($service,"'action'=>'server_command'"),'Änderungen laufen über Plugin-Befehle');
ok154(str_contains($service,'array_diff($desired,$current)'),'Komplettabgleich berechnet Hinzufügungen');
ok154(str_contains($service,'array_diff($current,$desired)'),'Komplettabgleich berechnet Entfernungen');
ok154(str_contains($controller,'WhitelistLiveService::state'),'LiveController liefert Whitelist-Daten');
ok154(str_contains($controller,'whitelistAction'),'LiveController hat Whitelist-Aktion');
ok154(str_contains($routes,'/live/whitelist'),'Whitelist-POST-Route vorhanden');
ok154(str_contains($template,'id="whitelist"'),'Whitelist ist direkt in Liveverwaltung eingebaut');
ok154(str_contains($template,'Markierte hinzufügen'),'Mehrfach-Hinzufügen vorhanden');
ok154(str_contains($template,'Markierte entfernen'),'Mehrfach-Entfernen vorhanden');
ok154(str_contains($template,'Komplette Liste abgleichen'),'Differenzabgleich vorhanden');
ok154(str_contains($js,'data-whitelist-select-visible'),'Sichtbare Spieler können schnell markiert werden');

echo "\nv1.5.4 Whitelist-Live-Manager-Test erfolgreich.\n";
