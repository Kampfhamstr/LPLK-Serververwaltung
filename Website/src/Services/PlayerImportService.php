<?php
namespace App\Services;

use App\Core\Database;
use App\Core\Jsonc;

final class PlayerImportService
{
    public const ROLES = ['spieler','vip','member','admin','founder'];

    public static function parse(string $content): array
    {
        $found = [];
        try {
            $data = Jsonc::decode($content);
            self::walkStructured($data, $found);
        } catch (\Throwable) {
            // Freitext/CSV/TXT wird unten per SteamID-Muster verarbeitet.
        }
        preg_match_all('/(?<!\d)(7656119\d{10})(?!\d)/', $content, $matches);
        foreach (($matches[1] ?? []) as $steamId) {
            $found[(string)$steamId] ??= ['steam_id64'=>(string)$steamId,'name'=>null,'role'=>null];
        }
        ksort($found, SORT_STRING);
        return array_values($found);
    }

    public static function import(string $content, string $defaultRole = 'spieler', ?int $serverId = null, ?string $serverRole = null): array
    {
        if (!in_array($defaultRole, self::ROLES, true)) $defaultRole = 'spieler';
        if ($serverRole !== null && !in_array($serverRole, self::ROLES, true)) $serverRole = null;
        $items = self::parse($content);
        $pdo = Database::connection();
        $created = 0; $existing = 0; $assigned = 0; $ids = [];
        $pdo->beginTransaction();
        try {
            foreach ($items as $item) {
                $steam = $item['steam_id64'];
                $stmt = $pdo->prepare('SELECT id FROM game_players WHERE steam_id64=?');
                $stmt->execute([$steam]);
                $id = $stmt->fetchColumn();
                if ($id === false) {
                    $display = trim((string)($item['name'] ?? ''));
                    if ($display === '') $display = 'Steam ' . substr($steam, -6);
                    $role = in_array((string)($item['role'] ?? ''), self::ROLES, true) ? (string)$item['role'] : $defaultRole;
                    $pdo->prepare('INSERT INTO game_players(display_name,steam_id64,community_role,status,active,created_at,updated_at) VALUES(?,?,?,?,1,?,?)')
                        ->execute([$display,$steam,$role,'active',date('c'),date('c')]);
                    $id = (int)$pdo->lastInsertId();
                    $created++;
                } else {
                    $id = (int)$id; $existing++;
                }
                $ids[] = $id;
                if ($serverId && $serverRole) {
                    $primaryId = PlayerAccountService::primaryId($id);
                    $family = PlayerAccountService::propagateServerRole($primaryId, $serverId, $serverRole);
                    $assigned += count($family);
                }
            }
            $pdo->commit();
        } catch (\Throwable $e) {
            $pdo->rollBack();
            throw $e;
        }
        return ['found'=>count($items),'created'=>$created,'existing'=>$existing,'assigned'=>$assigned,'player_ids'=>$ids];
    }

    private static function walkStructured(mixed $data, array &$found): void
    {
        if (!is_array($data)) return;
        if (isset($data['Permissions']['Players']) && is_array($data['Permissions']['Players'])) {
            foreach ($data['Permissions']['Players'] as $steam => $groups) {
                if (!preg_match('/^7656119\d{10}$/', (string)$steam)) continue;
                $role = null;
                if (is_array($groups)) foreach ($groups as $g) if (in_array($g, self::ROLES, true)) { $role=$g; break; }
                $found[(string)$steam] = ['steam_id64'=>(string)$steam,'name'=>null,'role'=>$role];
            }
        }
        foreach ($data as $key => $value) {
            if (is_array($value) && isset($value['identity']) && preg_match('/^7656119\d{10}$/', (string)$value['identity'])) {
                $role = null;
                foreach ((array)($value['groups'] ?? []) as $group) {
                    if (is_string($group) && str_starts_with($group, '#serververwaltung/')) {
                        $candidate = substr($group, strlen('#serververwaltung/'));
                        if (in_array($candidate, self::ROLES, true)) { $role=$candidate; break; }
                    }
                }
                $steam = (string)$value['identity'];
                $found[$steam] = ['steam_id64'=>$steam,'name'=>is_string($key)?$key:null,'role'=>$role];
            }
            if (is_array($value)) self::walkStructured($value, $found);
        }
    }
}
