<?php
namespace App\Controllers;
use App\Core\Auth;
use App\Core\View;
use App\Services\LiveServerService;
use App\Services\ServerJoinService;

final class ServerJoinController
{
    public function show(string $id): void
    {
        Auth::requireLogin();
        $json = ($_GET['format'] ?? '') === 'json';
        header('Cache-Control: no-store');
        if (!ctype_digit($id) || !Auth::canServer((int)$id, 'view') || !Auth::canAny(['servers.view','dashboard.view','live.view'])) {
            http_response_code(403);
            if ($json) { header('Content-Type: application/json'); echo '{"error":"Kein Serverzugriff."}'; }
            else View::render('error',['title'=>'Kein Serverzugriff','message'=>'Für diesen Server fehlt die Freigabe.']);
            return;
        }
        $server = LiveServerService::server((int)$id);
        session_write_close(); // A slow panel must not block this user's other tabs.
        try { $join = ServerJoinService::resolve($server); }
        catch (\Throwable) { $join = ['error'=>'Die öffentliche Spieladresse ist gerade nicht verfügbar. Bitte erneut versuchen.']; }
        if ($json) {
            header('Content-Type: application/json; charset=UTF-8');
            if (isset($join['error'])) http_response_code(503);
            echo json_encode($join, JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
        } else View::render('server_join',['title'=>'Server beitreten','server'=>$server,'join'=>$join]);
    }
}
