<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Audit;
use App\Core\Database;
use App\Core\Security;
use App\Core\View;
use App\Services\CommunityService;

final class ForumController
{
    public function index(): void
    {
        Auth::requirePermission('community.forum.view');
        $category = strtolower(trim((string)($_GET['category'] ?? '')));
        if (!isset(CommunityService::FORUM_CATEGORIES[$category])) $category = '';

        View::render('forum_index', [
            'title' => 'Forum',
            'threads' => CommunityService::threads($category !== '' ? $category : null),
            'categories' => CommunityService::FORUM_CATEGORIES,
            'category' => $category,
            'canModerate' => $this->canModerate(),
        ]);
    }

    public function create(): void
    {
        Auth::requirePermission('community.forum.post');
        Security::verifyCsrf();

        try {
            $threadId = CommunityService::createThread(
                (int)Auth::user()['id'],
                strtolower(trim((string)($_POST['category'] ?? 'allgemein'))),
                (string)($_POST['title'] ?? ''),
                (string)($_POST['body'] ?? '')
            );
            Audit::log('forum.thread.created','forum_thread',(string)$threadId);
            header('Location: /forum/' . $threadId);
        } catch (\Throwable $e) {
            View::flash('error', $e->getMessage());
            header('Location: /forum');
        }
    }

    public function thread(string $id): void
    {
        Auth::requirePermission('community.forum.view');
        $threadId = (int)$id;
        $thread = CommunityService::thread($threadId);

        if (!$thread) {
            http_response_code(404);
            View::render('error',['title'=>'Thema nicht gefunden','message'=>'Dieses Forum-Thema existiert nicht oder wurde entfernt.']);
            return;
        }

        View::render('forum_thread', [
            'title' => $thread['title'],
            'thread' => $thread,
            'posts' => CommunityService::posts($threadId),
            'canModerate' => $this->canModerate(),
        ]);
    }

    public function reply(string $id): void
    {
        Auth::requirePermission('community.forum.post');
        Security::verifyCsrf();

        $threadId = (int)$id;
        try {
            $postId = CommunityService::reply($threadId, (int)Auth::user()['id'], (string)($_POST['body'] ?? ''));
            Audit::log('forum.post.created','forum_post',(string)$postId,['thread_id'=>$threadId]);
        } catch (\Throwable $e) {
            View::flash('error', $e->getMessage());
        }
        header('Location: /forum/' . $threadId . '#reply');
    }

    public function toggleLock(string $id): void
    {
        $this->requireModeration();
        Security::verifyCsrf();

        $threadId = (int)$id;
        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT is_locked FROM forum_threads WHERE id=? AND deleted_at IS NULL');
        $stmt->execute([$threadId]);
        $current = $stmt->fetchColumn();
        if ($current === false) {
            View::flash('error','Thema nicht gefunden.');
            header('Location: /forum');
            return;
        }

        $new = (int)$current ? 0 : 1;
        $pdo->prepare('UPDATE forum_threads SET is_locked=?,updated_at=? WHERE id=?')
            ->execute([$new,date('c'),$threadId]);
        Audit::log('forum.thread.lock','forum_thread',(string)$threadId,['locked'=>(bool)$new]);
        View::flash('success',$new?'Thema wurde gesperrt.':'Thema wurde wieder geöffnet.');
        header('Location: /forum/' . $threadId);
    }

    public function togglePin(string $id): void
    {
        $this->requireModeration();
        Security::verifyCsrf();

        $threadId = (int)$id;
        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT is_pinned FROM forum_threads WHERE id=? AND deleted_at IS NULL');
        $stmt->execute([$threadId]);
        $current = $stmt->fetchColumn();
        if ($current === false) {
            View::flash('error','Thema nicht gefunden.');
            header('Location: /forum');
            return;
        }

        $new = (int)$current ? 0 : 1;
        $pdo->prepare('UPDATE forum_threads SET is_pinned=?,updated_at=? WHERE id=?')
            ->execute([$new,date('c'),$threadId]);
        Audit::log('forum.thread.pin','forum_thread',(string)$threadId,['pinned'=>(bool)$new]);
        View::flash('success',$new?'Thema wurde angeheftet.':'Thema wurde nicht mehr angeheftet.');
        header('Location: /forum/' . $threadId);
    }

    public function deleteThread(string $id): void
    {
        $this->requireModeration();
        Security::verifyCsrf();

        $threadId = (int)$id;
        Database::connection()->prepare('UPDATE forum_threads SET deleted_at=?,updated_at=? WHERE id=?')
            ->execute([date('c'),date('c'),$threadId]);
        Audit::log('forum.thread.deleted','forum_thread',(string)$threadId);
        View::flash('success','Forum-Thema wurde entfernt.');
        header('Location: /forum');
    }

    public function deletePost(string $threadId, string $postId): void
    {
        $this->requireModeration();
        Security::verifyCsrf();

        Database::connection()->prepare('UPDATE forum_posts SET deleted_at=? WHERE id=? AND thread_id=?')
            ->execute([date('c'),(int)$postId,(int)$threadId]);
        Audit::log('forum.post.deleted','forum_post',(string)(int)$postId,['thread_id'=>(int)$threadId]);
        View::flash('success','Beitrag wurde entfernt.');
        header('Location: /forum/' . (int)$threadId);
    }

    private function requireModeration(): void
    {
        Auth::requirePermission('community.forum.moderate');
    }

    private function canModerate(): bool
    {
        return Auth::can('community.forum.moderate');
    }
}
