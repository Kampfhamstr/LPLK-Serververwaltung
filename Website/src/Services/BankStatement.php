<?php
namespace App\Services;

/** Read-only reconciliation of the bounded ledger export, in ledger order. */
final class BankStatement
{
    public static function from(array $account):array
    {
        $rows=$account['history'];$incoming=0;$outgoing=0;$opening=null;
        $balance=$account['balance'];$seen=[];
        foreach($rows as $row){
            if(isset($seen[$row['Id']])||$row['Balance']!==$balance)throw new \RuntimeException('Buchungsfolge passt nicht zum Kontostand.');
            $seen[$row['Id']]=true;
            $balance-=$row['Delta'];
            if($balance<0||$balance>9000000000000000)throw new \RuntimeException('Ungültiger Vortrag.');
            if($row['Delta']>0)$incoming+=$row['Delta'];else $outgoing-=$row['Delta'];
            $opening=$balance;
        }
        return ['rows'=>$rows,'count'=>count($rows),'opening'=>$opening,'incoming'=>$incoming,'outgoing'=>$outgoing,
            'net'=>$incoming-$outgoing,'closing'=>$account['balance'],'oldest'=>$rows?end($rows)['At']:null];
    }

    public static function money(int $value,bool $signed=false):string
    {
        // Avoid float conversion: turnover totals may exceed JavaScript/float integer precision.
        $digits=ltrim((string)$value,'-');
        return ($value<0?'−':($signed&&$value>0?'+':'')) . preg_replace('/\B(?=(\d{3})+(?!\d))/','.',$digits).' $';
    }

    public static function date(string $value):string
    {
        return (new \DateTimeImmutable($value))->setTimezone(new \DateTimeZone('Europe/Berlin'))->format('d.m.Y · H:i:s');
    }
}
