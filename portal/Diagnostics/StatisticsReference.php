<?php
// Read-only allowlist. Never export ranking records, identities or integration secrets.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
function statsJson(string $p):array {
 if(is_link($p)||realpath($p)!==$p)throw new RuntimeException('Unsafe reference path');
 $s=file_get_contents($p);
 $s=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|/\*.*?\*/|//[^\r\n]*~s',static fn($m)=>$m[1]??'',$s);
 $s=preg_replace_callback('~("(?:[^"\\\\]|\\\\.)*")|,(\s*[}\]])~s',static fn($m)=>!empty($m[1])?$m[1]:$m[2],$s);
 return json_decode($s,true,64,JSON_THROW_ON_ERROR);
}
function pick(array $a,string $keys):array{return array_intersect_key($a,array_flip(explode(' ',$keys)));}
try {
 require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
 $uuid='11110003-0000-4000-8000-000000000003';$s=Pterodactyl\Models\Server::query()->where('uuid',$uuid)->firstOrFail();
 $a=$app->make('db')->table('allocations')->where('id',$s->allocation_id)->first(['ip','port']);
 if($a->ip!=='203.0.113.10'||$a->port!==27019)throw new RuntimeException('Identity mismatch');
 $root='/var/lib/pterodactyl/volumes/'.$uuid.'/game/csgo/addons/swiftlys2';
 $paths=['headshot'=>'configs/plugins/headshotonly/config.jsonc','damage'=>'configs/plugins/k4.damageinfo/k4-damageinfo.jsonc','gotv'=>'configs/plugins/k4.gotv/k4-gotv.jsonc','stattrak'=>'configs/plugins/nadekill_stattrak/config.jsonc','headshotDll'=>'plugins/HeadshotOnly/HeadshotOnly.dll','damageDll'=>'plugins/K4-DamageInfo/K4-DamageInfo.dll','gotvDll'=>'plugins/K4-GOTV/K4-GOTV.dll','stattrakDll'=>'plugins/NadeKillStatTrak/NadeKillStatTrak.dll'];$hash=[];
 foreach($paths as $key=>$relative){$p=$root.'/'.$relative;if(is_link($p)||realpath($p)!==$p)throw new RuntimeException('Missing/linked '.$key);$hash[$key]=hash_file('sha256',$p);}
 $h=statsJson($root.'/'.$paths['headshot'])['HeadshotOnly'];$d=statsJson($root.'/'.$paths['damage']);$g=statsJson($root.'/'.$paths['gotv']);$n=statsJson($root.'/'.$paths['stattrak']);
 $d=$d['K4DamageInfo']??$d;$g=$g['K4GOTV']??$g;$n=$n['NadeKillStatTrak']??$n;
 $hs=pick($h['Statistics']??[],'Enabled TopCount MinShotsForEfficiencyRanking MinKillsForEfficiencyRanking HeadshotQuoteWeight KillEfficiencyWeight HeadshotEfficiencyWeight VolumeWeight KillEfficiencyForFullScore HeadshotEfficiencyForFullScore HeadshotsForFullVolumeScore');$hs['BotTraining']=pick($h['Statistics']['BotTraining']??[],'Enabled OnlyWhenAllEnemiesAreBots MinShotsForRanking MinKillsForRanking');
 $nade=['Enabled'=>$n['Enabled']??null];foreach(['Counting'=>'TrackAllWeapons MergeKnifeVariants IgnoreBotAttackers IgnoreBotVictims CountSelfKills CountDuringWarmup SaveImmediatelyOnKill InfernoAttributionSeconds UnknownInfernoFallback','Hud'=>'Enabled DefaultPlayerEnabled TriggerMode InspectCooldownMilliseconds MessageDurationMilliseconds KeepVisibleWhileHeld ShowWeaponServerRank ShowWeaponLeader ShowTotalWeaponKills','Ranking'=>'TopEntries IncludeZeroKillPlayers LargeDisplayEnabled','Messages'=>'AnnounceKillToAttacker BroadcastKills'] as $k=>$keys)$nade[$k]=pick($n[$k]??[],$keys);
 $demo=[];foreach(['AutoRecord'=>'Enabled CropRounds IdlePlayerCountThreshold IdleTimeSeconds RecordWarmup StopOnIdle','DemoRequest'=>'Enabled DeleteUnused PrintAll','General'=>'MinimumDemoDuration AutoCleanupEnabled AutoCleanupFileAgeHours AutoCleanupIntervalMinutes DeleteDemoAfterUpload DeleteEveryDemoFromServerAfterServerStart DemoDirectory'] as $k=>$keys)$demo[$k]=pick($g[$k]??[],$keys);
 $demo['integrations']=['databaseConfigured'=>!empty($g['DatabaseConnection'])&&$g['DatabaseConnection']!=='host','webhookConfigured'=>!empty($g['Discord']['WebhookURL']),'ftpEnabled'=>$g['Ftp']['Enabled']??false,'megaEnabled'=>$g['Mega']['Enabled']??false];
 ob_end_clean();echo json_encode(['checkedAt'=>gmdate('c'),'server'=>'11110003','readOnly'=>true,'productionChanged'=>false,'headshotEnabled'=>$h['Enabled']??null,'statistics'=>$hs,'damage'=>pick($d,'RoundEndSummary AllowDeathPrint ShowOnlyKiller ShowFriendlyFire ShowAllDamages CenterDamageInfo CenterDamageMode ConsoleDamageInfo FFAMode NoRoundsMode CenterInfoTimeout ConsoleRequirePermission CenterRequirePermission SummaryRequirePermission'),'stattrak'=>$nade,'demos'=>$demo,'sha256'=>$hash],JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Statistics reference failed','type'=>get_class($e),'detail'=>$e instanceof RuntimeException?$e->getMessage():'parse failed']),"\n";exit(1);}
