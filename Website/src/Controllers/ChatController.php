<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Audit;
use App\Core\Security;
use App\Core\View;
use App\Services\CommunityService;

final class ChatController
{
    public function messages(): void
    {
        Auth::requirePermission('community.chat.view');

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        echo json_encode([
            'messages' => CommunityService::latestChatMessages(30),
            'generated_at' => gmdate('c'),
        ], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    }

    public function post(): void
    {
        Auth::requirePermission('community.chat.post');
        Security::verifyCsrf();

        try {
            $message = CommunityService::postChat((int)Auth::user()['id'], (string)($_POST['message'] ?? ''));
            Audit::log('dashboard.chat.posted','chat_message',(string)$message['id']);
            $this->respond(true, ['message'=>$message]);
        } catch (\Throwable $e) {
            $this->respond(false, ['error'=>$e->getMessage()], 422);
        }
    }

    public function delete(string $id): void
    {
        Auth::requirePermission('community.chat.moderate');
        Security::verifyCsrf();

        $messageId = (int)$id;
        CommunityService::deleteChat($messageId);
        Audit::log('dashboard.chat.deleted','chat_message',(string)$messageId);
        $this->respond(true, []);
    }

    private function canModerate(): bool
    {
        return Auth::can('community.chat.moderate');
    }

    private function respond(bool $ok, array $payload, int $status = 200): void
    {
        $accept = strtolower((string)($_SERVER['HTTP_ACCEPT'] ?? ''));
        $xhr = str_contains($accept, 'application/json');

        if ($xhr) {
            http_response_code($status);
            header('Content-Type: application/json; charset=utf-8');
            header('Cache-Control: no-store');
            echo json_encode(['ok'=>$ok] + $payload, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
            return;
        }

        if (!$ok && isset($payload['error'])) View::flash('error', (string)$payload['error']);
        header('Location: /dashboard#dashboard-chat');
    }
}
