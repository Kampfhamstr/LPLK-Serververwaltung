<?php
declare(strict_types=1);
$root=dirname(__DIR__);require $root.'/src/bootstrap.php';
use App\Services\PluginUpdateService;
@set_time_limit(300);
$check=PluginUpdateService::checkDueSources(3600);$auto=PluginUpdateService::runAutoUpdates();
echo '['.date('c').'] GitHub geprüft: '.$check['ok'].'/'.$check['checked']."\n";foreach($check['errors'] as $plugin=>$error)echo '[CHECK ERROR] '.$plugin.': '.$error."\n";
echo '['.date('c').'] Auto-Updates queued: '.$auto['queued'].'; skipped: '.$auto['skipped']."\n";foreach($auto['errors'] as $error)echo '[AUTO ERROR] '.$error."\n";
