<?php
namespace App\Services;
use App\Core\Auth;
final class ServerRankingService
{
    public const LABELS=['rating'=>'PvP-Gesamtwertung','kills'=>'PvP-Kills','headshots'=>'Headshots','pickup'=>'Kills mit übernommener Waffe','playtime'=>'Spielzeit in Sekunden','botAccuracy'=>'Bottraining: Trefferquote'];
    public static function parse(string $raw):array
    {
        $d=PlayerEconomyService::decode($raw,262144);PlayerEconomyService::recent($d['generatedAt']??null);
        if(($d['schema']??null)!==1||($d['profile']??null)!=='standard'||!is_array($d['boards']??null))throw new \RuntimeException('Ungültige Ranglisten.');
        foreach(['PvpEnabled','BotTrainingEnabled','CountPvpWarmup','CountTeamKills'] as $k)if(!is_bool($d['collection'][$k]??null))throw new \RuntimeException('Erfassungsregel fehlt.');
        foreach(['MinShots','MinKills','MinBotShots','MinBotKills'] as $k)PlayerEconomyService::number($d['collection'][$k]??null,1);
        foreach(self::LABELS as $metric=>$label){
            $rows=$d['boards'][$metric]??null;if(!is_array($rows)||count($rows)>100)throw new \RuntimeException('Rangliste fehlt.');
            if(!($d['collection'][$metric==='botAccuracy'?'BotTrainingEnabled':'PvpEnabled'])&&count($rows)>0)throw new \RuntimeException('Deaktivierte Rangliste enthält Werte.');
            $seen=[];$last=INF;$rank=0;
            foreach($rows as $i=>$r){
                if(!is_array($r)||!is_string($r['steamId']??null)||!BankNetworkPlan::steam($r['steamId'])||isset($seen[$r['steamId']])||!is_string($r['name']??null)||strlen($r['name'])>200||!is_int($r['value']??null)&&!is_float($r['value']??null)||!is_finite((float)$r['value'])||$r['value']<0||$r['value']>$last)throw new \RuntimeException('Ungültiger Ranglisteneintrag.');
                $value=(float)$r['value'];if($i===0||$value!==$last)$rank=$i+1;if(($r['rank']??null)!==$rank)throw new \RuntimeException('Ungültiger Rang.');
                PlayerEconomyService::number($r['shots']??null);PlayerEconomyService::number($r['kills']??null);
                if(in_array($metric,['rating','botAccuracy'],true)){$bot=$metric==='botAccuracy';if($value>100.000001||$r['shots']<$d['collection'][$bot?'MinBotShots':'MinShots']||$r['kills']<$d['collection'][$bot?'MinBotKills':'MinKills'])throw new \RuntimeException('Qualifikation fehlt.');}
                $last=$value;$seen[$r['steamId']]=true;
            }
        }
        return $d;
    }
    public static function read(int $serverId):array
    {
        if(!Auth::can('players.view')||!Auth::canServer($serverId,'view'))throw new \RuntimeException('Keine Berechtigung.');
        $s=LiveServerService::server($serverId);if(($s['server_identifier']??'')!=='11110004')return ['state'=>'unavailable','message'=>'Statistikrangliste für diesen Server noch nicht freigegeben.'];
        try{$c=ConnectorFactory::make($s);$root='game/csgo/addons/swiftlys2/data/LPLK_Statistics';$status=PlayerEconomyService::decode($c->read($root.'/status.json'),32768);PlayerEconomyService::recent($status['updatedAt']??null);
            if(!in_array($status['version']??null,['0.2.0','0.2.1'],true)||($status['Enabled']??false)!==true||($status['reloadError']??null)!==null||($status['saveError']??null)!==null)throw new \RuntimeException('Statistik nicht verfügbar.');
            return ['state'=>'ready','data'=>self::parse($c->read($root.'/web/ranking.json'))];
        }catch(\Throwable){return ['state'=>'unavailable','message'=>'Keine aktuelle Statistikrangliste verfügbar.'];}
    }
}
