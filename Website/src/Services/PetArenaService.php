<?php
namespace App\Services;

use App\Core\Database;
use PDO;

final class PetArenaService
{
    public const TACTICS = [
        'aggressive'=>['label'=>'Aggressiv','description'=>'+12 % Schaden, −10 % Verteidigung'],
        'defensive'=>['label'=>'Defensiv','description'=>'+15 % Verteidigung, −8 % Schaden'],
        'tricky'=>['label'=>'Taktisch','description'=>'+8 % Treffer, +6 % Ausweichen, −5 % Schaden'],
    ];

    public const OPPONENTS = [
        'meadow_bully'=>['label'=>'Wiesen-Rüpel','emoji'=>'🐀','level'=>1,'archetype'=>'attacker','type'=>'Angreifer','rating'=>900,'coins'=>12,'xp'=>10,'description'=>'Teilt kräftig aus, hält aber wenig aus.'],
        'tank_toad'=>['label'=>'Panzerkröte','emoji'=>'🐢','level'=>2,'archetype'=>'guardian','type'=>'Wächter','rating'=>960,'coins'=>15,'xp'=>13,'description'=>'Sehr zäh – Instinkt und Tempo helfen.'],
        'crow_trickster'=>['label'=>'Krähen-Trickser','emoji'=>'🐦‍⬛','level'=>3,'archetype'=>'trickster','type'=>'Trickser','rating'=>1020,'coins'=>18,'xp'=>16,'description'=>'Schnell und schwer zu treffen.'],
        'forest_wolf'=>['label'=>'Waldwolf','emoji'=>'🐺','level'=>5,'archetype'=>'attacker','type'=>'Angreifer','rating'=>1110,'coins'=>23,'xp'=>21,'description'=>'Gefährlich, sobald die Lebenspunkte sinken.'],
        'moss_golem'=>['label'=>'Moosgolem','emoji'=>'🗿','level'=>7,'archetype'=>'guardian','type'=>'Wächter','rating'=>1210,'coins'=>29,'xp'=>27,'description'=>'Seine Rüstung verlangt einen ausdauernden Build.'],
        'crystal_lynx'=>['label'=>'Kristallluchs','emoji'=>'🐆','level'=>9,'archetype'=>'trickster','type'=>'Trickser','rating'=>1330,'coins'=>36,'xp'=>34,'description'=>'Blitzschnell und mit scharfem Fokus.'],
        'storm_griffin'=>['label'=>'Sturmgreif','emoji'=>'🦅','level'=>12,'archetype'=>'attacker','type'=>'Elite','rating'=>1480,'coins'=>46,'xp'=>44,'description'=>'Ein harter Test für Ausrüstung und Skillung.'],
        'chrono_phoenix'=>['label'=>'Chronophönix','emoji'=>'🔥🦅','level'=>16,'archetype'=>'boss','type'=>'Boss','rating'=>1700,'coins'=>62,'xp'=>60,'description'=>'Der Meister der Arena besitzt keine klare Schwäche.'],
    ];

    private const DAILY_TICKETS=5;
    private const ENERGY_COST=8;
    private const COOLDOWN_SECONDS=15;

    public static function overview(int $userId):array
    {
        $pdo=Database::connection();$pet=PetGameService::activePet($pdo,$userId);$currentSeason=date('Y-m');
        if(!$pet)return ['arenaProfile'=>self::defaultProfile($currentSeason),'opponents'=>[],'arenaBattles'=>[],'arenaTicketsLeft'=>0,'arenaCooldownUntil'=>null,'arenaTactics'=>self::TACTICS,'arenaLeaderboard'=>[]];
        $profileStatement=$pdo->prepare('SELECT * FROM pet_arena_profiles WHERE pet_id=? AND user_id=?');$profileStatement->execute([(int)$pet['id'],$userId]);$storedProfile=$profileStatement->fetch();$profile=$storedProfile&&((string)$storedProfile['season_key']===$currentSeason)?$storedProfile:self::defaultProfile($currentSeason);$profile['league']=self::league((int)$profile['rating']);
        $wonStatement=$pdo->prepare("SELECT DISTINCT opponent_key FROM pet_arena_battles WHERE pet_id=? AND user_id=? AND result='win'");$wonStatement->execute([(int)$pet['id'],$userId]);$won=array_fill_keys(array_map('strval',$wonStatement->fetchAll(PDO::FETCH_COLUMN)),true);
        $period=date('Y-m-d');$daily=$pdo->prepare('SELECT COUNT(*) FROM pet_arena_battles WHERE user_id=? AND pet_id=? AND period_key=?');$daily->execute([$userId,(int)$pet['id'],$period]);$used=(int)$daily->fetchColumn();
        $cooldown=isset($profile['last_battle_epoch'])&&$profile['last_battle_epoch']!==null?(int)$profile['last_battle_epoch']+self::COOLDOWN_SECONDS:null;
        $awayStatement=$pdo->prepare("SELECT 1 FROM pet_adventures WHERE user_id=? AND pet_id=? AND status='running' LIMIT 1");$awayStatement->execute([$userId,(int)$pet['id']]);$away=(bool)$awayStatement->fetchColumn();
        $arenaBlockedReason=null;
        if($away)$arenaBlockedReason='Dein Pet ist noch auf Abenteuer. Hole zuerst das Ergebnis ab.';
        elseif((int)$pet['health']<20)$arenaBlockedReason='Dein Pet braucht mindestens 20 Gesundheit für die Arena.';
        elseif((int)$pet['energy']<self::ENERGY_COST)$arenaBlockedReason='Dein Pet braucht mindestens '.self::ENERGY_COST.' Energie für die Arena.';
        elseif($used>=self::DAILY_TICKETS)$arenaBlockedReason='Alle fünf Arena-Eintritte für heute wurden verbraucht.';
        elseif($cooldown!==null&&$cooldown>time())$arenaBlockedReason='Dein Pet sammelt sich noch für den nächsten Kampf.';
        $opponents=[];$previous=null;
        foreach(self::OPPONENTS as $key=>$definition){
            $definition['key']=$key;$definition['name']=$definition['label'];$definition['defeated']=isset($won[$key]);$definition['unlocked']=$previous===null||isset($won[$previous]);$definition['level_ready']=(int)$pet['level']>=(int)$definition['level'];$definition['available']=$definition['unlocked']&&$definition['level_ready']&&$arenaBlockedReason===null;$definition['ticket_cost']=1;$definition['is_npc']=true;$definition['style_label']=$definition['type'];$definition['power']=(int)round((int)$definition['rating']/8);
            if(!$definition['unlocked'])$definition['locked_reason']='Besiege zuerst '.self::OPPONENTS[$previous]['label'].'.';elseif(!$definition['level_ready'])$definition['locked_reason']='Wird ab Pet-Level '.(int)$definition['level'].' freigeschaltet.';elseif($arenaBlockedReason!==null)$definition['locked_reason']=$arenaBlockedReason;
            $definition['difficulty_label']=self::difficultyLabel((int)($profile['rating']??1000),(int)$definition['rating']);$opponents[$key]=$definition;$previous=$key;
        }
        $history=$pdo->prepare('SELECT * FROM pet_arena_battles WHERE pet_id=? AND user_id=? ORDER BY created_at_epoch DESC,id DESC LIMIT 10');$history->execute([(int)$pet['id'],$userId]);$battles=[];
        foreach($history->fetchAll() as $battle){$battle['opponent']=self::OPPONENTS[(string)$battle['opponent_key']]??['label'=>(string)$battle['opponent_key'],'emoji'=>'⚔️'];$battle['opponent_name']=$battle['opponent']['label'];$battle['tactic_label']=self::TACTICS[(string)$battle['tactic']]['label']??(string)$battle['tactic'];$battle['battle_log']=self::decode((string)$battle['battle_log_json']);foreach($battle['battle_log'] as &$round)if(is_array($round)&&!isset($round['text']))$round['text']=implode(' ',array_map('strval',(array)($round['events']??[])));unset($round);$battle['snapshot']=self::decode((string)$battle['stats_snapshot_json']);$battle['rating_change']=(int)$battle['rating_after']-(int)$battle['rating_before'];$battles[]=$battle;}
        $leaderboardStatement=$pdo->prepare(
            "SELECT ap.rating,ap.wins,p.name,p.species_key,trim(u.steam_personaname) AS owner_name
             FROM pet_arena_profiles ap
             JOIN pets p ON p.id=ap.pet_id
             JOIN users u ON u.id=ap.user_id
             WHERE ap.season_key=? AND p.active=1 AND u.active=1
               AND trim(COALESCE(u.steam_verified_at,''))<>''
               AND length(trim(COALESCE(u.steam_id64,'')))=17
               AND trim(u.steam_id64) GLOB '7656119[0-9]*'
               AND trim(u.steam_id64) NOT GLOB '*[^0-9]*'
               AND trim(COALESCE(u.steam_personaname,''))<>''
               AND NOT (
                   trim(u.steam_personaname) COLLATE BINARY = ('Steam ' || substr(trim(u.steam_id64),-6))
                   AND trim(COALESCE(u.steam_profile_url,''))=''
                   AND trim(COALESCE(u.steam_avatar_url,''))=''
               )
             ORDER BY ap.rating DESC,ap.wins DESC
             LIMIT 10"
        );$leaderboardStatement->execute([$currentSeason]);$leaderboard=$leaderboardStatement->fetchAll();
        return ['arenaProfile'=>$profile,'opponents'=>$opponents,'arenaBattles'=>$battles,'arenaTicketsLeft'=>max(0,self::DAILY_TICKETS-$used),'arenaCooldownUntil'=>$cooldown&&$cooldown>time()?$cooldown:null,'arenaTactics'=>self::TACTICS,'arenaLeaderboard'=>$leaderboard,'arenaBlockedReason'=>$arenaBlockedReason];
    }

    public static function fight(int $userId,string $opponentKey,string $tacticKey,string $idempotencyKey):array
    {
        if(!isset(self::OPPONENTS[$opponentKey]))throw new \RuntimeException('Wähle einen gültigen Arena-Gegner.');if(!isset(self::TACTICS[$tacticKey]))throw new \RuntimeException('Wähle eine gültige Kampftaktik.');PetGameService::assertIdempotencyKey($idempotencyKey);
        $pdo=Database::connection();$opponentDefinition=self::OPPONENTS[$opponentKey];
        return Database::withBusyRetry(function()use($pdo,$userId,$opponentKey,$tacticKey,$idempotencyKey,$opponentDefinition):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $duplicate=$pdo->prepare('SELECT * FROM pet_arena_battles WHERE user_id=? AND idempotency_key=?');$duplicate->execute([$userId,$idempotencyKey]);
                if($saved=$duplicate->fetch()){
                    if((string)$saved['opponent_key']!==$opponentKey||(string)$saved['tactic']!==$tacticKey)throw new \RuntimeException('Diese Aktionskennung wurde bereits für einen anderen Arenakampf verwendet. Bitte lade die Seite neu.');
                    $pdo->exec('COMMIT');return self::fightResponse($saved,true);
                }
                $pet=PetGameService::activePet($pdo,$userId,false);if(!$pet)throw new \RuntimeException('Adoptiere zuerst ein Pet.');$effectivePet=PetGameService::decayed($pet);PetGameService::ensureNoActiveAdventure($pdo,(int)$pet['id']);
                if((int)$effectivePet['health']<20)throw new \RuntimeException('Dein Pet braucht mindestens 20 Gesundheit für die Arena.');if((int)$pet['level']<(int)$opponentDefinition['level'])throw new \RuntimeException('Dieser Gegner wird erst ab Level '.(int)$opponentDefinition['level'].' freigeschaltet.');
                self::assertOpponentUnlocked($pdo,$userId,(int)$pet['id'],$opponentKey);$now=time();$period=date('Y-m-d',$now);$iso=date('c',$now);$season=date('Y-m');
                $pdo->prepare('INSERT OR IGNORE INTO pet_arena_profiles(pet_id,user_id,rating,wins,losses,draws,season_key,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?)')->execute([(int)$pet['id'],$userId,1000,0,0,0,$season,$iso,$iso]);
                $profileStatement=$pdo->prepare('SELECT * FROM pet_arena_profiles WHERE pet_id=? AND user_id=?');$profileStatement->execute([(int)$pet['id'],$userId]);$profile=$profileStatement->fetch();
                if((string)$profile['season_key']!==$season){
                    $pdo->prepare('UPDATE pet_arena_profiles SET rating=1000,wins=0,losses=0,draws=0,season_key=?,last_battle_epoch=NULL,updated_at=? WHERE pet_id=? AND user_id=?')->execute([$season,$iso,(int)$pet['id'],$userId]);
                    $profile=array_merge($profile,['rating'=>1000,'wins'=>0,'losses'=>0,'draws'=>0,'season_key'=>$season,'last_battle_epoch'=>null]);
                }
                if($profile['last_battle_epoch']!==null&&(int)$profile['last_battle_epoch']+self::COOLDOWN_SECONDS>$now)throw new \RuntimeException('Kurze Verschnaufpause: Der nächste Arenakampf ist in '.((int)$profile['last_battle_epoch']+self::COOLDOWN_SECONDS-$now).' Sekunden möglich.');
                $daily=$pdo->prepare('SELECT COUNT(*) FROM pet_arena_battles WHERE user_id=? AND pet_id=? AND period_key=?');$daily->execute([$userId,(int)$pet['id'],$period]);if((int)$daily->fetchColumn()>=self::DAILY_TICKETS)throw new \RuntimeException('Alle fünf Arena-Eintritte für heute wurden verbraucht.');
                $firstDailyWin=$pdo->prepare("SELECT 1 FROM pet_arena_battles WHERE user_id=? AND pet_id=? AND period_key=? AND result='win' LIMIT 1");$firstDailyWin->execute([$userId,(int)$pet['id'],$period]);$hadDailyWin=(bool)$firstDailyWin->fetchColumn();
                $pet=PetGameService::spendVitals($pdo,$pet,self::ENERGY_COST,4,$now);$stats=PetStatsService::stats($pdo,$pet);$seed=random_int(1,2147483646);$simulation=self::simulate($pet,$stats,$opponentDefinition,$tacticKey,$seed);
                $result=$simulation['result'];$score=$result==='win'?1:($result==='draw'?.5:0);$expected=1/(1+(10**(((int)$opponentDefinition['rating']-(int)$profile['rating'])/400)));$ratingChange=(int)round(24*($score-$expected));$ratingAfter=PetGameService::clamp((int)$profile['rating']+$ratingChange,0,5000);
                $coins=$result==='win'?(int)$opponentDefinition['coins']:0;$xp=$result==='win'?(int)$opponentDefinition['xp']:(int)max(2,round((int)$opponentDefinition['xp']*.4));$training=$result==='win'&&!$hadDailyWin?1:0;
                $snapshot=['rules_version'=>'1.13.0','seed'=>$seed,'player'=>$simulation['player'],'opponent'=>$simulation['opponent'],'pet_stats'=>$stats,'tactic'=>$tacticKey];
                $insert=$pdo->prepare('INSERT INTO pet_arena_battles(user_id,pet_id,opponent_key,tactic,result,idempotency_key,period_key,stats_snapshot_json,battle_log_json,rounds,energy_cost,rating_before,rating_after,reward_coins,reward_xp,reward_training_points,created_at_epoch,created_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)');
                $insert->execute([$userId,(int)$pet['id'],$opponentKey,$tacticKey,$result,$idempotencyKey,$period,PetGameService::json($snapshot),PetGameService::json($simulation['log']),(int)$simulation['rounds'],self::ENERGY_COST,(int)$profile['rating'],$ratingAfter,$coins,$xp,$training,$now,$iso]);$battleId=(int)$pdo->lastInsertId();
                if($coins>0)PetGameService::changeCurrency($pdo,$userId,'coins',$coins,'arena','arena:'.$battleId.':coins',$iso);if($training>0)PetGameService::changeCurrency($pdo,$userId,'training_points',$training,'arena','arena:'.$battleId.':training',$iso);PetGameService::addXp($pdo,$pet,$xp,$iso);
                $counter=$result==='win'?'wins':($result==='loss'?'losses':'draws');$pdo->prepare("UPDATE pet_arena_profiles SET rating=?,{$counter}={$counter}+1,season_key=?,last_battle_epoch=?,updated_at=? WHERE pet_id=? AND user_id=?")->execute([$ratingAfter,$season,$now,$iso,(int)$pet['id'],$userId]);
                $pdo->exec('COMMIT');return self::fightResponse(['id'=>$battleId,'opponent_key'=>$opponentKey,'result'=>$result,'rating_before'=>(int)$profile['rating'],'rating_after'=>$ratingAfter,'reward_coins'=>$coins,'reward_xp'=>$xp,'reward_training_points'=>$training],false);
            }catch(\Throwable $e){PetGameService::rollback($pdo);throw $e;}
        });
    }

    private static function simulate(array $pet,array $stats,array $opponentDefinition,string $tacticKey,int $seed):array
    {
        $care=((int)$pet['health']+(int)$pet['hunger']+(int)$pet['energy']+(int)$pet['happiness'])/4;$careFactor=.9+.1*$care/100+min(.05,(int)$pet['bond']/2000);
        $player=['name'=>(string)$pet['name'],'hp'=>(int)$stats['combat_hp'],'max_hp'=>(int)$stats['combat_hp'],'attack'=>(int)round((8+1.8*(int)$pet['level']+1.6*(int)$stats['strength'])*$careFactor),'defense'=>(int)round((5+1.2*(int)$pet['level']+1.3*(int)$stats['vitality']+(int)$stats['skill_effects']['armor_bonus'])*$careFactor),'speed'=>(int)round(8+.7*(int)$pet['level']+1.4*(int)$stats['agility']),'focus'=>(int)round(8+.7*(int)$pet['level']+1.4*(int)$stats['instinct']),'hit_bonus'=>0,'dodge_bonus'=>(int)$stats['skill_effects']['dodge_percent'],'crit_bonus'=>(int)$stats['skill_effects']['crit_percent'],'finisher'=>(int)$stats['skill_effects']['finisher_percent'],'second_wind'=>(int)$stats['skill_effects']['second_wind_percent']];
        if($tacticKey==='aggressive'){$player['attack']=(int)round($player['attack']*1.12);$player['defense']=(int)round($player['defense']*.9);}elseif($tacticKey==='defensive'){$player['attack']=(int)round($player['attack']*.92);$player['defense']=(int)round($player['defense']*1.15);}else{$player['attack']=(int)round($player['attack']*.95);$player['hit_bonus']=8;$player['dodge_bonus']+=6;}
        $opponent=self::opponentStats($opponentDefinition);$random=self::rng($seed);$log=[];$secondWindUsed=false;$bossHealUsed=false;
        for($round=1;$round<=12;$round++){
            $events=[];$order=$player['speed']>=$opponent['speed']?['player','opponent']:['opponent','player'];
            foreach($order as $attacker){
                if($player['hp']<=0||$opponent['hp']<=0)break;
                if($attacker==='player'){
                    $event=self::strike($player,$opponent,$random,true);$opponent['hp']=$event['target_hp'];$events[]=$event['text'];
                }else{
                    $event=self::strike($opponent,$player,$random,false);$player['hp']=$event['target_hp'];$events[]=$event['text'];
                    if(!$secondWindUsed&&$player['second_wind']>0&&$player['hp']>0&&$player['hp']/$player['max_hp']<.25){$heal=(int)round($player['max_hp']*$player['second_wind']/100);$player['hp']=min($player['max_hp'],$player['hp']+$heal);$secondWindUsed=true;$events[]=$player['name'].' nutzt Zweiter Atem und heilt '.$heal.' LP.';}
                }
                if(!$bossHealUsed&&$opponentDefinition['archetype']==='boss'&&$opponent['hp']>0&&$opponent['hp']/$opponent['max_hp']<.35){$heal=(int)round($opponent['max_hp']*.12);$opponent['hp']=min($opponent['max_hp'],$opponent['hp']+$heal);$bossHealUsed=true;$events[]=$opponent['name'].' entfacht Chronofeuer und heilt '.$heal.' LP.';}
            }
            $log[]=['round'=>$round,'events'=>$events,'player_hp'=>max(0,$player['hp']),'player_max_hp'=>$player['max_hp'],'opponent_hp'=>max(0,$opponent['hp']),'opponent_max_hp'=>$opponent['max_hp']];
            if($player['hp']<=0||$opponent['hp']<=0)break;
        }
        $rounds=count($log);$playerRatio=max(0,$player['hp'])/$player['max_hp'];$opponentRatio=max(0,$opponent['hp'])/$opponent['max_hp'];$result=abs($playerRatio-$opponentRatio)<.0001?'draw':($playerRatio>$opponentRatio?'win':'loss');
        return ['result'=>$result,'rounds'=>$rounds,'log'=>$log,'player'=>$player,'opponent'=>$opponent];
    }

    private static function strike(array $attacker,array $defender,callable $random,bool $playerAttacks):array
    {
        $hit=PetGameService::clamp(82+(int)round(1.2*($attacker['focus']-$defender['speed']))+(int)$attacker['hit_bonus']-(int)$defender['dodge_bonus'],65,95);$actor=$playerAttacks?$attacker['name']:$attacker['name'];
        if($random(1,100)>$hit)return ['target_hp'=>$defender['hp'],'text'=>$actor.' verfehlt den Angriff.'];
        $critChance=PetGameService::clamp(5+(int)round(.6*($attacker['speed']-$defender['focus']))+(int)$attacker['crit_bonus'],5,25);$critical=$random(1,100)<=$critChance;$mitigation=min(.55,$defender['defense']/($defender['defense']+60));$variance=$random(90,110)/100;$damage=max(1,(int)round($attacker['attack']*$variance*(1-$mitigation)));
        if((int)($attacker['finisher']??0)>0&&$defender['hp']/$defender['max_hp']<.3)$damage=(int)round($damage*(1+$attacker['finisher']/100));if($critical)$damage=(int)round($damage*1.5);$hp=max(0,(int)$defender['hp']-$damage);
        return ['target_hp'=>$hp,'text'=>$actor.' trifft für '.$damage.' Schaden'.($critical?' – kritisch!':'.')];
    }

    private static function opponentStats(array $definition):array
    {
        $level=(int)$definition['level'];$attribute=10+intdiv(max(0,$level-1),3);$hp=80+$attribute*8;$attack=(int)round(8+1.8*$level+1.6*$attribute);$defense=(int)round(5+1.2*$level+1.3*$attribute);$speed=(int)round(8+.7*$level+1.4*$attribute);$focus=$speed;
        if($definition['archetype']==='attacker'){$attack=(int)round($attack*1.15);$hp=(int)round($hp*.9);$defense=(int)round($defense*.9);}elseif($definition['archetype']==='guardian'){$hp=(int)round($hp*1.2);$defense=(int)round($defense*1.15);$speed=(int)round($speed*.9);}elseif($definition['archetype']==='trickster'){$hp=(int)round($hp*.9);$speed=(int)round($speed*1.15);$focus=(int)round($focus*1.15);}else{$hp=(int)round($hp*1.08);$attack=(int)round($attack*1.08);$defense=(int)round($defense*1.08);$speed=(int)round($speed*1.08);$focus=(int)round($focus*1.08);}
        return ['name'=>(string)$definition['label'],'hp'=>$hp,'max_hp'=>$hp,'attack'=>$attack,'defense'=>$defense,'speed'=>$speed,'focus'=>$focus,'hit_bonus'=>0,'dodge_bonus'=>0,'crit_bonus'=>0,'finisher'=>0];
    }

    private static function assertOpponentUnlocked(PDO $pdo,int $userId,int $petId,string $key):void
    {
        $keys=array_keys(self::OPPONENTS);$index=array_search($key,$keys,true);if($index===0)return;$previous=$keys[$index-1];$statement=$pdo->prepare("SELECT 1 FROM pet_arena_battles WHERE user_id=? AND pet_id=? AND opponent_key=? AND result='win' LIMIT 1");$statement->execute([$userId,$petId,$previous]);if(!$statement->fetchColumn())throw new \RuntimeException('Besiege zuerst '.self::OPPONENTS[$previous]['label'].'.');
    }

    private static function fightResponse(array $battle,bool $duplicate):array
    {
        $change=(int)$battle['rating_after']-(int)$battle['rating_before'];$label=['win'=>'Sieg','loss'=>'Niederlage','draw'=>'Unentschieden'][(string)$battle['result']]??'Kampf beendet';$message=$label.': +'.(int)$battle['reward_xp'].' XP';if((int)$battle['reward_coins']>0)$message.=', +'.(int)$battle['reward_coins'].' Münzen';if((int)($battle['reward_training_points']??0)>0)$message.=', +1 Trainingspunkt';$message.=', Rating '.($change>=0?'+':'').$change.'.';
        return ['duplicate'=>$duplicate,'battle_id'=>(int)$battle['id'],'opponent_key'=>(string)$battle['opponent_key'],'result'=>(string)$battle['result'],'rating_change'=>$change,'message'=>$duplicate?'Dieser Kampf wurde bereits gewertet.':$message];
    }

    private static function rng(int $seed):callable{return static function(int $min,int $max)use(&$seed):int{$seed=(int)(($seed*48271)%2147483647);return $min+($seed%($max-$min+1));};}
    private static function defaultProfile(?string $season=null):array{return ['rating'=>1000,'wins'=>0,'losses'=>0,'draws'=>0,'league'=>'Bronze','season_key'=>$season??date('Y-m'),'last_battle_epoch'=>null];}
    private static function league(int $rating):string{return $rating>=1500?'Legendär':($rating>=1300?'Gold':($rating>=1100?'Silber':'Bronze'));}
    private static function difficultyLabel(int $rating,int $opponent):string{$delta=$opponent-$rating;return $delta<=-120?'Leicht':($delta<=80?'Ausgeglichen':($delta<=250?'Schwer':'Boss-Herausforderung'));}
    private static function decode(string $json):array{$value=json_decode($json,true);return is_array($value)?$value:[];}
}
