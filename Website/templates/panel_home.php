<?php
use App\Core\Auth;
use App\Core\Security;
$isOperator=Auth::canAny(['servers.manage','plugins.install','settings.view']);
$isModerator=Auth::can('moderation.view');
$homeLinks=[['/profile','Mein Profil','Steam-Verknüpfung und persönliche Einstellungen']];
if(Auth::can('players.view')) $homeLinks[]=['/players','Spieler finden','Profile, Statistiken und freigegebene Konten'];
if(Auth::can('servers.view')) $homeLinks[]=['/servers','Meine Server','Server auswählen und beitreten'];
if($isModerator) $homeLinks[]=['/moderation','Moderationsfälle','Verwarnungen, Sperren und interne Dokumentation'];
if($isOperator && Auth::can('plugins.view')) $homeLinks[]=['/plugins','Addons & Updates','Installationen und verfügbare Updates'];
?>
<section class="panel ux-home" data-home-mode="<?=$isOperator?'administration':($isModerator?'moderation':'player')?>">
<div class="panel-header"><h2><?=$isOperator?'Deine Server im Blick':($isModerator?'Dein Moderationsbereich':'Willkommen in der Community')?></h2><p>Deine wichtigsten Wege. Angezeigt werden nur Bereiche, für die du berechtigt bist.</p></div>
<div class="ux-shortcuts"><?php foreach($homeLinks as $link):?><a href="<?=Security::e($link[0])?>"><strong><?=Security::e($link[1])?></strong><span><?=Security::e($link[2])?></span></a><?php endforeach;?></div>
<?php if($isOperator || $isModerator):?><div class="ux-home-notes">
<?php $errorCount=count(array_filter($servers??[],static fn($s)=>!empty($s['last_error'])));if($errorCount):?><a href="#server-status"><?=$errorCount?> Server mit Prüfbedarf</a><?php endif;?>
<?php if(Auth::can('users.invite') && !empty($stats['pending'])):?><a href="/invitations"><?=(int)$stats['pending']?> offene Einladungen</a><?php endif;?>
<a href="#announcements">Mitteilungen ansehen</a></div><?php endif;?>
<?php if(Auth::can('servers.view') && !empty($servers)):?>
<details class="ux-personal-servers" open><summary>Favoriten und zuletzt besuchte Server</summary><p class="small-muted">Favoriten mit dem Stern markieren. Auswahl wird für dein Konto in diesem Browser gespeichert.</p>
<div class="ux-server-list" data-ux-server-list><?php foreach($servers as $homeServer):?><div data-ux-server="<?=(int)$homeServer['id']?>"><a href="/servers/<?=(int)$homeServer['id']?>"><?=Security::e($homeServer['name'])?></a><span data-ux-recent hidden>Zuletzt besucht</span><button type="button" class="btn btn-secondary btn-small" data-ux-favorite="<?=(int)$homeServer['id']?>" aria-pressed="false" aria-label="<?=Security::e($homeServer['name'])?> als Favorit merken">☆</button></div><?php endforeach;?></div>
</details><?php endif;?>
</section>
