<?php
// Fixed read-only source inspection; personal identities are counted, never exported.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
function referenceJson(string $path):array {
 if(is_link($path)||realpath($path)!==$path)throw new RuntimeException('Unsafe or missing reference path: '.basename($p??$path));
 $raw=file_get_contents($path);
 $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|/\*.*?\*/|//[^\r\n]*~s',static fn($m)=>$m[1]??'',$raw);
 $raw=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|,(\s*[}\]])~s',static fn($m)=>!empty($m[1])?$m[1]:$m[2],$raw);
 return json_decode($raw,true,64,JSON_THROW_ON_ERROR);
}
try {
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $uuid='11110003-0000-4000-8000-000000000003';$server=Pterodactyl\Models\Server::query()->where('uuid',$uuid)->firstOrFail();
 $allocation=$app->make('db')->table('allocations')->where('id',$server->allocation_id)->first(['ip','port']);
 if($allocation->ip!=='203.0.113.10'||$allocation->port!==27019)throw new RuntimeException('Identity mismatch');
 $root='/var/lib/pterodactyl/volumes/'.$uuid.'/game/csgo/addons/swiftlys2';
  $candidates=[$root.'/configs/plugins/KampfhamsterTeamSwitchBlocker/config.jsonc',$root.'/configs/plugins/kampfhamster.team_switch_blocker/config.jsonc'];
 $existing=array_values(array_filter($candidates,static fn($p)=>file_exists($p)||is_link($p)));
 if(count($existing)!==1)throw new RuntimeException('Expected exactly one known TeamSwitch config: '.count($existing));
 $config=$existing[0];$dll=$root.'/plugins/KampfhamsterTeamSwitchBlocker/KampfhamsterTeamSwitchBlocker.dll';
 foreach([$root,$config,$dll] as $p)if(is_link($p)||realpath($p)!==$p)throw new RuntimeException('Unsafe or missing reference path: '.basename($p??$path));
 $j=referenceJson($config);$c=$j['TeamSwitchBlocker']??null;if(!is_array($c))throw new RuntimeException('Missing settings');
 $fields=['Enabled','ShowBlockedMessageInChat','ShowBlockedMessageInCenter','AutoDetectMaxRounds','FallbackMaxRounds','BlockAtMaxRoundsPercent','AllowHalftimeTeamSwitchException','HalftimeSwitchGraceSeconds','TreatWinScoreAsMatchEnd','BlockConfiguredSteamIdsFromWinningTeamDuringMatchEnd','MatchEndGuardWindowSeconds','ShowForceToLosingTeamMessage','EnableLosingTeamMenu','RestrictLosingTeamMenuToAdmins','LosingTeamMenuAdminPermissions','BlockOnlyIfTeamIsLeading','BlockBothTeamsIfBothReachedLimit','AllowSpectator','ForceBackIfSwitchStillHappened','AnnounceWhenTeamBecomesProtected','ResetScoreOnNewMap','ResetScoreOnNewMatch','UseRoundEndFallback','AllowChatStatusCommand','Debug','BlockedCommands'];
 $bridge=referenceJson($root.'/data/serververwaltung_bridge/status.json');
 $plugins=array_values(array_map(static fn($p)=>array_intersect_key($p,array_flip(['id','version','status'])),array_filter($bridge['plugins']??[],static fn($p)=>($p['id']??'')==='kampfhamster.team_switch_blocker')));
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'server'=>'11110003','readOnly'=>true,'productionChanged'=>false,'settings'=>array_intersect_key($c,array_flip($fields)),'configuredIdentityCount'=>count($c['ForceToLosingTeamSteamIds']??[]),'personalIdentitiesCopied'=>false,'plugins'=>$plugins,'configSha256'=>hash_file('sha256',$config),'dllSha256'=>hash_file('sha256',$dll)],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Team rules reference failed','type'=>get_class($e),'line'=>$e->getLine(),'detail'=>$e instanceof RuntimeException?$e->getMessage():'parse failed']),"\n";exit(1);}


