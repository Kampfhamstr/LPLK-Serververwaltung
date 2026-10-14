<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 $root='/var/www/serververwaltung';spl_autoload_register(static function($class)use($root){if(str_starts_with($class,'App\\')){$p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($p))require $p;}});
 App\Core\Config::load($root.'/config/app.php');$s=App\Services\LiveServerService::server(7);
 if($s['server_identifier']!=='11110004'||$s['name']!=='[TEST][LPLK] Beispielserver')throw new RuntimeException('Scope mismatch');
 $state=App\Services\LiveServerService::status(7);
 if(empty($state['bridge_online'])||!is_numeric($state['status_age_seconds']??null)||$state['status_age_seconds']>10||!is_array($state['players']??null)||count($state['players'])>0)throw new RuntimeException('Fresh empty test server required');
 $id=App\Services\LiveServerService::queue(7,'server_command',['command'=>'sw_lplk_economy titleprobe']);
 ob_end_clean();echo json_encode(['server'=>7,'queued'=>$id,'actualJournalMustBeVerified'=>true],JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Titles probe failed','type'=>get_class($e),'detail'=>$e instanceof RuntimeException?$e->getMessage():'failed']),"\n";exit(1);}



