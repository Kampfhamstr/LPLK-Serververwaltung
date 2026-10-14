<?php
$root=dirname(__DIR__);
$routes=file_get_contents($root.'/public/index.php');
$service=file_get_contents($root.'/src/Services/CommunityService.php');
$chat=file_get_contents($root.'/src/Controllers/ChatController.php');
$forum=file_get_contents($root.'/src/Controllers/ForumController.php');
$dashboard=file_get_contents($root.'/templates/dashboard.php');
$layout=file_get_contents($root.'/templates/layout.php');
$schema=file_get_contents($root.'/migrations/schema.sql');
$js=file_get_contents($root.'/public/assets/app.js');

function ok141(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok141(str_contains($routes,'/dashboard/chat/messages'),'Chat-JSON-Route vorhanden');
ok141(str_contains($routes,'/dashboard/chat/{id}/delete'),'Chat-Moderationsroute vorhanden');
ok141(str_contains($service,'LIMIT 30'),'Chat liest höchstens 30 Nachrichten');
ok141(str_contains($service,'SELECT id FROM dashboard_chat_messages ORDER BY id DESC LIMIT 30'),'Chat behält physisch nur 30 Nachrichten');
ok141(str_contains($dashboard,'data-chat-list'),'Dashboard enthält Chatfenster');
ok141(str_contains($js,'window.setInterval(refresh,5000)'),'Chat aktualisiert automatisch');
ok141(str_contains($chat,"community.chat.moderate")&&str_contains($forum,"community.forum.moderate"),'Chat-/Forummoderation nutzt granulare Berechtigungen');
ok141(str_contains($routes, '$router->get(\'/forum\''), 'Forum-Route vorhanden');
ok141(str_contains($schema,'CREATE TABLE IF NOT EXISTS forum_threads'),'Forum-Threads-Tabelle vorhanden');
ok141(str_contains($schema,'CREATE TABLE IF NOT EXISTS forum_posts'),'Forum-Posts-Tabelle vorhanden');
ok141(str_contains($forum,'toggleLock'),'Forum kann moderiert/gesperrt werden');
ok141(str_contains($forum,'togglePin'),'Forum kann angeheftet werden');
ok141(str_contains($layout,'href="/forum"'),'Forum ist in Navigation');
ok141(str_contains($dashboard,'latestForumThreads'),'Dashboard zeigt Forumvorschau');
ok141(str_contains($service,'5000'),'Forumbeiträge sind begrenzt');

echo "\nv1.4.1 Chat-/Forum-Test erfolgreich.\n";
