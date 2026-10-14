<?php
namespace App\Services;

/** Preparation only: no activation, import or balance-writing method exists here. */
final class BankNetworkPlan
{
    public const PRODUCTION=['11110001','11110007','11110003','11110002','11110006','11110005'];
    public const TEST='11110004';
    public static function group(string $server):?array
    {
        if($server===self::TEST)return ['id'=>'lplk-test-11110004','name'=>'Getrennter Testverbund','state'=>'prepared'];
        if(in_array($server,self::PRODUCTION,true))return ['id'=>'lplk-production','name'=>'Gemeinsame Produktivbank','state'=>'prepared'];
        return null;
    }
    public static function steam(string $id):bool{return preg_match('/^[0-9]{17}$/D',$id)===1&&strcmp($id,'76561197960265728')>=0&&strcmp($id,'76561202255233023')<=0;}
    public static function inventory(array $snapshots):array
    {
        if(count($snapshots)>6)throw new \InvalidArgumentException('Zu viele Quellserver.');
        $seen=[];$databases=[];$accounts=[];$blocked=[];$sources=[];
        foreach($snapshots as $s){
            if(!is_array($s)||!in_array($s['server']??null,self::PRODUCTION,true)||isset($seen[$s['server']]))throw new \InvalidArgumentException('Unbekannter, Test- oder doppelter Quellserver.');
            $id=$s['server'];$seen[$id]=true;
            if(!preg_match('/^[a-f0-9]{64}$/D',$s['sha256']??'')||!is_string($s['databaseIdentity']??null)||!preg_match('/^[a-zA-Z0-9:_-]{1,128}$/D',$s['databaseIdentity'])||!is_string($s['period']??null)||strlen($s['period'])<1||strlen($s['period'])>80||!is_bool($s['frozen']??null)||!is_int($s['pending']??null)||$s['pending']<0||!is_array($s['accounts']??null)||count($s['accounts'])>100000)throw new \InvalidArgumentException('Unvollständiger Bankbestand.');
            $at=strtotime($s['capturedAt']??'');if(!$at||$at>time()+60)throw new \InvalidArgumentException('Ungültiger Bestandszeitpunkt.');
            if(isset($databases[$s['databaseIdentity']]))$blocked[]='Gemeinsame Quelldatenbank bei '.$databases[$s['databaseIdentity']].' und '.$id.': nicht doppelt übernehmen.';
            $databases[$s['databaseIdentity']]=$id;
            if(!$s['frozen']||$s['pending']>0)$blocked[]=$id.': laufende Bank oder offene Vorgänge; kein freigegebener Abschlussbestand.';
            $local=[];
            foreach($s['accounts'] as $a){
                if(!is_array($a)||!is_string($a['steamId']??null)||!self::steam($a['steamId'])||isset($local[$a['steamId']])||!is_int($a['balance']??null)||$a['balance']<0||$a['balance']>9000000000000000)throw new \InvalidArgumentException('Ungültiges oder doppeltes Konto.');
                $steam=$a['steamId'];$local[$steam]=true;$accounts[$steam][$id]=$a['balance'];
            }
            $sources[]=['server'=>$id,'sha256'=>$s['sha256'],'databaseIdentity'=>$s['databaseIdentity'],'period'=>$s['period'],'capturedAt'=>$s['capturedAt'],'count'=>count($local)];
        }
        $missing=array_values(array_diff(self::PRODUCTION,array_keys($seen)));foreach($missing as $id)$blocked[]=$id.': vollständiger Abschlussbestand fehlt.';
        $players=[];ksort($accounts,SORT_STRING);
        foreach($accounts as $steam=>$balances){$sum=array_sum($balances);$players[]=['steamId'=>(string)$steam,'sources'=>$balances,'multipleServers'=>count($balances)>1,'candidateMaximum'=>max($balances),'candidateSum'=>$sum,'sumWithinLimit'=>$sum<=9000000000000000,'selectedBalance'=>null];}
        return ['schema'=>1,'group'=>'lplk-production','state'=>'review_required','activatable'=>false,'generatedAt'=>gmdate('c'),'sources'=>$sources,'missingServers'=>$missing,'blockers'=>$blocked,'players'=>$players,'mergeRule'=>null,
            'requiredDecisions'=>['Guthabenregel und Überschneidungen bestätigen','Einheitliche Limits, Resetperiode, Zinsen und Steuern festlegen','Lotto, Raub, Titel und offene Ansprüche je Quelle abgleichen','Zentrale Buchungsstelle und atomare Cash-Reservierungen im Test prüfen','Abschlussbestände einfrieren, Sicherungen und Importbelege verifizieren','Konkrete Produktionsumschaltung freigeben']];
    }
}
