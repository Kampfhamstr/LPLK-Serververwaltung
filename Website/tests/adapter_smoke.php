<?php
require dirname(__DIR__) . '/src/Core/Jsonc.php';
require dirname(__DIR__) . '/src/Core/TOTP.php';
require dirname(__DIR__) . '/src/Services/SwiftlyAdapter.php';
require dirname(__DIR__) . '/src/Services/CounterStrikeSharpAdapter.php';

use App\Core\Jsonc;
use App\Core\TOTP;
use App\Services\SwiftlyAdapter;
use App\Services\CounterStrikeSharpAdapter;

function check(bool $condition, string $message): void {
    if (!$condition) throw new RuntimeException($message);
    echo "OK  $message\n";
}

$source = <<<'JSON'
{
  // Kommentar
  "Permissions": {
    "Players": {
      "76561199386534600": ["admin"],
      "76561198000439224": ["admin"],
      "76561199000000001": ["external"]
    },
    "PermissionGroups": {
      "__default": [],
      "admin": ["*", "wl.admin"],
      "external": ["external.keep"]
    }
  }
}
JSON;
$decoded = Jsonc::decode($source);
check(isset($decoded['Permissions']['PermissionGroups']['admin']), 'JSONC-Kommentare werden gelesen');

$assignments = [['display_name'=>'Test','steam_id64'=>'76561199386534600','role_name'=>'founder']];
$roles = ['spieler'=>['base.use'],'vip'=>['vip.use'],'member'=>[],'admin'=>['wl.admin'],'founder'=>['*']];
$swift = Jsonc::decode(SwiftlyAdapter::build($source, $assignments, $roles, ['76561199386534600','76561198000439224']));
check(($swift['Permissions']['Players']['76561199386534600'][0] ?? '') === 'founder', 'SwiftlyS2-Zuordnung wird aktualisiert');
check(!isset($swift['Permissions']['Players']['76561198000439224']), 'Entfernte verwaltete SwiftlyS2-Zuordnung wird entfernt');
check(isset($swift['Permissions']['Players']['76561199000000001']), 'Fremde SwiftlyS2-Spielereinträge bleiben erhalten');
check(in_array('vip.use', $swift['Permissions']['PermissionGroups']['admin'], true), 'SwiftlyS2-Rechte werden nach oben vererbt');
check(in_array('external.keep', $swift['Permissions']['PermissionGroups']['external'], true), 'Fremde SwiftlyS2-Gruppen bleiben erhalten');

$admins = '{"External":{"identity":"76561199000000001","groups":["#other/admin"]},"Managed":{"identity":"76561199386534600","groups":["#serververwaltung/admin"]},"Remove":{"identity":"76561198000439224","groups":["#serververwaltung/admin"]}}';
$cssAdmins = json_decode(CounterStrikeSharpAdapter::buildAdmins($admins, $assignments, ['76561199386534600','76561198000439224']), true, 512, JSON_THROW_ON_ERROR);
check(isset($cssAdmins['External']), 'Fremder CounterStrikeSharp-Admin bleibt erhalten');
check(!isset($cssAdmins['Remove']), 'Entfernter verwalteter CounterStrikeSharp-Admin wird entfernt');
check(in_array('#serververwaltung/founder', $cssAdmins['Managed']['groups'], true), 'CounterStrikeSharp-Gruppe wird aktualisiert');

$groups = '{"#other/admin":{"flags":["@other/right"],"immunity":55}}';
$cssGroups = json_decode(CounterStrikeSharpAdapter::buildGroups($groups, $roles), true, 512, JSON_THROW_ON_ERROR);
check(isset($cssGroups['#other/admin']), 'Fremde CounterStrikeSharp-Gruppe bleibt erhalten');
check(in_array('@css/root', $cssGroups['#serververwaltung/founder']['flags'], true), 'Founder erhält CounterStrikeSharp root');

$secret = TOTP::generateSecret();
check(TOTP::verify($secret, TOTP::code($secret)), 'TOTP-Erzeugung und Prüfung funktionieren');

echo "\nAdapter-Smoke-Test erfolgreich.\n";
