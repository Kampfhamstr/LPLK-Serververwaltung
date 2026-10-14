<?php
namespace App\Services;

use App\Core\Database;
use PDO;

final class PetService
{
    public const SPECIES = [
        // Die Kategorie ist rein kosmetisch. Alle Arten starten mit identischen Werten.
        'cat'=>['label'=>'Katze','emoji'=>'🐈','temperament'=>'neugierig','tier'=>'classic'],
        'dog'=>['label'=>'Hund','emoji'=>'🐕','temperament'=>'treu','tier'=>'classic'],
        'rabbit'=>['label'=>'Hase','emoji'=>'🐇','temperament'=>'flink','tier'=>'classic'],
        'hamster'=>['label'=>'Hamster','emoji'=>'🐹','temperament'=>'quirlig','tier'=>'classic'],
        'guinea_pig'=>['label'=>'Meerschweinchen','emoji'=>'🐹🥕','temperament'=>'sanft','tier'=>'classic'],
        'budgie'=>['label'=>'Wellensittich','emoji'=>'🐦','temperament'=>'gesellig','tier'=>'classic'],
        'turtle'=>['label'=>'Schildkröte','emoji'=>'🐢','temperament'=>'geduldig','tier'=>'classic'],
        'goldfish'=>['label'=>'Goldfisch','emoji'=>'🐠','temperament'=>'ruhig','tier'=>'classic'],

        'fox'=>['label'=>'Fuchs','emoji'=>'🦊','temperament'=>'schlau','tier'=>'cool'],
        'raccoon'=>['label'=>'Waschbär','emoji'=>'🦝','temperament'=>'verspielt','tier'=>'cool'],
        'owl'=>['label'=>'Eule','emoji'=>'🦉','temperament'=>'weise','tier'=>'cool'],
        'panda'=>['label'=>'Panda','emoji'=>'🐼','temperament'=>'gelassen','tier'=>'cool'],
        'wolf'=>['label'=>'Wolf','emoji'=>'🐺','temperament'=>'wild','tier'=>'cool'],
        'axolotl'=>['label'=>'Axolotl','emoji'=>'🦎💧','temperament'=>'entspannt','tier'=>'cool'],
        'penguin'=>['label'=>'Pinguin','emoji'=>'🐧','temperament'=>'charmant','tier'=>'cool'],
        'otter'=>['label'=>'Otter','emoji'=>'🦦','temperament'=>'abenteuerlustig','tier'=>'cool'],

        'dragon'=>['label'=>'Kleiner Drache','emoji'=>'🐉','temperament'=>'mutig','tier'=>'epic'],
        'unicorn'=>['label'=>'Einhorn','emoji'=>'🦄','temperament'=>'magisch','tier'=>'epic'],
        'phoenix'=>['label'=>'Phönix','emoji'=>'🔥🦅','temperament'=>'unbeugsam','tier'=>'epic'],
        'griffin'=>['label'=>'Greif','emoji'=>'🦅🦁','temperament'=>'legendär','tier'=>'epic'],
        'kraken'=>['label'=>'Kraken','emoji'=>'🐙🌊','temperament'=>'geheimnisvoll','tier'=>'epic'],
        'hellhound'=>['label'=>'Höllenhund','emoji'=>'🐕🔥','temperament'=>'wachsam','tier'=>'epic'],
        'moon_stag'=>['label'=>'Mondhirsch','emoji'=>'🦌🌙','temperament'=>'besonnen','tier'=>'epic'],
        'hydra'=>['label'=>'Hydra','emoji'=>'🐍🐍🐍','temperament'=>'ungezähmt','tier'=>'epic'],
    ];

    public const SPECIES_TIERS = [
        'classic'=>['label'=>'Klassische Freunde','description'=>'Süß, vertraut und immer an deiner Seite.'],
        'cool'=>['label'=>'Coole Begleiter','description'=>'Wild, außergewöhnlich und voller Charakter.'],
        'epic'=>['label'=>'Epische Wesen','description'=>'Legendäre Optik – fair und genauso stark wie alle anderen.'],
    ];

    private const ACTIONS = [
        'feed'=>['label'=>'Füttern','message'=>'Lecker! Der Hunger ist gestillt.','cooldown'=>900,'xp'=>3,'hunger'=>24,'energy'=>0,'happiness'=>2,'bond'=>1,'health'=>2,'training'=>0],
        'pet'=>['label'=>'Streicheln','message'=>'Dein Pet genießt die Aufmerksamkeit.','cooldown'=>1800,'xp'=>2,'hunger'=>0,'energy'=>0,'happiness'=>15,'bond'=>4,'health'=>0,'training'=>0],
        'play'=>['label'=>'Spielen','message'=>'Was für ein Spaß!','cooldown'=>2700,'xp'=>7,'hunger'=>-7,'energy'=>-12,'happiness'=>20,'bond'=>3,'health'=>0,'training'=>0],
        'sleep'=>['label'=>'Schlafen','message'=>'Dein Pet hat sich gut ausgeruht.','cooldown'=>7200,'xp'=>2,'hunger'=>-3,'energy'=>35,'happiness'=>2,'bond'=>1,'health'=>3,'training'=>0],
        'train'=>['label'=>'Trainieren','message'=>'Training geschafft! Du erhältst einen Trainingspunkt.','cooldown'=>3600,'xp'=>8,'hunger'=>-9,'energy'=>-18,'happiness'=>-2,'bond'=>2,'health'=>0,'training'=>1],
        'heal'=>['label'=>'Heilen','message'=>'Die Grundpflege hat deinem Pet gutgetan.','cooldown'=>21600,'xp'=>3,'hunger'=>0,'energy'=>5,'happiness'=>3,'bond'=>1,'health'=>30,'training'=>0],
    ];

    public static function overview(int $userId): array
    {
        $pdo=Database::connection(); $pet=self::pet($pdo,$userId);
        if(!$pet)return ['pet'=>null,'species'=>self::SPECIES,'speciesTiers'=>self::SPECIES_TIERS,'actions'=>self::ACTIONS,'tasks'=>[],'leaderboard'=>self::leaderboard($pdo)];
        $pet=self::decayed($pet);
        Database::withBusyRetry(static function()use($pdo,$userId,$pet):void{self::ensureDailyTasks($pdo,$userId,(int)$pet['id']);},3);
        $cooldowns=[];$s=$pdo->prepare('SELECT action_type,available_at FROM pet_action_cooldowns WHERE pet_id=?');$s->execute([(int)$pet['id']]);
        foreach($s->fetchAll() as $row)$cooldowns[$row['action_type']]=$row['available_at'];
        return ['pet'=>$pet,'species'=>self::SPECIES,'speciesTiers'=>self::SPECIES_TIERS,'actions'=>self::ACTIONS,'tasks'=>self::tasks($pdo,$userId),'wallet'=>self::wallet($pdo,$userId),'cooldowns'=>$cooldowns,'leaderboard'=>self::leaderboard($pdo)];
    }

    /**
     * Liefert nur die unkritischen Anzeigedaten für das Pet in der Seitenleiste.
     * Die Navigation darf auch vor einer noch ausstehenden Migration funktionieren.
     */
    public static function sidebarPet(int $userId): ?array
    {
        try {
            $statement=Database::connection()->prepare('SELECT id,name,species_key FROM pets WHERE user_id=? AND active=1 LIMIT 1');
            $statement->execute([$userId]);
            $pet=$statement->fetch();
            if(!$pet)return null;
            $speciesKey=(string)($pet['species_key']??'');
            $species=self::SPECIES[$speciesKey]??['label'=>'Pet','emoji'=>'🐾','tier'=>'classic'];
            return [
                'id'=>(int)$pet['id'],
                'name'=>(string)$pet['name'],
                'species_key'=>$speciesKey,
                'label'=>(string)$species['label'],
                'emoji'=>(string)$species['emoji'],
                'tier'=>(string)($species['tier']??'classic'),
            ];
        } catch (\Throwable) {
            return null;
        }
    }

    public static function adopt(int $userId,string $species,string $name): int
    {
        if(!isset(self::SPECIES[$species]))throw new \RuntimeException('Bitte wähle eine gültige Tierart.');
        $name=self::validName($name);$pdo=Database::connection();
        return Database::withBusyRetry(function()use($pdo,$userId,$species,$name):int{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                if(self::pet($pdo,$userId))throw new \RuntimeException('Du hast bereits ein aktives Pet.');$now=date('c');
                $pdo->prepare('INSERT INTO pets(user_id,species_key,name,level,xp,health,hunger,energy,happiness,bond,last_state_at,version,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)')->execute([$userId,$species,$name,1,0,100,85,85,80,10,$now,1,$now,$now]);
                $id=(int)$pdo->lastInsertId();
                $pdo->prepare('INSERT INTO pet_wallets(user_id,coins,training_points,updated_at) VALUES(?,?,?,?) ON CONFLICT(user_id) DO UPDATE SET coins=pet_wallets.coins+excluded.coins,updated_at=excluded.updated_at')->execute([$userId,100,0,$now]);
                $pdo->prepare('INSERT INTO pet_wallet_ledger(user_id,currency,amount,reason,reference_key,created_at) VALUES(?,?,?,?,?,?)')->execute([$userId,'coins',100,'adoption','pet:'.$id,$now]);
                $pdo->prepare('INSERT INTO pet_inventory(user_id,item_key,item_name,item_type,quantity,equipped,created_at) VALUES(?,?,?,?,?,?,?)')->execute([$userId,'starter_collar','Starter-Halsband','cosmetic',1,1,$now]);
                $pdo->prepare('INSERT INTO pet_items(user_id,pet_id,template_key,item_name,slot,rarity,item_level,level_required,strength_bonus,agility_bonus,vitality_bonus,instinct_bonus,equipped,source_type,source_id,source_index,created_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)')->execute([$userId,$id,'adventurer_scarf','Abenteurer-Halstuch','charm','common',1,1,0,1,0,1,1,'starter',$id,0,$now]);
                self::ensureDailyTasks($pdo,$userId,$id);$pdo->exec('COMMIT');return $id;
            }catch(\Throwable $e){try{$pdo->exec('ROLLBACK');}catch(\Throwable){}throw $e;}
        });
    }

    public static function performAction(int $userId,string $action,string $key): array
    {
        if(!isset(self::ACTIONS[$action]))throw new \RuntimeException('Unbekannte Pflegeaktion.');
        if(!preg_match('/^[a-zA-Z0-9_-]{16,80}$/',$key))throw new \RuntimeException('Die Aktionskennung ist ungültig. Bitte lade die Seite neu.');
        $pdo=Database::connection();$def=self::ACTIONS[$action];
        return Database::withBusyRetry(function()use($pdo,$userId,$action,$key,$def):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $dupe=$pdo->prepare('SELECT pet_id,action_type FROM pet_actions WHERE user_id=? AND idempotency_key=?');$dupe->execute([$userId,$key]);
                if($saved=$dupe->fetch()){
                    if((string)$saved['action_type']!==$action)throw new \RuntimeException('Diese Aktionskennung wurde bereits für eine andere Pet-Aktion verwendet. Bitte lade die Seite neu.');
                    $pdo->exec('COMMIT');return ['duplicate'=>true,'action'=>$action,'pet_id'=>(int)$saved['pet_id'],'message'=>'Diese Aktion wurde bereits verarbeitet.'];
                }
                $pet=self::pet($pdo,$userId);if(!$pet)throw new \RuntimeException('Adoptiere zuerst ein Pet.');
                $away=$pdo->prepare("SELECT 1 FROM pet_adventures WHERE pet_id=? AND status='running' LIMIT 1");$away->execute([(int)$pet['id']]);if($away->fetchColumn())throw new \RuntimeException('Dein Pet ist auf Abenteuer. Hole zuerst das Ergebnis ab, bevor du es pflegst.');
                $pet=self::decayed($pet);$now=time();
                $cd=$pdo->prepare('SELECT available_at FROM pet_action_cooldowns WHERE pet_id=? AND action_type=?');$cd->execute([(int)$pet['id'],$action]);$until=$cd->fetchColumn();
                if($until!==false&&strtotime((string)$until)>=$now)throw new \RuntimeException('Diese Aktion ist noch '.self::duration(strtotime((string)$until)-$now).' im Cooldown.');
                if(in_array($action,['play','train'],true)&&(int)$pet['energy']<abs((int)$def['energy']))throw new \RuntimeException('Dein Pet hat dafür nicht genug Energie.');
                $xp=(int)$pet['xp']+(int)$def['xp'];$values=[];foreach(['health','hunger','energy','happiness','bond']as$field)$values[$field]=self::clamp((float)$pet[$field]+(float)$def[$field]);
                $iso=date('c',$now);$available=date('c',$now+(int)$def['cooldown']);
                $upd=$pdo->prepare('UPDATE pets SET level=?,xp=?,health=?,hunger=?,energy=?,happiness=?,bond=?,last_state_at=?,version=version+1,updated_at=? WHERE id=? AND version=?');
                $upd->execute([self::levelForXp($xp),$xp,$values['health'],$values['hunger'],$values['energy'],$values['happiness'],$values['bond'],$iso,$iso,(int)$pet['id'],(int)$pet['version']]);
                if($upd->rowCount()!==1)throw new \RuntimeException('Das Pet wurde parallel geändert. Bitte erneut versuchen.');
                $pdo->prepare('INSERT INTO pet_actions(pet_id,user_id,action_type,idempotency_key,effects_json,created_at) VALUES(?,?,?,?,?,?)')->execute([(int)$pet['id'],$userId,$action,$key,json_encode($def,JSON_UNESCAPED_UNICODE),$iso]);
                $pdo->prepare('INSERT INTO pet_action_cooldowns(pet_id,action_type,available_at) VALUES(?,?,?) ON CONFLICT(pet_id,action_type) DO UPDATE SET available_at=excluded.available_at')->execute([(int)$pet['id'],$action,$available]);
                if((int)$def['training']>0)self::credit($pdo,$userId,'training_points',(int)$def['training'],'care:'.$key,$iso);
                self::ensureDailyTasks($pdo,$userId,(int)$pet['id']);
                self::progressTasks($pdo,$userId,$action);$pdo->exec('COMMIT');return ['duplicate'=>false,'action'=>$action,'pet_id'=>(int)$pet['id'],'message'=>$def['message'].' +'.$def['xp'].' XP'];
            }catch(\Throwable $e){try{$pdo->exec('ROLLBACK');}catch(\Throwable){}throw $e;}
        });
    }

    public static function claimTask(int $userId,int $taskId): array
    {
        $pdo=Database::connection();return Database::withBusyRetry(function()use($pdo,$userId,$taskId):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{$s=$pdo->prepare('SELECT * FROM pet_tasks WHERE id=? AND user_id=?');$s->execute([$taskId,$userId]);$task=$s->fetch();
                if(!$task||(int)$task['progress']<(int)$task['target'])throw new \RuntimeException('Diese Aufgabe ist noch nicht abgeschlossen.');if($task['claimed_at']!==null)throw new \RuntimeException('Diese Aufgabe wurde bereits eingelöst.');
                $now=date('c');$u=$pdo->prepare('UPDATE pet_tasks SET claimed_at=? WHERE id=? AND claimed_at IS NULL');$u->execute([$now,$taskId]);if($u->rowCount()!==1)throw new \RuntimeException('Diese Aufgabe wurde bereits eingelöst.');
                self::credit($pdo,$userId,'coins',(int)$task['reward_coins'],'task:'.$taskId,$now);$pet=self::pet($pdo,$userId);$xp=(int)$pet['xp']+(int)$task['reward_xp'];
                $pdo->prepare('UPDATE pets SET xp=?,level=?,version=version+1,updated_at=? WHERE id=?')->execute([$xp,self::levelForXp($xp),$now,(int)$pet['id']]);$pdo->exec('COMMIT');return $task;
            }catch(\Throwable $e){try{$pdo->exec('ROLLBACK');}catch(\Throwable){}throw $e;}
        });
    }

    private static function pet(PDO $pdo,int $userId):array|false{$s=$pdo->prepare('SELECT * FROM pets WHERE user_id=? AND active=1 LIMIT 1');$s->execute([$userId]);return $s->fetch();}
    private static function decayed(array $pet):array{$hours=max(0,(time()-strtotime((string)$pet['last_state_at']))/3600);$startHunger=(float)$pet['hunger'];$pet['hunger']=self::clamp($startHunger-$hours*1.25);$pet['energy']=self::clamp((float)$pet['energy']-$hours*.6);$pet['happiness']=self::clamp((float)$pet['happiness']-$hours*.8);$hoursUntilLow=$startHunger<=15?0:($startHunger-15)/1.25;$neglectHours=max(0,$hours-$hoursUntilLow);if($neglectHours>0)$pet['health']=self::clamp((float)$pet['health']-$neglectHours*.3);return $pet;}
    private static function clamp(float $v):int{return(int)round(max(0,min(100,$v)));}
    private static function validName(string $name):string{$name=trim(preg_replace('/\s+/u',' ',$name)??'');$len=function_exists('mb_strlen')?mb_strlen($name):(preg_match_all('/./us',$name,$characters)?count($characters[0]):strlen($name));if($len<2||$len>24||!preg_match('/^[\p{L}\p{N}][\p{L}\p{N} ._-]*$/u',$name))throw new \RuntimeException('Der Pet-Name muss 2–24 Zeichen lang sein und darf nur Buchstaben, Zahlen, Leerzeichen, Punkt, Binde- und Unterstrich enthalten.');return $name;}
    private static function levelForXp(int $xp):int{$sum=0;for($l=1;$l<20;$l++){$sum+=(int)round(100*($l**1.45));if($xp<$sum)return$l;}return 20;}
    private static function duration(int $seconds):string{$minutes=(int)ceil($seconds/60);return$minutes>=60?(int)ceil($minutes/60).' Std.':$minutes.' Min.';}
    private static function wallet(PDO $pdo,int $userId):array{$s=$pdo->prepare('SELECT coins,training_points FROM pet_wallets WHERE user_id=?');$s->execute([$userId]);return$s->fetch()?:['coins'=>0,'training_points'=>0];}
    private static function credit(PDO $pdo,int $userId,string $currency,int $amount,string $reference,string $now):void
    {
        if(!in_array($currency,['coins','training_points'],true)||$amount<=0)throw new \InvalidArgumentException('Ungültige Pet-Gutschrift.');
        $pdo->prepare('INSERT OR IGNORE INTO pet_wallets(user_id,coins,training_points,updated_at) VALUES(?,?,?,?)')->execute([$userId,0,0,$now]);$column=$currency==='coins'?'coins':'training_points';
        $update=$pdo->prepare("UPDATE pet_wallets SET {$column}={$column}+?,updated_at=? WHERE user_id=?");$update->execute([$amount,$now,$userId]);if($update->rowCount()!==1)throw new \RuntimeException('Die Pet-Gutschrift konnte nicht verbucht werden.');
        $pdo->prepare('INSERT INTO pet_wallet_ledger(user_id,currency,amount,reason,reference_key,created_at) VALUES(?,?,?,?,?,?)')->execute([$userId,$currency,$amount,'reward',$reference,$now]);
    }
    private static function ensureDailyTasks(PDO $pdo,int $userId,int $petId):void{$day=date('Y-m-d');$defs=[['feed','Einmal füttern',1,20,8],['pet','Zeit zum Kuscheln',1,15,6],['train','Eine Trainingsrunde',1,30,12]];$s=$pdo->prepare('INSERT OR IGNORE INTO pet_tasks(user_id,pet_id,period_key,action_type,title,target,progress,reward_coins,reward_xp,created_at) VALUES(?,?,?,?,?,?,?,?,?,?)');foreach($defs as$d)$s->execute([$userId,$petId,$day,$d[0],$d[1],$d[2],0,$d[3],$d[4],date('c')]);}
    private static function progressTasks(PDO $pdo,int $userId,string $action):void{$pdo->prepare('UPDATE pet_tasks SET progress=MIN(target,progress+1) WHERE user_id=? AND period_key=? AND action_type=? AND claimed_at IS NULL')->execute([$userId,date('Y-m-d'),$action]);}
    private static function tasks(PDO $pdo,int $userId):array{$s=$pdo->prepare('SELECT * FROM pet_tasks WHERE user_id=? AND period_key=? ORDER BY id');$s->execute([$userId,date('Y-m-d')]);return$s->fetchAll();}
    /** Öffentliche Pet-Rangliste: Besitzer werden ausschließlich über Steam benannt. */
    private static function leaderboard(PDO $pdo):array
    {
        return $pdo->query(
            "SELECT p.name,p.species_key,p.level,p.xp,p.bond,
                    trim(u.steam_personaname) AS owner_name,u.web_role
             FROM pets p
             JOIN users u ON u.id=p.user_id
             WHERE p.active=1 AND u.active=1
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
             ORDER BY p.level DESC,p.xp DESC,p.bond DESC
             LIMIT 10"
        )->fetchAll();
    }
}
