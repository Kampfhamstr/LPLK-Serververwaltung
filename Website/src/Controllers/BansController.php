<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Security;
use App\Core\View;
use App\Services\AntiAdvertisementService;
use App\Services\GlobalBanService;

final class BansController
{
    public function index(): void
    {
        Auth::requirePermission('moderation.view');
        $filters=[
            'status'=>trim((string)($_GET['status']??'active')),
            'reason'=>trim((string)($_GET['reason']??'')),
            'q'=>trim((string)($_GET['q']??'')),
            'limit'=>min(500,max(25,(int)($_GET['limit']??200))),
        ];
        View::render('bans',[
            'title'=>'Globale Banverwaltung',
            'bans'=>GlobalBanService::search($filters),
            'filters'=>$filters,
            'reasons'=>GlobalBanService::reasons(),
            'durations'=>GlobalBanService::durations(),
            'activeCount'=>GlobalBanService::countActive(),
            'canTemporary'=>Auth::can('moderation.ban.temporary'),
            'canPermanent'=>Auth::can('moderation.ban.permanent'),
            'canUnban'=>Auth::can('moderation.unban'),
        ]);
    }

    public function createForm(): void
    {
        $this->requireBanCreationPermission();
        $chatId=max(0,(int)($_GET['chat_id']??0));
        $chat=$chatId>0?GlobalBanService::chatMessage($chatId):null;
        $advertisingEventId=max(0,(int)($_GET['advertising_event_id']??0));
        $advertisingEvent=$advertisingEventId>0?AntiAdvertisementService::getEvent($advertisingEventId):null;
        if($advertisingEventId>0 && !$advertisingEvent){View::flash('error','Werbeereignis wurde nicht gefunden.');header('Location: /moderation');exit;}
        $steam=trim((string)($_GET['steamid']??($chat['steam_id64']??($advertisingEvent['steam_id64']??''))));
        $name=trim((string)($_GET['name']??($chat['player_name']??($advertisingEvent['player_name']??''))));
        View::render('ban_form',[
            'title'=>'Globalen Ban erstellen','chat'=>$chat,'advertisingEvent'=>$advertisingEvent,'steamId64'=>$steam,'playerName'=>$name,
            'reasons'=>GlobalBanService::reasons(),'durations'=>GlobalBanService::durations(),
            'canTemporary'=>Auth::can('moderation.ban.temporary'),
            'canPermanent'=>Auth::can('moderation.ban.permanent'),
            'returnTo'=>$this->safeReturn((string)($_GET['_return']??'/bans'))
        ]);
    }

    public function create(): void
    {
        Auth::requireLogin(); Security::verifyCsrf();
        try{
            $steam=trim((string)($_POST['steam_id64']??''));
            $name=trim((string)($_POST['player_name']??''));
            $reasonCode=trim((string)($_POST['reason_code']??'other'));
            $detail=trim((string)($_POST['reason_detail']??''));
            $minutes=$this->minutesFromPost();
            $this->requireDurationPermission($minutes);
            $chatId=max(0,(int)($_POST['chat_message_id']??0));
            $advertisingEventId=max(0,(int)($_POST['advertising_event_id']??0));
            $id=GlobalBanService::create($steam,$name,$reasonCode,$detail,$minutes,(int)(Auth::user()['id']??0),$chatId?:null,$advertisingEventId?:null);
            View::flash('success','Globaler Ban #'.$id.' wurde gespeichert und über Fast Live an alle Server verteilt.');
            header('Location: ' . $this->safeReturn((string)($_POST['_return']??'/bans'))); exit;
        }catch(\Throwable $e){
            View::flash('error',$e->getMessage());
            header('Location: /bans/new'); exit;
        }
    }

    public function update(string $id): void
    {
        Auth::requireLogin(); Security::verifyCsrf();
        try{
            $minutes=$this->minutesFromPost();
            $this->requireDurationPermission($minutes);
            GlobalBanService::update((int)$id,trim((string)($_POST['reason_code']??'other')),trim((string)($_POST['reason_detail']??'')),$minutes);
            View::flash('success','Globaler Ban #'.(int)$id.' wurde aktualisiert.');
        }catch(\Throwable $e){View::flash('error',$e->getMessage());}
        header('Location: /bans'); exit;
    }

    public function unban(string $id): void
    {
        Auth::requirePermission('moderation.unban'); Security::verifyCsrf();
        try{
            GlobalBanService::unban((int)$id,trim((string)($_POST['unban_reason']??'')),(int)(Auth::user()['id']??0));
            View::flash('success','Globaler Ban #'.(int)$id.' wurde aufgehoben. Die Entbannung gilt auf allen Servern.');
        }catch(\Throwable $e){View::flash('error',$e->getMessage());}
        header('Location: ' . $this->safeReturn((string)($_POST['_return']??'/bans'))); exit;
    }

    private function requireBanCreationPermission(): void
    {
        Auth::requireLogin();
        if(Auth::can('moderation.ban.temporary')||Auth::can('moderation.ban.permanent'))return;
        http_response_code(403);
        View::render('error',['title'=>'Zugriff verweigert','message'=>'Für globale Bans fehlt die erforderliche Berechtigung.']);
        exit;
    }

    private function requireDurationPermission(int $minutes): void
    {
        $permission=$minutes===0?'moderation.ban.permanent':'moderation.ban.temporary';
        if(Auth::can($permission))return;
        throw new \RuntimeException($minutes===0?'Permanente Bans sind für dieses Konto nicht erlaubt.':'Zeitliche Bans sind für dieses Konto nicht erlaubt.');
    }

    private function safeReturn(string $value): string
    {
        $value=trim($value);
        if($value==='' || !str_starts_with($value,'/') || str_starts_with($value,'//') || preg_match('/[\r\n]/',$value)) return '/bans';
        return substr($value,0,1000);
    }

    private function minutesFromPost(): int
    {
        $minutes=(int)($_POST['minutes']??1440);
        if($minutes===-1) $minutes=(int)($_POST['custom_minutes']??60);
        return max(0,min(5256000,$minutes));
    }
}
