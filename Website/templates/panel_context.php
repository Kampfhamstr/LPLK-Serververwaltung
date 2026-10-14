<?php use App\Core\Security; $uiContext=\App\Services\PanelNavigation::context($currentPath,$player??null,$server??null,(int)($selected??0)); ?>
<?php if($uiContext):?>
<section class="ux-context" data-ux-context data-kind="<?=$uiContext['kind']?>" data-entity-id="<?=$uiContext['id']?>" data-base="<?=Security::e($uiContext['base'])?>" data-feature-server="<?=$uiContext['server']?>">
<nav aria-label="Seitenpfad" class="ux-breadcrumb"><a href="<?=Security::e($uiContext['parent'])?>"><?=Security::e($uiContext['parentLabel'])?></a><span aria-hidden="true">/</span><strong><?=Security::e($uiContext['name'])?></strong></nav>
<?php if($uiContext['kind']==='server'):?><button type="button" class="btn btn-secondary btn-small" data-ux-favorite="<?=$uiContext['id']?>" aria-pressed="false">Als Favorit merken</button><?php endif;?>
<nav class="ux-context-links" aria-label="<?=Security::e($uiContext['parentLabel'])?>bereiche">
<?php foreach($uiContext['links'] as $link):?><a href="<?=Security::e($link[0])?>" <?php if(isset($link[2])):?>data-ux-feature="<?=Security::e($link[2])?>"<?php endif;?>><?=Security::e($link[1])?></a><?php endforeach;?>
</nav><span class="small-muted" data-feature-note role="status"></span>
</section><?php endif;?>
