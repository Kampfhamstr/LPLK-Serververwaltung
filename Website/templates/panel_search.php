<?php use App\Core\Security; ?>
<section class="panel ux-search-page">
<h2>Spieler und Server finden</h2>
<p>Name oder SteamID eingeben. Es werden ausschließlich für dich freigegebene Ergebnisse angezeigt.</p>
<form method="get" action="/search" class="ux-search-form" role="search">
<label for="search-page-query">Suchbegriff</label>
<input id="search-page-query" type="search" name="q" maxlength="160" value="<?=Security::e($query)?>" placeholder="Spielername, SteamID oder Server" autofocus>
<button class="btn btn-primary">Suchen</button></form>
<?php if($searchError):?><p role="alert"><?=Security::e($searchError)?></p>
<?php elseif($query===''):?><p class="muted">Tipp: Ein Teil des Namens genügt.</p>
<?php elseif(!$results):?><p role="status">Keine passenden Ergebnisse. Prüfe die Schreibweise oder verwende die SteamID.</p>
<?php else:?><p role="status"><?=count($results)?> Ergebnisse · höchstens 15 je Bereich</p><ul class="ux-search-results">
<?php foreach($results as $item):?><li><a href="<?=Security::e($item['url'])?>"><span class="ux-result-type"><?=Security::e($item['kind'])?></span><strong><?=Security::e($item['label'])?></strong><span><?=Security::e($item['detail'])?></span></a></li><?php endforeach;?>
</ul><?php endif;?>
</section>
