<?php
namespace App\Services;

use App\Core\Database;
use PDO;

/**
 * Gemeinsame Pet-Spielwiese. Die Animationen laufen im Browser, während nur
 * bewusst ausgelöste, kurzlebige Spielmomente serverseitig synchronisiert
 * werden. Weder das Ziel-Pet noch Wallets oder Kampfwerte werden verändert.
 */
final class PetPlaygroundService
{
    public const INTERACTIONS = [
        'greet'=>['label'=>'Pfoten-Gruß','emoji'=>'👋','description'=>'Beide Pets laufen zusammen und begrüßen sich fröhlich.','story'=>'begrüßen sich mit einem fröhlichen Pfotenwink.'],
        'sniff'=>['label'=>'Schnupperrunde','emoji'=>'🌿','description'=>'Gemeinsam wird die spannendste Ecke der Wiese untersucht.','story'=>'erkunden schnuppernd eine geheimnisvolle Spur.'],
        'chase'=>['label'=>'Fangspiel','emoji'=>'🐾','description'=>'Ein kurzes, wildes Fangspiel über die ganze Wiese.','story'=>'sausen bei einem wilden Fangspiel über die Wiese.'],
        'ball'=>['label'=>'Balljagd','emoji'=>'⚽','description'=>'Die beiden jagen abwechselnd dem hüpfenden Ball hinterher.','story'=>'jagen gemeinsam dem hüpfenden Ball hinterher.'],
    ];

    private const MAX_VISIBLE_PETS=12;
    private const DAILY_LIMIT=24;
    private const GLOBAL_COOLDOWN_SECONDS=12;
    private const PAIR_COOLDOWN_SECONDS=60;
    private const EVENT_LIFETIME_SECONDS=180;
    private const TARGET_DAILY_LIMIT=60;

    public static function overview(int $userId):array
    {
        $pdo=Database::connection();$pet=PetGameService::activePet($pdo,$userId);
        if(!$pet)return self::emptyOverview(self::steamIdentity($pdo,$userId)!==null);

        $now=time();$steamIdentity=self::steamIdentity($pdo,$userId);$steamVerified=$steamIdentity!==null;$availability=[];$roster=self::roster($pdo,$userId,(int)$pet['id'],$now,$steamVerified);$roster=self::addAvailability($pdo,$userId,(int)$pet['id'],$roster,$now,$steamVerified,$availability);$playmates=array_values(array_filter($roster,static fn(array $entry):bool=>empty($entry['is_self'])));

        $sessions=$steamVerified?self::recentSessions($pdo,$userId,(int)$pet['id']):[];$featured=null;
        foreach($sessions as $session)if((int)$session['expires_at_epoch']>$now){$featured=$session;break;}

        return [
            'playgroundPets'=>$roster,
            'playmates'=>$playmates,
            'playInteractions'=>self::INTERACTIONS,
            'playSessions'=>$sessions,
            'playStartsLeft'=>$availability['playStartsLeft'],
            'playDailyLimit'=>$availability['playDailyLimit'],
            'playCooldownUntil'=>$availability['playCooldownUntil'],
            'playBlockedReason'=>$availability['playBlockedReason'],
            'playSteamVerified'=>$steamVerified,
            'featuredPlaySession'=>$featured,
            'playStateCursor'=>self::count($pdo,'SELECT COALESCE(MAX(id),0) FROM pet_playground_interactions'),
            'serverNowEpoch'=>$now,
        ];
    }

    public static function state(int $userId,int $afterId=0):array
    {
        $pdo=Database::connection();$pet=PetGameService::activePet($pdo,$userId);
        if(!$pet)return ['pets'=>[],'events'=>[],'cursor'=>0,'server_time'=>time(),'playStartsLeft'=>0,'playDailyLimit'=>self::DAILY_LIMIT,'playCooldownUntil'=>null,'playBlockedReason'=>'Adoptiere zuerst ein Pet.','playSteamVerified'=>self::steamIdentity($pdo,$userId)!==null];
        $now=time();$steamVerified=self::steamIdentity($pdo,$userId)!==null;$availability=[];$roster=self::roster($pdo,$userId,(int)$pet['id'],$now,$steamVerified);$roster=self::addAvailability($pdo,$userId,(int)$pet['id'],$roster,$now,$steamVerified,$availability);$visibleIds=array_fill_keys(array_map(static fn(array $entry):int=>(int)$entry['pet_id'],$roster),true);
        // Der Cursor ist die feste Obergrenze dieses Polls. Ein danach eingefügtes
        // Event bleibt dadurch garantiert für den nächsten Poll sichtbar.
        $afterId=max(0,$afterId);$cursor=self::count($pdo,'SELECT COALESCE(MAX(id),0) FROM pet_playground_interactions');$events=[];
        if($visibleIds&&$cursor>$afterId){
            $ids=array_keys($visibleIds);$placeholders=implode(',',array_fill(0,count($ids),'?'));
            $statement=$pdo->prepare("SELECT id,actor_pet_id,target_pet_id,interaction_type,created_at_epoch,expires_at_epoch FROM pet_playground_interactions WHERE id>? AND id<=? AND expires_at_epoch>? AND actor_pet_id IN ({$placeholders}) AND target_pet_id IN ({$placeholders}) ORDER BY id LIMIT 100");
            $statement->execute(array_merge([$afterId,$cursor,$now],$ids,$ids));
        }else{$statement=null;}
        $eventRows=$statement?$statement->fetchAll():[];
        foreach($eventRows as $row){
            if(!isset($visibleIds[(int)$row['actor_pet_id']],$visibleIds[(int)$row['target_pet_id']]))continue;
            if(!isset(self::INTERACTIONS[(string)$row['interaction_type']]))continue;
            $events[]=['id'=>(int)$row['id'],'actor_pet_id'=>(int)$row['actor_pet_id'],'target_pet_id'=>(int)$row['target_pet_id'],'interaction_type'=>(string)$row['interaction_type'],'created_at_epoch'=>(int)$row['created_at_epoch'],'expires_at_epoch'=>(int)$row['expires_at_epoch']];
        }
        // Bei einer vollen Seite nur bis zur tatsächlich gescannten ID
        // bestätigen. So holt der nächste Poll weitere sichtbare Events ab.
        if(count($eventRows)===100)$cursor=(int)$eventRows[99]['id'];
        return [
            'pets'=>$roster,'events'=>$events,'cursor'=>$cursor,'server_time'=>$now,
            'playStartsLeft'=>$availability['playStartsLeft'],'playDailyLimit'=>$availability['playDailyLimit'],
            'playCooldownUntil'=>$availability['playCooldownUntil'],'playBlockedReason'=>$availability['playBlockedReason'],'playSteamVerified'=>$steamVerified,
        ];
    }

    public static function play(int $userId,int $targetPetId,string $interactionType,string $idempotencyKey):array
    {
        if($targetPetId<=0)throw new \RuntimeException('Dieses Pet ist gerade nicht auf der Spielwiese.');
        if(!isset(self::INTERACTIONS[$interactionType]))throw new \RuntimeException('Wähle eine gültige Spielart.');
        PetGameService::assertIdempotencyKey($idempotencyKey);$pdo=Database::connection();$definition=self::INTERACTIONS[$interactionType];

        return Database::withBusyRetry(function()use($pdo,$userId,$targetPetId,$interactionType,$idempotencyKey,$definition):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $pet=PetGameService::activePet($pdo,$userId,false);if(!$pet)throw new \RuntimeException('Adoptiere zuerst ein Pet.');
                if(self::steamIdentity($pdo,$userId)===null)throw new \RuntimeException('Für die öffentliche Spielwiese musst du zuerst dein Steam-Konto im Profil verknüpfen und verifizieren.');
                if((int)$pet['id']===$targetPetId)throw new \RuntimeException('Dieses Pet ist gerade nicht auf der Spielwiese.');
                $now=time();$eligible=false;
                foreach(self::roster($pdo,$userId,(int)$pet['id'],$now,true) as $candidate)if(empty($candidate['is_self'])&&(int)$candidate['pet_id']===$targetPetId){$eligible=true;break;}
                if(!$eligible)throw new \RuntimeException('Dieses Pet ist gerade nicht auf der Spielwiese.');
                $targetStatement=$pdo->prepare("SELECT p.id,p.user_id,p.name,p.species_key,p.level FROM pets p JOIN users u ON u.id=p.user_id WHERE p.id=? AND p.active=1 AND u.active=1 AND p.user_id<>? AND length(trim(COALESCE(u.steam_id64,'')))=17 AND trim(u.steam_id64) GLOB '7656119[0-9]*' AND trim(u.steam_id64) NOT GLOB '*[^0-9]*' AND trim(COALESCE(u.steam_verified_at,''))<>'' AND trim(COALESCE(u.steam_personaname,''))<>'' LIMIT 1");
                $targetStatement->execute([$targetPetId,$userId]);$target=$targetStatement->fetch();
                if(!$target)throw new \RuntimeException('Dieses Pet ist gerade nicht auf der Spielwiese.');

                // Idempotente Wiederholungen bleiben nur gültig, solange beide
                // Konten weiterhin öffentlich über Steam teilnehmen dürfen.
                $duplicate=$pdo->prepare('SELECT * FROM pet_playground_interactions WHERE actor_user_id=? AND idempotency_key=?');$duplicate->execute([$userId,$idempotencyKey]);
                if($saved=$duplicate->fetch()){
                    if((int)$saved['target_pet_id']!==$targetPetId||(string)$saved['interaction_type']!==$interactionType)throw new \RuntimeException('Diese Aktionskennung wurde bereits für einen anderen Spielmoment verwendet. Bitte lade die Seite neu.');
                    $pdo->exec('COMMIT');return self::playResponse($saved,$definition,true);
                }

                PetGameService::ensureNoActiveAdventure($pdo,(int)$pet['id']);
                $targetAway=self::count($pdo,"SELECT COUNT(*) FROM pet_adventures WHERE pet_id=? AND user_id=? AND status='running'",[$targetPetId,(int)$target['user_id']])>0;
                if($targetAway)throw new \RuntimeException('Dieses Pet ist gerade nicht auf der Spielwiese.');

                $period=date('Y-m-d',$now);
                $daily=self::count($pdo,'SELECT COUNT(*) FROM pet_playground_interactions WHERE actor_user_id=? AND actor_pet_id=? AND period_key=?',[$userId,(int)$pet['id'],$period]);
                if($daily>=self::DAILY_LIMIT)throw new \RuntimeException('Dein Pet hat heute bereits alle 24 gemeinsamen Spielmomente genutzt.');
                $lastGlobal=self::count($pdo,'SELECT COALESCE(MAX(created_at_epoch),0) FROM pet_playground_interactions WHERE actor_user_id=? AND actor_pet_id=?',[$userId,(int)$pet['id']]);
                if($lastGlobal+self::GLOBAL_COOLDOWN_SECONDS>$now)throw new \RuntimeException('Kurze Pfotenpause: Der nächste Spielmoment ist in '.($lastGlobal+self::GLOBAL_COOLDOWN_SECONDS-$now).' Sekunden möglich.');
                $lastPair=self::count($pdo,'SELECT COALESCE(MAX(created_at_epoch),0) FROM pet_playground_interactions WHERE (actor_pet_id=? AND target_pet_id=?) OR (actor_pet_id=? AND target_pet_id=?)',[(int)$pet['id'],$targetPetId,$targetPetId,(int)$pet['id']]);
                if($lastPair+self::PAIR_COOLDOWN_SECONDS>$now)throw new \RuntimeException('Diese beiden Pets haben gerade erst miteinander gespielt. Pro Paar gilt eine Minute Pause.');
                $incoming=self::count($pdo,'SELECT COUNT(*) FROM pet_playground_interactions WHERE target_pet_id=? AND period_key=?',[$targetPetId,$period]);
                if($incoming>=self::TARGET_DAILY_LIMIT)throw new \RuntimeException('Dieses Pet ruht sich für heute von den vielen Spielmomenten aus.');

                $iso=date('c',$now);$expires=$now+self::EVENT_LIFETIME_SECONDS;$insert=$pdo->prepare('INSERT INTO pet_playground_interactions(actor_user_id,actor_pet_id,target_user_id,target_pet_id,interaction_type,period_key,idempotency_key,created_at_epoch,expires_at_epoch,created_at) VALUES(?,?,?,?,?,?,?,?,?,?)');
                $insert->execute([$userId,(int)$pet['id'],(int)$target['user_id'],$targetPetId,$interactionType,$period,$idempotencyKey,$now,$expires,$iso]);$id=(int)$pdo->lastInsertId();
                $pdo->exec('COMMIT');
                return self::playResponse(['id'=>$id,'actor_pet_id'=>(int)$pet['id'],'target_pet_id'=>$targetPetId,'interaction_type'=>$interactionType,'created_at_epoch'=>$now,'expires_at_epoch'=>$expires,'actor_name'=>(string)$pet['name'],'target_name'=>(string)$target['name']],$definition,false);
            }catch(\Throwable $error){PetGameService::rollback($pdo);throw $error;}
        });
    }

    private static function emptyOverview(bool $steamVerified=false):array
    {
        return ['playgroundPets'=>[],'playmates'=>[],'playInteractions'=>self::INTERACTIONS,'playSessions'=>[],'playStartsLeft'=>0,'playDailyLimit'=>self::DAILY_LIMIT,'playCooldownUntil'=>null,'playBlockedReason'=>'Adoptiere zuerst ein Pet.','playSteamVerified'=>$steamVerified,'featuredPlaySession'=>null,'playStateCursor'=>0,'serverNowEpoch'=>time()];
    }

    private static function roster(PDO $pdo,int $userId,int $petId,int $now,bool $viewerSteamVerified):array
    {
        $onlineSince=date('c',$now-180);$statement=$pdo->prepare(
            "SELECT p.id AS pet_id,p.user_id,p.name,p.species_key,p.level,p.happiness,
                    u.steam_id64 AS owner_steam_id64,u.steam_personaname AS owner_steam_personaname,
                    u.steam_profile_url AS owner_steam_profile_url,u.steam_avatar_url AS owner_steam_avatar_url,
                    u.steam_verified_at AS owner_steam_verified_at,u.last_seen_at,
                    CASE WHEN u.last_seen_at IS NOT NULL AND u.last_seen_at>=? THEN 1 ELSE 0 END AS owner_online
             FROM pets p
             JOIN users u ON u.id=p.user_id
             WHERE p.active=1 AND u.active=1
               AND (p.user_id=? OR (
                   CAST(? AS INTEGER)=1
                   AND length(trim(COALESCE(u.steam_id64,'')))=17
                   AND trim(u.steam_id64) GLOB '7656119[0-9]*'
                   AND trim(u.steam_id64) NOT GLOB '*[^0-9]*'
                   AND trim(COALESCE(u.steam_verified_at,''))<>''
                   AND trim(COALESCE(u.steam_personaname,''))<>''
                   AND NOT (
                       trim(u.steam_personaname) COLLATE BINARY = ('Steam ' || substr(trim(u.steam_id64),-6))
                       AND trim(COALESCE(u.steam_profile_url,''))=''
                       AND trim(COALESCE(u.steam_avatar_url,''))=''
                   )
               ))
               AND (p.user_id=? OR NOT EXISTS(
                   SELECT 1 FROM pet_adventures away
                   WHERE away.pet_id=p.id AND away.user_id=p.user_id AND away.status='running'
               ))
             ORDER BY CASE WHEN p.id=? THEN 0 ELSE 1 END,owner_online DESC,COALESCE(u.last_seen_at,u.updated_at) DESC,p.level DESC,p.id
             LIMIT ".self::MAX_VISIBLE_PETS
        );
        $statement->execute([$onlineSince,$userId,$viewerSteamVerified?1:0,$userId,$petId]);$items=[];
        foreach($statement->fetchAll() as $row){
            $speciesKey=(string)$row['species_key'];$species=PetService::SPECIES[$speciesKey]??['label'=>'Pet','emoji'=>'🐾','tier'=>'classic'];$happiness=(int)$row['happiness'];
            $owner=self::steamLabel($row,'owner_');
            $isSelf=(int)$row['pet_id']===$petId;
            if($owner===null&&!$isSelf)continue;
            $items[]=[
                'pet_id'=>(int)$row['pet_id'],'name'=>(string)$row['name'],'level'=>(int)$row['level'],'owner_name'=>$owner??'Nicht öffentlich',
                'species_key'=>$speciesKey,'species_label'=>(string)$species['label'],'emoji'=>(string)$species['emoji'],'tier'=>(string)($species['tier']??'classic'),
                'is_self'=>$isSelf,'owner_online'=>(bool)$row['owner_online'],
                'mood_label'=>$happiness>=75?'Ausgelassen':($happiness>=45?'Neugierig':'Ganz gemütlich'),
            ];
        }
        return $items;
    }

    private static function addAvailability(PDO $pdo,int $userId,int $petId,array $roster,int $now,bool $viewerSteamVerified,?array &$summary=null):array
    {
        $period=date('Y-m-d',$now);$daily=self::count($pdo,'SELECT COUNT(*) FROM pet_playground_interactions WHERE actor_user_id=? AND actor_pet_id=? AND period_key=?',[$userId,$petId,$period]);
        $lastGlobal=self::count($pdo,'SELECT COALESCE(MAX(created_at_epoch),0) FROM pet_playground_interactions WHERE actor_user_id=? AND actor_pet_id=?',[$userId,$petId]);
        $away=self::count($pdo,"SELECT COUNT(*) FROM pet_adventures WHERE user_id=? AND pet_id=? AND status='running'",[$userId,$petId])>0;
        $cooldownUntil=$lastGlobal>0&&$lastGlobal+self::GLOBAL_COOLDOWN_SECONDS>$now?$lastGlobal+self::GLOBAL_COOLDOWN_SECONDS:null;
        $hasPlaymate=(bool)array_filter($roster,static fn(array $entry):bool=>empty($entry['is_self']));
        $globalReason=null;
        if(!$viewerSteamVerified)$globalReason='Verknüpfe und verifiziere zuerst dein Steam-Konto im Profil, um die öffentliche Spielwiese zu nutzen.';
        elseif($away)$globalReason='Dein Pet ist noch auf Abenteuer. Hole zuerst das Ergebnis ab.';
        elseif(!$hasPlaymate)$globalReason='Gerade ist noch kein anderes Community-Pet auf der Spielwiese.';
        elseif($daily>=self::DAILY_LIMIT)$globalReason='Dein Pet hat heute bereits alle 24 gemeinsamen Spielmomente genutzt.';
        elseif($cooldownUntil!==null)$globalReason='Kurze Pfotenpause: Gleich ist der nächste Spielmoment möglich.';
        $summary=['playStartsLeft'=>max(0,self::DAILY_LIMIT-$daily),'playDailyLimit'=>self::DAILY_LIMIT,'playCooldownUntil'=>$cooldownUntil,'playBlockedReason'=>$globalReason];
        $pairStatement=$pdo->prepare('SELECT actor_pet_id,target_pet_id,created_at_epoch FROM pet_playground_interactions WHERE created_at_epoch>? AND (actor_pet_id=? OR target_pet_id=?) ORDER BY created_at_epoch DESC');
        $pairStatement->execute([$now-self::PAIR_COOLDOWN_SECONDS,$petId,$petId]);$pairTimes=[];
        foreach($pairStatement->fetchAll() as $row){$peer=(int)$row['actor_pet_id']===$petId?(int)$row['target_pet_id']:(int)$row['actor_pet_id'];if(!isset($pairTimes[$peer]))$pairTimes[$peer]=(int)$row['created_at_epoch'];}
        $incomingCounts=self::incomingCounts($pdo,$period,$roster);
        foreach($roster as &$entry){
            if(!empty($entry['is_self'])){$entry['can_interact']=false;$entry['pair_cooldown_until']=null;$entry['target_resting']=false;$entry['locked_reason']='';continue;}
            $pairUntil=($pairTimes[(int)$entry['pet_id']]??0)+self::PAIR_COOLDOWN_SECONDS;$targetResting=($incomingCounts[(int)$entry['pet_id']]??0)>=self::TARGET_DAILY_LIMIT;$entry['pair_cooldown_until']=$pairUntil>$now?$pairUntil:null;$entry['target_resting']=$targetResting;
            $entry['can_interact']=$globalReason===null&&$pairUntil<=$now&&!$targetResting;$entry['locked_reason']=$globalReason??($pairUntil>$now?'Diese beiden Pets machen eine kurze Pause.':($targetResting?'Dieses Pet ruht sich für heute aus.':''));
        }
        unset($entry);return $roster;
    }

    private static function incomingCounts(PDO $pdo,string $period,array $pets):array
    {
        $ids=[];
        foreach($pets as $pet)if(empty($pet['is_self'])&&isset($pet['pet_id']))$ids[]=(int)$pet['pet_id'];
        $ids=array_values(array_unique(array_filter($ids,static fn(int $id):bool=>$id>0)));
        if(!$ids)return [];
        $placeholders=implode(',',array_fill(0,count($ids),'?'));
        $statement=$pdo->prepare("SELECT target_pet_id,COUNT(*) AS total FROM pet_playground_interactions WHERE period_key=? AND target_pet_id IN ({$placeholders}) GROUP BY target_pet_id");
        $statement->execute(array_merge([$period],$ids));$counts=[];
        foreach($statement->fetchAll() as $row)$counts[(int)$row['target_pet_id']]=(int)$row['total'];
        return $counts;
    }

    private static function recentSessions(PDO $pdo,int $userId,int $petId):array
    {
        $statement=$pdo->prepare(
            'SELECT s.*,actor.name AS actor_name,target.name AS target_name,
                    actor_owner.steam_id64 AS actor_owner_steam_id64,actor_owner.steam_personaname AS actor_owner_steam_personaname,actor_owner.steam_verified_at AS actor_owner_steam_verified_at,
                    actor_owner.steam_profile_url AS actor_owner_steam_profile_url,actor_owner.steam_avatar_url AS actor_owner_steam_avatar_url,
                    target_owner.steam_id64 AS target_owner_steam_id64,target_owner.steam_personaname AS target_owner_steam_personaname,target_owner.steam_verified_at AS target_owner_steam_verified_at,
                    target_owner.steam_profile_url AS target_owner_steam_profile_url,target_owner.steam_avatar_url AS target_owner_steam_avatar_url
             FROM pet_playground_interactions s
             JOIN pets actor ON actor.id=s.actor_pet_id AND actor.user_id=s.actor_user_id
             JOIN pets target ON target.id=s.target_pet_id AND target.user_id=s.target_user_id
             JOIN users actor_owner ON actor_owner.id=s.actor_user_id
             JOIN users target_owner ON target_owner.id=s.target_user_id
             WHERE (s.actor_pet_id=? OR s.target_pet_id=?)
               AND actor_owner.active=1 AND target_owner.active=1
               AND trim(COALESCE(actor_owner.steam_id64,\'\'))<>\'\'
               AND trim(COALESCE(actor_owner.steam_verified_at,\'\'))<>\'\'
               AND trim(COALESCE(actor_owner.steam_personaname,\'\'))<>\'\'
               AND trim(COALESCE(target_owner.steam_id64,\'\'))<>\'\'
               AND trim(COALESCE(target_owner.steam_verified_at,\'\'))<>\'\'
               AND trim(COALESCE(target_owner.steam_personaname,\'\'))<>\'\'
             ORDER BY s.created_at_epoch DESC,s.id DESC LIMIT 10'
        );
        $statement->execute([$petId,$petId]);$rows=[];
        foreach($statement->fetchAll() as $row){
            $type=(string)$row['interaction_type'];$definition=self::INTERACTIONS[$type]??['label'=>'Spielmoment','emoji'=>'🐾'];
            $actorOwner=self::steamLabel($row,'actor_owner_');$targetOwner=self::steamLabel($row,'target_owner_');
            if($actorOwner===null||$targetOwner===null)continue;
            $rows[]=[
                'id'=>(int)$row['id'],'actor_pet_id'=>(int)$row['actor_pet_id'],'target_pet_id'=>(int)$row['target_pet_id'],'interaction_type'=>$type,
                'interaction_label'=>(string)$definition['label'],'interaction_emoji'=>(string)$definition['emoji'],'actor_name'=>(string)$row['actor_name'],'target_name'=>(string)$row['target_name'],
                'actor_owner_name'=>$actorOwner,
                'target_owner_name'=>$targetOwner,
                'is_initiator'=>(int)$row['actor_user_id']===$userId,'created_at'=>(string)$row['created_at'],'created_at_epoch'=>(int)$row['created_at_epoch'],'expires_at_epoch'=>(int)$row['expires_at_epoch'],'created_at_label'=>date('d.m.Y H:i',(int)$row['created_at_epoch']),
            ];
        }
        return $rows;
    }

    private static function steamIdentity(PDO $pdo,int $userId):?array
    {
        $statement=$pdo->prepare('SELECT steam_id64,steam_personaname,steam_profile_url,steam_avatar_url,steam_verified_at FROM users WHERE id=? AND active=1 LIMIT 1');
        $statement->execute([$userId]);$identity=$statement->fetch();
        return is_array($identity)&&self::steamLabel($identity)!==null?$identity:null;
    }

    /** Besitzerlabels stammen ausschließlich aus einer vollständig verifizierten Steam-Identität. */
    private static function steamLabel(array $row,string $prefix=''):?string
    {
        $steamId=trim((string)($row[$prefix.'steam_id64']??''));
        $verifiedAt=trim((string)($row[$prefix.'steam_verified_at']??''));
        $persona=trim((string)($row[$prefix.'steam_personaname']??''));
        $profileUrl=trim((string)($row[$prefix.'steam_profile_url']??''));
        $avatarUrl=trim((string)($row[$prefix.'steam_avatar_url']??''));
        $legacySynthetic=preg_match('/^7656119\d{10}$/',$steamId)===1
            &&hash_equals('Steam '.substr($steamId,-6),$persona)
            &&$profileUrl===''&&$avatarUrl==='';
        return preg_match('/^7656119\d{10}$/',$steamId)===1&&$verifiedAt!==''&&$persona!==''&&!$legacySynthetic?$persona:null;
    }

    private static function playResponse(array $row,array $definition,bool $duplicate):array
    {
        $actor=(string)($row['actor_name']??'Dein Pet');$target=(string)($row['target_name']??'das andere Pet');
        return ['duplicate'=>$duplicate,'interaction_id'=>(int)$row['id'],'actor_pet_id'=>(int)$row['actor_pet_id'],'target_pet_id'=>(int)$row['target_pet_id'],'interaction_type'=>(string)$row['interaction_type'],'message'=>$duplicate?'Dieser Spielmoment wurde bereits gestartet.':$actor.' und '.$target.' '.$definition['story']];
    }

    private static function count(PDO $pdo,string $sql,array $parameters=[]):int
    {
        $statement=$pdo->prepare($sql);$statement->execute($parameters);return (int)$statement->fetchColumn();
    }
}
