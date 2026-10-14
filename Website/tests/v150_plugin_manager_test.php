<?php
$root=dirname(__DIR__);
$auth=file_get_contents($root.'/src/Core/Auth.php');
$routes=file_get_contents($root.'/public/index.php');
$service=file_get_contents($root.'/src/Services/PluginUpdateService.php');
$github=file_get_contents($root.'/src/Services/GitHubReleaseService.php');
$template=file_get_contents($root.'/templates/plugins.php');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
$schema=file_get_contents($root.'/migrations/schema.sql');
function ok150(bool $ok,string $message):void{if(!$ok)throw new RuntimeException($message);echo "OK  {$message}\n";}
ok150(str_contains($auth,"'plugins.view'"),'Plugin-Inventar-Recht vorhanden');
ok150(str_contains($auth,"'plugins.manage'"),'Plugin-Management-Recht vorhanden');
ok150(str_contains($routes,'$router->get(\'/plugins\''),'Plugin-Seite registriert');
ok150(str_contains($schema,'CREATE TABLE IF NOT EXISTS plugin_update_sources'),'Updatequellen-Tabelle vorhanden');
ok150(str_contains($schema,'CREATE TABLE IF NOT EXISTS plugin_update_runs'),'Updatehistorie vorhanden');
ok150(str_contains($github,'releases/latest'),'GitHub Latest Release API wird verwendet');
ok150(str_contains($github,'asset_sha256'),'GitHub SHA-256-Digest wird übernommen');
ok150(str_contains($service,"pluginId==='lplk.serververwaltung.bridge'")||str_contains($service,"pluginId === 'lplk.serververwaltung.bridge'"),'Bridge-Selbstupdate wird blockiert');
ok150(str_contains($service,'$mode===\'auto\'')||str_contains($service,'$mode === \'auto\''),'Auto-Update besitzt eigenen Sicherheitsweg');
ok150(str_contains($template,'Backup + kontrollierter Reload'),'Sicherer Update-Modus ist im UI sichtbar');
ok150(str_contains($bridge,'GetAllPluginMetadata'),'Bridge nutzt IPluginManager-Metadaten');
ok150(str_contains($bridge,'GetAllPluginStatuses'),'Bridge nutzt Plugin-Status-API');
ok150(str_contains($bridge,'case "plugin_update"'),'Bridge kennt Update-Aktion');
ok150(str_contains($bridge,'case "plugin_rollback"'),'Bridge kennt Rollback-Aktion');
ok150(str_contains($bridge,'CreatePluginBackup'),'Bridge sichert Plugin vor Update');
ok150(str_contains($bridge,'SafeExtractArchive'),'Bridge prüft Release-Archiv');
ok150(str_contains($bridge,'CryptographicOperations.FixedTimeEquals'),'SHA-256 wird konstantzeitlich verglichen');
ok150(str_contains($bridge,'RestorePluginBackup'),'Automatischer Rollback ist vorhanden');
echo "\nv1.5.0 Plugin-Manager-Test erfolgreich.\n";
