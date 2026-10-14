<?php use App\Core\Security;use App\Core\Auth;use App\Services\PlayerShowcaseService;
$identity=PlayerShowcaseService::identity($player);
$showcaseServers=$showcaseServers??[];
$showcaseSelected=0;foreach($showcaseServers as $option)if(($option['server_identifier']??'')==='11110004'){$showcaseSelected=(int)$option['id'];break;}
if(!$showcaseSelected&&$showcaseServers)$showcaseSelected=(int)$showcaseServers[0]['id'];
?>
<section class="panel player-showcase" aria-label="Spielerprofil und persönliche Highlights">
  <div class="showcase-hero">
    <div class="showcase-avatar"><span aria-hidden="true"><?=Security::e($identity['initial'])?></span><?php if($identity['avatarUrl']):?><img src="<?=Security::e($identity['avatarUrl'])?>" alt="Steam-Avatar von <?=Security::e($identity['name'])?>" width="120" height="120" data-showcase-avatar><?php endif;?></div>
    <div class="showcase-identity" data-player-status-host data-player-steamid="<?=Security::e($identity['steam'])?>">
      <span class="showcase-eyebrow">LPLK · Spielerprofil</span><h2><?=Security::e($identity['name'])?></h2>
      <div class="showcase-badges"><span class="game-role game-role-<?=Security::e($player['community_role'])?>"><?=Security::e($roles[$player['community_role']]??$player['community_role'])?></span><span class="showcase-tag"><?=$isSecondary?'Zweitaccount':'Hauptaccount'?></span><span class="showcase-tag"><?=Security::e($identity['visibility'])?></span></div>
      <div class="showcase-steam"><span>SteamID64</span><code><?=Security::e($identity['steam']?:'Nicht hinterlegt')?></code></div>
      <?php if($isSecondary):?><p class="showcase-account-link">Hauptaccount: <a href="/players/<?=(int)$player['main_player_id']?>"><?=Security::e($player['main_steam_personaname']?:$player['main_display_name'])?></a></p><?php elseif($secondaryAccounts):?><p class="showcase-account-link"><?=count($secondaryAccounts)?> verknüpfte Zweitaccounts · Statistiken beziehen sich auf diese SteamID.</p><?php endif;?>
    </div>
    <div class="showcase-actions">
      <?php if($identity['profileUrl']):?><a class="btn btn-primary" href="<?=Security::e($identity['profileUrl'])?>" target="_blank" rel="noopener noreferrer">Steam-Profil öffnen ↗</a><?php endif;?>
      <?php if($identity['steam']!==''&&Auth::canAny(['live.kick','moderation.ban.temporary','moderation.ban.permanent','moderation.unban','live.mute','live.gag','live.message','live.whitelist'])):?><button type="button" class="btn btn-secondary player-action-button" data-player-actions data-player-steamid="<?=Security::e($identity['steam'])?>" data-player-name="<?=Security::e($identity['name'])?>">Spieleraktionen</button><?php endif;?>
      <?php if($canSteamSync):?><form method="post" action="/players/<?=(int)$player['id']?>/steam-sync"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-secondary" <?=$steamConfigured?'':'disabled'?>>Steam-Profil aktualisieren</button></form><?php endif;?>
      <small><?=$identity['syncedAt']?'Profildaten: '.Security::e($identity['syncedAt']).' · Berlin':'Steam-Profildaten noch nicht synchronisiert.'?></small>
    </div>
  </div>
  <?php if(!empty($player['steam_sync_error'])):?><p class="showcase-sync-note">Steam-Aktualisierung derzeit nicht verfügbar. Bereits bekannte Profildaten werden weiterhin angezeigt.</p><?php endif;?>
  <div class="showcase-highlights" data-player-showcase data-player-id="<?=(int)$player['id']?>">
    <div class="showcase-section-head"><div><span class="showcase-eyebrow">Deine Leistungen</span><h3>Server-Highlights</h3></div><div class="showcase-server-controls"><label>Statistiken auf<select data-showcase-server <?=$showcaseServers?'':'disabled'?>><?php if(!$showcaseServers):?><option>Kein freigegebener Server</option><?php endif;?><?php foreach($showcaseServers as $option):?><option value="<?=(int)$option['id']?>" <?=(int)$option['id']===$showcaseSelected?'selected':''?>><?=Security::e($option['name'])?></option><?php endforeach;?></select></label><button type="button" class="btn btn-secondary" data-showcase-reload <?=$showcaseServers?'':'disabled'?>>Neu laden</button></div></div>
    <p class="showcase-message" data-showcase-message role="status"><?=$showcaseServers?'Statistiken werden geladen …':'Keine Serverstatistiken für dieses Profil freigegeben.'?></p>
    <div class="showcase-cards" data-showcase-cards></div><div class="showcase-facts" data-showcase-facts></div>
    <p class="showcase-source" data-showcase-source></p><p class="showcase-source" data-showcase-rules></p>
    <div class="showcase-links"><a class="btn btn-secondary btn-small" href="/players/<?=(int)$player['id']?>/statistics<?=$showcaseSelected?'?server='.$showcaseSelected:''?>" data-showcase-statistics>Alle Statistiken</a><a class="btn btn-secondary btn-small" href="/players/<?=(int)$player['id']?>/economy<?=$showcaseSelected?'?server='.$showcaseSelected:''?>" data-showcase-economy>Bank &amp; Wirtschaft</a><?php if($showcaseSelected):?><a class="btn btn-secondary btn-small" href="/servers/<?=$showcaseSelected?>/ranking" data-showcase-ranking>Serverranglisten</a><?php endif;?></div>
    <noscript><p>Die Highlights benötigen JavaScript. Über „Alle Statistiken“ kannst du die Werte auch ohne JavaScript ansehen.</p></noscript>
  </div>
</section>
