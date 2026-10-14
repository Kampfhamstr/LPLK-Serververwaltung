<?php
namespace App\Services;

final class CounterStrikeSharpAdapter
{
    public const ADMINS_PATH = 'game/csgo/addons/counterstrikesharp/configs/admins.json';
    public const GROUPS_PATH = 'game/csgo/addons/counterstrikesharp/configs/admin_groups.json';
    public const ROLES = ['spieler','vip','member','admin','founder'];

    public static function buildAdmins(string $current, array $assignments, array $managedSteamIds = []): string
    {
        $data = json_decode($current, true, 512, JSON_THROW_ON_ERROR);
        if (!is_array($data)) $data=[];
        $assignedIds = array_column($assignments, 'steam_id64');
        foreach ($data as $key => $entry) {
            $identity = (string)($entry['identity'] ?? '');
            if (in_array($identity, $managedSteamIds, true) && !in_array($identity, $assignedIds, true)) unset($data[$key]);
        }
        foreach ($assignments as $a) {
            $key = self::uniqueKey($data, (string)$a['display_name'], (string)$a['steam_id64']);
            $existing = $data[$key] ?? [];
            $existing['identity'] = (string)$a['steam_id64'];
            $existing['groups'] = ['#serververwaltung/' . $a['role_name']];
            $data[$key] = $existing;
        }
        return json_encode($data, JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE) . "\n";
    }

    public static function buildGroups(string $current, array $rolePermissions): string
    {
        $data = json_decode($current, true, 512, JSON_THROW_ON_ERROR);
        if (!is_array($data)) $data=[];
        $cumulative=[];
        $immunity=['spieler'=>0,'vip'=>20,'member'=>50,'admin'=>80,'founder'=>100];
        foreach (self::ROLES as $role) {
            foreach (($rolePermissions[$role] ?? []) as $perm) {
                if ($perm !== '' && !in_array($perm, $cumulative, true)) $cumulative[]=$perm;
            }
            $flags=[];
            foreach ($cumulative as $perm) {
                if ($perm === '*') continue;
                $flags[] = str_starts_with($perm, '@') ? $perm : '@' . ltrim($perm, '@');
            }
            if ($role === 'founder' && !in_array('@css/root', $flags, true)) $flags[]='@css/root';
            $data['#serververwaltung/' . $role] = [
                'flags' => array_values(array_unique($flags)),
                'immunity' => $immunity[$role],
            ];
        }
        return json_encode($data, JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE) . "\n";
    }

    private static function uniqueKey(array $data, string $name, string $steam): string
    {
        foreach ($data as $key => $entry) if (($entry['identity'] ?? null) === $steam) return (string)$key;
        $base = trim($name) !== '' ? trim($name) : $steam;
        $key=$base; $i=2;
        while (isset($data[$key])) $key=$base . ' (' . $i++ . ')';
        return $key;
    }
}
