<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\View;
use App\Services\LiveServerService;
use App\Services\ServerChatService;

final class ServerChatController
{
    public function index(string $id): void
    {
        $sid=(int)$id; Auth::requirePermission('live.view'); Auth::requireServer($sid,'live');
        $server=LiveServerService::server($sid);
        $filters=[
            'player'=>trim((string)($_GET['player']??'')),
            'q'=>trim((string)($_GET['q']??'')),
            'from'=>trim((string)($_GET['from']??'')),
            'to'=>trim((string)($_GET['to']??'')),
            'limit'=>min(250,max(25,(int)($_GET['limit']??100))),
            'before_id'=>max(0,(int)($_GET['before_id']??0)),
        ];
        $messages=ServerChatService::search($sid,$filters);
        $nextBefore=null;
        if(count($messages)>=$filters['limit']){
            $ids=array_map(static fn(array $m):int=>(int)($m['id']??0),$messages);
            $ids=array_filter($ids,static fn(int $v):bool=>$v>0);
            if($ids)$nextBefore=min($ids);
        }
        View::render('server_chat',[
            'title'=>$server['name'].' · Chat-Archiv','server'=>$server,'messages'=>$messages,'filters'=>$filters,
            'total'=>ServerChatService::count($sid),'players'=>ServerChatService::players($sid),'nextBefore'=>$nextBefore
        ]);
    }

    public function messages(string $id): void
    {
        $sid=(int)$id; Auth::requirePermission('live.view'); Auth::requireServer($sid,'live');
        header('Content-Type: application/json; charset=utf-8'); header('Cache-Control: no-store');
        $rows=ServerChatService::search($sid,[
            'player'=>trim((string)($_GET['player']??'')),'q'=>trim((string)($_GET['q']??'')),
            'from'=>trim((string)($_GET['from']??'')),'to'=>trim((string)($_GET['to']??'')),
            'before_id'=>(int)($_GET['before_id']??0),'limit'=>min(100,max(1,(int)($_GET['limit']??30)))
        ]);
        echo json_encode(['messages'=>$rows,'total'=>ServerChatService::count($sid)],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
    }
}
