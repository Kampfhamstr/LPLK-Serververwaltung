<?php
namespace App\Services;

use App\Core\Database;
use PDO;

final class PetSkillService
{
    public const RESET_COST=75;

    public const SKILLS = [
        'hunter'=>[
            'label'=>'Jäger','emoji'=>'⚔️','description'=>'Kraft, kritische Treffer und ein starkes Finale.',
            'nodes'=>[
                'sharp_claws'=>['label'=>'Scharfe Krallen','max_rank'=>3,'level'=>1,'effect'=>'+2 Kraft je Rang','requires'=>null],
                'eagle_eye'=>['label'=>'Adlerauge','max_rank'=>3,'level'=>4,'effect'=>'+2 Instinkt und +2 % Krit je Rang','requires'=>['key'=>'sharp_claws','rank'=>2]],
                'finisher'=>['label'=>'Vollstrecker','max_rank'=>1,'level'=>8,'effect'=>'+15 % Schaden gegen Gegner unter 30 % LP','requires'=>['key'=>'eagle_eye','rank'=>3]],
            ],
        ],
        'guardian'=>[
            'label'=>'Wächter','emoji'=>'🛡️','description'=>'Zähigkeit, Rüstung und eine zweite Chance.',
            'nodes'=>[
                'thick_fur'=>['label'=>'Dickes Fell','max_rank'=>3,'level'=>1,'effect'=>'+2 Zähigkeit je Rang','requires'=>null],
                'stone_skin'=>['label'=>'Steinhaut','max_rank'=>3,'level'=>4,'effect'=>'+3 Rüstung je Rang','requires'=>['key'=>'thick_fur','rank'=>2]],
                'second_wind'=>['label'=>'Zweiter Atem','max_rank'=>1,'level'=>8,'effect'=>'Einmalig 15 % Kampf-LP heilen','requires'=>['key'=>'stone_skin','rank'=>3]],
            ],
        ],
        'explorer'=>[
            'label'=>'Trickser','emoji'=>'🧭','description'=>'Tempo, Ausweichen und bessere Abenteuerfunde.',
            'nodes'=>[
                'light_paws'=>['label'=>'Leichte Pfoten','max_rank'=>3,'level'=>1,'effect'=>'+2 Tempo je Rang','requires'=>null],
                'feint'=>['label'=>'Finte','max_rank'=>3,'level'=>4,'effect'=>'+2 % Ausweichen je Rang','requires'=>['key'=>'light_paws','rank'=>2]],
                'treasure_nose'=>['label'=>'Schatznase','max_rank'=>3,'level'=>8,'effect'=>'+5 % Fund- und Seltenheitschance je Rang','requires'=>['key'=>'feint','rank'=>3]],
            ],
        ],
    ];

    public static function overview(int $userId):array
    {
        $pdo=Database::connection();$pet=PetGameService::activePet($pdo,$userId);
        if(!$pet)return ['skillTree'=>[],'skillRanks'=>[],'spentSkillPoints'=>0,'skillPointCap'=>0,'resetCost'=>self::RESET_COST];
        $ranks=self::ranks($pdo,(int)$pet['id'],$userId);$spent=array_sum($ranks);$cap=self::pointCap((int)$pet['level']);$wallet=PetGameService::wallet($pdo,$userId);$tree=[];
        foreach(self::SKILLS as $branchKey=>$branch){
            $branchView=$branch;$branchView['key']=$branchKey;$branchView['icon']=$branch['emoji'];$branchView['nodes']=[];
            foreach($branch['nodes'] as $key=>$definition){
                $rank=(int)($ranks[$key]??0);$reason=self::lockedReason($definition,$rank,$ranks,$spent,$cap,(int)$pet['level']);
                $cost=$rank+1;if($reason===null&&(int)$wallet['training_points']<$cost)$reason='Nicht genügend Trainingspunkte.';
                $definition['key']=$key;$definition['current_rank']=$rank;$definition['next_cost']=$cost;$definition['cost']=$cost;$definition['level_required']=$definition['level'];$definition['can_unlock']=$reason===null;$definition['locked_reason']=$reason;
                if($definition['requires'])$definition['prerequisite_label']=self::skillLabel((string)$definition['requires']['key']).' Rang '.(int)$definition['requires']['rank'];
                $branchView['nodes'][$key]=$definition;
            }
            $tree[$branchKey]=$branchView;
        }
        return ['skillTree'=>$tree,'skillRanks'=>$ranks,'spentSkillPoints'=>$spent,'skillPointCap'=>$cap,'resetCost'=>self::RESET_COST];
    }

    public static function unlock(int $userId,string $skillKey,string $idempotencyKey):array
    {
        $definition=self::definition($skillKey);if(!$definition)throw new \RuntimeException('Dieser Skill existiert nicht.');PetGameService::assertIdempotencyKey($idempotencyKey);$pdo=Database::connection();
        return Database::withBusyRetry(function()use($pdo,$userId,$skillKey,$idempotencyKey,$definition):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $duplicate=$pdo->prepare('SELECT pet_id,skill_key,new_rank,cost_points,event_type FROM pet_skill_spends WHERE user_id=? AND idempotency_key=?');$duplicate->execute([$userId,$idempotencyKey]);
                if($saved=$duplicate->fetch()){
                    if((string)$saved['event_type']!=='unlock'||(string)$saved['skill_key']!==$skillKey)throw new \RuntimeException('Diese Aktionskennung wurde bereits für eine andere Pet-Aktion verwendet. Bitte lade die Seite neu.');
                    $pdo->exec('COMMIT');return ['duplicate'=>true,'pet_id'=>(int)$saved['pet_id'],'skill_key'=>(string)$saved['skill_key'],'rank'=>(int)$saved['new_rank'],'cost'=>(int)$saved['cost_points'],'message'=>'Dieser Skillkauf wurde bereits verarbeitet.'];
                }
                $pet=PetGameService::activePet($pdo,$userId,false);if(!$pet)throw new \RuntimeException('Adoptiere zuerst ein Pet.');$petId=(int)$pet['id'];$ranks=self::ranks($pdo,$petId,$userId);$rank=(int)($ranks[$skillKey]??0);$spent=array_sum($ranks);$cap=self::pointCap((int)$pet['level']);
                if($reason=self::lockedReason($definition,$rank,$ranks,$spent,$cap,(int)$pet['level']))throw new \RuntimeException($reason);$newRank=$rank+1;$cost=$newRank;$now=time();$iso=date('c',$now);
                PetGameService::changeCurrency($pdo,$userId,'training_points',-$cost,'skill_unlock','skill-unlock:'.$idempotencyKey,$iso);
                $pdo->prepare('INSERT INTO pet_skill_ranks(pet_id,user_id,skill_key,rank,created_at,updated_at) VALUES(?,?,?,?,?,?) ON CONFLICT(pet_id,skill_key) DO UPDATE SET rank=excluded.rank,updated_at=excluded.updated_at')->execute([$petId,$userId,$skillKey,$newRank,$iso,$iso]);
                $pdo->prepare('INSERT INTO pet_skill_spends(user_id,pet_id,event_type,skill_key,rank_before,new_rank,cost_points,refunded_points,cost_coins,idempotency_key,created_at_epoch,created_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)')->execute([$userId,$petId,'unlock',$skillKey,$rank,$newRank,$cost,0,0,$idempotencyKey,$now,$iso]);
                $pdo->exec('COMMIT');return ['duplicate'=>false,'pet_id'=>$petId,'skill_key'=>$skillKey,'rank'=>$newRank,'cost'=>$cost,'message'=>self::skillLabel($skillKey).' ist jetzt Rang '.$newRank.'.'];
            }catch(\Throwable $e){PetGameService::rollback($pdo);throw $e;}
        });
    }

    public static function reset(int $userId,string $idempotencyKey):array
    {
        PetGameService::assertIdempotencyKey($idempotencyKey);$pdo=Database::connection();
        return Database::withBusyRetry(function()use($pdo,$userId,$idempotencyKey):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $duplicate=$pdo->prepare('SELECT pet_id,refunded_points,cost_coins,event_type FROM pet_skill_spends WHERE user_id=? AND idempotency_key=?');$duplicate->execute([$userId,$idempotencyKey]);
                if($saved=$duplicate->fetch()){
                    if((string)$saved['event_type']!=='reset')throw new \RuntimeException('Diese Aktionskennung wurde bereits für eine andere Pet-Aktion verwendet. Bitte lade die Seite neu.');
                    $pdo->exec('COMMIT');return ['duplicate'=>true,'pet_id'=>(int)$saved['pet_id'],'refunded'=>(int)$saved['refunded_points'],'cost'=>(int)$saved['cost_coins'],'message'=>'Dieser Skill-Reset wurde bereits verarbeitet.'];
                }
                $pet=PetGameService::activePet($pdo,$userId,false);if(!$pet)throw new \RuntimeException('Adoptiere zuerst ein Pet.');$ranks=self::ranks($pdo,(int)$pet['id'],$userId);
                if(array_sum($ranks)<=0)throw new \RuntimeException('Es sind noch keine Skillpunkte verteilt.');
                $refundStatement=$pdo->prepare("SELECT COALESCE(SUM(cost_points),0) FROM pet_skill_spends WHERE pet_id=? AND user_id=? AND event_type='unlock' AND id>COALESCE((SELECT MAX(id) FROM pet_skill_spends WHERE pet_id=? AND user_id=? AND event_type='reset'),0)");$refundStatement->execute([(int)$pet['id'],$userId,(int)$pet['id'],$userId]);$refund=max(0,(int)$refundStatement->fetchColumn());
                $now=time();$iso=date('c',$now);PetGameService::changeCurrency($pdo,$userId,'coins',-self::RESET_COST,'skill_reset','skill-reset:'.$idempotencyKey.':coins',$iso);
                PetGameService::changeCurrency($pdo,$userId,'training_points',$refund,'skill_reset','skill-reset:'.$idempotencyKey.':training',$iso);
                $pdo->prepare('DELETE FROM pet_skill_ranks WHERE pet_id=? AND user_id=?')->execute([(int)$pet['id'],$userId]);
                $pdo->prepare('INSERT INTO pet_skill_spends(user_id,pet_id,event_type,skill_key,rank_before,new_rank,cost_points,refunded_points,cost_coins,idempotency_key,created_at_epoch,created_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)')->execute([$userId,(int)$pet['id'],'reset','__reset__',0,0,0,$refund,self::RESET_COST,$idempotencyKey,$now,$iso]);
                $pdo->exec('COMMIT');return ['duplicate'=>false,'pet_id'=>(int)$pet['id'],'refunded'=>$refund,'cost'=>self::RESET_COST,'message'=>'Skillbaum zurückgesetzt: '.$refund.' Trainingspunkte wurden erstattet.'];
            }catch(\Throwable $e){PetGameService::rollback($pdo);throw $e;}
        });
    }

    private static function lockedReason(array $definition,int $rank,array $ranks,int $spent,int $cap,int $level):?string
    {
        if($rank>=(int)$definition['max_rank'])return 'Maximalrang erreicht.';
        if($level<(int)$definition['level'])return 'Benötigt Pet-Level '.(int)$definition['level'].'.';
        if($spent>=$cap)return 'Das Skill-Limit für dieses Pet-Level ist erreicht.';
        $requires=$definition['requires'];if($requires&&((int)($ranks[$requires['key']]??0)<(int)$requires['rank']))return 'Benötigt '.self::skillLabel((string)$requires['key']).' auf Rang '.(int)$requires['rank'].'.';
        return null;
    }

    private static function ranks(PDO $pdo,int $petId,int $userId):array
    {
        $statement=$pdo->prepare('SELECT skill_key,rank FROM pet_skill_ranks WHERE pet_id=? AND user_id=?');$statement->execute([$petId,$userId]);$ranks=[];
        foreach($statement->fetchAll() as $row)$ranks[(string)$row['skill_key']]=(int)$row['rank'];return $ranks;
    }

    private static function definition(string $skillKey):?array{foreach(self::SKILLS as $branch)if(isset($branch['nodes'][$skillKey]))return $branch['nodes'][$skillKey];return null;}
    private static function skillLabel(string $skillKey):string{$definition=self::definition($skillKey);return $definition['label']??$skillKey;}
    private static function pointCap(int $level):int{return min(12,max(1,1+intdiv(max(1,$level),2)));}
}
