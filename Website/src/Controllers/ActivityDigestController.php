<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Services\ActivityDigestService;

final class ActivityDigestController
{
    public function show(): void
    {
        Auth::requireLogin();

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store, no-cache, must-revalidate');

        if (!ActivityDigestService::pending()) {
            echo json_encode(
                ['ok'=>true,'pending'=>false],
                JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR
            );
            return;
        }

        try {
            $digest = ActivityDigestService::buildForCurrentLogin();
            ActivityDigestService::markConsumed();

            echo json_encode(
                ['ok'=>true] + $digest,
                JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR
            );
        } catch (\Throwable $e) {
            http_response_code(500);
            echo json_encode(
                ['ok'=>false,'error'=>'Aktivitätsübersicht konnte nicht geladen werden.'],
                JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR
            );
        }
    }
}
