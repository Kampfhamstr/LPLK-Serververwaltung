<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();$stage='identity';
try{
 $web='/var/www/serververwaltung';spl_autoload_register(static function($c)use($web){if(str_starts_with($c,'App\\')){ $p=$web.'/src/'.str_replace('\\','/',substr($c,4)).'.php';if(is_file($p))require $p;}});
 App\Core\Config::load($web.'/config/app.php');$server=App\Services\LiveServerService::server(7);
 if($server['server_identifier']!=='11110004'||$server['name']!=='[TEST][LPLK] Beispielserver')throw new RuntimeException('Scope');
 $connector=App\Services\ConnectorFactory::make($server);$details=$connector->serverDetails();
 if(($details['uuid']??'')!=='11110004-0000-4000-8000-000000000004')throw new RuntimeException('UUID');
 $allocation=App\Services\ServerJoinService::fromDetails($details);
 if($allocation['address']!=='203.0.113.10:27020')throw new RuntimeException('Address');
 $stage='whitelist';$wlPath='game/csgo/addons/swiftlys2/data/LPLK_Whitelist/status.json';
 $wl=json_decode($connector->read($wlPath),true,512,JSON_THROW_ON_ERROR);
 if(($wl['Enabled']??false)!==true||($wl['Mode']??0)!==1||($wl['entries']??0)<1||!empty($wl['reloadError']))throw new RuntimeException('Whitelist not enforced');
 $configPath='game/csgo/cfg/server.cfg';$before=$connector->read($configPath);
 $stage='persistent';$count=0;
 $after=preg_replace('/^[\t ]*sv_password\b[^\r\n]*$/mi','sv_password ""',$before,-1,$count);
 if($count!==1||!is_string($after))throw new RuntimeException('Unexpected password setting');
 if($before!==$after)$connector->write($configPath,$after);
 if(!hash_equals(hash('sha256',$after),hash('sha256',$connector->read($configPath))))throw new RuntimeException('Config verification');
 $stage='runtime';$connector->sendCommand('sv_password ""');
 $stage='query';$passwordProtected=null;
 for($attempt=0;$attempt<5;$attempt++){
  usleep(500000);$sock=stream_socket_client('udp://203.0.113.10:27020',$errno,$error,3);if(!$sock)continue;stream_set_timeout($sock,3);
  $request="\xff\xff\xff\xffTSource Engine Query\0";fwrite($sock,$request);$packet=fread($sock,4096);
  if(strlen($packet)>=9&&ord($packet[4])===0x41){fwrite($sock,$request.substr($packet,5,4));$packet=fread($sock,4096);}fclose($sock);
  if(strlen($packet)<20||substr($packet,0,5)!=="\xff\xff\xff\xffI")continue;
  $offset=6;for($i=0;$i<4;$i++){$end=strpos($packet,"\0",$offset);if($end===false)throw new RuntimeException('Query format');$offset=$end+1;}
  $offset+=7;if(strlen($packet)<=$offset)throw new RuntimeException('Query truncated');$passwordProtected=ord($packet[$offset])!==0;if(!$passwordProtected)break;
 }
 if($passwordProtected!==false)throw new RuntimeException('Runtime password state not confirmed');
 $afterWl=json_decode($connector->read($wlPath),true,512,JSON_THROW_ON_ERROR);
 if(($afterWl['Enabled']??false)!==true||($afterWl['Mode']??0)!==1||$afterWl['entries']!==$wl['entries'])throw new RuntimeException('Whitelist state changed');
 App\Core\Audit::log('server.test.password_removed','server','7',['password_protected'=>false,'whitelist_preserved'=>true]);
 ob_end_clean();echo json_encode(['server'=>'11110004','address'=>$allocation['address'],'passwordProtected'=>false,'saved'=>true,'runtimeQueryConfirmed'=>true,'whitelistEnabled'=>true,'whitelistEntries'=>$wl['entries'],'restartNeeded'=>false]),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Test password removal verification incomplete','stage'=>$stage,'type'=>get_class($e)]),"\n";exit(1);}
