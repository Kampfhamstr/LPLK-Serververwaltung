<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Security;
use App\Services\PanelUserDirectoryService;

final class PanelUsersController
{
    public function sidebar(): void
    {
        Auth::requireLogin();
        $this->json([
            'ok'=>true,
            'users'=>PanelUserDirectoryService::sidebarUsers(),
            'unread_total'=>PanelUserDirectoryService::unreadTotal(),
            'generated_at'=>gmdate('c'),
        ]);
    }

    public function detail(string $id): void
    {
        Auth::requireLogin();
        try {
            $data = PanelUserDirectoryService::detail((int)$id);
            $this->json(['ok'=>true] + $data);
        } catch (\Throwable $e) {
            $this->json(['ok'=>false,'error'=>$e->getMessage()],404);
        }
    }

    public function message(string $id): void
    {
        Auth::requireLogin();
        Security::verifyCsrf();

        try {
            $message = PanelUserDirectoryService::sendMessage(
                (int)$id,
                (string)($_POST['message'] ?? '')
            );
            $this->json(['ok'=>true,'message'=>$message]);
        } catch (\Throwable $e) {
            $this->json(['ok'=>false,'error'=>$e->getMessage()],422);
        }
    }

    public function account(string $id): void
    {
        Auth::requirePermission('users.manage');
        Security::verifyCsrf();

        try {
            $result = PanelUserDirectoryService::updateAccount(
                (int)$id,
                (string)($_POST['web_role'] ?? 'spieler'),
                isset($_POST['active']) && (string)$_POST['active'] === '1'
            );
            $this->json(['ok'=>true,'user'=>$result]);
        } catch (\Throwable $e) {
            $this->json(['ok'=>false,'error'=>$e->getMessage()],422);
        }
    }

    private function json(array $payload, int $status = 200): void
    {
        http_response_code($status);
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        echo json_encode(
            $payload,
            JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR
        );
    }
}
