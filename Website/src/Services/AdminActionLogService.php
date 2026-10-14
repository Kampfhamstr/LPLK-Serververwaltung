<?php
namespace App\Services;

final class AdminActionLogService
{
    private const RELATIVE_PATH = 'game/csgo/addons/swiftlys2/data/serververwaltung_bridge/admin_actions.log';
    private const DEFAULT_LIMIT = 100;
    private const MAX_LIMIT = 500;
    private const MAX_READ_BYTES = 1024 * 1024;

    public static function latest(int $serverId, int $limit = self::DEFAULT_LIMIT): array
    {
        $limit = max(1, min(self::MAX_LIMIT, $limit));

        $server = LiveServerService::server($serverId);
        $connector = ConnectorFactory::make($server);

        try {
            // Bekannter Dateipfad: direkt lesen. Ein vorheriges exists() würde
            // bei Pterodactyl einen zusätzlichen /files/list-Request erzeugen.
            $content = $connector->read(self::RELATIVE_PATH);
            if (strlen($content) > self::MAX_READ_BYTES) {
                $content = substr($content, -self::MAX_READ_BYTES);
                $newline = strpos($content, "\n");
                if ($newline !== false) {
                    $content = substr($content, $newline + 1);
                }
            }

            $lines = preg_split('/\r\n|\r|\n/', trim($content)) ?: [];
            $lines = array_values(array_filter($lines, static fn(string $line): bool => trim($line) !== ''));
            $lines = array_slice($lines, -$limit);
            $lines = array_reverse($lines);

            $entries = [];
            foreach ($lines as $line) {
                $entries[] = self::parseLine($line);
            }

            return [
                'exists'=>true,
                'path'=>self::RELATIVE_PATH,
                'entries'=>$entries,
                'error'=>null,
            ];
        } catch (\Throwable $e) {
            $message = $e->getMessage();
            $missing = str_contains($message, 'HTTP 404')
                || str_contains(strtolower($message), 'nicht gefunden')
                || str_contains(strtolower($message), 'not found');

            return [
                'exists'=>false,
                'path'=>self::RELATIVE_PATH,
                'entries'=>[],
                'error'=>$missing ? null : $message,
            ];
        }
    }

    private static function parseLine(string $line): array
    {
        $parts = array_map('trim', explode('|', $line));

        $timestamp = $parts[0] ?? '';
        $status = strtoupper($parts[1] ?? '');
        $actor = $parts[2] ?? '';
        $role = $parts[3] ?? '';

        $action = '';
        $commandId = '';
        $error = '';

        foreach (array_slice($parts, 4) as $part) {
            if (str_starts_with($part, 'ID=')) {
                $commandId = trim(substr($part, 3));
                continue;
            }
            if (str_starts_with($part, 'Fehler=')) {
                $error = trim(substr($part, 7));
                continue;
            }
            $action = $action === '' ? $part : ($action . ' | ' . $part);
        }

        $sortTs = 0;
        if (preg_match('/^(\d{2})\.(\d{2})\.(\d{2})\s+(\d{2}):(\d{2}):(\d{2})$/', $timestamp, $m)) {
            $year = 2000 + (int)$m[3];
            $sortTs = mktime(
                (int)$m[4], (int)$m[5], (int)$m[6],
                (int)$m[2], (int)$m[1], $year
            ) ?: 0;
        }

        return [
            'timestamp'=>$timestamp,
            'status'=>$status === 'FEHLER' ? 'FEHLER' : 'OK',
            'actor'=>$actor,
            'role'=>$role,
            'action'=>$action,
            'command_id'=>$commandId,
            'error'=>$error,
            'sort_ts'=>$sortTs,
            'search'=>strtolower(trim(implode(' ', [
                $timestamp,$status,$actor,$role,$action,$commandId,$error
            ]))),
        ];
    }
}
