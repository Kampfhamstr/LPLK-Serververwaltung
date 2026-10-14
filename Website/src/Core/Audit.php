<?php
namespace App\Core;

final class Audit
{
    public static function log(string $action, ?string $targetType = null, ?string $targetId = null, array $details = []): void
    {
        try {
            $uid = $_SESSION['user_id'] ?? null;
            $stmt = Database::connection()->prepare('INSERT INTO audit_log(user_id, action, target_type, target_id, details_json, ip_address, created_at) VALUES(?,?,?,?,?,?,?)');
            $stmt->execute([$uid, $action, $targetType, $targetId, $details ? json_encode($details, JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES) : null, Security::clientIp(), date('c')]);
        } catch (\Throwable) {
        }
    }
}
