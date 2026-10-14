<?php
namespace App\Services;

use App\Core\Auth;

final class PlayerStatisticsService
{
    private const ROOT='game/csgo/addons/swiftlys2/data/LPLK_Statistics';
    public static function read(int $serverId,string $steam):array
    {
        if(!Auth::can('players.view')||!Auth::canServer($serverId,'view'))throw new \RuntimeException('Keine Berechtigung.');
        if(!preg_match('/^7656119\d{10}$/D',$steam))return ['state'=>'missing','message'=>'Für diesen Account liegt keine gültige SteamID64 vor.'];
        $server=LiveServerService::server($serverId);
        // Initial rollout is explicitly limited to this isolated test profile.
        if(($server['server_identifier']??'')!=='11110004')return ['state'=>'not_installed','message'=>'LPLK_Statistics ist für diesen Server noch nicht freigegeben.'];
        try {
            $connector=ConnectorFactory::make($server);
            $statusRaw=$connector->read(self::ROOT.'/status.json');
            if(strlen($statusRaw)>32768)throw new \RuntimeException('Status too large');
            $status=json_decode($statusRaw,true,32,JSON_THROW_ON_ERROR);
            if(($status['Enabled']??false)!==true)return ['state'=>'disabled','message'=>'Das Statistik-Addon ist ausgeschaltet.'];
            $time=strtotime((string)($status['updatedAt']??''));
            if(!$time||$time>time()+60||time()-$time>120)return ['state'=>'stale','message'=>'Keine aktuelle Rückmeldung des Statistik-Addons. Gespeicherte Werte werden nicht als aktueller Stand angezeigt.'];
            $file=self::ROOT.'/web/'.$steam.'.json';
            if(!$connector->exists($file))return ['state'=>'empty','message'=>'Für diesen Spieler wurden auf diesem Testserver noch keine Statistiken erfasst.'];
            return ['state'=>'ready','data'=>self::parse($connector->read($file),$steam),'runtime'=>['version'=>$status['version']??null,'pvpEnabled'=>$status['PvpEnabled']??null,'weaponTracking'=>$status['WeaponTracking']??null,'saveError'=>$status['saveError']??null,'reloadError'=>$status['reloadError']??null]];
        } catch(\Throwable) {
            return ['state'=>'unavailable','message'=>'Statistik derzeit nicht abrufbar. Verbindung oder Datenformat prüfen.'];
        }
    }
    public static function parse(string $raw,string $steam):array
    {
        if(strlen($raw)>131072)throw new \RuntimeException('Export too large');
        $d=json_decode($raw,true,32,JSON_THROW_ON_ERROR);
        if(($d['schema']??null)!==1||($d['steamId']??null)!==$steam||($d['profile']??null)!=='standard')throw new \RuntimeException('Identity/schema mismatch');
        foreach(['generatedAt','lastSeen'] as $k)if(!is_string($d[$k]??null)||strtotime($d[$k])===false)throw new \RuntimeException('Invalid timestamp');
        $result=['generatedAt'=>$d['generatedAt'],'lastSeen'=>$d['lastSeen'],'profile'=>'standard'];
        $groups=['pvp'=>['Shots','Kills','Headshots','Deaths'],'bots'=>['Shots','Hits','HeadHits','Kills','Headshots','Deaths'],'extra'=>['Assists','FlashAssists','Damage','DamageTaken','Rounds','Wins','PlaySeconds','BombsPlanted','BombsDefused','NoScope','Wallbang','ThroughSmoke','Blind','InAir','EnemyWeapon','TeamWeapon','UnknownWeaponOrigin','BestPickupStreak']];
        foreach($groups as $group=>$keys)foreach($keys as $key){$v=$d[$group][$key]??null;if(!is_int($v)||$v<0||$v>2000000000000)throw new \RuntimeException('Invalid counter');$result[$group][$key]=$v;}
        foreach(['kd','headshotPercent','rating'] as $key){$v=$d['pvp'][$key]??null;if($v!==null&&(!is_numeric($v)||!is_finite((float)$v)||$v<0))throw new \RuntimeException('Invalid ratio');$result['pvp'][$key]=$v;}
        $since=$d['extra']['Since']??null;if($since!==null&&(!is_string($since)||strtotime($since)===false))throw new \RuntimeException('Invalid collection start');$result['extra']['Since']=$since;
        if(!is_array($d['weapons']??null)||count($d['weapons'])>300)throw new \RuntimeException('Invalid weapon list');
        $result['weapons']=[];foreach($d['weapons'] as $key=>$v){if(!preg_match('/^[a-z0-9_]{1,40}$/D',(string)$key)||!is_int($v)||$v<0||$v>2000000000000)throw new \RuntimeException('Invalid weapon');$result['weapons'][$key]=$v;}arsort($result['weapons']);
        $result['collection']=[];foreach(['PvpEnabled','BotTrainingEnabled','WeaponTracking'] as $key){if(!is_bool($d['collection'][$key]??null))throw new \RuntimeException('Invalid collection state');$result['collection'][$key]=$d['collection'][$key];}
        return $result;
    }
}
