<?php
use App\Core\Security;
use App\Services\BankStatement;
$statement=BankStatement::from($d);
$serverName='';foreach($servers as $serverOption)if((int)$serverOption['id']===$selected)$serverName=$serverOption['name'];
?>
<section class="panel bank-statement" aria-labelledby="statement-title">
  <header class="bank-statement-head">
    <div><span class="bank-eyebrow">LPLK · Spielerkonto</span><h3 id="statement-title">Kontoauszug</h3><p><?=Security::e($serverName)?> <span class="bank-scope">Separates Testkonto</span></p></div>
    <button class="btn btn-secondary bank-no-print" type="button" data-bank-print>Drucken / PDF</button>
  </header>
  <div class="bank-account-grid">
    <div class="bank-owner"><span class="bank-label">Kontoinhaber</span><strong><?=Security::e($player['display_name'])?></strong><span class="bank-label">Kontokennung · SteamID64</span><span class="mono"><?=Security::e($d['steamId'])?></span><span class="bank-label">Aktuelle Bankperiode</span><span><?=$d['period']?></span></div>
    <div class="bank-balance"><span class="bank-label">Gebuchter Kontostand</span><strong><?=Security::e($money($d['balance']))?></strong><span>Spielwährung · Dollar</span><span class="bank-updated">Stand <?=Security::e(BankStatement::date($d['generatedAt']))?> (Berlin)</span></div>
  </div>
  <div class="bank-totals" aria-label="Bilanz des angezeigten Buchungsausschnitts">
    <div><span class="bank-label">Vortrag vor dem Ausschnitt</span><strong><?=$statement['opening']===null?'Nicht ermittelbar':Security::e($money($statement['opening']))?></strong></div>
    <div><span class="bank-label">Gutschriften im Ausschnitt</span><strong class="bank-credit"><?=Security::e(BankStatement::money($statement['incoming'],true))?></strong></div>
    <div><span class="bank-label">Belastungen im Ausschnitt</span><strong class="bank-debit"><?=Security::e($money(-$statement['outgoing']))?></strong></div>
    <div><span class="bank-label">Veränderung im Ausschnitt</span><strong><?=Security::e(BankStatement::money($statement['net'],true))?></strong></div>
  </div>
  <div class="bank-statement-notes"><p><strong>Buchungsausschnitt · <?=$statement['count']?> von höchstens 20 exportierten Buchungen.</strong> Neueste Buchung zuerst. Der Ausschnitt kann frühere Bankperioden enthalten und ist kein vollständiger Monats- oder Jahresauszug. Alle Uhrzeiten: Europe/Berlin.</p>
  <?php if($statement['count']):?><p>Vortrag + Gutschriften − Belastungen = gebuchter Kontostand. Seit <?=Security::e(BankStatement::date($statement['oldest']))?>.</p><?php endif;?>
  <?php if($d['betClaims']>0):?><p>Offene Wettauszahlungen: <strong><?=Security::e($money($d['betClaims']))?></strong>. Noch nicht gutgeschrieben und nicht in dieser Bilanz enthalten.</p><?php endif;?>
  <?php if($d['pendingCash']):?><p class="bank-pending"><strong>Cash-Buchung in Prüfung.</strong> Webänderungen sind bis zur Klärung gesperrt.</p><?php endif;?></div>
  <?php if(!$statement['count']):?><div class="bank-empty"><strong>Noch keine Buchungen im Export</strong><p>Der gebuchte Kontostand ist verfügbar. Ohne Buchungen lassen sich kein Vortrag und kein Buchungszeitraum bestimmen.</p></div>
  <?php else:?><p class="bank-scroll-hint">Weitere Spalten: Tabelle seitlich scrollen →</p><div class="table-wrap" tabindex="0" role="region" aria-label="Kontobewegungen, auf kleinen Bildschirmen seitlich scrollbar"><table class="bank-bookings">
    <caption>Kontobewegungen · <?=$statement['count']?> Buchungen · Beträge in Spiel-Dollar</caption>
    <thead><tr><th scope="col">Buchungszeit</th><th scope="col">Buchungstext / Beleg</th><th scope="col" class="bank-number">Gutschrift</th><th scope="col" class="bank-number">Belastung</th><th scope="col" class="bank-number">Saldo danach</th></tr></thead>
    <tbody><?php foreach($statement['rows'] as $h):?><tr><td class="bank-time"><?=Security::e(BankStatement::date($h['At']))?></td><td><strong><?=Security::e($h['Reason'])?></strong><small class="bank-reference">Beleg <?=Security::e($h['Id'])?></small><?php if($h['Delta']===0):?><small class="bank-reference">Ohne Guthabenänderung</small><?php endif;?></td><td class="bank-number bank-credit"><?=$h['Delta']>0?Security::e(BankStatement::money($h['Delta'],true)):'—'?></td><td class="bank-number bank-debit"><?=$h['Delta']<0?Security::e($money($h['Delta'])):'—'?></td><td class="bank-number"><strong><?=Security::e($money($h['Balance']))?></strong></td></tr><?php endforeach;?></tbody>
    <tfoot><tr><th colspan="4" scope="row">Gebuchter Kontostand · Stand des Exports</th><td class="bank-number"><strong><?=Security::e($money($statement['closing']))?></strong></td></tr></tfoot>
  </table></div><?php endif;?>
</section>
