<?php
use App\Core\Auth; use App\Core\Security; use App\Core\View; use App\Core\Config; use App\Services\GlobalBanService; use App\Services\ModerationService; use App\Services\PetService;
$me=Auth::user(); $flashes=View::consumeFlashes();
$sidebarPet=$me && (bool)Config::get('pets.enabled',true) && Auth::can('pets.use') ? PetService::sidebarPet((int)$me['id']) : null;
$themePreference=strtolower(trim((string)($me['ui_theme']??'system')));
if(!in_array($themePreference,['light','dark','system'],true)) $themePreference='system';
$currentPath=(string)(parse_url($_SERVER['REQUEST_URI']??'/',PHP_URL_PATH)?:'/');
$isPetPage=$currentPath==='/pets'||str_starts_with($currentPath,'/pets/');
$isPetPlayground=$currentPath==='/pets/playground';
$navClass=static function(string $prefix,string $accent) use($currentPath):string { $active=$currentPath===$prefix||str_starts_with($currentPath,rtrim($prefix,'/').'/'); return 'nav-item nav-accent-'.$accent.($active?' is-active':''); };
?><!doctype html>
<html lang="de" data-theme-preference="<?=Security::e($themePreference)?>" data-authenticated="<?=$me?'1':'0'?>">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex,nofollow">
<?php if($me):?><meta name="csrf-token" content="<?=Security::e(Security::csrfToken())?>"><?php endif;?>
<title><?=Security::e($title ?? Config::get('app_name','Serververwaltung'))?> · <?=Security::e(Config::get('app_name','Serververwaltung'))?></title>
<meta name="color-scheme" content="light dark">
<script src="/assets/theme-init.js?v=1.15.2.8"></script>
<link rel="stylesheet" href="/assets/app.css?v=1.15.2.8">
<?php if(preg_match('~^/players/[0-9]+$~',$currentPath)):?><link rel="stylesheet" href="/assets/player-showcase.css?v=1.15.2.8"><script src="/assets/player-showcase.js?v=1.15.2.8" defer></script><?php endif;?>
<?php if(preg_match('~^/players/[0-9]+/economy(?:/|$)~',$currentPath)):?><link rel="stylesheet" href="/assets/bank-statement.css?v=1.15.2.8"><script src="/assets/bank-statement.js?v=1.15.2.8" defer></script><?php endif;?>
<link rel="stylesheet" href="/assets/pet-companion.css?v=1.15.2.8">
<?php if($isPetPage):?><link rel="stylesheet" href="/assets/pet-game.css?v=1.15.2.8"><?php endif;?>
<?php if($isPetPlayground):?><link rel="stylesheet" href="/assets/pet-playground.css?v=1.15.2.8"><?php endif;?>
<link rel="stylesheet" href="/assets/panel-usability.css?v=1.15.2.8">
<script src="/assets/panel-usability.js?v=1.15.2.8" defer></script>
</head>
<body class="ui-mode-cards" data-ui-mode="cards" data-ux-user="<?=(int)($me['id']??0)?>"<?php if($me && !empty($_SESSION['_activity_digest_pending'])):?> data-activity-digest-pending="1"<?php endif;?>>
<?php if($me): ?>
<a class="ux-skip-link" href="#main-content">Zum Inhalt</a>
<div class="app-shell">
<aside class="sidebar" id="panel-sidebar">
<div class="brand"><div class="brand-title">Serververwaltung</div><div class="brand-subtitle">LPLK Community & Server</div></div>
<?php require __DIR__.'/panel_navigation.php'; ?>
<?php if($sidebarPet): ?>
<section class="sidebar-pet-home sidebar-pet-tier-<?=Security::e($sidebarPet['tier'])?>" data-sidebar-pet data-pet-owner="<?=(int)$me['id']?>" data-pet-name="<?=Security::e($sidebarPet['name'])?>" data-pet-emoji="<?=Security::e($sidebarPet['emoji'])?>">
  <a class="sidebar-pet-habitat" href="/pets" aria-label="<?=Security::e($sidebarPet['name'])?>, <?=Security::e($sidebarPet['label'])?>, bei Meine Pets öffnen">
    <span class="sidebar-pet-sparkle" aria-hidden="true">✦</span>
    <span class="sidebar-pet-character" data-sidebar-pet-character aria-hidden="true">
      <span class="pet-emoji-actor" data-pet-emoji aria-hidden="true"><?=Security::e($sidebarPet['emoji'])?></span>
      <span class="pet-mischief-mark" aria-hidden="true">💧</span>
    </span>
  </a>
  <div class="sidebar-pet-info"><strong><?=Security::e($sidebarPet['name'])?></strong><span data-pet-status>Bereit für Unsinn</span></div>
  <button class="sidebar-pet-motion" type="button" data-pet-motion-toggle aria-pressed="true" aria-label="Pet-Animation ein- oder ausschalten">Animation: An</button>
</section>
<?php endif; ?>
<div class="sidebar-bottom"><a class="sidebar-user" href="/profile"><?php if(!empty($me['steam_verified_at'])&&!empty($me['steam_id64'])):?><img src="/users/<?=(int)$me['id']?>/steam-avatar" alt=""><?php endif;?><span><?=Security::e(Auth::displayName($me))?></span></a><form method="post" action="/logout"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="link-button" type="submit">Abmelden</button></form></div>
</aside>
<main class="main" id="main-content" tabindex="-1">
<div class="ux-topbar"><button type="button" class="btn btn-secondary ux-menu-toggle" aria-expanded="false" aria-controls="panel-sidebar" data-ux-menu>Menü</button><?php if(Auth::canAny(['players.view','servers.view'])):?><form method="get" action="/search" class="ux-global-search" role="search"><label for="global-search">Spieler oder Server suchen</label><input id="global-search" type="search" name="q" maxlength="160" placeholder="Name oder SteamID" autocomplete="off"><button class="btn btn-secondary">Suchen</button></form><?php endif;?><button type="button" class="btn btn-secondary ux-motion-button" data-ux-motion aria-pressed="false">Ruhige Ansicht</button></div>
<header class="page-header"><div><h1><?=Security::e($title ?? '')?></h1></div><div class="header-actions">
<div class="ui-theme-switch" data-ui-theme-switch role="group" aria-label="Farbschema">
  <span class="ui-theme-switch-label">Design</span>
  <button type="button" class="ui-theme-option<?=$themePreference==='light'?' is-active':''?>" data-ui-theme-mode="light" aria-pressed="<?=$themePreference==='light'?'true':'false'?>">Hell</button>
  <button type="button" class="ui-theme-option<?=$themePreference==='dark'?' is-active':''?>" data-ui-theme-mode="dark" aria-pressed="<?=$themePreference==='dark'?'true':'false'?>">Dunkel</button>
  <button type="button" class="ui-theme-option<?=$themePreference==='system'?' is-active':''?>" data-ui-theme-mode="system" aria-pressed="<?=$themePreference==='system'?'true':'false'?>">System</button>
</div>
<div class="ui-view-switch" data-ui-view-switch role="group" aria-label="Globale Darstellung">
  <span class="ui-view-switch-label">Ansicht</span>
  <button type="button" class="ui-view-option is-active" data-ui-view-mode="cards" aria-pressed="true">Kacheln</button>
  <button type="button" class="ui-view-option" data-ui-view-mode="compact" aria-pressed="false">Kompakt</button>
</div>
<a class="notification-link" href="/dashboard#announcements">Mitteilungen <span data-notification-count><?=Auth::unreadAnnouncementCount()?></span></a><?php if(Auth::founderTwoFactorLocked($me)):?><div class="role-badge role-badge-limited">Admin-Modus · Founder gesperrt</div><?php else:?><div class="role-badge role-badge-<?=Security::e(Auth::normalizeWebRole((string)$me['web_role']))?>"><?=Security::e(Auth::webRoleLabel((string)$me['web_role']))?></div><?php endif;?></div></header>
<?php foreach($flashes as $f):?><div class="alert alert-<?=Security::e($f['type'])?>" role="<?=$f['type']==='error'?'alert':'status'?>"><?=Security::e($f['message'])?></div><?php endforeach;?>
<?php if(Auth::founderTwoFactorLocked($me)):?><div class="twofactor-required-banner founder-twofactor-banner"><div><strong>Founder-Sicherheitsmodus aktiv</strong><p>2FA ist noch nicht eingerichtet. Bis zur Aktivierung gelten ausschließlich die Rechte der Admin-Rollenvorlage. Danach wird Founder automatisch vollständig freigeschaltet.</p></div><a class="btn btn-warning btn-small" href="/profile#twofactor">Founder durch 2FA freischalten</a></div><?php elseif(Auth::twoFactorSetupRequired($me)):?><div class="twofactor-required-banner"><div><strong>2FA für Ihre Rolle erforderlich</strong><p>Die Rollenvorlage verlangt Zwei-Faktor-Authentifizierung. Bis zur Einrichtung sind Funktions- und Serverrechte gesperrt.</p></div><a class="btn btn-primary btn-small" href="/profile#twofactor">2FA jetzt einrichten</a></div><?php endif;?>
<?php require __DIR__.'/panel_context.php'; ?>
<?php $formDraft=($template??'')!=='error'?\App\Services\PanelFormDraft::take($currentPath,(int)$me['id']):null; if($formDraft):?><div hidden data-ux-draft="<?=Security::e(json_encode($formDraft,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR))?>"></div><?php endif;?>
<?=$content?>
</main>
<aside class="panel-users-drawer" data-panel-users-drawer aria-label="Webpanel-Nutzer">
  <button type="button" class="panel-users-toggle" data-panel-users-toggle aria-expanded="false">
    <span class="panel-users-toggle-label">Nutzer</span>
    <span class="panel-users-toggle-online" data-panel-users-online>0</span>
    <span class="panel-users-toggle-unread" data-panel-users-unread hidden>0</span>
  </button>

  <div class="panel-users-drawer-inner">
    <div class="panel-users-list-view" data-panel-users-list-view>
      <div class="panel-users-drawer-head">
        <div>
          <strong>Webpanel-Nutzer</strong>
          <span data-panel-users-summary>Wird geladen…</span>
        </div>
        <button type="button" class="drawer-icon-button" data-panel-users-close title="Schließen">×</button>
      </div>
      <div class="panel-users-role-legend">
        <span class="panel-role-founder">Founder</span>
        <span class="panel-role-admin">Admin</span>
        <span class="panel-role-member">Member</span>
        <span class="panel-role-vip">VIP</span>
        <span class="panel-role-spieler">Spieler</span>
      </div>
      <div class="panel-users-list" data-panel-users-list>
        <div class="muted panel-users-loading">Nutzer werden geladen…</div>
      </div>
    </div>

    <div class="panel-user-detail-view" data-panel-user-detail-view hidden>
      <div class="panel-users-drawer-head">
        <button type="button" class="drawer-back-button" data-panel-user-back>← Nutzer</button>
        <button type="button" class="drawer-icon-button" data-panel-users-close title="Schließen">×</button>
      </div>
      <div data-panel-user-detail>
        <div class="muted panel-users-loading">Nutzer wird geladen…</div>
      </div>
    </div>
  </div>
</aside>


<?php $hasPlayerQuickActions=Auth::can('live.kick')||Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])||Auth::can('live.mute')||Auth::can('live.gag')||Auth::can('live.message')||Auth::can('live.whitelist')||Auth::can('moderation.warn')||Auth::can('moderation.notes')||Auth::can('moderation.global_comms')||Auth::can('moderation.evidence'); ?>
<?php if($hasPlayerQuickActions):?>
<div class="player-action-overlay" data-player-action-modal hidden>
  <section class="player-action-dialog" role="dialog" aria-modal="true" aria-labelledby="playerActionTitle">
    <header class="player-action-head">
      <div>
        <span class="eyebrow">Spieleraktionen</span>
        <h2 id="playerActionTitle" data-player-action-name>Spieler</h2>
        <span class="mono" data-player-action-steam></span>
      </div>
      <button type="button" class="drawer-icon-button" data-player-action-close aria-label="Schließen">×</button>
    </header>

    <div class="player-action-loading" data-player-action-loading>Live-Status wird geprüft…</div>
    <div class="player-action-body" data-player-action-body hidden>
      <div class="player-action-context">
        <label>Aktueller Server
          <select data-player-action-server></select>
        </label>
        <div class="small-muted" data-player-action-server-note></div>
      </div>
      <div class="player-action-restrictions" data-player-action-restrictions hidden aria-live="polite"></div>

      <?php if(Auth::canAny(['moderation.ban.temporary','moderation.ban.permanent','moderation.unban'])):?>
      <section class="player-action-card player-action-ban-card">
        <div><h3>Globaler Ban</h3><p>Gilt automatisch auf allen verbundenen Servern.</p></div>
        <div data-player-action-ban-free>
          <a class="btn btn-danger" href="/bans/new" data-player-action-ban-link>Global bannen</a>
        </div>
        <form method="post" class="form-stack" data-player-action-unban hidden>
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
          <input type="hidden" name="_return" data-player-action-return>
          <div class="status-pill status-danger" data-player-action-ban-status>Global gebannt</div>
          <label>Entbannungsgrund<input type="text" name="unban_reason" maxlength="300" value="Global über Spieleraktionen entbannt" required></label>
          <button class="btn btn-secondary" type="submit" data-confirm="Diesen Spieler wirklich global entbannen?">Global entbannen</button>
        </form>
      </section>
      <?php endif;?>

      <div class="player-action-live-grid" data-player-action-live-area>
        <?php if(Auth::can('live.kick')):?>
        <form method="post" action="/player-actions/live" class="player-action-card form-stack" data-player-live-action="kick" data-confirm="Spieler wirklich vom ausgewählten Server kicken?">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="server_id"><input type="hidden" name="action" value="kick"><input type="hidden" name="_return">
          <h3>Kick</h3><label>Grund<input type="text" name="reason" maxlength="160" value="Von der Serververwaltung entfernt."></label><button class="btn btn-danger">Kicken</button>
        </form>
        <?php endif;?>

        <?php if(Auth::can('live.mute')):?>
        <section class="player-action-card">
          <h3>Sprachchat sperren</h3>
          <form method="post" action="/player-actions/live" class="form-stack" data-player-live-action="mute">
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="server_id"><input type="hidden" name="action" value="mute"><input type="hidden" name="_return">
            <label>Dauer<select name="minutes"><option value="10">10 Minuten</option><option value="30">30 Minuten</option><option value="60" selected>1 Stunde</option><option value="360">6 Stunden</option><option value="1440">1 Tag</option><option value="10080">7 Tage</option><option value="43200">30 Tage</option><option value="0">Permanent</option></select></label>
            <label>Grund<input type="text" name="reason" maxlength="160" value="Voicechat-Regelverstoß"></label><button class="btn btn-primary">Sprachchat sperren</button>
          </form>
          <form method="post" action="/player-actions/live" data-player-live-action="unmute"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="server_id"><input type="hidden" name="action" value="unmute"><input type="hidden" name="_return"><button class="btn btn-secondary btn-small">Sprachchat freigeben</button></form>
        </section>
        <?php endif;?>

        <?php if(Auth::can('live.gag')):?>
        <section class="player-action-card">
          <h3>Textchat sperren</h3>
          <form method="post" action="/player-actions/live" class="form-stack" data-player-live-action="gag">
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="server_id"><input type="hidden" name="action" value="gag"><input type="hidden" name="_return">
            <label>Dauer<select name="minutes"><option value="10">10 Minuten</option><option value="30">30 Minuten</option><option value="60" selected>1 Stunde</option><option value="360">6 Stunden</option><option value="1440">1 Tag</option><option value="10080">7 Tage</option><option value="43200">30 Tage</option><option value="0">Permanent</option></select></label>
            <label>Grund<input type="text" name="reason" maxlength="160" value="Chat-Regelverstoß"></label><button class="btn btn-primary">Textchat sperren</button>
          </form>
          <form method="post" action="/player-actions/live" data-player-live-action="ungag"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="server_id"><input type="hidden" name="action" value="ungag"><input type="hidden" name="_return"><button class="btn btn-secondary btn-small">Textchat freigeben</button></form>
        </section>
        <?php endif;?>

        <?php if(Auth::can('live.message')):?>
        <form method="post" action="/player-actions/live" class="player-action-card form-stack player-action-message-card" data-player-live-action="private_message">
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="server_id"><input type="hidden" name="action" value="private_message"><input type="hidden" name="_return">
          <h3>Nachricht an Spieler</h3>
          <label>Darstellung<select name="type"><option value="chat">Chat</option><option value="center">Bildschirmmitte</option><option value="alert">Alert</option><option value="notify">Notify</option></select></label>
          <label>Nachricht<textarea name="message" maxlength="500" rows="3" placeholder="Persönliche Nachricht" required></textarea></label>
          <button class="btn btn-primary">Nachricht senden</button>
        </form>
        <?php endif;?>

        <?php if(Auth::can('live.whitelist')):?>
        <form method="post" action="/player-actions/live" class="player-action-card form-stack" data-player-whitelist-action>
          <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="action" value="whitelist_add"><input type="hidden" name="_return">
          <h3>Whitelist</h3>
          <p>Serverbezogen: Die Freigabe gilt ausschließlich für den hier gewählten Gameserver.</p>
          <label>Server<select name="server_id" data-player-whitelist-server required></select></label>
          <button class="btn btn-primary">Auf diesem Server whitelisten</button>
        </form>
        <?php endif;?>
      </div>

      <?php if(Auth::can('moderation.warn')||Auth::can('moderation.notes')||Auth::can('moderation.global_comms')||Auth::can('moderation.evidence')):?>
      <section class="player-action-card player-action-moderation-card">
        <div><h3>Globale Moderation</h3><p>Serverübergreifende Maßnahmen und interne Dokumentation.</p></div>
        <div class="player-action-live-grid">
          <?php if(Auth::can('moderation.warn')):?>
          <form method="post" action="/moderation/warnings" class="form-stack" data-player-moderation-action>
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="player_name"><input type="hidden" name="chat_message_id"><input type="hidden" name="_return">
            <h3>Verwarnen</h3>
            <label>Grund<select name="reason_code"><?php foreach(GlobalBanService::reasons() as $rk=>$rl):?><option value="<?=Security::e($rk)?>"><?=Security::e($rl)?></option><?php endforeach;?></select></label>
            <label>Details<input type="text" name="detail" maxlength="700" placeholder="optional"></label>
            <label>Gültigkeit<input type="number" name="expires_days" min="0" max="3650" value="90"> Tage (0 = dauerhaft)</label>
            <?php if(Auth::can('moderation.ban.temporary')):?><label class="checkbox-line"><input type="checkbox" name="auto_escalate" value="1" checked> ab 3 aktiven Verwarnungen automatisch 1 Tag global bannen</label><?php else:?><div class="small-muted">Automatische Ban-Eskalation ist ohne Recht „Globaler zeitlicher Ban“ deaktiviert.</div><?php endif;?>
            <button class="btn btn-secondary">Verwarnung speichern</button>
          </form>
          <?php endif;?>

          <?php if(Auth::can('moderation.global_comms')):?>
          <form method="post" action="/moderation/sanctions" class="form-stack" data-player-moderation-action>
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="player_name"><input type="hidden" name="chat_message_id"><input type="hidden" name="_return">
            <h3>Global Mute / Gag</h3>
            <label>Art<select name="sanction_type"><option value="mute">Sprachchat sperren</option><option value="gag">Textchat sperren</option><option value="silence">Silence (Voice + Chat)</option></select></label>
            <label>Dauer<select name="minutes"><?php foreach(GlobalBanService::durations() as $mins=>$label): if($mins===-1) continue; ?><option value="<?=$mins?>" <?=$mins===60?'selected':''?>><?=Security::e($label)?></option><?php endforeach;?></select></label>
            <label>Grund<select name="reason_code"><?php foreach(GlobalBanService::reasons() as $rk=>$rl):?><option value="<?=Security::e($rk)?>"><?=Security::e($rl)?></option><?php endforeach;?></select></label>
            <label>Details<input type="text" name="detail" maxlength="500" placeholder="optional"></label>
            <button class="btn btn-secondary">Global anwenden</button>
          </form>
          <?php endif;?>

          <?php if(Auth::can('moderation.notes')):?>
          <form method="post" action="/moderation/notes" class="form-stack" data-player-moderation-action>
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="player_name"><input type="hidden" name="_return">
            <h3>Moderator-Notiz</h3><label>Interne Notiz<textarea name="note" maxlength="2000" rows="3" required></textarea></label><button class="btn btn-secondary">Notiz speichern</button>
          </form>
          <?php endif;?>

          <?php if(Auth::can('moderation.evidence')):?>
          <form method="post" action="/moderation/evidence" class="form-stack" data-player-moderation-action data-chat-evidence-form hidden>
            <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><input type="hidden" name="steam_id64"><input type="hidden" name="player_name"><input type="hidden" name="chat_message_id"><input type="hidden" name="evidence_type" value="chat"><input type="hidden" name="value" value="Chatnachricht"><input type="hidden" name="_return">
            <h3>Chat als Beweis sichern</h3><label>Bezeichnung<input type="text" name="label" maxlength="160" value="Serverchat-Beleg"></label><button class="btn btn-secondary">Chatnachricht sichern</button>
          </form>
          <?php endif;?>
        </div>
        <?php if(Auth::can('moderation.view')):?><a class="btn btn-secondary btn-small" href="/moderation" data-player-moderation-link>Moderationsakte durchsuchen</a><?php endif;?>
      </section>
      <?php endif;?>

      <div class="alert alert-warning" data-player-action-offline hidden>Der Spieler ist aktuell auf keinem für dich verwaltbaren Server online. Globaler Ban/Entban bleibt trotzdem verfügbar.</div>
    </div>
  </section>
</div>
<?php endif;?>

<div class="activity-digest-overlay" data-activity-digest hidden>
  <section class="activity-digest-dialog" role="dialog" aria-modal="true" aria-labelledby="activityDigestTitle">
    <header class="activity-digest-head">
      <div>
        <h2 id="activityDigestTitle">Seit deinem letzten Besuch</h2>
        <p data-activity-digest-since>Aktivitäten werden geladen…</p>
      </div>
      <button type="button" class="activity-digest-close" data-activity-digest-close aria-label="Schließen">×</button>
    </header>

    <div class="activity-digest-body" data-activity-digest-body>
      <div class="activity-digest-loading">Aktivitäten werden zusammengestellt…</div>
    </div>

    <footer class="activity-digest-footer">
      <span>Nur Aktivitäten seit dem vorherigen Login.</span>
      <button type="button" class="btn btn-secondary" data-activity-digest-close>Schließen</button>
    </footer>
  </section>
</div>

</div>

<?php if($sidebarPet): ?>
<div class="pet-escape-overlay" data-pet-escape-overlay hidden aria-hidden="true">
  <span class="pet-escape-character" data-pet-escape-character>
    <span class="pet-escape-facing" data-pet-escape-facing>
      <span class="pet-escape-emoji" data-pet-escape-emoji></span>
      <span class="pet-escape-mark" data-pet-escape-mark>💧</span>
    </span>
    <span class="pet-escape-bubble" data-pet-escape-bubble></span>
  </span>
  <span class="pet-butterfly pet-butterfly-one" data-pet-butterfly>🦋</span>
  <span class="pet-butterfly pet-butterfly-two" data-pet-butterfly>🦋</span>
  <span class="pet-butterfly pet-butterfly-three" data-pet-butterfly>🦋</span>
</div>
<?php endif; ?>
<?php else: ?>
<div class="auth-shell"><?php foreach($flashes as $f):?><div class="alert alert-<?=Security::e($f['type'])?>" role="<?=$f['type']==='error'?'alert':'status'?>"><?=Security::e($f['message'])?></div><?php endforeach;?><?=$content?></div>
<?php endif;?>
<script src="/assets/server-join.js?v=1.15.2.8"></script><script src="/assets/qrcode-local.js"></script>
<script src="/assets/app.js?v=1.15.2.8"></script>
<script src="/assets/pet-companion.js?v=1.15.2.8"></script>
<?php if($isPetPage):?><script src="/assets/pet-game.js?v=1.15.2.8"></script><?php endif;?>
<?php if($isPetPlayground):?><script src="/assets/pet-playground.js?v=1.15.2.8"></script><?php endif;?>
</body></html>
