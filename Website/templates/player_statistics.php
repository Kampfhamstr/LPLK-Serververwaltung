<?php use App\Core\Security;
$date=static fn(string $value)=>(new DateTimeImmutable($value))->setTimezone(new DateTimeZone('Europe/Berlin'))->format('d.m.Y H:i');
$num=static fn($v,$dec=0)=>$v===null?'—':number_format((float)$v,$dec,',','.');
$row=static function(string $label,$value)use($num){echo '<tr><th scope="row">'.Security::e($label).'</th><td>'.Security::e(is_string($value)?$value:$num($value)).'</td></tr>';};
?>
<section class="panel"><div class="panel-header"><h2>Spielerstatistik · <?=Security::e($player['display_name'])?></h2><p>Jeder Server führt eigene Werte. Bottraining, Testdaten und Zweitaccounts werden getrennt geführt.</p></div>
<div class="toolbar"><a class="btn btn-secondary" href="/players/<?=(int)$player['id']?>">Zurück zum Spieler</a> <a class="btn btn-secondary" href="/players/<?=(int)$player['id']?>/economy?server=<?=$selected?>">Bank &amp; Wirtschaft</a> <a class="btn btn-secondary" href="/servers/<?=$selected?>/ranking">Serverranglisten</a></div>
<form method="get" class="form-stack"><label>Server<select name="server"><?php foreach($servers as $s):?><option value="<?=(int)$s['id']?>" <?=(int)$s['id']===$selected?'selected':''?>><?=Security::e($s['name'])?></option><?php endforeach;?></select></label><button class="btn btn-primary">Statistik anzeigen</button></form>
</section>
<?php if(($economy['state']??'')==='ready'):?><section class="panel"><h3>Bank auf diesem Server</h3><p><strong><?=Security::e($num($economy['data']['balance']))?> $</strong> · Periode <?=(int)$economy['data']['period']?> · <?=Security::e($economy['data']['generatedAt'])?></p><a class="btn btn-secondary" href="/players/<?=(int)$player['id']?>/economy?server=<?=$selected?>">Konto, Fortschritt &amp; Verwaltung öffnen</a></section><?php endif;?>
<?php if($statistics['state']!=='ready'):?>
<section class="panel"><p><?=Security::e($statistics['message'])?></p><p class="muted">Fehlende Daten bedeuten nicht null Kills. Die Erfassung beginnt mit dem Einsatz des Addons auf diesem Server.</p></section>
<?php else:$d=$statistics['data'];$p=$d['pvp'];$b=$d['bots'];$x=$d['extra'];?>
<section class="panel"><div class="panel-header"><h2>Übersicht</h2><p>Standardprofil · Export <?=Security::e($date($d['generatedAt']))?> · zuletzt erfasst <?=Security::e($date($d['lastSeen']))?></p></div>
<div class="table-wrap"><table><tbody><?php $row('Kills',$p['Kills']);$row('Tode',$p['Deaths']);$row('K/D',$num($p['kd'],2));$row('Headshots',$p['Headshots']);$row('Headshotquote',$p['headshotPercent']===null?'—':$num($p['headshotPercent'],1).' %');$row('Schüsse',$p['Shots']);$row('Gesamtwertung',$p['rating']===null?'Noch nicht qualifiziert':$num($p['rating'],1).' / 100');?></tbody></table></div>
<?php if(!$d['collection']['PvpEnabled']):?><p class="muted">PvP-Erfassung derzeit ausgeschaltet; gespeicherter Stand.</p><?php endif;?></section>
<section class="panel"><details open><summary><strong>Besondere Kills & aufgehobene Waffen</strong></summary>
<?php if($x['Since']===null):?><p>Diese Werte wurden bisher noch nicht erfasst. Eine rückwirkende Rekonstruktion aus den alten Killzahlen ist nicht möglich.</p><?php else:?>
<p class="muted">Erfasst seit <?=Security::e($date($x['Since']))?>. Gegen menschliche Gegner außerhalb des Warmups. Ein Kill kann mehrere Merkmale erfüllen.</p>
<div class="table-wrap"><table><tbody><?php foreach(['NoScope'=>'No-Scope','Wallbang'=>'Durch eine Wand','ThroughSmoke'=>'Durch Rauch','Blind'=>'Angreifer geblendet','InAir'=>'Angreifer in der Luft','EnemyWeapon'=>'Mit übernommener Gegnerwaffe','TeamWeapon'=>'Mit übernommener Teamwaffe','BestPickupStreak'=>'Beste Killserie nach Waffenübernahme','UnknownWeaponOrigin'=>'Waffenkills mit unbekannter Herkunft'] as $k=>$label)$row($label,$x[$k]);?></tbody></table></div>
<p>Übernahmen zählen nur bei beobachtetem Besitzerwechsel derselben Waffeninstanz. Unbekannte Herkunft wird nicht als aufgehobene Gegnerwaffe gewertet.</p><?php endif;?></details></section>
<section class="panel"><details><summary><strong>Teamleistung, Runden & Spielzeit</strong></summary>
<?php if($x['Since']===null):?><p>Noch keine erweiterten Werte erfasst.</p><?php else:?><div class="table-wrap"><table><tbody><?php
foreach(['Assists'=>'Assists','FlashAssists'=>'Davon Flash-Assists','Damage'=>'Gesundheitsschaden an menschlichen Gegnern','DamageTaken'=>'Von menschlichen Gegnern erhaltener Schaden','Rounds'=>'Vollständig gespielte Runden','Wins'=>'Davon gewonnen','BombsPlanted'=>'Bomben gelegt','BombsDefused'=>'Bomben entschärft'] as $k=>$label)$row($label,$x[$k]);$row('Siegquote vollständiger Runden',$x['Rounds']>0?$num(100*$x['Wins']/$x['Rounds'],1).' %':'—');$row('Spielzeit in T/CT',$num($x['PlaySeconds']/3600,1).' h');?></tbody></table></div>
<p class="muted">Runden zählen bei Teilnahme von Anfang bis Ende im selben Team. Spielzeit umfasst auch Bottraining, ohne Warmup. Schaden enthält keinen Rüstungsschaden.</p><?php endif;?></details></section>
<section class="panel"><details><summary><strong>Waffen & Granaten</strong></summary>
<?php if(!$d['weapons']):?><p>Noch keine Waffenkills erfasst.</p><?php else:?><div class="table-wrap"><table><thead><tr><th>Waffe</th><th>Kills</th></tr></thead><tbody><?php foreach($d['weapons'] as $weapon=>$kills)$row($weapon,$kills);?></tbody></table></div><?php endif;?>
<?php if(!$d['collection']['WeaponTracking']):?><p class="muted">Waffenerfassung derzeit ausgeschaltet.</p><?php endif;?></details></section>
<section class="panel"><details><summary><strong>Bottraining</strong></summary><p>Eigene Wertung, getrennt von PvP. Je Schuss höchstens ein Treffer und ein Kopftreffer.</p><div class="table-wrap"><table><tbody><?php $row('Schüsse',$b['Shots']);$row('Treffer',$b['Hits']);$row('Kopftreffer',$b['HeadHits']);$row('Kills',$b['Kills']);$row('Headshotkills',$b['Headshots']);$row('Präzision',$b['Shots']>0?$num(100*$b['Hits']/$b['Shots'],1).' %':'—');?></tbody></table></div>
<?php if(!$d['collection']['BotTrainingEnabled']):?><p class="muted">Bottraining-Erfassung derzeit ausgeschaltet.</p><?php endif;?></details></section>
<?php endif;?>
