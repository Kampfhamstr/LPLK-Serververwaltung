<?php
namespace App\Services;

use App\Core\Database;
use PDO;

/**
 * Gemeinsame, kleine Bausteine für das Pet-Spiel. Mutierende Fachlogik bleibt
 * in den jeweiligen Diensten; diese Klasse hält Besitz-, Wallet- und
 * Zustandsregeln an einer Stelle konsistent.
 */
final class PetGameService
{
    public const EQUIPMENT_SLOTS = [
        'head'=>['label'=>'Kopfbedeckung','emoji'=>'🎩'],
        'body'=>['label'=>'Körper','emoji'=>'🛡️'],
        'paws'=>['label'=>'Pfoten','emoji'=>'🥾'],
        'charm'=>['label'=>'Talisman','emoji'=>'🧿'],
    ];

    public const RARITIES = [
        'common'=>['label'=>'Gewöhnlich','rank'=>1],
        'uncommon'=>['label'=>'Ungewöhnlich','rank'=>2],
        'rare'=>['label'=>'Selten','rank'=>3],
        'epic'=>['label'=>'Episch','rank'=>4],
        'legendary'=>['label'=>'Legendär','rank'=>5],
    ];

    public static function commonOverview(int $userId): array
    {
        $pdo=Database::connection();
        $pet=self::activePet($pdo,$userId);
        if(!$pet)return ['pet'=>null,'wallet'=>['coins'=>0,'training_points'=>0],'stats'=>null];
        return ['pet'=>$pet,'wallet'=>self::wallet($pdo,$userId),'stats'=>PetStatsService::stats($pdo,$pet)];
    }

    public static function equipmentOverview(int $userId): array
    {
        $pdo=Database::connection();$pet=self::activePet($pdo,$userId);
        if(!$pet)return ['items'=>[],'equipmentSlots'=>self::EQUIPMENT_SLOTS];
        $statement=$pdo->prepare('SELECT * FROM pet_items WHERE user_id=? AND pet_id=? ORDER BY equipped DESC, CASE rarity WHEN \'legendary\' THEN 5 WHEN \'epic\' THEN 4 WHEN \'rare\' THEN 3 WHEN \'uncommon\' THEN 2 ELSE 1 END DESC,item_level DESC,id DESC');
        $statement->execute([$userId,(int)$pet['id']]);
        $items=[];
        foreach($statement->fetchAll() as $item){
            $item['rarity_label']=self::RARITIES[(string)$item['rarity']]['label']??'Gewöhnlich';
            $item['slot_label']=self::EQUIPMENT_SLOTS[(string)$item['slot']]['label']??(string)$item['slot'];
            $item['stat_total']=(int)$item['strength_bonus']+(int)$item['agility_bonus']+(int)$item['vitality_bonus']+(int)$item['instinct_bonus'];
            $items[]=$item;
        }
        return ['items'=>$items,'equipmentSlots'=>self::EQUIPMENT_SLOTS];
    }

    public static function equipItem(int $userId,int $itemId,string $key):array
    {
        return self::changeItem($userId,$itemId,$key,true);
    }

    public static function unequipItem(int $userId,int $itemId,string $key):array
    {
        return self::changeItem($userId,$itemId,$key,false);
    }

    public static function salvageItem(int $userId,int $itemId,string $key):array
    {
        if($itemId<=0)throw new \RuntimeException('Der Gegenstand ist ungültig.');self::assertIdempotencyKey($key);$pdo=Database::connection();
        return Database::withBusyRetry(function()use($pdo,$userId,$itemId,$key):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $duplicate=$pdo->prepare('SELECT action_type,effects_json FROM pet_actions WHERE user_id=? AND idempotency_key=?');$duplicate->execute([$userId,$key]);
                if($duplicateRow=$duplicate->fetch()){
                    if((string)$duplicateRow['action_type']!=='item_salvage')throw new \RuntimeException('Diese Aktionskennung wurde bereits für eine andere Pet-Aktion verwendet. Bitte lade die Seite neu.');
                    $saved=json_decode((string)$duplicateRow['effects_json'],true);if((int)($saved['item_id']??0)!==$itemId)throw new \RuntimeException('Diese Aktionskennung wurde bereits für einen anderen Gegenstand verwendet. Bitte lade die Seite neu.');$pdo->exec('COMMIT');return ['duplicate'=>true,'item_id'=>$itemId,'coins'=>(int)($saved['coins']??0),'message'=>'Dieser Gegenstand wurde bereits verwertet.'];
                }
                $pet=self::activePet($pdo,$userId,false);if(!$pet)throw new \RuntimeException('Adoptiere zuerst ein Pet.');$select=$pdo->prepare('SELECT * FROM pet_items WHERE id=? AND user_id=? AND pet_id=?');$select->execute([$itemId,$userId,(int)$pet['id']]);$item=$select->fetch();
                if(!$item)throw new \RuntimeException('Dieser Gegenstand gehört nicht zu deinem aktiven Pet.');if((int)$item['equipped']===1)throw new \RuntimeException('Lege den Gegenstand vor dem Verwerten ab.');if((string)$item['source_type']==='starter')throw new \RuntimeException('Das persönliche Starteritem kann nicht verwertet werden.');
                $rarityValue=['common'=>5,'uncommon'=>10,'rare'=>20,'epic'=>40,'legendary'=>80][(string)$item['rarity']]??5;$coins=$rarityValue+2*(int)$item['item_level'];$now=date('c');$payload=['item_id'=>$itemId,'slot'=>(string)$item['slot'],'coins'=>$coins,'item_name'=>(string)$item['item_name']];
                $delete=$pdo->prepare('DELETE FROM pet_items WHERE id=? AND user_id=? AND pet_id=? AND equipped=0');$delete->execute([$itemId,$userId,(int)$pet['id']]);if($delete->rowCount()!==1)throw new \RuntimeException('Der Gegenstand konnte nicht verwertet werden.');
                self::changeCurrency($pdo,$userId,'coins',$coins,'item_salvage','item-salvage:'.$key,$now);
                $pdo->prepare('INSERT INTO pet_actions(pet_id,user_id,action_type,idempotency_key,effects_json,created_at) VALUES(?,?,?,?,?,?)')->execute([(int)$pet['id'],$userId,'item_salvage',$key,self::json($payload),$now]);
                $pdo->exec('COMMIT');return ['duplicate'=>false,'item_id'=>$itemId,'coins'=>$coins,'message'=>(string)$item['item_name'].' wurde für '.$coins.' Münzen verwertet.'];
            }catch(\Throwable $e){self::rollback($pdo);throw $e;}
        });
    }

    private static function changeItem(int $userId,int $itemId,string $key,bool $equip):array
    {
        if($itemId<=0)throw new \RuntimeException('Der Gegenstand ist ungültig.');
        self::assertIdempotencyKey($key);$pdo=Database::connection();$action=$equip?'item_equip':'item_unequip';
        return Database::withBusyRetry(function()use($pdo,$userId,$itemId,$key,$equip,$action):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $duplicate=$pdo->prepare('SELECT action_type,effects_json FROM pet_actions WHERE user_id=? AND idempotency_key=?');$duplicate->execute([$userId,$key]);
                if($duplicateRow=$duplicate->fetch()){
                    if((string)$duplicateRow['action_type']!==$action)throw new \RuntimeException('Diese Aktionskennung wurde bereits für eine andere Pet-Aktion verwendet. Bitte lade die Seite neu.');
                    $saved=json_decode((string)$duplicateRow['effects_json'],true);if((int)($saved['item_id']??0)!==$itemId)throw new \RuntimeException('Diese Aktionskennung wurde bereits für einen anderen Gegenstand verwendet. Bitte lade die Seite neu.');$pdo->exec('COMMIT');
                    return ['duplicate'=>true,'item_id'=>(int)($saved['item_id']??$itemId),'slot'=>(string)($saved['slot']??''),'message'=>'Diese Ausrüstungsänderung wurde bereits verarbeitet.'];
                }
                $pet=self::activePet($pdo,$userId,false);if(!$pet)throw new \RuntimeException('Adoptiere zuerst ein Pet.');
                $select=$pdo->prepare('SELECT * FROM pet_items WHERE id=? AND user_id=? AND pet_id=?');$select->execute([$itemId,$userId,(int)$pet['id']]);$item=$select->fetch();
                if(!$item)throw new \RuntimeException('Dieser Gegenstand gehört nicht zu deinem aktiven Pet.');
                if($equip){
                    if((int)$pet['level']<(int)$item['level_required'])throw new \RuntimeException('Dieser Gegenstand benötigt Level '.(int)$item['level_required'].'.');
                    $pdo->prepare('UPDATE pet_items SET equipped=0 WHERE pet_id=? AND slot=? AND equipped=1 AND id<>?')->execute([(int)$pet['id'],(string)$item['slot'],$itemId]);
                    $pdo->prepare('UPDATE pet_items SET equipped=1 WHERE id=? AND user_id=? AND pet_id=?')->execute([$itemId,$userId,(int)$pet['id']]);
                }else{
                    if((int)$item['equipped']!==1)throw new \RuntimeException('Dieser Gegenstand ist bereits abgelegt.');
                    $pdo->prepare('UPDATE pet_items SET equipped=0 WHERE id=? AND user_id=? AND pet_id=?')->execute([$itemId,$userId,(int)$pet['id']]);
                }
                $payload=['item_id'=>$itemId,'slot'=>(string)$item['slot'],'equipped'=>$equip];$now=date('c');
                $pdo->prepare('INSERT INTO pet_actions(pet_id,user_id,action_type,idempotency_key,effects_json,created_at) VALUES(?,?,?,?,?,?)')->execute([(int)$pet['id'],$userId,$action,$key,self::json($payload),$now]);
                $pdo->exec('COMMIT');
                return ['duplicate'=>false,'item_id'=>$itemId,'slot'=>(string)$item['slot'],'message'=>(string)$item['item_name'].($equip?' wurde ausgerüstet.':' wurde abgelegt.')];
            }catch(\Throwable $e){self::rollback($pdo);throw $e;}
        });
    }

    public static function activePet(PDO $pdo,int $userId,bool $withDecay=true):?array
    {
        $statement=$pdo->prepare('SELECT * FROM pets WHERE user_id=? AND active=1 LIMIT 1');$statement->execute([$userId]);$pet=$statement->fetch();
        if(!$pet)return null;
        return $withDecay?self::decayed($pet):$pet;
    }

    public static function decayed(array $pet):array
    {
        $last=strtotime((string)($pet['last_state_at']??''));$hours=max(0,(time()-($last?:time()))/3600);$startHunger=(float)$pet['hunger'];
        $pet['hunger']=self::clamp($startHunger-$hours*1.25);$pet['energy']=self::clamp((float)$pet['energy']-$hours*.6);$pet['happiness']=self::clamp((float)$pet['happiness']-$hours*.8);
        $neglect=max(0,$hours-($startHunger<=15?0:($startHunger-15)/1.25));if($neglect>0)$pet['health']=self::clamp((float)$pet['health']-$neglect*.3);
        return $pet;
    }

    public static function assertIdempotencyKey(string $key):void
    {
        if(!preg_match('/^[a-zA-Z0-9_-]{16,80}$/',$key))throw new \RuntimeException('Die Aktionskennung ist ungültig. Bitte lade die Seite neu.');
    }

    public static function ensureNoActiveAdventure(PDO $pdo,int $petId):void
    {
        $statement=$pdo->prepare("SELECT 1 FROM pet_adventures WHERE pet_id=? AND status='running' LIMIT 1");$statement->execute([$petId]);
        if($statement->fetchColumn())throw new \RuntimeException('Dein Pet ist noch auf Abenteuer. Hole zuerst das Ergebnis ab.');
    }

    public static function wallet(PDO $pdo,int $userId):array
    {
        $statement=$pdo->prepare('SELECT coins,training_points FROM pet_wallets WHERE user_id=?');$statement->execute([$userId]);
        return $statement->fetch()?:['coins'=>0,'training_points'=>0];
    }

    public static function ensureWallet(PDO $pdo,int $userId):void
    {
        $pdo->prepare('INSERT OR IGNORE INTO pet_wallets(user_id,coins,training_points,updated_at) VALUES(?,?,?,?)')->execute([$userId,0,0,date('c')]);
    }

    public static function changeCurrency(PDO $pdo,int $userId,string $currency,int $amount,string $reason,string $reference,string $now):void
    {
        if(!in_array($currency,['coins','training_points'],true))throw new \InvalidArgumentException('Unbekannte Pet-Währung.');
        if($amount===0)return;self::ensureWallet($pdo,$userId);$column=$currency==='coins'?'coins':'training_points';
        $statement=$pdo->prepare("UPDATE pet_wallets SET {$column}={$column}+?,updated_at=? WHERE user_id=? AND {$column}+? >= 0");$statement->execute([$amount,$now,$userId,$amount]);
        if($statement->rowCount()!==1)throw new \RuntimeException($currency==='coins'?'Nicht genügend Münzen.':'Nicht genügend Trainingspunkte.');
        $pdo->prepare('INSERT INTO pet_wallet_ledger(user_id,currency,amount,reason,reference_key,created_at) VALUES(?,?,?,?,?,?)')->execute([$userId,$currency,$amount,$reason,$reference,$now]);
    }

    public static function addXp(PDO $pdo,array $pet,int $xp,string $now):array
    {
        $newXp=max(0,(int)$pet['xp']+$xp);$level=self::levelForXp($newXp);
        $statement=$pdo->prepare('UPDATE pets SET xp=?,level=?,version=version+1,updated_at=? WHERE id=? AND user_id=?');$statement->execute([$newXp,$level,$now,(int)$pet['id'],(int)$pet['user_id']]);
        return ['xp'=>$newXp,'level'=>$level,'level_up'=>$level>(int)$pet['level']];
    }

    public static function spendVitals(PDO $pdo,array $pet,int $energyCost,int $hungerCost,int $now):array
    {
        $pet=self::decayed($pet);$energy=(int)$pet['energy'];
        if($energy<$energyCost)throw new \RuntimeException('Dein Pet hat dafür nicht genug Energie.');
        $iso=date('c',$now);$newEnergy=self::clamp($energy-$energyCost);$newHunger=self::clamp((int)$pet['hunger']-$hungerCost);
        $statement=$pdo->prepare('UPDATE pets SET health=?,hunger=?,energy=?,happiness=?,last_state_at=?,version=version+1,updated_at=? WHERE id=? AND user_id=? AND version=?');
        $statement->execute([(int)$pet['health'],$newHunger,$newEnergy,(int)$pet['happiness'],$iso,$iso,(int)$pet['id'],(int)$pet['user_id'],(int)$pet['version']]);
        if($statement->rowCount()!==1)throw new \RuntimeException('Das Pet wurde parallel geändert. Bitte erneut versuchen.');
        $pet['energy']=$newEnergy;$pet['hunger']=$newHunger;$pet['last_state_at']=$iso;$pet['version']=(int)$pet['version']+1;
        return $pet;
    }

    public static function levelForXp(int $xp):int
    {
        $sum=0;for($level=1;$level<20;$level++){$sum+=(int)round(100*($level**1.45));if($xp<$sum)return $level;}return 20;
    }

    public static function clamp(float $value,int $min=0,int $max=100):int{return (int)round(max($min,min($max,$value)));}
    public static function json(mixed $value):string{return json_encode($value,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);}
    public static function rollback(PDO $pdo):void{try{$pdo->exec('ROLLBACK');}catch(\Throwable){}}
}
