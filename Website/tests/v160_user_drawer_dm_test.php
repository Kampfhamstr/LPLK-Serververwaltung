<?php
$root=dirname(__DIR__);
$schema=file_get_contents($root.'/migrations/schema.sql');
$service=file_get_contents($root.'/src/Services/PanelUserDirectoryService.php');
$controller=file_get_contents($root.'/src/Controllers/PanelUsersController.php');
$routes=file_get_contents($root.'/public/index.php');
$layout=file_get_contents($root.'/templates/layout.php');
$css=file_get_contents($root.'/public/assets/app.css');
$js=file_get_contents($root.'/public/assets/app.js');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');

function ok160(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok160(str_contains($schema,'CREATE TABLE IF NOT EXISTS webpanel_direct_messages'),'Direktnachrichten-Tabelle vorhanden');
ok160(str_contains($service,'ONLINE_SECONDS = 180'),'Online-Fenster 3 Minuten');
ok160(str_contains($service,"['founder'=>1,'admin'=>2,'member'=>3,'vip'=>4,'spieler'=>5]"),'Sortierung Founder Admin Member VIP Spieler');
ok160(str_contains($service,'webpanel_direct_messages'),'Sidebar-Service verarbeitet Direktnachrichten');
ok160(str_contains($service,'updateAccount'),'Founder-Rollenverwaltung im Sidebar-Service');
ok160(str_contains($controller,'function sidebar'),'Sidebar-JSON-Endpunkt vorhanden');
ok160(str_contains($controller,'function message'),'Direktnachrichten-Endpunkt vorhanden');
ok160(str_contains($routes,'/panel-users/sidebar'),'Sidebar-Route vorhanden');
ok160(str_contains($routes,'/panel-users/{id}/messages'),'Nachrichten-Route vorhanden');
ok160(str_contains($layout,'data-panel-users-drawer'),'Rechte Nutzerleiste global im Layout');
ok160(str_contains($layout,'data-panel-users-toggle'),'Ausklappbarer rechter Rand-Tab vorhanden');
ok160(str_contains($css,'.panel-user-nav-item.role-founder'),'Founder-Farbe vorhanden');
ok160(str_contains($css,'.panel-user-nav-item.role-admin'),'Admin-Farbe vorhanden');
ok160(str_contains($css,'.panel-user-nav-item.role-member'),'Member-Farbe vorhanden');
ok160(str_contains($js,'v1.6.0: Webpanel user drawer'),'Drawer-JavaScript vorhanden');
ok160(str_contains($js,'data-panel-dm-form'),'Direktnachrichten-UI vorhanden');
ok160(str_contains($bridge,'"founder" => "[darkred]"'),'Founder-Ingamefarbe dunkelrot');
ok160(str_contains($bridge,'"admin" => "[red]"'),'Admin-Ingamefarbe rot');
ok160(str_contains($bridge,'"member" => "[blue]"'),'Member-Ingamefarbe blau');
ok160(str_contains($bridge,'BuildColoredAdminActionSummary') && str_contains($bridge,'$"[white] | {action}[default]"'),'Aktion weiß und Farbrücksetzung vorhanden');

echo "\nv1.6.0 User-Drawer-/DM-Test erfolgreich.\n";
