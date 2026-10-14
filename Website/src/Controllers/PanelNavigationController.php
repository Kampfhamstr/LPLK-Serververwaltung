<?php
namespace App\Controllers;
use App\Core\Auth;
use App\Core\View;
use App\Services\PanelSearch;
use App\Services\PanelFeatureStatus;

final class PanelNavigationController
{
    public function search(): void
    {
        Auth::requireLogin();
        if(!Auth::canAny(['players.view','servers.view'])) {
            http_response_code(403); View::render('error',['title'=>'Suche nicht freigegeben','message'=>'Für die Spieler- oder Serversuche fehlt die Berechtigung.']); return;
        }
        header('Cache-Control: private, no-store');
        $query=is_string($_GET['q']??null)?trim($_GET['q']):'';
        $error=strlen($query)>160?'Bitte höchstens 160 Zeichen eingeben.':null;
        View::render('panel_search',['title'=>'Spieler und Server finden','query'=>$query,'searchError'=>$error,'results'=>$error?[]:PanelSearch::find($query)]);
    }

    public function features(string $id): void
    {
        Auth::requirePermission('players.view'); Auth::requireServer((int)$id,'view');
        header('Content-Type: application/json; charset=UTF-8'); header('Cache-Control: private, no-store');
        echo json_encode(PanelFeatureStatus::read((int)$id),JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);
    }
}
