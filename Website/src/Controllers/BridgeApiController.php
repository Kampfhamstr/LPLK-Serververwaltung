<?php
namespace App\Controllers;

use App\Services\FastLiveService;

final class BridgeApiController
{
    public function exchange(string $id): void
    {
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        $serverId=(int)$id;
        $body=(string)file_get_contents('php://input');
        $timestamp=(string)($_SERVER['HTTP_X_BRIDGE_TIMESTAMP']??'');
        $signature=strtolower(trim((string)($_SERVER['HTTP_X_BRIDGE_SIGNATURE']??'')));
        try{
            $result=FastLiveService::receive($serverId,$body,$timestamp,$signature);
            echo json_encode($result,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
        }catch(\Throwable $e){
            $msg=$e->getMessage();
            http_response_code(str_contains(strtolower($msg),'signatur')||str_contains(strtolower($msg),'zeitstempel')||str_contains(strtolower($msg),'aktiviert')?401:400);
            echo json_encode(['ok'=>false,'error'=>$msg],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
        }
    }
}
