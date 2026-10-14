<?php
use App\Core\Security;
use App\Services\WeaponRankingService;
$selectedWeapon=WeaponRankingService::selected($selectedWeapon??null);
$weaponLabels=WeaponRankingService::labels();
$weaponRanking=$weaponRanking??['state'=>'unavailable','message'=>'Keine aktuelle Waffenrangliste verfügbar.'];
?>
<section class="panel" id="waffen"><details class="panel-header" <?=isset($_GET['weapon'])?'open':''?>>
<summary><strong>Waffen &amp; Granaten · Ranglisten</strong></summary>
<p>Wähle eine Waffe aus einer Gruppe. Messer und Zeus haben eigene Ranglisten; alle Messervarianten zählen gemeinsam.</p>
<form method="get" action="/servers/<?=(int)$server['id']?>/ranking#waffen" class="form-stack">
<label>Waffe<select name="weapon"><?php foreach(WeaponRankingService::GROUPS as $groupLabel=>$weapons):?><optgroup label="<?=Security::e($groupLabel)?>"><?php foreach($weapons as $key=>$label):?><option value="<?=Security::e($key)?>" <?=$key===$selectedWeapon?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></optgroup><?php endforeach;?></select></label>
<button class="btn btn-primary">Waffenrangliste anzeigen</button></form>
<h3><?=Security::e($weaponLabels[$selectedWeapon])?> · Kills</h3>
<?php if($weaponRanking['state']!=='ready'):?><p role="status"><?=Security::e($weaponRanking['message'])?></p>
<?php else:$weaponData=$weaponRanking['data'];$weaponRules=$weaponData['collection'];$weaponRows=$weaponData['boards'][$selectedWeapon];?>
<p class="muted">Stand <?=Security::e((new DateTimeImmutable($weaponData['generatedAt']))->setTimezone(new DateTimeZone('Europe/Berlin'))->format('d.m.Y H:i'))?> · menschliche Angreifer und Opfer, keine Selbstkills. Warmup <?=$weaponRules['CountWeaponWarmup']?'enthalten':'ausgeschlossen'?>; Teamkills <?=$weaponRules['CountTeamKills']?'enthalten':'ausgeschlossen'?>.</p>
<?php if(!$weaponRows):?><p role="status">Für diese Waffe wurden noch keine Kills erfasst.</p>
<?php else:?><div class="table-wrap"><table><thead><tr><th>Platz</th><th>Spieler</th><th>Kills</th></tr></thead><tbody>
<?php foreach($weaponRows as $row):?><tr><td><?=(int)$row['rank']?></td><td><?=Security::e($row['name'])?> <small><?=Security::e($row['steamId'])?></small></td><td><?=number_format($row['value'],0,',','.')?></td></tr><?php endforeach;?>
</tbody></table></div><p class="muted">Bis zu 100 Einträge für diese Waffe. Gleiche Killzahlen erhalten denselben Rang. Ein Profil-Highlight setzt mindestens fünf gewertete Spieler und einen Platz unter den ersten drei voraus.</p><?php endif;endif;?>
</details></section>
