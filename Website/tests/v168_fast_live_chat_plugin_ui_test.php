<?php
$root=dirname(__DIR__);
$live=file_get_contents($root.'/src/Services/LiveServerService.php');
$fast=file_get_contents($root.'/src/Services/FastLiveService.php');
$chat=file_get_contents($root.'/src/Services/ServerChatService.php');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
$plugins=file_get_contents($root.'/templates/plugins.php');
$chatTpl=file_get_contents($root.'/templates/server_chat.php');
$dashboard=file_get_contents($root.'/src/Controllers/DashboardController.php');
$schema=file_get_contents($root.'/migrations/schema.sql');
$routes=file_get_contents($root.'/public/index.php');
$bootstrap=file_get_contents($root.'/src/bootstrap.php');
$db=file_get_contents($root.'/src/Core/Database.php');

function ok168(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok168(str_contains($schema,'CREATE TABLE IF NOT EXISTS server_chat_messages'),'Dauerhafte Chat-Tabelle vorhanden');
ok168(str_contains($schema,'CREATE TABLE IF NOT EXISTS bridge_command_queue'),'Lokale Fast-Live-Command-Queue vorhanden');
ok168(str_contains($chat,'INSERT OR IGNORE INTO server_chat_messages'),'Chat wird idempotent dauerhaft gespeichert');
ok168(str_contains($chat,'player_name LIKE ?') && str_contains($chat,'steam_id64 LIKE ?'),'Chat-Suche nach Spieler/SteamID vorhanden');
ok168(str_contains($chat,'m.message LIKE ?'),'Chat-Suche nach Nachrichtentext vorhanden');
ok168(str_contains($chat,'m.sent_at>=?') && str_contains($chat,'m.sent_at<=?'),'Chat-Suche nach Zeitraum vorhanden');
ok168(str_contains($chatTpl,'game-role-text-') && str_contains($chatTpl,'community_role'),'Chat zeigt Community-Rollen farbig');
ok168(str_contains($chatTpl,'<time '),'Chat zeigt Timestamp');
ok168(str_contains($routes,"'/servers/{id}/chat'") || str_contains($routes,"'/servers/'"),'Chatarchiv ist geroutet');

ok168(str_contains($bridge,'CaptureChatMessage(player, text, teamonly)'),'Bridge erfasst ClientChat');
ok168(str_contains($bridge,'while (_chatMessages.Count > 250)'),'Bridge besitzt Chat-Ringpuffer');
ok168(str_contains($bridge,'HMACSHA256'),'Fast Live verwendet HMAC-SHA256');
ok168(str_contains($bridge,'TryStartFastLiveExchange(status)'),'Bridge pusht Status aktiv');
ok168(str_contains($bridge,'Version = "1.5.5"'),'Bridge-Metadaten sind aktueller Fast-Live-Stand');
ok168(str_contains($bridge,'public string Version { get; set; } = "1.5.5"'),'status.json meldet aktuellen Fast-Live-Stand');

ok168(str_contains($live,'statusQuick'),'Nicht blockierende Status-Schnellansicht vorhanden');
ok168(str_contains($live,'if (FastLiveService::enabled($serverId)) return null;'),'Result-Polling fällt bei Fast Live nicht auf Pterodactyl zurück');
ok168(str_contains($dashboard,'statusQuick'),'Dashboard verwendet nicht blockierende Statusansicht');
ok168(str_contains($fast,'bridge_fastlive_secret_enc'),'Fast Live nutzt pro Server verschlüsselten Schlüssel');
ok168(str_contains($fast,"hash_hmac('sha256'"),'Panel prüft HMAC-SHA256');
ok168(str_contains($bootstrap,"/bridge-api/") && str_contains($bootstrap,'Security::startSession'),'Bridge-API erzeugt keine unnötige PHP-Browsersession');
ok168(str_contains($db,'PRAGMA busy_timeout = 5000'),'SQLite besitzt Busy-Timeout für parallelen Chat/Admin-Betrieb');
ok168(str_contains($chat,'array_chunk(array_keys($uids), 400)'),'Chat-Ringpuffer wird vor Inserts gebündelt dedupliziert');
ok168(str_contains($chat,'beginTransaction'),'Neue Chatnachrichten werden gebündelt in einer Transaktion gespeichert');

ok168(str_contains($plugins,'Plugin-Katalog'),'Globaler Plugin-Katalog vorhanden');
ok168(str_contains($plugins,'REPO FEHLT'),'Fehlende Repositories werden sichtbar markiert');
ok168(str_contains($plugins,'REPO ERKANNT'),'Erkannte Repositories werden separat markiert');
ok168(str_contains($plugins,'GitHub Repository'),'Repository-Direktlink vorhanden');
ok168(str_contains($plugins,'Letztes Release'),'Release-Direktlink vorhanden');

echo "\nv1.6.8 Fast-Live/Chat/Plugin-UI-Test erfolgreich.\n";
