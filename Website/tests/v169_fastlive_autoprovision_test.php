<?php
$root = dirname(__DIR__);
function ok169(bool $condition, string $label): void {
    if (!$condition) { fwrite(STDERR, "FEHLER: {$label}\n"); exit(1); }
    echo "OK  {$label}\n";
}
$service = file_get_contents($root.'/src/Services/FastLiveService.php');
$controller = file_get_contents($root.'/src/Controllers/ServersController.php');
$routes = file_get_contents($root.'/public/index.php');
$template = file_get_contents($root.'/templates/server_view.php');
$migrate = file_get_contents($root.'/bin/migrate.php');
$bridge = file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
$csproj = file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/SerververwaltungBridge.csproj');
$cli = file_get_contents($root.'/bin/provision-fastlive.php');

ok169(str_contains($service,"public const REMOTE_CONFIG_PATH = 'game/csgo/addons/swiftlys2/data/serververwaltung_bridge/fastlive.json'"),'zentraler Fast-Live-Zielpfad vorhanden');
ok169(str_contains($service,'public static function ensureEnabled(int $serverId): string'),'Fast Live kann standardmäßig serverbezogen vorbereitet werden');
ok169(str_contains($service,'public static function provision(int $serverId): array'),'automatische Connector-Bereitstellung vorhanden');
ok169(str_contains($service,'ConnectorFactory::make($server)'),'Auto-Provisioning benutzt bestehenden Server-Connector');
ok169(str_contains($service,'$connector->write(self::REMOTE_CONFIG_PATH,$json)'),'fastlive.json wird automatisch geschrieben');
ok169(str_contains($service,'$connector->read(self::REMOTE_CONFIG_PATH)'),'geschriebene Fast-Live-Konfiguration wird verifiziert');

ok169(str_contains($controller,'FastLiveService::ensureEnabled($id);'),'neue Server starten mit vorbereitetem Fast Live');
ok169(str_contains($controller,'FastLiveService::provision((int)$id);'),'Server-Scan provisioniert Fast Live automatisch');
ok169(str_contains($controller,'public function provisionFastLive'),'manuelle Wiederholungsaktion vorhanden');
ok169(str_contains($routes,"/servers/{id}/fast-live/provision"),'Provisioning-Route registriert');
ok169(str_contains($template,'Automatische Bereitstellung aktiv'),'UI erklärt automatischen Betrieb');
ok169(str_contains($template,'Schlüssel erneuern & bereitstellen'),'Schlüsselrotation provisioniert direkt');

ok169(str_contains($migrate,'FastLiveService::ensureEnabled'),'Upgrade erzeugt fehlende Fast-Live-Secrets automatisch');
ok169(str_contains($cli,'FastLiveService::provision($id)'),'CLI kann alle vorhandenen Server provisionieren');

ok169(str_contains($bridge,'Version = "1.5.5"'),'Bridge-Metadaten sind aktueller Auto-Provision-Stand');
ok169(str_contains($csproj,'<Version>1.5.5</Version>'),'Bridge-Projektversion ist aktueller Auto-Provision-Stand');
ok169(str_contains($bridge,'TryReloadFastLiveConfigIfChanged();'),'Bridge überwacht Fast-Live-Konfiguration im laufenden Betrieb');
ok169(str_contains($bridge,'Fast Live wartet auf automatische Bereitstellung'),'Bridge wartet ohne Fehler auf Auto-Provisioning');
ok169(str_contains($bridge,'Kein Bridge-Neustart erforderlich.'),'Konfigurationsübernahme ohne Neustart dokumentiert');

echo "\nv1.6.9 Fast-Live-Auto-Provisioning-Test erfolgreich.\n";
