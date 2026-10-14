<?php
namespace App\Services;

use App\Core\Jsonc;

final class SwiftlyAdapter
{
    public const PATH = 'game/csgo/addons/swiftlys2/configs/permissions.jsonc';
    public const ROLES = ['spieler','vip','member','admin','founder'];

    public static function build(string $current, array $assignments, array $rolePermissions, array $managedSteamIds = []): string
    {
        $data = Jsonc::decode($current);
        $data['Permissions'] ??= [];
        $data['Permissions']['Players'] ??= [];
        $data['Permissions']['PermissionGroups'] ??= [];
        $players = $data['Permissions']['Players'];
        $assignedIds = array_column($assignments, 'steam_id64');
        foreach ($managedSteamIds as $steamId) {
            if (!in_array((string)$steamId, $assignedIds, true)) unset($players[(string)$steamId]);
        }
        foreach ($assignments as $a) {
            $players[(string)$a['steam_id64']] = [(string)$a['role_name']];
        }
        $data['Permissions']['Players'] = $players;
        $groups = $data['Permissions']['PermissionGroups'];
        $cumulative=[];
        foreach (self::ROLES as $role) {
            foreach (($rolePermissions[$role] ?? []) as $perm) {
                if ($perm !== '' && !in_array($perm, $cumulative, true)) $cumulative[]=$perm;
            }
            $groups[$role] = array_values($cumulative);
        }
        if (!in_array('*', $groups['founder'] ?? [], true)) $groups['founder'][]='*';
        $groups['__default'] ??= [];
        $data['Permissions']['PermissionGroups'] = $groups;
        return Jsonc::encode($data);
    }
}
