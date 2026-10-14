<?php use App\Core\Security; use App\Core\Auth; use App\Services\CommunityService; ?>
<div class="forum-layout">
<section class="panel">
  <div class="panel-header split-header">
    <div><h2>Internes Forum</h2><p class="muted">Dauerhafte Diskussionen für alle angemeldeten Webpanel-Nutzer.</p></div>
    <?php if(Auth::can('community.forum.post')):?><a class="btn btn-primary btn-small" href="#new-thread">Neues Thema</a><?php endif;?>
  </div>

  <div class="forum-category-tabs">
    <a href="/forum" class="<?=$category===''?'active':''?>">Alle</a>
    <?php foreach($categories as $key=>$label):?>
      <a href="/forum?category=<?=rawurlencode($key)?>" class="<?=$category===$key?'active':''?>"><?=Security::e($label)?></a>
    <?php endforeach;?>
  </div>

  <div class="forum-thread-list">
    <?php if(empty($threads)):?><div class="forum-empty muted">In diesem Bereich gibt es noch keine Themen.</div><?php endif;?>
    <?php foreach($threads as $thread):?>
      <a class="forum-thread-row" href="/forum/<?=(int)$thread['id']?>">
        <div class="forum-thread-main">
          <strong><?=Security::e($thread['title'])?></strong>
          <span><?=Security::e($thread['category_label'])?> · <?=Security::e($thread['author_display_name'])?> · <?=Security::e($thread['created_at'])?></span>
        </div>
        <div class="forum-thread-flags">
          <?php if($thread['is_pinned']):?><span class="status-pill">ANGEHEFTET</span><?php endif;?>
          <?php if($thread['is_locked']):?><span class="status-pill status-warning">GESPERRT</span><?php endif;?>
          <span><?=max(0,(int)$thread['post_count']-1)?> Antworten</span>
        </div>
      </a>
    <?php endforeach;?>
  </div>
</section>

<?php if(Auth::can('community.forum.post')):?>
<section class="panel" id="new-thread">
  <div class="panel-header"><h2>Neues Thema</h2><p class="muted">Alle Panelrollen dürfen Themen erstellen. Administrationsrechte werden dadurch nicht erweitert.</p></div>
  <form method="post" action="/forum" class="form-stack">
    <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
    <div class="form-grid">
      <label>Kategorie
        <select name="category"><?php foreach($categories as $key=>$label):?><option value="<?=Security::e($key)?>"><?=Security::e($label)?></option><?php endforeach;?></select>
      </label>
      <label>Titel<input type="text" name="title" maxlength="120" required></label>
    </div>
    <label>Erster Beitrag<textarea name="body" rows="6" maxlength="5000" required></textarea></label>
    <button class="btn btn-primary" type="submit">Thema erstellen</button>
  </form>
</section>
</div>

<?php endif;?>
