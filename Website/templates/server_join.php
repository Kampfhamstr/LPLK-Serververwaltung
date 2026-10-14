<?php use App\Core\Security; ?>
<section class="panel"><div class="panel-header"><h2><?=Security::e($server['name'])?></h2></div>
<?php if(isset($join['url'])): ?>
<p><a class="btn btn-success" href="<?=Security::e($join['url'])?>">Mit Steam beitreten</a></p>
<p>Spieladresse: <code><?=Security::e($join['address'])?></code></p>
<p>Steam muss installiert sein. Erlaube dem Browser, Steam zu öffnen. Ein benötigtes Serverpasswort wird im Spiel abgefragt.</p>
<p>Alternativ in der CS2-Konsole: <code>connect <?=Security::e($join['address'])?></code></p>
<?php else: ?><p role="status"><?=Security::e($join['error'])?></p><a class="btn btn-secondary" href="/servers/<?=(int)$server['id']?>/join">Erneut versuchen</a><?php endif; ?>
</section>
