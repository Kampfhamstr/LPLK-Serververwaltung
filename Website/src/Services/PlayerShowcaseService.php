<?php
namespace App\Services;
use App\Core\Auth;

final class PlayerShowcaseService
{
    public static function identity(array $player):array
    {
        $steam=(string)($player['steam_id64']??'');$valid=BankNetworkPlan::steam($steam);
        $name=trim((string)($player['steam_personaname']??''))?:trim((string)($player['display_name']??''));
        $name=$name!==''?$name:'Spieler';
        return ['name'=>$name,'initial'=>mb_strtoupper(mb_substr($name,0,1)),'steam'=>$steam,
            'profileUrl'=>$valid?'https://steamcommunity.com/profiles/'.$steam:null,
            'avatarUrl'=>$valid?'/players/'.(int)$player['id'].'/avatar':null,
            'visibility'=>match((int)($player['steam_visibility']??0)){1=>'Privates Profil',2=>'Nur für Freunde',3=>'Öffentliches Profil',default=>'Sichtbarkeit nicht bekannt'},
            'syncedAt'=>self::date($player['steam_last_synced_at']??null)];
    }

    public static function date(?string $value):?string
    {
        try{return $value?(new \DateTimeImmutable($value))->setTimezone(new \DateTimeZone('Europe/Berlin'))->format('d.m.Y H:i'):null;}catch(\Throwable){return null;}
    }

    public static function read(int $serverId,string $steam):array
    {
        if(!Auth::can('players.view')||!Auth::canServer($serverId,'view'))throw new \RuntimeException('Keine Berechtigung.');
        $stats=PlayerStatisticsService::read($serverId,$steam);
        if($stats['state']!=='ready')return ['state'=>$stats['state'],'message'=>$stats['message'],'cards'=>[],'facts'=>[]];
        $runtime=$stats['runtime'];
        if(!in_array($runtime['version'],['0.2.0','0.2.1'],true)||!is_bool($runtime['pvpEnabled'])||$runtime['saveError']!==null||$runtime['reloadError']!==null)return ['state'=>'unavailable','message'=>'Kein freigegebener, fehlerfreier Statistikstand verfügbar.','cards'=>[],'facts'=>[]];
        $stats['data']['collection']['PvpEnabled']=$stats['data']['collection']['PvpEnabled']&&$runtime['pvpEnabled'];
        $ranking=$runtime['pvpEnabled']?ServerRankingService::read($serverId):['state'=>'disabled'];
        $weapons=$runtime['version']==='0.2.1'&&($runtime['weaponTracking']??false)===true?WeaponRankingService::read($serverId):['state'=>'unavailable'];
        return self::compose($stats['data'],$ranking,$steam,$weapons);
    }

    public static function compose(array $data,array $ranking,string $steam,array $weapons=['state'=>'unavailable']):array
    {
        $pvp=$data['collection']['PvpEnabled'];
        $weaponReady=$data['collection']['WeaponTracking']&&($weapons['state']??null)==='ready'&&($weapons['data']['collection']['Enabled']??false)&&($weapons['data']['collection']['WeaponTracking']??false);
        if(!$pvp&&!$weaponReady)return ['state'=>'disabled','message'=>'PvP-Statistiken sind auf diesem Server ausgeschaltet. Bottraining findest du getrennt in der Statistikansicht.','cards'=>[],'facts'=>[]];
        $p=$data['pvp'];$e=$data['extra'];$kills=$p['Kills'];$deaths=$p['Deaths'];
        if($pvp&&$p['Headshots']>$kills)return ['state'=>'unavailable','message'=>'Die Statistikwerte sind derzeit nicht konsistent.','cards'=>[],'facts'=>[]];
        $num=static fn(int|float $v,int $decimals=0)=>number_format($v,$decimals,',','.');
        $hs=$kills>0?100*$p['Headshots']/$kills:null;
        $facts=[['label'=>'PvP-Kills','value'=>$num($kills)],['label'=>'K/D · PvP','value'=>$deaths>0?$num($kills/$deaths,2):'—'],
            ['label'=>'Kopfschussquote · PvP','value'=>$hs!==null?$num($hs,1).' %':'—'],
            ['label'=>'Spielzeit · inkl. Bottraining','value'=>$num(intdiv($e['PlaySeconds'],3600)).' h '.$num(intdiv($e['PlaySeconds']%3600,60)).' min']];
        if(!$pvp)$facts=[];
        $cards=[];$used=[];$rankingAt=null;$weaponRankingAt=null;$rules='';
        if($pvp&&($ranking['state']??null)==='ready'){
            $r=$ranking['data'];$rankingAt=self::date($r['generatedAt']);
            $rules='PvP-Ranglisten: '.($r['collection']['CountPvpWarmup']?'mit':'ohne').' Warmup, '.($r['collection']['CountTeamKills']?'mit':'ohne').' Teamkills.';
            foreach(['rating','pickup','headshots','kills'] as $metric){
                $board=$r['boards'][$metric]??[];
                // Top-100 exports cannot prove a percentile or total population. Require at least five listed competitors.
                if(count($board)<5)continue;
                foreach($board as $row){
                    if($row['steamId']!==$steam||$row['rank']>3||$row['value']<=0)continue;
                    $ties=count(array_filter($board,static fn($entry)=>$entry['rank']===$row['rank']))>1;
                    $label=match($metric){'rating'=>'PvP-Gesamtwertung','pickup'=>'Übernommene Waffen','headshots'=>'Kopfschüsse',default=>'PvP-Kills'};
                    $cards[]=['key'=>$metric,'kind'=>'ranking','rank'=>$row['rank'],'label'=>$label,'value'=>($ties?'Geteilter Platz ':'Platz ').$row['rank'],
                        'detail'=>$num($row['value'],$metric==='rating'?1:0).($metric==='rating'?' Punkte':($metric==='headshots'?' Kopfschüsse':' Kills')).' · Serverrangliste mit mindestens '.count($board).' gewerteten Spielern'];
                    $used[$metric]=true;break;
                }
            }
        }
        if($weaponReady){
            $w=$weapons['data'];$weaponRankingAt=self::date($w['generatedAt']);
            $rules.=($rules?' ':'').'Waffenkills: menschliche Spieler, '.($w['collection']['CountWeaponWarmup']?'mit':'ohne').' Warmup, '.($w['collection']['CountTeamKills']?'mit':'ohne').' Teamkills. Messervarianten werden zusammengefasst.';
            foreach(WeaponRankingService::labels() as $weapon=>$label){
                $board=$w['boards'][$weapon];if(count($board)<5)continue;
                foreach($board as $row){
                    if($row['steamId']!==$steam||$row['rank']>3)continue;
                    $ties=count(array_filter($board,static fn($entry)=>$entry['rank']===$row['rank']))>1;
                    $cards[]=['key'=>'weapon_'.$weapon,'weapon'=>$weapon,'kind'=>'ranking','rank'=>$row['rank'],'label'=>$label.'-Kills','value'=>($ties?'Geteilter Platz ':'Platz ').$row['rank'],
                        'detail'=>$num($row['value']).' Kills · Waffenrangliste mit mindestens '.count($board).' gewerteten Spielern'];break;
                }
            }
        }
        usort($cards,static fn($a,$b)=>$a['rank']<=>$b['rank']);
        $milestone=static function(string $key,string $label,string $value,string $detail)use(&$cards,&$used){if(!isset($used[$key])){$cards[]=['key'=>$key,'kind'=>'milestone','label'=>$label,'value'=>$value,'detail'=>$detail];$used[$key]=true;}};
        if($pvp){
        if($kills>=25&&$hs>=40)$milestone('headshots','Treffsicher',$num($hs,1).' %','Persönlicher Meilenstein: mindestens 40 % Kopfschüsse bei mindestens 25 PvP-Kills.');
        if($e['BestPickupStreak']>=3)$milestone('pickup','Mit fremder Ausrüstung',$num($e['BestPickupStreak']).' in Folge','Beste Killserie mit einer beobachtet übernommenen Waffe · persönliche Bestleistung.');
        if($e['EnemyWeapon']+$e['TeamWeapon']>=5)$milestone('pickup','Waffen übernommen',$num($e['EnemyWeapon']+$e['TeamWeapon']).' Kills','Persönlicher Meilenstein: mindestens 5 Kills mit nachweislich übernommener Waffe. Unbekannte Herkunft zählt nicht.');
        if($e['BombsPlanted']+$e['BombsDefused']>=10)$milestone('objectives','Bomben gelegt & entschärft',$num($e['BombsPlanted']).' gelegt · '.$num($e['BombsDefused']).' entschärft','Persönlicher Meilenstein: zusammen mindestens 10 gelegte oder entschärfte Bomben. Beide Werte werden getrennt gezählt, jeweils ohne Warmup.');
        if($e['Assists']>=25)$milestone('assists','Starker Rückhalt',$num($e['Assists']).' Assists','Persönlicher Meilenstein: mindestens 25 Unterstützungen gegen menschliche Gegner.');
        if($e['NoScope']>=10)$milestone('noscope','Ohne Zielfernrohr',$num($e['NoScope']).' No-Scopes','Persönlicher Meilenstein: mindestens 10 No-Scope-Kills gegen menschliche Gegner.');
        }
        return ['state'=>'ready','message'=>$cards?'Deine besonderen Leistungen auf diesem Server.':'Noch keine qualifizierten Highlights. Mit weiteren Spielen wächst dein Profil.',
            'cards'=>array_slice($cards,0,3),'facts'=>$facts,'statsAt'=>self::date($data['generatedAt']),'rankingAt'=>$rankingAt,'weaponRankingAt'=>$weaponRankingAt,'since'=>self::date($e['Since']),
            'rules'=>$rules,'notes'=>'Rangplätze gelten nur für diesen Server. Persönliche Meilensteine sind keine Platzierung. Spezialkills und Assists zählen gegen menschliche Gegner außerhalb des Warmups; Bombenaktionen ebenfalls ohne Warmup.'];
    }
}
