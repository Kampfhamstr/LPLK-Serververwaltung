<?php
use App\Core\Security;
use App\Services\BankStatement;
$money=static fn(int $v)=>BankStatement::money($v);
$bankView=($bankView??'overview')==='statement'?'statement':'overview';
$viewUrl='/players/'.(int)$player['id'].'/economy?server='.$selected;
if($pending)$viewUrl.='&request='.rawurlencode(substr($pending['request']['Id'],4));
?>
<div class="bank-page">
<section class="panel bank-navigation"><h2>Bank & Wirtschaft · <?=Security::e($player['display_name'])?></h2>
<div class="toolbar"><a class="btn btn-secondary" href="/players/<?=(int)$player['id']?>">Spieler</a><a class="btn btn-secondary" href="/players/<?=(int)$player['id']?>/statistics?server=<?=$selected?>">Statistiken</a><a class="btn btn-secondary" href="/servers/<?=$selected?>/ranking">Serverranglisten</a></div>
<form method="get" class="form-stack"><input type="hidden" name="view" value="<?=$bankView?>"><label>Server<select name="server"><?php foreach($servers as $s):?><option value="<?=(int)$s['id']?>" <?=(int)$s['id']===$selected?'selected':''?>><?=Security::e($s['name'])?></option><?php endforeach;?></select></label><button class="btn btn-primary">Konto anzeigen</button></form>
<p>Guthaben und Buchungen gehören zum ausgewählten Server. Testguthaben bleiben getrennt; die gemeinsame Produktivbank ist noch nicht aktiviert.</p>
<nav class="bank-view-switch" aria-label="Kontoansicht"><a class="btn btn-secondary" href="<?=Security::e($viewUrl.'&view=overview')?>" <?=$bankView==='overview'?'aria-current="page"':''?>>Übersicht &amp; Fortschritt</a><a class="btn btn-secondary" href="<?=Security::e($viewUrl.'&view=statement')?>" <?=$bankView==='statement'?'aria-current="page"':''?>>Kontoauszug &amp; Bilanz</a></nav></section>
<?php if($result):?><section class="panel"><h3>Auftrag <?=Security::e($pending['request']['Id'])?></h3><p><?=Security::e($result['message'])?></p>
<?php if($result['state']==='applied'):?><p>Bestätigt: <?=Security::e($money($result['receipt']['Before']))?> → <?=Security::e($money($result['receipt']['After']))?> · <?=Security::e($result['receipt']['AppliedAt'])?></p><?php endif;?>
<a class="btn btn-secondary" href="<?=Security::e($viewUrl.'&view='.$bankView)?>">Buchungsbeleg prüfen</a></section><?php endif;?>
<?php if($economy['state']!=='ready'):?><section class="panel"><p><?=Security::e($economy['message'])?></p></section><?php else:$d=$economy['data'];?>
<?php if($bankView==='statement'):require __DIR__.'/player_bank_statement.php';else:?>
<section class="panel bank-secondary"><h3>Kontostand</h3><p><strong><?=Security::e($money($d['balance']))?></strong> · Periode <?=$d['period']?> · Stand <?=Security::e($d['generatedAt'])?></p>
<p>Offene Wettauszahlungen: <?=Security::e($money($d['betClaims']))?>. Diese Ansprüche sind noch nicht im Kontostand enthalten.</p>
<details><summary>Lotterie, Ausbau &amp; Titel</summary><dl><dt>Gebuchte Dauerlose</dt><dd><?=$d['permanentTickets']?></dd><dt>Gekaufte Lose dieser Bankperiode</dt><dd><?=$d['purchasedTickets']?></dd><dt>Tresorstufe</dt><dd><?=$d['vaultLevel']?></dd><dt>Räuberstufe / Spionstufe</dt><dd><?=$d['robberLevel']?> / <?=$d['spyLevel']?></dd><dt>Aktiver Titel (Kennung)</dt><dd><?=Security::e($d['activeTitle']?:'Keiner gewählt')?></dd></dl><p>Gespeicherter Fortschritt. Einzelne Funktionen können im Addon ausgeschaltet sein.</p></details>
<?php if($d['pendingCash']):?><p>Cash-Buchung offen: Konto bis zur Prüfung gesperrt.</p><?php endif;?>
<details><summary>Letzte Buchungen</summary><div class="table-wrap"><table><thead><tr><th>Zeit</th><th>Vorgang</th><th>Änderung</th><th>Danach</th></tr></thead><tbody><?php foreach($d['history'] as $h):?><tr><td><?=Security::e($h['At'])?></td><td><?=Security::e($h['Reason'])?></td><td><?=Security::e($money($h['Delta']))?></td><td><?=Security::e($money($h['Balance']))?></td></tr><?php endforeach;?></tbody></table></div></details></section>
<?php endif;?>
<?php if($canManage&&$economy['changesEnabled']&&!$d['pendingCash']&&!$pending):?><section class="panel bank-secondary"><h3>Bankguthaben ändern</h3><form method="post" action="/players/<?=(int)$player['id']?>/economy/prepare" class="form-stack">
<input type="hidden" name="view" value="<?=$bankView?>">
<input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="server" value="<?=$selected?>">
<label>Änderung<select name="operation"><option value="add">Gutschreiben</option><option value="remove">Abziehen</option><option value="set">Guthaben setzen</option></select></label>
<label>Betrag in ganzen Dollar<input name="amount" type="number" min="0" max="9000000000000000" step="1" required></label><label>Begründung<input name="reason" minlength="5" maxlength="120" required></label><button class="btn btn-primary">Änderung prüfen</button></form></section>
<?php elseif(!$pending):?><section class="panel"><p>Änderungen benötigen das Recht „Bankguthaben verwalten“, passende Spieler-/Serverrechte und die aktivierte Webbank-Verwaltung im Addon.</p></section><?php endif;?>
<?php endif;?>
<?php if($pending&&(!$result||in_array($result['state'],['pending','unknown'],true))):?><section class="panel"><h3>Bankänderung bestätigen</h3>
<p><?=Security::e($money($pending['before']))?> → <strong><?=Security::e($money($pending['after']))?></strong></p><p>Begründung: <?=Security::e($pending['request']['Reason'])?></p><p>Auftragsnummer: <?=Security::e($pending['request']['Id'])?>. Veraltete Kontostände werden abgewiesen.</p>
<form method="post" action="/players/<?=(int)$player['id']?>/economy/confirm"><input type="hidden" name="view" value="<?=$bankView?>"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="request" value="<?=Security::e(substr($pending['request']['Id'],4))?>"><button class="btn btn-primary"><?=$result?'Denselben Auftrag erneut senden':'Bankänderung verbindlich bestätigen'?></button></form></section><?php endif;?>
</div>
