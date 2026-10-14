<?php use App\Core\Security; use App\Core\Auth; ?>
<section class="panel">
  <div class="panel-header split-header">
    <div>
      <div class="forum-breadcrumb"><a href="/forum">Forum</a> / <?=Security::e($thread['category_label'])?></div>
      <h2><?=Security::e($thread['title'])?></h2>
      <p class="muted">Erstellt von <?=Security::e($thread['author_display_name'])?> · <?=Security::e($thread['created_at'])?></p>
    </div>
    <div class="forum-thread-flags">
      <?php if($thread['is_pinned']):?><span class="status-pill">ANGEHEFTET</span><?php endif;?>
      <?php if(!Auth::can('community.forum.post')):?><div class="alert alert-info">Für dieses Konto ist das Schreiben im Forum nicht freigegeben.</div>
  <?php elseif($thread['is_locked']):?><span class="status-pill status-warning">GESPERRT</span><?php endif;?>
    </div>
  </div>

  <?php if($canModerate):?>
  <div class="forum-moderation-bar">
    <form method="post" action="/forum/<?=(int)$thread['id']?>/pin"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-secondary btn-small"><?=$thread['is_pinned']?'Nicht mehr anheften':'Anheften'?></button></form>
    <form method="post" action="/forum/<?=(int)$thread['id']?>/lock"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-secondary btn-small"><?=$thread['is_locked']?'Wieder öffnen':'Sperren'?></button></form>
    <form method="post" action="/forum/<?=(int)$thread['id']?>/delete" data-confirm="Dieses Forum-Thema wirklich entfernen?"><input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>"><button class="btn btn-danger btn-small">Thema entfernen</button></form>
  </div>
  <?php endif;?>
</section>

<section class="forum-post-list">
  <?php foreach($posts as $index=>$post):?>
    <article class="panel forum-post" id="post-<?=(int)$post['id']?>">
      <div class="forum-post-author">
        <?php if($post['steam_verified']):?><img src="/users/<?=(int)$post['user_id']?>/steam-avatar" alt=""><?php else:?><span class="forum-avatar-fallback"><?=Security::e(strtoupper(substr($post['author_display_name'],0,1)))?></span><?php endif;?>
        <div><strong><?=Security::e($post['author_display_name'])?></strong><span><?=Security::e($post['web_role_label'])?></span></div>
      </div>
      <div class="forum-post-content">
        <div class="forum-post-time">#<?=$index+1?> · <?=Security::e($post['created_at'])?></div>
        <div class="forum-post-body"><?=nl2br(Security::e($post['body']))?></div>
      </div>
      <?php if($canModerate && $index>0):?>
      <form method="post" action="/forum/<?=(int)$thread['id']?>/posts/<?=(int)$post['id']?>/delete" data-confirm="Diesen Beitrag wirklich entfernen?" class="forum-post-delete">
        <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
        <button class="link-button">Löschen</button>
      </form>
      <?php endif;?>
    </article>
  <?php endforeach;?>
</section>

<section class="panel" id="reply">
  <div class="panel-header"><h2>Antworten</h2></div>
  <?php if($thread['is_locked']):?>
    <div class="alert alert-info">Dieses Thema ist gesperrt. Neue Antworten sind nicht möglich.</div>
  <?php else:?>
    <form method="post" action="/forum/<?=(int)$thread['id']?>/reply" class="form-stack">
      <input type="hidden" name="_csrf" value="<?=Security::e(Security::csrfToken())?>">
      <label>Antwort<textarea name="body" rows="6" maxlength="5000" required></textarea></label>
      <button class="btn btn-primary" type="submit">Antwort veröffentlichen</button>
    </form>
  <?php endif;?>
</section>
