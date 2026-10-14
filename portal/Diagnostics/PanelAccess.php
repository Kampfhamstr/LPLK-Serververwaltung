<?php
// Fixed, read-only diagnostic. Never select API tokens, identifiers, passwords or 2FA secrets.
ini_set('display_errors', '0');
ini_set('log_errors', '0');
ob_start();
try {
    require getcwd() . '/vendor/autoload.php';
    $app = require getcwd() . '/bootstrap/app.php';
    $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $db = $app->make('db')->connection();
    $schema = $db->getSchemaBuilder();
    $report = ['twoFactorRequired' => (int) config('pterodactyl.auth.2fa_required'), 'keys' => [], 'recentIpBlocks' => []];
    foreach ($db->table('api_keys')->orderByDesc('id')->limit(10)->get(['id', 'user_id', 'key_type', 'allowed_ips', 'created_at', 'last_used_at']) as $key) {
        $user = $db->table('users')->where('id', $key->user_id)->first(['root_admin', 'use_totp']);
        $allowed = json_decode($key->allowed_ips ?? '[]', true);
        $ips = [];
        foreach (is_array($allowed) ? $allowed : [] as $ip) {
            if (is_string($ip) && preg_match('/^[0-9a-fA-F:.\/]{1,64}$/D', $ip)) $ips[] = $ip;
        }
        $report['keys'][] = [
            'id' => (int) $key->id,
            'type' => (int) $key->key_type === 1 ? 'client' : ((int) $key->key_type === 2 ? 'application' : 'other'),
            'allowedIps' => $ips,
            'createdAt' => $key->created_at,
            'lastUsedAt' => $key->last_used_at,
            'accountIsAdmin' => $user ? (bool) $user->root_admin : null,
            'accountHasTwoFactor' => $user ? (bool) $user->use_totp : null,
        ];
    }
    if ($schema->hasTable('activity_logs') && $schema->hasColumn('activity_logs', 'timestamp')) {
        $columns = ['timestamp', 'ip'];
        $hasKeyId = $schema->hasColumn('activity_logs', 'api_key_id');
        if ($hasKeyId) $columns[] = 'api_key_id';
        foreach ($db->table('activity_logs')->where('event', 'auth:ip-blocked')->where('timestamp', '>=', gmdate('Y-m-d H:i:s', time() - 86400))->orderByDesc('timestamp')->limit(10)->get($columns) as $event) {
            $report['recentIpBlocks'][] = [
                'at' => $event->timestamp,
                'ip' => filter_var($event->ip, FILTER_VALIDATE_IP) ? $event->ip : null,
                'keyId' => $hasKeyId && $event->api_key_id !== null ? (int) $event->api_key_id : null,
            ];
        }
    }
    ob_end_clean();
    echo json_encode($report, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR), "\n";
} catch (Throwable $error) {
    if (ob_get_level()) ob_end_clean();
    echo '{"error":"Panel-Zugriffsdiagnose konnte nicht gelesen werden."}', "\n";
    exit(1);
}
