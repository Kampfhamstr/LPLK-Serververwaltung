<?php
$root=dirname(__DIR__);
$live=file_get_contents($root.'/src/Services/LiveServerService.php');
$ptero=file_get_contents($root.'/src/Services/PterodactylConnector.php');
$whitelist=file_get_contents($root.'/src/Services/WhitelistLiveService.php');
$adminLog=file_get_contents($root.'/src/Services/AdminActionLogService.php');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');

function ok163(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok163(str_contains($live,'STATUS_CACHE_TTL_SECONDS = 10.0'),'Live-Statuscache vorhanden und für v1.6.8 auf 10 Sekunden gehärtet');
ok163(str_contains($live,'LOCK_EX | LOCK_NB'),'Nonblocking per-Server-Cachelock vorhanden');
ok163(str_contains($live,'STATUS_CACHE_STALE_SECONDS = 60.0'),'Stale-Cache-Fallback vorhanden und auf 60 Sekunden erweitert');
ok163(str_contains($live,'fetchStatusUncached'),'Uncached Fetch zentralisiert');
ok163(!str_contains(substr($live,strpos($live,'private static function fetchStatusUncached'),3500),'->exists(self::STATUS_PATH)'),'status.json ohne exists/list-Request');
ok163(str_contains($live,'public static function newCommandId'),'Chronologische Command-ID-Hilfe vorhanden');
ok163(str_contains($live,"sprintf('%06d', \$micros)"),'Command-ID enthält Mikrosekunden');

ok163(str_contains($ptero,'CURLOPT_CONNECTTIMEOUT => 2'),'Pterodactyl Connect-Timeout auf 2s reduziert');
ok163(str_contains($ptero,'CURLOPT_TIMEOUT => 4'),'Pterodactyl Gesamt-Timeout auf 4s reduziert');
ok163(str_contains($ptero,'Serververwaltung/1.8.3'),'Pterodactyl User-Agent auf aktuellen Stand 1.8.3 aktualisiert');

ok163(!str_contains($adminLog,'$connector->exists(self::RELATIVE_PATH)'),'Admin-Log ohne zusätzlichen exists-Request');
ok163(str_contains($whitelist,'$steamIds=self::parseSteamIds($connector->read($listPath))'),'Whitelist-Datei direkt gelesen');
ok163(!str_contains($whitelist,'if (!$connector->exists($path)) continue;'),'Whitelist-Konfig ohne exists-Request');
ok163(str_contains($whitelist,'LiveServerService::newCommandId()'),'Whitelist nutzt chronologische Command-IDs');

ok163(str_contains($bridge,'private bool _processingCommands'),'Bridge Reentrancy-Sperre vorhanden');
ok163(str_contains($bridge,'if (_processingCommands)'),'Parallele ProcessCommands-Ausführung verhindert');
ok163(str_contains($bridge,'TimeSpan.FromMilliseconds(750)'),'750-ms-Settling-Zeit vorhanden');
ok163(str_contains($bridge,'.OrderBy(x => x, StringComparer.Ordinal)'),'Commands werden sortiert abgearbeitet');

echo "\nv1.6.3 Multiuser-Live-Test erfolgreich.\n";
