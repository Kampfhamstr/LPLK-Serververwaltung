<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Security;
use App\Core\View;
use App\Services\AntiAdvertisementService;
use App\Services\ModerationService;

final class ModerationController
{
    public function statuses(): void
    {
        Auth::requirePermission('moderation.view');
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');

        $raw = (string)($_GET['steamids'] ?? '');
        $ids = preg_split('/[,;\s]+/', $raw) ?: [];
        try {
            $map = ModerationService::statusMap($ids);
            echo json_encode(['ok'=>true,'players'=>$map], JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
        } catch (\Throwable $e) {
            http_response_code(500);
            echo json_encode(['ok'=>false,'error'=>'Moderationsstatus konnte nicht geladen werden.'], JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
        }
    }

    public function index():void
    {
        Auth::requirePermission('moderation.view');
        $q=trim((string)($_GET['q']??''));
        $advertisingStatus=trim((string)($_GET['advertising_status']??''));
        $moderationData=ModerationService::dashboard($q,$advertisingStatus);
        View::render('moderation',['title'=>'Moderation','q'=>$q,'advertisingStatus'=>$advertisingStatus,'moderationData'=>$moderationData,'reasons'=>ModerationService::reasons(),'durations'=>ModerationService::durations()]);
    }
    public function reportAdvertising():void
    {
        Auth::requirePermission('moderation.evidence'); Security::verifyCsrf();
        try{
            $id=AntiAdvertisementService::reportChatMessage(max(0,(int)($_POST['chat_message_id']??0)),(int)(Auth::user()['id']??0));
            View::flash('success','Werbe-/Spammeldung #'.$id.' wurde mit Chat-Beleg an die Moderation übergeben.');
        }catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function reviewAdvertising(string $id):void
    {
        Auth::requireLogin(); Security::verifyCsrf();
        $status=trim((string)($_POST['status']??'resolved'));
        if($status==='protected'){
            if(!Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent'])){
                View::flash('error','Für die serverübergreifende Werbeschutz-Sperre fehlt die Ban-Berechtigung.');$this->back();
            }
        }else{
            Auth::requirePermission('moderation.evidence');
        }
        try{
            AntiAdvertisementService::reviewEvent((int)$id,$status,(string)($_POST['note']??''),(int)(Auth::user()['id']??0));
            $label=$status==='protected'?'SteamID wurde serverübergreifend in den Werbeschutz übernommen.':($status==='false_positive'?'Ereignis wurde als Fehlalarm markiert.':'Ereignis wurde abgeschlossen.');
            View::flash('success',$label);
        }catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function addAdvertisingRule():void
    {
        Auth::requirePermission('moderation.advertising.rules'); Security::verifyCsrf();
        try{
            AntiAdvertisementService::addRule((string)($_POST['rule_type']??''),(string)($_POST['rule_value']??''),(int)(Auth::user()['id']??0));
            View::flash('success','Werbeschutzregel gespeichert und wird über Fast Live an alle Server verteilt.');
        }catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function toggleAdvertisingRule():void
    {
        Auth::requirePermission('moderation.advertising.rules'); Security::verifyCsrf();
        try{
            AntiAdvertisementService::setRuleActive((string)($_POST['rule_type']??''),(string)($_POST['rule_value']??''),!empty($_POST['active']),(int)(Auth::user()['id']??0));
            View::flash('success','Werbeschutzregel aktualisiert und wird über Fast Live an alle Server verteilt.');
        }catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function warning():void
    {
        Auth::requirePermission('moderation.warn'); Security::verifyCsrf();
        try{
            $r=ModerationService::createWarning(trim((string)$_POST['steam_id64']),trim((string)($_POST['player_name']??'')),trim((string)($_POST['reason_code']??'other')),trim((string)($_POST['detail']??'')),(int)(Auth::user()['id']??0),max(0,(int)($_POST['chat_message_id']??0))?:null,max(0,(int)($_POST['expires_days']??90)),!empty($_POST['auto_escalate'])&&Auth::can('moderation.ban.temporary'));
            $msg='Verwarnung #'.$r['id'].' gespeichert. Aktive Verwarnungen: '.$r['active_count'].'.'; if(!empty($r['global_ban_id']))$msg.=' Automatische Eskalation: globaler Ban #'.$r['global_ban_id'].'.';
            View::flash('success',$msg);
        }catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function resolveWarning(string $id):void
    {
        Auth::requirePermission('moderation.warn'); Security::verifyCsrf();
        try{ModerationService::resolveWarning((int)$id,(string)($_POST['reason']??''),(int)(Auth::user()['id']??0));View::flash('success','Verwarnung erledigt.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function note():void
    {
        Auth::requirePermission('moderation.notes'); Security::verifyCsrf();
        try{ModerationService::addNote(trim((string)$_POST['steam_id64']),trim((string)($_POST['player_name']??'')),(string)($_POST['note']??''),(int)(Auth::user()['id']??0));View::flash('success','Interne Moderator-Notiz gespeichert.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function evidence():void
    {
        Auth::requirePermission('moderation.evidence'); Security::verifyCsrf();
        try{ModerationService::addEvidence(trim((string)$_POST['steam_id64']),trim((string)($_POST['player_name']??'')),trim((string)($_POST['evidence_type']??'note')),(string)($_POST['label']??''),(string)($_POST['value']??''),(int)(Auth::user()['id']??0),max(0,(int)($_POST['chat_message_id']??0))?:null,max(0,(int)($_POST['global_ban_id']??0))?:null,max(0,(int)($_POST['warning_id']??0))?:null);View::flash('success','Beweis gespeichert.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function sanction():void
    {
        Auth::requirePermission('moderation.global_comms'); Security::verifyCsrf();
        try{ModerationService::createSanction(trim((string)$_POST['steam_id64']),trim((string)($_POST['player_name']??'')),trim((string)($_POST['sanction_type']??'gag')),trim((string)($_POST['reason_code']??'other')),trim((string)($_POST['detail']??'')),$this->minutes(),(int)(Auth::user()['id']??0),max(0,(int)($_POST['chat_message_id']??0))?:null);View::flash('success','Globale Kommunikationssperre gespeichert und wird über Fast Live auf alle Server verteilt.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function revokeSanction(string $id):void
    {
        Auth::requirePermission('moderation.global_comms'); Security::verifyCsrf();
        try{ModerationService::revokeSanction((int)$id,(string)($_POST['reason']??''),(int)(Auth::user()['id']??0));View::flash('success','Globale Kommunikationssperre aufgehoben.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function appeal():void
    {
        Auth::requirePermission('moderation.appeals'); Security::verifyCsrf();
        try{ModerationService::createAppeal(max(0,(int)($_POST['global_ban_id']??0)),trim((string)$_POST['steam_id64']),trim((string)($_POST['player_name']??'')),(string)($_POST['statement']??''),(int)(Auth::user()['id']??0));View::flash('success','Appeal erfasst.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    public function appealUpdate(string $id):void
    {
        Auth::requirePermission('moderation.appeals'); Security::verifyCsrf();
        try{ModerationService::updateAppeal((int)$id,trim((string)($_POST['status']??'reviewing')),(string)($_POST['decision']??''),(int)(Auth::user()['id']??0));View::flash('success','Appeal aktualisiert.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}
        $this->back();
    }
    private function minutes():int{$m=(int)($_POST['minutes']??60);if($m===-1)$m=(int)($_POST['custom_minutes']??60);return max(0,min(5256000,$m));}
    private function back():never{$r=trim((string)($_POST['_return']??'/moderation'));if($r===''||!str_starts_with($r,'/')||str_starts_with($r,'//')||preg_match('/[\r\n]/',$r))$r='/moderation';header('Location: '.substr($r,0,1000));exit;}
}
