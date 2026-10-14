<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 $root='/var/www/serververwaltung';spl_autoload_register(static function($class)use($root){if(str_starts_with($class,'App\\')){$p=$root.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($p))require $p;}});
 App\Core\Config::load($root.'/config/app.php');$s=App\Services\LiveServerService::server(7);
 if($s['server_identifier']!=='11110004'||$s['name']!=='[TEST][LPLK] Beispielserver')throw new RuntimeException('Scope mismatch');
 $state=App\Services\LiveServerService::status(7);if(empty($state['bridge_online']))throw new RuntimeException('Bridge offline');
 if(count(array_filter($state['players']??[],static fn($p)=>empty($p['is_bot'])))>0)throw new RuntimeException('Empty test server required for recording check');
 $mode='__LPLK_DEMO_MODE__';if(!in_array($mode,['start','stop'],true))throw new RuntimeException('Invalid mode');
 $id=App\Services\LiveServerService::queue(7,'server_command',['command'=>'sw_lplk_demos '.$mode]);
 ob_end_clean();echo json_encode(['server'=>7,'mode'=>$mode,'queued'=>$id,'actualRecordingMustBeVerified'=>true],JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Demo check failed','type'=>get_class($e),'detail'=>$e instanceof RuntimeException?$e->getMessage():'failed']),"\n";exit(1);}
