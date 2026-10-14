<?php
namespace App\Services;
use App\Core\Auth;

final class WeaponRankingService
{
    public const GROUPS=[
        'Messer & Zeus'=>['knife'=>'Messer','taser'=>'Zeus x27'],
        'Pistolen'=>['glock'=>'Glock-18','hkp2000'=>'P2000','usp_silencer'=>'USP-S','elite'=>'Dual Berettas','p250'=>'P250','fiveseven'=>'Five-SeveN','tec9'=>'Tec-9','cz75a'=>'CZ75-Auto','deagle'=>'Desert Eagle','revolver'=>'R8 Revolver'],
        'MPs'=>['mac10'=>'MAC-10','mp9'=>'MP9','mp7'=>'MP7','mp5sd'=>'MP5-SD','ump45'=>'UMP-45','p90'=>'P90','bizon'=>'PP-Bizon'],
        'Gewehre'=>['ak47'=>'AK-47','m4a1'=>'M4A4','m4a1_silencer'=>'M4A1-S','galilar'=>'Galil AR','famas'=>'FAMAS','aug'=>'AUG','sg556'=>'SG 553'],
        'Scharfschützen'=>['awp'=>'AWP','ssg08'=>'SSG 08','scar20'=>'SCAR-20','g3sg1'=>'G3SG1'],
        'Schrot & MG'=>['nova'=>'Nova','xm1014'=>'XM1014','mag7'=>'MAG-7','sawedoff'=>'Sawed-Off','m249'=>'M249','negev'=>'Negev'],
        'Granaten'=>['hegrenade'=>'HE-Granate','flashbang'=>'Blendgranate','smokegrenade'=>'Rauchgranate','decoy'=>'Ködergranate','molotov'=>'Molotow','incgrenade'=>'Brandgranate','inferno'=>'Feuer ohne eindeutigen Granatentyp'],
        'Sprengstoff'=>['c4'=>'C4-Explosion']
    ];
    public static function labels():array {return array_merge(...array_values(self::GROUPS));}
    public static function selected(mixed $value):string {return is_string($value)&&isset(self::labels()[$value])?$value:'knife';}
    public static function parse(string $raw):array
    {
        $d=PlayerEconomyService::decode($raw,4194304);PlayerEconomyService::recent($d['generatedAt']??null);
        if(($d['schema']??null)!==1||($d['profile']??null)!=='standard'||!is_array($d['boards']??null))throw new \RuntimeException('Ungültige Waffenranglisten.');
        foreach(['Enabled','WeaponTracking','CountWeaponWarmup','CountTeamKills'] as $key)if(!is_bool($d['collection'][$key]??null))throw new \RuntimeException('Erfassungsregel fehlt.');
        $labels=self::labels();if(count($d['boards'])!==count($labels)||array_diff_key($labels,$d['boards']))throw new \RuntimeException('Waffenkatalog unvollständig.');
        foreach($labels as $weapon=>$label){
            $rows=$d['boards'][$weapon];if(!is_array($rows)||!array_is_list($rows)||count($rows)>100)throw new \RuntimeException('Ungültige Waffenrangliste.');
            if((!$d['collection']['Enabled']||!$d['collection']['WeaponTracking'])&&$rows)throw new \RuntimeException('Deaktivierte Waffenrangliste enthält Werte.');
            $seen=[];$last=null;$rank=0;
            foreach($rows as $i=>$r){
                if(!is_array($r)||!is_string($r['steamId']??null)||!BankNetworkPlan::steam($r['steamId'])||isset($seen[$r['steamId']])||!is_string($r['name']??null)||strlen($r['name'])>400||!is_int($r['value']??null)||$r['value']<1||$r['value']>2000000000000||($last!==null&&$r['value']>$last))throw new \RuntimeException('Ungültiger Waffenranglisteneintrag.');
                if($i===0||$r['value']!==$last)$rank=$i+1;if(($r['rank']??null)!==$rank)throw new \RuntimeException('Ungültiger Waffenrang.');
                $last=$r['value'];$seen[$r['steamId']]=true;
            }
        }
        return $d;
    }
    public static function read(int $serverId):array
    {
        if(!Auth::can('players.view')||!Auth::canServer($serverId,'view'))throw new \RuntimeException('Keine Berechtigung.');
        $server=LiveServerService::server($serverId);if(($server['server_identifier']??'')!=='11110004')return ['state'=>'unavailable','message'=>'Waffenranglisten für diesen Server noch nicht freigegeben.'];
        try{
            $connector=ConnectorFactory::make($server);$root='game/csgo/addons/swiftlys2/data/LPLK_Statistics';
            $status=PlayerEconomyService::decode($connector->read($root.'/status.json'),32768);PlayerEconomyService::recent($status['updatedAt']??null);
            if(($status['Enabled']??false)!==true||($status['WeaponTracking']??false)!==true)return ['state'=>'disabled','message'=>'Waffenstatistiken sind auf diesem Server ausgeschaltet.'];
            if(($status['version']??null)!=='0.2.1'||($status['saveError']??null)!==null||($status['weaponExportError']??null)!==null||($status['reloadError']??null)!==null)throw new \RuntimeException('Kein freigegebener Waffenexport.');
            $data=self::parse($connector->read($root.'/web/weapons.json'));
            if(!$data['collection']['Enabled']||!$data['collection']['WeaponTracking'])return ['state'=>'disabled','message'=>'Waffenstatistiken sind auf diesem Server ausgeschaltet.'];
            return ['state'=>'ready','data'=>$data];
        }catch(\Throwable){return ['state'=>'unavailable','message'=>'Keine aktuelle Waffenrangliste verfügbar.'];}
    }
}
