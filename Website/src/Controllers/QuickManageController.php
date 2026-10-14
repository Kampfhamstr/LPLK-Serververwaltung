<?php
namespace App\Controllers;

use App\Core\Audit;
use App\Core\Auth;
use App\Core\Security;
use App\Services\LiveServerService;
use App\Services\ModerationService;
use App\Services\PterodactylLiveService;

/**
 * v1.8.9: Kompakte AJAX-Schnellverwaltung aus der Serverübersicht.
 * Die vorhandenen granularen Rechte werden für jede Aktion erneut serverseitig geprüft.
 */
final class QuickManageController
{
    private ?string $queuedCommandId = null;

    public function context(string $id): void
    {
        Auth::requireLogin();
        $sid=(int)$id;
        if($sid<=0 || !Auth::can('servers.view') || !Auth::canServer($sid,'view') || !Auth::canServer($sid,'live')){
            $this->json(['ok'=>false,'error'=>'Kein Zugriff auf diesen Server.'],403); return;
        }
        try{
            $server=LiveServerService::server($sid);
            $status=LiveServerService::statusQuick($sid);
            $maps=[];
            foreach(LiveServerService::installedWorkshopMaps($sid) as $workshopId=>$map){
                $maps[]=[
                    'id'=>(string)$workshopId,
                    'name'=>(string)($map['title']??$map['label']??$workshopId),
                    'label'=>(string)($map['label']??$map['title']??$workshopId),
                    'source_folder'=>(string)($map['source_folder']??''),
                ];
            }
            usort($maps,static fn(array $a,array $b):int=>strnatcasecmp($a['name'],$b['name']));
            $players=[];
            foreach((array)($status['players']??[]) as $p){
                $players[]=[
                    'name'=>(string)($p['name']??$p['display_name']??'Unbekannt'),
                    'steam_id64'=>(string)($p['steam_id64']??''),
                    'is_bot'=>!empty($p['is_bot']),
                    'team'=>(string)($p['team']??'Unassigned'),
                    'kills'=>(int)($p['kills']??0),
                    'score'=>(int)($p['score']??0),
                    'user_id'=>(int)($p['user_id']??0),
                    'player_id'=>(int)($p['player_id']??-1),
                ];
            }
            $can=[];
            foreach([
                'map'=>'live.map','kick'=>'live.kick','ban'=>'live.ban','warn'=>'moderation.warn','gag'=>'live.gag','mute'=>'live.mute',
                'slay'=>'live.slay','freeze'=>'live.freeze','respawn'=>'live.respawn','team'=>'live.team','message'=>'live.message',
                'bots'=>'live.bots','rules'=>'live.rules','weapon_mode'=>'live.weapon_mode','power_start'=>'server.power.start',
                'power_restart'=>'server.power.restart','power_stop'=>'server.power.stop','console'=>'server.console.execute'
            ] as $key=>$perm){ $can[$key]=Auth::can($perm); }
            $this->json([
                'ok'=>true,
                'server'=>['id'=>$sid,'name'=>(string)$server['name']],
                'status'=>[
                    'online'=>!empty($status['bridge_online']),
                    'map'=>(string)($status['map']??'—'),
                    'players'=>(int)($status['player_count']??count($players)),
                    'max_players'=>(int)($status['max_players']??0),
                    'ct_score'=>(int)($status['ct_score']??0),
                    't_score'=>(int)($status['t_score']??0),
                ],
                'maps'=>$maps,'players'=>$players,'can'=>$can,
            ]);
        }catch(\Throwable $e){$this->json(['ok'=>false,'error'=>$e->getMessage()],503);}
    }

    public function action(string $id): void
    {
        Auth::requireLogin(); Security::verifyCsrf();
        $sid=(int)$id;
        if($sid<=0 || !Auth::canServer($sid,'live')){$this->json(['ok'=>false,'error'=>'Kein Live-Zugriff.'],403);return;}
        $op=strtolower(trim((string)($_POST['operation']??'')));
        try{
            $this->queuedCommandId=null;
            $result=$this->execute($sid,$op);
            if($this->queuedCommandId!==null){
                $pending=(array)($_SESSION['_quick_manage_pending']??[]);
                foreach($pending as $key=>$entry){
                    if(!is_array($entry)||(int)($entry['created_at']??0)<time()-900)unset($pending[$key]);
                }
                $pending[$this->queuedCommandId]=['server_id'=>$sid,'created_at'=>time()];
                $_SESSION['_quick_manage_pending']=array_slice($pending,-50,null,true);
                $this->json(['ok'=>true,'state'=>'queued','command_id'=>$this->queuedCommandId,'message'=>$result.' Warte auf Bestätigung der Bridge.']);
                return;
            }
            $this->json(['ok'=>true,'message'=>$result]);
        }catch(\Throwable $e){$this->json(['ok'=>false,'error'=>$e->getMessage()],400);}
    }

    public function result(string $id): void
    {
        Auth::requireLogin();
        $sid=(int)$id;
        if($sid<=0||!Auth::canServer($sid,'live')){$this->json(['ok'=>false,'error'=>'Kein Live-Zugriff.'],403);return;}
        $cid=(string)($_GET['command_id']??'');
        $pending=$_SESSION['_quick_manage_pending'][$cid]??null;
        if(!preg_match('/^[A-Za-z0-9_-]{1,100}$/',$cid)||!is_array($pending)
            ||(int)($pending['server_id']??0)!==$sid||(int)($pending['created_at']??0)<time()-900){
            $this->json(['ok'=>false,'error'=>'Diese Befehlsbestätigung ist nicht verfügbar.'],404);return;
        }
        // A slow file connector must not hold the user's session lock while polling.
        if(session_status()===PHP_SESSION_ACTIVE)session_write_close();
        try{
            $result=LiveServerService::result($sid,$cid);
            if($result===null){$this->json(['ok'=>true,'state'=>'pending','command_id'=>$cid]);return;}
            $success=($result['success']??null)===true;
            $this->json(['ok'=>true,'state'=>$success?'confirmed':'failed','command_id'=>$cid,
                'message'=>$success?'Die Bridge hat den Befehl bestätigt.':(string)($result['message']??'Die Bridge konnte den Befehl nicht ausführen.')]);
        }catch(\Throwable $e){$this->json(['ok'=>false,'error'=>$e->getMessage()],503);}
    }

    private function queue(int $serverId,string $action,array $payload=[],?string $targetSteamId=null): string
    {
        return $this->queuedCommandId=LiveServerService::queue($serverId,$action,$payload,$targetSteamId);
    }

    private function execute(int $sid,string $op): string
    {
        $steam=trim((string)($_POST['steam_id64']??''));
        $require=function(string $perm)use($sid):void{Auth::requirePermission($perm);Auth::requireServer($sid,'live');};
        $clean=static function(string $v,int $max=240):string{$v=trim(preg_replace('/[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F\\x7F]/u','',$v)??$v);return function_exists('mb_substr')?mb_substr($v,0,$max):substr($v,0,$max);};
        $validSteam=static fn(string $v):bool=>preg_match('/^7656119\\d{10}$/',$v)===1;

        if($op==='map_change'){
            $require('live.map'); $map=trim((string)($_POST['map']??''));
            if(!preg_match('/^(?:[a-zA-Z0-9_\\-\\.]{2,80}|[0-9]{6,20})$/',$map))throw new \InvalidArgumentException('Ungültige Map.');
            $seconds=(int)($_POST['seconds']??5); if(!in_array($seconds,[0,5,10,30,60],true))$seconds=5;
            $mapName=$map;$folder='';$installed=LiveServerService::installedWorkshopMaps($sid);
            if(isset($installed[$map])){$mapName=(string)($installed[$map]['title']??$map);$folder=(string)($installed[$map]['source_folder']??'');}
            $cid=$this->queue($sid,'map_change',['map'=>$map,'map_name'=>$mapName,'source_folder'=>$folder,'countdown_seconds'=>$seconds,'message'=>'Mapwechsel zu {map} in {seconds} Sekunden.','type'=>'center']);
            Audit::log('quick.map_change','server',(string)$sid,['map'=>$map,'command_id'=>$cid]); return 'Mapwechsel wurde übermittelt.';
        }
        if(in_array($op,['kick','ban','gag','ungag','mute','unmute','slay','respawn'],true)){
            $perm=match($op){'kick'=>'live.kick','ban'=>'live.ban','gag','ungag'=>'live.gag','mute','unmute'=>'live.mute','slay'=>'live.slay','respawn'=>'live.respawn'};$require($perm);
            if(!$validSteam($steam))throw new \InvalidArgumentException('Bitte einen menschlichen Online-Spieler auswählen.');
            $payload=[];
            if($op==='kick')$payload=['reason'=>$clean((string)($_POST['reason']??'Von der Schnellverwaltung entfernt.'),160)];
            if($op==='ban')$payload=['minutes'=>max(0,min(5256000,(int)($_POST['minutes']??60))),'reason'=>$clean((string)($_POST['reason']??'Serverban'),160)];
            if(in_array($op,['gag','mute'],true))$payload=['minutes'=>max(0,min(5256000,(int)($_POST['minutes']??60))),'reason'=>$clean((string)($_POST['reason']??'Moderationsmaßnahme'),160)];
            $cid=$this->queue($sid,$op,$payload,$steam);Audit::log('quick.'.$op,'server_player',$steam,['server_id'=>$sid,'command_id'=>$cid]);return ucfirst($op).' wurde übermittelt.';
        }
        if($op==='warn'){
            $require('moderation.warn'); if(!$validSteam($steam))throw new \InvalidArgumentException('Bitte einen menschlichen Online-Spieler auswählen.');
            $name=$clean((string)($_POST['player_name']??$steam),80);$detail=$clean((string)($_POST['reason']??'Verwarnung über Schnellverwaltung'),700);
            $r=ModerationService::createWarning($steam,$name,'other',$detail,(int)(Auth::user()['id']??0),null,90,false);return 'Verwarnung #'.(int)$r['id'].' wurde gespeichert.';
        }
        if($op==='team'){
            $require('live.team'); if(!$validSteam($steam))throw new \InvalidArgumentException('Bitte einen menschlichen Online-Spieler auswählen.');
            $team=strtolower((string)($_POST['team']??'spectator'));if(!in_array($team,['t','ct','spectator'],true))$team='spectator';
            $cid=$this->queue($sid,'team',['team'=>$team],$steam);Audit::log('quick.team','server_player',$steam,['server_id'=>$sid,'team'=>$team,'command_id'=>$cid]);return 'Teamwechsel wurde übermittelt.';
        }
        if($op==='broadcast'){
            $require('live.message');$message=$clean((string)($_POST['message']??''),500);if($message==='')throw new \InvalidArgumentException('Nachricht darf nicht leer sein.');
            $cid=$this->queue($sid,'broadcast',['type'=>'chat','message'=>$message]);Audit::log('quick.broadcast','server',(string)$sid,['command_id'=>$cid]);return 'Servernachricht wurde übermittelt.';
        }
        if(in_array($op,['power_start','power_restart','power_stop'],true)){
            $signal=substr($op,6);$require('server.power.'.$signal);PterodactylLiveService::power($sid,$signal);Audit::log('quick.power.'.$signal,'server',(string)$sid,[]);return ucfirst($signal).' wurde an Pterodactyl gesendet.';
        }
        if($op==='round_restart'){$require('live.map');$cid=$this->queue($sid,'round_restart',['countdown_seconds'=>5,'message'=>'Rundenneustart in {seconds} Sekunden.','type'=>'center']);Audit::log('quick.round_restart','server',(string)$sid,['command_id'=>$cid]);return 'Rundenneustart wurde übermittelt.';}
        if(in_array($op,['warmup_start','warmup_end'],true)){$require('live.rules');$operation=$op==='warmup_start'?'start':'end';$cid=$this->queue($sid,'warmup_control',['operation'=>$operation]);Audit::log('quick.warmup','server',(string)$sid,['operation'=>$operation,'command_id'=>$cid]);return 'Warmup-Aktion wurde übermittelt.';}
        if(in_array($op,['swap_teams','scramble_teams'],true)){$require('live.rules');$cid=$this->queue($sid,'round_control',['operation'=>$op]);Audit::log('quick.round_control','server',(string)$sid,['operation'=>$op,'command_id'=>$cid]);return 'Teamaktion wurde übermittelt.';}
        if(in_array($op,['bot_add_t','bot_add_ct','bot_kick'],true)){$require('live.bots');$action=$op==='bot_kick'?'bot_kick':'bot_add';$payload=$action==='bot_add'?['team'=>$op==='bot_add_t'?'t':'ct']:[];$cid=$this->queue($sid,$action,$payload);Audit::log('quick.bot','server',(string)$sid,['operation'=>$op,'command_id'=>$cid]);return 'Bot-Aktion wurde übermittelt.';}
        if(in_array($op,['friendly_fire_on','friendly_fire_off','headshot_on','headshot_off'],true)){
            $require('live.rules');$enabled=str_ends_with($op,'_on');
            if(str_starts_with($op,'headshot_')){$require('live.weapon_mode');$cid=$this->queue($sid,'headshot_only',['enabled'=>$enabled]);}
            else{$cid=$this->queue($sid,'server_rules',['friendly_fire'=>$enabled]);}
            Audit::log('quick.rule','server',(string)$sid,['operation'=>$op,'command_id'=>$cid]);return 'Regel wurde übermittelt.';
        }
        throw new \InvalidArgumentException('Unbekannte Schnellaktion.');
    }

    private function json(array $data,int $status=200):void{http_response_code($status);header('Content-Type: application/json; charset=utf-8');header('Cache-Control: no-store');echo json_encode($data,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);}
}
