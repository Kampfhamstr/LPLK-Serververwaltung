<?php
namespace App\Services;

use App\Core\Database;
use PDO;

final class PetAdventureService
{
    public const ADVENTURES = [
        'sunny_meadow'=>[
            'label'=>'Sonnenwiese','emoji'=>'🌻','description'=>'Leichte Spuren, freche Käfer und ein guter Ort für den ersten Fund.',
            'level'=>1,'duration'=>600,'energy'=>10,'hunger'=>5,'difficulty'=>1,'target'=>10,'base_coins'=>14,'base_xp'=>20,'drop'=>55,'max_rarity'=>'rare','max_item_level'=>5,
        ],
        'old_mill'=>[
            'label'=>'Alte Mühle','emoji'=>'🌾','description'=>'Knarrende Dielen und versteckte Vorratskammern verlangen Mut und Trittsicherheit.',
            'level'=>3,'duration'=>1800,'energy'=>18,'hunger'=>8,'difficulty'=>2,'target'=>13,'base_coins'=>30,'base_xp'=>42,'drop'=>70,'max_rarity'=>'epic','max_item_level'=>9,
        ],
        'moon_forest'=>[
            'label'=>'Mondwald','emoji'=>'🌙','description'=>'Im Nebel helfen scharfe Sinne. Seltene Relikte warten abseits der Wege.',
            'level'=>6,'duration'=>3600,'energy'=>26,'hunger'=>12,'difficulty'=>3,'target'=>17,'base_coins'=>58,'base_xp'=>78,'drop'=>82,'max_rarity'=>'legendary','max_item_level'=>15,
        ],
        'dragon_ruins'=>[
            'label'=>'Drachenruinen','emoji'=>'🏔️','description'=>'Eine lange Expedition mit harten Prüfungen und der besten Beute.',
            'level'=>10,'duration'=>10800,'energy'=>35,'hunger'=>16,'difficulty'=>4,'target'=>22,'base_coins'=>105,'base_xp'=>140,'drop'=>92,'max_rarity'=>'legendary','max_item_level'=>20,
        ],
    ];

    public const TACTICS = [
        'cautious'=>['label'=>'Vorsichtig','description'=>'+8 % auf Prüfungen, Belohnungen × 0,9','chance'=>8,'reward'=>.9,'loot'=>0,'checks'=>['vitality','instinct','vitality']],
        'explore'=>['label'=>'Erkundend','description'=>'+15 % Fundchance, ausgewogene Prüfungen','chance'=>0,'reward'=>1.0,'loot'=>15,'checks'=>['instinct','agility','instinct']],
        'bold'=>['label'=>'Mutig','description'=>'−5 % auf Prüfungen, Belohnungen × 1,2','chance'=>-5,'reward'=>1.2,'loot'=>5,'checks'=>['strength','strength','agility']],
    ];

    private const MAX_DAILY_STARTS=3;

    public static function overview(int $userId):array
    {
        $pdo=Database::connection();$pet=PetGameService::activePet($pdo,$userId);
        if(!$pet)return ['adventures'=>[],'adventureTactics'=>self::TACTICS,'activeAdventure'=>null,'lastAdventure'=>null,'adventureStartsLeft'=>0,'serverNowEpoch'=>time()];
        $stats=PetStatsService::stats($pdo,$pet);$catalog=[];
        foreach(self::ADVENTURES as $key=>$definition){
            $levelReady=(int)$pet['level']>=(int)$definition['level'];$healthReady=(int)$pet['health']>=25;$energyReady=(int)$pet['energy']>=(int)$definition['energy'];
            $definition['key']=$key;$definition['available']=$levelReady&&$healthReady&&$energyReady;
            $definition['estimated_chance']=self::estimateChance($pet,$stats,$definition,self::TACTICS['explore']);
            $definition['icon']=$definition['emoji'];$definition['duration_minutes']=(int)ceil((int)$definition['duration']/60);$definition['energy_cost']=$definition['energy'];$definition['min_level']=$definition['level'];$definition['max_rarity_label']=PetGameService::RARITIES[(string)$definition['max_rarity']]['label'];
            $definition['check_label']='Drei Attributprüfungen · ca. '.$definition['estimated_chance'].' %';$definition['loot_hint']='Bis '.PetGameService::RARITIES[(string)$definition['max_rarity']]['label'].' · Ausrüstung, Münzen und XP';$definition['tactics']=self::TACTICS;
            if(!$levelReady)$definition['locked_reason']='Wird mit Pet-Level '.(int)$definition['level'].' freigeschaltet.';elseif(!$healthReady)$definition['locked_reason']='Dein Pet braucht mindestens 25 Gesundheit.';elseif(!$energyReady)$definition['locked_reason']='Dein Pet braucht dafür '.(int)$definition['energy'].' Energie.';
            $definition['duration_label']=self::durationLabel((int)$definition['duration']);$catalog[$key]=$definition;
        }
        $activeStatement=$pdo->prepare("SELECT id,adventure_key,difficulty,started_at_epoch,ready_at_epoch,status,result_json FROM pet_adventures WHERE user_id=? AND pet_id=? AND status='running' LIMIT 1");
        $activeStatement->execute([$userId,(int)$pet['id']]);$active=$activeStatement->fetch()?:null;
        if($active){
            $stored=self::decode((string)$active['result_json']);$key=(string)$active['adventure_key'];$definition=self::ADVENTURES[$key]??[];
            $active=[
                'id'=>(int)$active['id'],'adventure_key'=>$key,'label'=>(string)($definition['label']??$key),'emoji'=>(string)($definition['emoji']??'🗺️'),
                'tactic'=>(string)($stored['tactic']??''),'tactic_label'=>(string)(self::TACTICS[(string)($stored['tactic']??'')]['label']??'Unbekannt'),
                'description'=>(string)($definition['description']??''),'started_at_epoch'=>(int)$active['started_at_epoch'],'started_at'=>date('c',(int)$active['started_at_epoch']),
                'ready_at_epoch'=>(int)$active['ready_at_epoch'],'ready_at'=>date('c',(int)$active['ready_at_epoch']),'ready'=>(int)$active['ready_at_epoch']<=time(),'can_claim'=>(int)$active['ready_at_epoch']<=time(),
            ];
        }
        $lastStatement=$pdo->prepare("SELECT id,adventure_key,started_at_epoch,ready_at_epoch,claimed_at_epoch,result_json FROM pet_adventures WHERE user_id=? AND pet_id=? AND status='claimed' ORDER BY claimed_at_epoch DESC,id DESC LIMIT 1");
        $lastStatement->execute([$userId,(int)$pet['id']]);$last=$lastStatement->fetch()?:null;
        if($last){
            $result=self::decode((string)$last['result_json']);$events=[];foreach((array)($result['checks']??[]) as $check)$events[]=['text'=>(string)($check['story']??'Prüfung').' '.(!empty($check['passed'])?'Geschafft.':'Nicht geschafft.').' ('.(int)($check['roll']??0).' / '.(int)($check['chance']??0).')'];
            $item=is_array($result['item']??null)?$result['item']:null;$loot=[];if($item)$loot[]=$item+['name'=>$item['item_name'],'attributes'=>['Kraft'=>(int)$item['strength_bonus'],'Tempo'=>(int)$item['agility_bonus'],'Zähigkeit'=>(int)$item['vitality_bonus'],'Instinkt'=>(int)$item['instinct_bonus']]];
            $rewards=(array)($result['rewards']??[]);$last=array_merge($last,['result'=>$result,'status'=>'claimed','claimed'=>true,'events'=>$events,'loot'=>$loot,'coins'=>(int)($rewards['coins']??0),'xp'=>(int)($rewards['xp']??0),'training_points'=>(int)($rewards['training_points']??0),'label'=>self::ADVENTURES[(string)$last['adventure_key']]['label']??(string)$last['adventure_key']]);
        }
        [$dayStart,$dayEnd]=self::dayBounds();$daily=$pdo->prepare('SELECT COUNT(*) FROM pet_adventures WHERE user_id=? AND started_at_epoch>=? AND started_at_epoch<?');$daily->execute([$userId,$dayStart,$dayEnd]);
        return ['adventures'=>$catalog,'adventureTactics'=>self::TACTICS,'activeAdventure'=>$active,'lastAdventure'=>$last,'adventureStartsLeft'=>max(0,self::MAX_DAILY_STARTS-(int)$daily->fetchColumn()),'serverNowEpoch'=>time()];
    }

    public static function start(int $userId,string $adventureKey,string $tacticKey,string $idempotencyKey):array
    {
        if(!isset(self::ADVENTURES[$adventureKey]))throw new \RuntimeException('Wähle ein gültiges Abenteuer.');
        if(!isset(self::TACTICS[$tacticKey]))throw new \RuntimeException('Wähle eine gültige Vorgehensweise.');
        PetGameService::assertIdempotencyKey($idempotencyKey);$definition=self::ADVENTURES[$adventureKey];$tactic=self::TACTICS[$tacticKey];$pdo=Database::connection();
        return Database::withBusyRetry(function()use($pdo,$userId,$adventureKey,$tacticKey,$idempotencyKey,$definition,$tactic):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $duplicate=$pdo->prepare('SELECT id,adventure_key,result_json FROM pet_adventures WHERE user_id=? AND idempotency_key=?');$duplicate->execute([$userId,$idempotencyKey]);
                if($row=$duplicate->fetch()){
                    $stored=self::decode((string)$row['result_json']);$storedTactic=(string)($stored['tactic']??'');
                    if((string)$row['adventure_key']!==$adventureKey||$storedTactic!==$tacticKey)throw new \RuntimeException('Diese Aktionskennung wurde bereits für ein anderes Abenteuer verwendet. Bitte lade die Seite neu.');
                    $pdo->exec('COMMIT');return ['duplicate'=>true,'adventure_id'=>(int)$row['id'],'adventure_key'=>(string)$row['adventure_key'],'tactic'=>$storedTactic,'message'=>'Dieses Abenteuer wurde bereits gestartet.'];
                }
                $pet=PetGameService::activePet($pdo,$userId,false);if(!$pet)throw new \RuntimeException('Adoptiere zuerst ein Pet.');$effectivePet=PetGameService::decayed($pet);
                PetGameService::ensureNoActiveAdventure($pdo,(int)$pet['id']);
                if((int)$pet['level']<(int)$definition['level'])throw new \RuntimeException('Dieses Abenteuer wird erst ab Level '.(int)$definition['level'].' freigeschaltet.');
                if((int)$effectivePet['health']<25)throw new \RuntimeException('Dein Pet braucht mindestens 25 Gesundheit für ein Abenteuer.');
                $inventory=$pdo->prepare('SELECT COUNT(*) FROM pet_items WHERE user_id=? AND pet_id=?');$inventory->execute([$userId,(int)$pet['id']]);
                if((int)$inventory->fetchColumn()>=60)throw new \RuntimeException('Das Inventar ist voll. Es sind höchstens 60 Gegenstände möglich.');
                [$dayStart,$dayEnd]=self::dayBounds();$daily=$pdo->prepare('SELECT COUNT(*) FROM pet_adventures WHERE user_id=? AND started_at_epoch>=? AND started_at_epoch<?');$daily->execute([$userId,$dayStart,$dayEnd]);
                if((int)$daily->fetchColumn()>=self::MAX_DAILY_STARTS)throw new \RuntimeException('Heute wurden bereits alle drei Abenteuer gestartet. Morgen geht es weiter.');
                $now=time();$pet=PetGameService::spendVitals($pdo,$pet,(int)$definition['energy'],(int)$definition['hunger'],$now);$stats=PetStatsService::stats($pdo,$pet);
                $result=self::rollResult($pet,$stats,$adventureKey,$tacticKey,$definition,$tactic);$ready=$now+(int)$definition['duration'];$iso=date('c',$now);
                $statement=$pdo->prepare('INSERT INTO pet_adventures(user_id,pet_id,adventure_key,status,difficulty,idempotency_key,started_at_epoch,ready_at_epoch,result_json,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?)');
                $statement->execute([$userId,(int)$pet['id'],$adventureKey,'running',(int)$definition['difficulty'],$idempotencyKey,$now,$ready,PetGameService::json($result),$iso,$iso]);$id=(int)$pdo->lastInsertId();$pdo->exec('COMMIT');
                return ['duplicate'=>false,'adventure_id'=>$id,'adventure_key'=>$adventureKey,'tactic'=>$tacticKey,'message'=>$pet['name'].' erkundet jetzt die '.$definition['label'].'. Rückkehr in '.self::durationLabel((int)$definition['duration']).'.'];
            }catch(\Throwable $e){PetGameService::rollback($pdo);throw $e;}
        });
    }

    public static function claim(int $userId,int $adventureId,string $idempotencyKey):array
    {
        if($adventureId<=0)throw new \RuntimeException('Das Abenteuer ist ungültig.');PetGameService::assertIdempotencyKey($idempotencyKey);$pdo=Database::connection();
        return Database::withBusyRetry(function()use($pdo,$userId,$adventureId,$idempotencyKey):array{
            $pdo->exec('BEGIN IMMEDIATE');
            try{
                $keyOwner=$pdo->prepare('SELECT id FROM pet_adventures WHERE user_id=? AND claim_idempotency_key=?');$keyOwner->execute([$userId,$idempotencyKey]);$keyAdventure=$keyOwner->fetchColumn();
                if($keyAdventure!==false&&(int)$keyAdventure!==$adventureId)throw new \RuntimeException('Diese Aktionskennung wurde bereits für eine andere Belohnung verwendet. Bitte lade die Seite neu.');
                $statement=$pdo->prepare('SELECT * FROM pet_adventures WHERE id=? AND user_id=?');$statement->execute([$adventureId,$userId]);$adventure=$statement->fetch();
                if(!$adventure)throw new \RuntimeException('Dieses Abenteuer gehört nicht zu dir.');$result=self::claimResult((string)$adventure['result_json'],(string)$adventure['adventure_key']);
                if((string)$adventure['status']==='claimed'){
                    if(hash_equals((string)$adventure['claim_idempotency_key'],$idempotencyKey)){$pdo->exec('COMMIT');return self::claimResponse($adventure,$result,true);}
                    throw new \RuntimeException('Diese Abenteuerbelohnung wurde bereits abgeholt.');
                }
                $now=time();if((int)$adventure['ready_at_epoch']>$now)throw new \RuntimeException('Dein Pet ist noch unterwegs. Die Serverzeit schützt das Ergebnis vor vorzeitigem Abholen.');
                $iso=date('c',$now);$update=$pdo->prepare("UPDATE pet_adventures SET status='claimed',claim_idempotency_key=?,claimed_at_epoch=?,updated_at=? WHERE id=? AND user_id=? AND status='running' AND ready_at_epoch<=?");
                $update->execute([$idempotencyKey,$now,$iso,$adventureId,$userId,$now]);if($update->rowCount()!==1)throw new \RuntimeException('Die Belohnung wurde parallel verarbeitet. Bitte lade die Seite neu.');
                $pet=PetGameService::activePet($pdo,$userId,false);if(!$pet||(int)$pet['id']!==(int)$adventure['pet_id'])throw new \RuntimeException('Das aktive Pet passt nicht mehr zum Abenteuer.');
                $rewards=is_array($result['rewards']??null)?$result['rewards']:[];$coins=max(0,(int)($rewards['coins']??0));$xp=max(0,(int)($rewards['xp']??0));$training=max(0,(int)($rewards['training_points']??0));
                if($coins>0)PetGameService::changeCurrency($pdo,$userId,'coins',$coins,'adventure','adventure:'.$adventureId.':coins',$iso);
                if($training>0)PetGameService::changeCurrency($pdo,$userId,'training_points',$training,'adventure','adventure:'.$adventureId.':training',$iso);
                PetGameService::addXp($pdo,$pet,$xp,$iso);
                $item=is_array($result['item']??null)?$result['item']:null;
                if($item){
                    $insert=$pdo->prepare('INSERT INTO pet_items(user_id,pet_id,template_key,item_name,slot,rarity,item_level,level_required,strength_bonus,agility_bonus,vitality_bonus,instinct_bonus,equipped,source_type,source_id,source_index,created_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)');
                    $insert->execute([$userId,(int)$pet['id'],$item['template_key'],$item['item_name'],$item['slot'],$item['rarity'],$item['item_level'],$item['level_required'],$item['strength_bonus'],$item['agility_bonus'],$item['vitality_bonus'],$item['instinct_bonus'],0,'adventure',$adventureId,0,$iso]);
                }
                $adventure['claim_idempotency_key']=$idempotencyKey;$pdo->exec('COMMIT');return self::claimResponse($adventure,$result,false);
            }catch(\Throwable $e){PetGameService::rollback($pdo);throw $e;}
        });
    }

    private static function rollResult(array $pet,array $stats,string $adventureKey,string $tacticKey,array $definition,array $tactic):array
    {
        $checks=[];$successes=0;$care=((int)$pet['health']+(int)$pet['hunger']+(int)$pet['energy']+(int)$pet['happiness'])/4;
        $stories=['strength'=>['Ein versperrter Weg verlangt kräftige Pfoten.','Eine schwere Truhe muss bewegt werden.'],'agility'=>['Ein schmaler Pfad prüft jeden Schritt.','Eine flinke Kreatur schnappt sich die Spur.'],'vitality'=>['Wind und Wetter testen die Ausdauer.','Ein Hindernis verlangt Geduld und Standfestigkeit.'],'instinct'=>['Eine fast unsichtbare Fährte führt weiter.','Ein leises Rascheln verrät ein Geheimnis.']];
        foreach($tactic['checks'] as $index=>$stat){
            $chance=PetGameService::clamp(55+4*((int)$pet['level']-(int)$definition['level'])+3*((int)$stats[$stat]-(int)$definition['target'])+(int)$tactic['chance']+(int)round(($care-50)/10)+intdiv((int)$pet['bond'],25),20,95);
            $roll=random_int(1,100);$passed=$roll<=$chance;if($passed)$successes++;
            $checks[]=['stat'=>$stat,'stat_label'=>self::statLabel($stat),'chance'=>$chance,'roll'=>$roll,'passed'=>$passed,'story'=>$stories[$stat][$index%2]];
        }
        $factor=[0=>.35,1=>.7,2=>1.0,3=>1.25][$successes];$rewardFactor=$factor*(float)$tactic['reward'];
        $coins=max(1,(int)round((int)$definition['base_coins']*$rewardFactor*random_int(90,110)/100));$xp=max(1,(int)round((int)$definition['base_xp']*$rewardFactor));
        $lootChance=PetGameService::clamp((int)$definition['drop']+(int)$tactic['loot']+(int)$stats['skill_effects']['loot_bonus_percent']+($successes===3?10:0)-($successes===0?(int)round((int)$definition['drop']/2):0),5,100);
        $item=random_int(1,100)<=$lootChance?self::rollItem($pet,$stats,$adventureKey,$definition,$successes):null;
        $training=($successes===3&&random_int(1,100)<=30)?1:0;$outcomes=[0=>['key'=>'mishap','label'=>'Missgeschick'],1=>['key'=>'narrow','label'=>'Knapp geschafft'],2=>['key'=>'success','label'=>'Erfolg'],3=>['key'=>'heroic','label'=>'Heldentat']];
        return [
            'rules_version'=>'1.13.0','adventure_key'=>$adventureKey,'tactic'=>$tacticKey,
            'snapshot'=>['level'=>(int)$pet['level'],'care'=>(int)round($care),'bond'=>(int)$pet['bond'],'stats'=>$stats],
            'checks'=>$checks,'successes'=>$successes,'outcome'=>$outcomes[$successes],
            'rewards'=>['coins'=>$coins,'xp'=>$xp,'training_points'=>$training],'loot_chance'=>$lootChance,'item'=>$item,
        ];
    }

    private static function rollItem(array $pet,array $stats,string $adventureKey,array $definition,int $successes):array
    {
        $rarityOrder=['common','uncommon','rare','epic','legendary'];$weightsByAdventure=[
            'sunny_meadow'=>[78,20,2,0,0],'old_mill'=>[55,34,10,1,0],'moon_forest'=>[30,42,23,4,1],'dragon_ruins'=>[15,35,35,12,3],
        ];
        $weights=$weightsByAdventure[$adventureKey];$boost=(int)$stats['skill_effects']['loot_bonus_percent']+($successes===3?8:0);$roll=min(100,random_int(1,100)+$boost);$cursor=0;$rarity='common';
        foreach($weights as $index=>$weight){$cursor+=$weight;if($roll<=$cursor){$rarity=$rarityOrder[$index];break;}}
        $maximum=array_search((string)$definition['max_rarity'],$rarityOrder,true);$rank=array_search($rarity,$rarityOrder,true);if($rank>$maximum)$rarity=$rarityOrder[$maximum];
        $itemLevel=max(1,min((int)$definition['max_item_level'],(int)$pet['level']+random_int(-1,1)));$base=['common'=>2,'uncommon'=>4,'rare'=>7,'epic'=>10,'legendary'=>14][$rarity];$budget=$base+intdiv(max(0,$itemLevel-1),4);
        $templates=[
            'head'=>[['trail_hat','Pfadfinderhut'],['crystal_goggles','Kristallbrille'],['storm_crown','Sturmkrone']],
            'body'=>[['leather_harness','Lederharnisch'],['moss_armor','Moosrüstung'],['dragon_coat','Drachenschuppenmantel']],
            'paws'=>[['trail_boots','Wanderstiefel'],['cloud_paws','Wolkenpfoten'],['ember_claws','Glutklauen']],
            'charm'=>[['lucky_acorn','Glückseichel'],['moon_compass','Mondkompass'],['ancient_talisman','Uralter Talisman']],
        ];
        $slot=array_rand($templates);$template=$templates[$slot][random_int(0,count($templates[$slot])-1)];$rarityPrefix=['common'=>'Solider','uncommon'=>'Feiner','rare'=>'Seltener','epic'=>'Epischer','legendary'=>'Legendärer'][$rarity];
        $statsBonus=['strength_bonus'=>0,'agility_bonus'=>0,'vitality_bonus'=>0,'instinct_bonus'=>0];$preferred=['head'=>'instinct_bonus','body'=>'vitality_bonus','paws'=>'agility_bonus','charm'=>'strength_bonus'][$slot];
        $statsBonus[$preferred]++;
        for($point=1;$point<$budget;$point++){
            $keys=array_keys($statsBonus);$choice=(random_int(1,100)<=55)?$preferred:$keys[random_int(0,count($keys)-1)];$statsBonus[$choice]++;
        }
        return array_merge([
            'template_key'=>$template[0],'item_name'=>$rarityPrefix.' '.$template[1],'slot'=>$slot,'rarity'=>$rarity,'rarity_label'=>PetGameService::RARITIES[$rarity]['label'],'item_level'=>$itemLevel,'level_required'=>max(1,min((int)$pet['level'],$itemLevel)),
        ],$statsBonus);
    }

    private static function estimateChance(array $pet,array $stats,array $definition,array $tactic):int
    {
        $care=((int)$pet['health']+(int)$pet['hunger']+(int)$pet['energy']+(int)$pet['happiness'])/4;$total=0;
        foreach($tactic['checks'] as $stat)$total+=PetGameService::clamp(55+4*((int)$pet['level']-(int)$definition['level'])+3*((int)$stats[$stat]-(int)$definition['target'])+(int)$tactic['chance']+(int)round(($care-50)/10)+intdiv((int)$pet['bond'],25),20,95);
        return (int)round($total/count($tactic['checks']));
    }

    private static function claimResponse(array $adventure,array $result,bool $duplicate):array
    {
        $rewards=$result['rewards']??[];$item=$result['item']??null;$message=($result['outcome']['label']??'Abenteuer beendet').': +'.(int)($rewards['coins']??0).' Münzen, +'.(int)($rewards['xp']??0).' XP';
        if((int)($rewards['training_points']??0)>0)$message.=', +'.(int)$rewards['training_points'].' Trainingspunkt';$message.=$item?' und '.$item['item_name'].'.':'. Diesmal wurde kein Gegenstand gefunden.';
        return ['duplicate'=>$duplicate,'adventure_id'=>(int)$adventure['id'],'coins'=>(int)($rewards['coins']??0),'xp'=>(int)($rewards['xp']??0),'item_name'=>$item['item_name']??null,'message'=>$duplicate?'Diese Belohnung wurde bereits gutgeschrieben.':$message];
    }

    private static function claimResult(string $json,string $expectedAdventureKey):array
    {
        try{$value=json_decode($json,true,512,JSON_THROW_ON_ERROR);}catch(\JsonException $error){throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot ist beschädigt. Die Belohnung wurde nicht verändert.',0,$error);}
        if(!is_array($value)||!isset(self::ADVENTURES[$expectedAdventureKey])||($value['rules_version']??null)!=='1.13.0'||($value['adventure_key']??null)!==$expectedAdventureKey||!is_string($value['tactic']??null)||!isset(self::TACTICS[$value['tactic']])||!is_array($value['snapshot']??null)||!is_int($value['snapshot']['level']??null)||$value['snapshot']['level']<1||$value['snapshot']['level']>20||!is_int($value['successes']??null)||$value['successes']<0||$value['successes']>3||!is_array($value['outcome']??null)||!is_string($value['outcome']['label']??null)||!is_array($value['rewards']??null))throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot ist unvollständig. Die Belohnung wurde nicht verändert.');
        foreach(['coins','xp','training_points'] as $field)if(!isset($value['rewards'][$field])||!is_int($value['rewards'][$field])||$value['rewards'][$field]<0)throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot enthält ungültige Belohnungen. Die Belohnung wurde nicht verändert.');
        $definition=self::ADVENTURES[$expectedAdventureKey];$tactic=self::TACTICS[$value['tactic']];$successes=$value['successes'];$factor=[.35,.7,1.0,1.25][$successes];
        $expectedXp=max(1,(int)round((int)$definition['base_xp']*$factor*(float)$tactic['reward']));$minimumCoins=max(1,(int)round((int)$definition['base_coins']*$factor*(float)$tactic['reward']*.9));$maximumCoins=max(1,(int)round((int)$definition['base_coins']*$factor*(float)$tactic['reward']*1.1));
        if($value['rewards']['xp']!==$expectedXp||$value['rewards']['coins']<$minimumCoins||$value['rewards']['coins']>$maximumCoins||$value['rewards']['training_points']>1||($value['rewards']['training_points']===1&&$successes!==3))throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot enthält unplausible Belohnungen. Die Belohnung wurde nicht verändert.');
        $item=$value['item']??null;
        if($item!==null){
            $required=['template_key','item_name','slot','rarity','item_level','level_required','strength_bonus','agility_bonus','vitality_bonus','instinct_bonus'];
            if(!is_array($item))throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot enthält ungültige Beute. Die Belohnung wurde nicht verändert.');
            foreach($required as $field)if(!array_key_exists($field,$item))throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot enthält unvollständige Beute. Die Belohnung wurde nicht verändert.');
            if(!is_string($item['slot'])||!isset(PetGameService::EQUIPMENT_SLOTS[$item['slot']])||!is_string($item['rarity'])||!isset(PetGameService::RARITIES[$item['rarity']])||!is_string($item['template_key'])||strlen($item['template_key'])<2||strlen($item['template_key'])>64||!is_string($item['item_name'])||strlen($item['item_name'])<2||strlen($item['item_name'])>100)throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot enthält ungültige Beute. Die Belohnung wurde nicht verändert.');
            foreach(['item_level','level_required','strength_bonus','agility_bonus','vitality_bonus','instinct_bonus'] as $field)if(!is_int($item[$field])||$item[$field]<0)throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot enthält ungültige Beutewerte. Die Belohnung wurde nicht verändert.');
            $rarities=array_keys(PetGameService::RARITIES);$rarityRank=array_search($item['rarity'],$rarities,true);$maximumRarityRank=array_search((string)$definition['max_rarity'],$rarities,true);$snapshotLevel=$value['snapshot']['level'];$minimumItemLevel=max(1,min((int)$definition['max_item_level'],$snapshotLevel-1));$maximumItemLevel=max(1,min((int)$definition['max_item_level'],$snapshotLevel+1));
            $baseBudget=['common'=>2,'uncommon'=>4,'rare'=>7,'epic'=>10,'legendary'=>14][$item['rarity']];$expectedBudget=$baseBudget+intdiv(max(0,$item['item_level']-1),4);$actualBudget=$item['strength_bonus']+$item['agility_bonus']+$item['vitality_bonus']+$item['instinct_bonus'];
            if($rarityRank===false||$maximumRarityRank===false||$rarityRank>$maximumRarityRank||$item['item_level']<$minimumItemLevel||$item['item_level']>$maximumItemLevel||$item['level_required']!==max(1,min($snapshotLevel,$item['item_level']))||$actualBudget!==$expectedBudget)throw new \UnexpectedValueException('Der gespeicherte Abenteuer-Snapshot enthält unplausible Beutewerte. Die Belohnung wurde nicht verändert.');
        }
        return $value;
    }

    private static function decode(string $json):array{$value=json_decode($json,true);return is_array($value)?$value:[];}
    private static function statLabel(string $stat):string{return ['strength'=>'Kraft','agility'=>'Tempo','vitality'=>'Zähigkeit','instinct'=>'Instinkt'][$stat]??$stat;}
    private static function dayBounds():array{return [strtotime('today'),strtotime('tomorrow')];}
    private static function durationLabel(int $seconds):string{$minutes=(int)ceil($seconds/60);return $minutes>=60?((int)ceil($minutes/60)).' Std.':$minutes.' Min.';}
}
