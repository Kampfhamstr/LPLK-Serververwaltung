<?php use App\Services\PanelNavigation; use App\Core\Security; ?>
<nav class="ux-main-nav" aria-label="Hauptnavigation" id="main-navigation">
<?php foreach(PanelNavigation::groups() as $groupLabel=>$groupItems): $groupActive=(bool)array_filter($groupItems,static fn($i)=>PanelNavigation::active($currentPath,$i[0])); ?>
<?php if(count($groupItems)===1):$item=$groupItems[0];?><a class="nav-item<?=$groupActive?' is-active':''?>" href="<?=Security::e($item[0])?>" <?=$groupActive?'aria-current="page"':''?>><?=Security::e($groupLabel)?></a>
<?php else:?><details class="ux-nav-group" <?=$groupActive?'open':''?>><summary><?=Security::e($groupLabel)?></summary><div>
<?php foreach($groupItems as $item):$active=PanelNavigation::active($currentPath,$item[0]);?><a class="nav-item<?=$active?' is-active':''?>" href="<?=Security::e($item[0])?>" <?=$active?'aria-current="page"':''?>><?=Security::e($item[1])?></a><?php endforeach;?>
</div></details><?php endif;?><?php endforeach;?>
</nav>
