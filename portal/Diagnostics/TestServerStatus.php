<?php
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
    require getcwd().'/vendor/autoload.php'; $app=require getcwd().'/bootstrap/app.php';
    $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $db=$app->make('db')->connection();
    $server=$db->table('servers')->where('external_id','lplk-inner-circle-test-20261011')->first(['id','uuid','status']);
    if (!$server || $server->uuid!=='11110004-0000-4000-8000-000000000004') throw new RuntimeException('Scope mismatch');
    $token=$db->table('server_variables')->where('server_id',$server->id)->where('variable_id',76)->value('variable_value');
    $clean=static function($value) use($token) {
        if(strlen((string)$token)>0) $value=str_replace($token,'[secret]',$value);
        $value=preg_replace('/\b[0-9a-fA-F]{32}\b/','[redacted]',$value);
        $value=preg_replace('/\x1b\[[0-9;]*m/','',$value);
        $value=preg_replace(['/\b7656119\d{10}\b/','/\b(?:\d{1,3}\.){3}\d{1,3}:\d+\b/'],['[steamid]','[endpoint]'],$value);
        // Keep the entire JSON report below the SSH runner's output limit.
        return mb_substr($value,-1400);
    };
    $path='/var/lib/pterodactyl/volumes/'.$server->uuid;
    $deps=$path.'/game/csgo/addons/swiftlys2/bin/managed/SwiftlyS2.CS2.deps.json';
    $dep=is_file($deps)?json_decode(file_get_contents($deps),true):[];
    $bridgeFile=$path.'/game/csgo/addons/swiftlys2/data/serververwaltung_bridge/status.json';
    $bridge=is_file($bridgeFile)?json_decode(file_get_contents($bridgeFile),true):[];
    $humans=isset($bridge['players'])?count(array_filter($bridge['players'],static fn($p)=>($p['is_bot']??false)!==true)):null;
    $mapFile=$path.'/game/csgo/addons/swiftlys2/data/LPLK_Maps/status.json';
    $mapStatus=is_file($mapFile)?json_decode(file_get_contents($mapFile),true):null;
    if(is_array($mapStatus)) unset($mapStatus['limitReview']); // Full table is read through the bounded Panel file API.
    $vpkHeaders=[];
    foreach(array_slice(glob($path.'/game/bin/linuxsteamrt64/steamapps/workshop/content/730/*/*.vpk')?:[],0,8) as $vpk) {
        if(is_link($vpk)||realpath($vpk)!==$vpk)continue;
        $file=fopen($vpk,'rb');if(!$file)continue;$head=fread($file,12);if(strlen($head)!==12){fclose($file);continue;}
        $header=unpack('Vsignature/Vversion/Vsize',$head);
        if($header['signature']!==0x55aa1234||!in_array($header['version'],[1,2],true)||$header['size']>16*1024*1024){fclose($file);continue;}
        if($header['version']===2)fread($file,16);$tree=fread($file,$header['size']);fclose($file);
        preg_match_all('/maps\\/([A-Za-z0-9_\\/-]+)\x00/',$tree,$matches);
        $vpkHeaders[]=['id'=>basename(dirname($vpk)),'file'=>basename($vpk),'treeBytes'=>$header['size'],'vmap'=>str_contains($tree,"vmap_c\0"),'vwrld'=>str_contains($tree,"vwrld_c\0"),'mapDirectories'=>array_slice(array_values(array_unique($matches[1]??[])),0,8)];
    }
    $webExports=[];$webBackups=[];
    foreach(['LPLK_Economy','LPLK_Statistics'] as $module){
        $file=$path.'/game/csgo/addons/swiftlys2/data/'.$module.'/web/ranking.json';
        $data=is_file($file)&&!is_link($file)&&filesize($file)<262144?json_decode(file_get_contents($file),true):null;
        $webExports[$module]=is_array($data)?['schema'=>$data['schema']??null,'profile'=>$data['profile']??null,'generatedAt'=>$data['generatedAt']??null,'rows'=>isset($data['rows'])?count($data['rows']):null,'boards'=>isset($data['boards'])?array_map('count',$data['boards']):null,'sha256'=>hash_file('sha256',$file)]:null;
        $file=$path.'/.lplk-rollback/'.$module.'.data-before-web-economy.zip';
        $webBackups[$module]=is_file($file)&&!is_link($file)?['bytes'=>filesize($file),'sha256'=>hash_file('sha256',$file)]:null;
    }
    $statsRoot=$path.'/game/csgo/addons/swiftlys2/data/LPLK_Statistics';$weaponFile=$statsRoot.'/web/weapons.json';
    $weaponData=is_file($weaponFile)&&!is_link($weaponFile)&&filesize($weaponFile)<4194304?json_decode(file_get_contents($weaponFile),true):null;
    $weaponReport=['schema'=>$weaponData['schema']??null,'generatedAt'=>$weaponData['generatedAt']??null,'boards'=>isset($weaponData['boards'])?array_map('count',$weaponData['boards']):null,'bytes'=>is_file($weaponFile)?filesize($weaponFile):null,'settingsHash'=>is_file($statsRoot.'/settings.json')?hash_file('sha256',$statsRoot.'/settings.json'):null,'dataHash'=>is_file($statsRoot.'/statistics.json')?hash_file('sha256',$statsRoot.'/statistics.json'):null];
    $report=['checkedAt'=>gmdate('c'),'weaponRanking'=>$weaponReport,'webEconomyExports'=>$webExports,'webEconomyBackups'=>$webBackups,'maps'=>$mapStatus,'players'=>count($bridge['players']??[]),'humans'=>$humans,'bridgeAge'=>isset($bridge['updated_at'])?time()-strtotime($bridge['updated_at']):null,'status'=>$server->status,'hasOwnSteamToken'=>strlen((string)$token)>0,
        'whitelist'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Whitelist/status.json'),true),
        'sounds'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Sounds/status.json'),true),
        'anticamp'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_AntiCamp/status.json'),true),
        'teams'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Teams/status.json'),true),
        'weapons'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Weapons/status.json'),true),
        'combat'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_CombatRules/status.json'),true),
        'economy'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/status.json'),true),
        'killRewardProbe'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/killreward-probe.json'),true),
        'bountyProbe'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/bounty-probe.json'),true),
        'bonusProbe'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/bonus-probe.json'),true),
        'titlesProbe'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/titles-probe.json'),true),
        'robberyProbe'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/robbery-probe.json'),true),
        'lotteryProbe'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/lottery-probe.json'),true),
        'bettingProbe'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/betting-probe.json'),true),
        'economyProbe'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Economy/probe.json'),true),
        'economyLog'=>$clean(preg_replace('/[ \t]+/',' ',(string)shell_exec('docker logs --since 8m --tail 2600 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "^LPLK_Economy (0|probe|betprobe|lottoprobe|robprobe|titleprobe|bonusprobe|bountyprobe|killprobe|Kopfgeld|Titel|Wetten|Lotto|Bankraub)|Exception:|Unable to load|cannot open|SQLitePCLRaw|libe_sqlite3|bereit=" | tail -24'))),
        'nadeeffects'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_NadeEffects/status.json'),true),
        'nadeeffectsLog'=>$clean((string)shell_exec('docker logs --since 8m --tail 2200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "^LPLK_NadeEffects (0|probe)|LPLK_NadeEffects.Plugin" | tail -8')),
        'fluch'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Fluch/status.json'),true),
        'fluchLog'=>$clean((string)shell_exec('docker logs --since 8m --tail 2200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "^LPLK_Fluch (0|probe)|LPLK_Fluch.Plugin" | tail -8')),
        'thirdperson'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_ThirdPerson/status.json'),true),
        'thirdpersonLog'=>$clean((string)shell_exec('docker logs --since 8m --tail 2200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "^LPLK_ThirdPerson (0|probe|trace|:)|LPLK_ThirdPerson.Plugin|^LPLK_Admin [0-9].*Module:" | tail -12')),
        'statistics'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Statistics/status.json'),true),
        'demos'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Demos/status.json'),true),
        'demosLog'=>$clean((string)shell_exec('docker logs --since 10m --tail 1800 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "Exception|LPLK_Demos.Plugin|Access.*denied|DirectoryNotFound|InvalidOperation" | tail -12')),
        'statisticsLog'=>$clean((string)shell_exec('docker logs --since 5m --tail 1200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "^LPLK_(Statistics|Demos) [0-9].*enabled=" | tail -2')),
        'weaponsLog'=>$clean((string)shell_exec('docker logs --since 5m --tail 1200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "^LPLK_(Weapons|CombatRules) [0-9].*enabled=" | tail -2')),
        'navigationLog'=>$clean((string)shell_exec('docker logs --since 5m --tail 1200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "^LPLK_Admin [0-9].*(Module:|Spielermenü:)" | tail -3')),
        'teamsLog'=>$clean((string)shell_exec('docker logs --since 5m --tail 1200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "LPLK_Admin [0-9].*Module:|LPLK_Teams [0-9].*enabled=" | tail -4')),
        'antiteamflash'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_AntiTeamFlash/status.json'),true),
        'antiteamflashLog'=>$clean((string)shell_exec('docker logs --since 5m --tail 1200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "LPLK_Admin [0-9].*Module:|LPLK_AntiTeamFlash [0-9].*enabled=" | tail -4')),
        'chatguard'=>json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_ChatGuard/status.json'),true),
        'chatguardLog'=>$clean((string)shell_exec('docker logs --since 5m --tail 1200 11110004-0000-4000-8000-000000000004 2>&1 | grep -E "LPLK_Admin [0-9].*Module:|LPLK_ChatGuard [0-9].*enabled=" | tail -6')),
        'anticampLog'=>$clean((string)shell_exec('docker logs --since 10m --tail 2500 11110004-0000-4000-8000-000000000004 2>&1 | grep -Ei -A 6 "LPLK_AntiCamp|sw_lplk_anticamp" | tail -35')),
        'connectionLog'=>preg_replace(['/\b7656119\d{10}\b/','/\b(?:\d{1,3}\.){3}\d{1,3}:\d+\b/'],['[steamid]','[endpoint]'],$clean((string)shell_exec('docker logs --since 20m --tail 2000 11110004-0000-4000-8000-000000000004 2>&1 | grep -Ei -B 2 -A 3 "parse|signon|disconnect|connect.*client|reject|overflow|NETMSG|unserialize|Client.*connect|password" | tail -110'))),
        'disk'=>trim((string)shell_exec('du -sh '.escapeshellarg($path).' 2>/dev/null')),
        'container'=>$clean((string)shell_exec("docker inspect --format '{{.State.Status}} {{.State.ExitCode}}' 11110004-0000-4000-8000-000000000004 2>/dev/null")),
        'workshopLog'=>$clean((string)shell_exec('docker logs --tail 1000 11110004-0000-4000-8000-000000000004 2>&1 | grep -Ei "Workshop|UGC|Download|library folder|steamapps" | tail -35')),
        'vpkHeaders'=>$vpkHeaders,
        'testFreeBytes'=>disk_free_space($path),
        'workshopExpectedBytes'=>array_sum(array_column(json_decode(@file_get_contents($path.'/game/csgo/addons/swiftlys2/data/LPLK_Maps/workshop-cache.json'),true)?:[],'Bytes')),
        'swiftly'=>array_values(array_filter(array_keys($dep['libraries']??[]),fn($x)=>str_starts_with($x,'SwiftlyS2.CS2/'))),
        'log'=>$clean((string)shell_exec('docker logs --tail 1800 11110004-0000-4000-8000-000000000004 2>&1 | grep -Ei -A 8 "LPLK|Plugin.*(fail|load)|Exception" | tail -100')),
        'bridge'=>['updated_at'=>$bridge['updated_at']??null,'map'=>$bridge['map']??null,'plugins'=>$bridge['plugins']??[]],
        'startupFiles'=>$clean((string)shell_exec('find '.escapeshellarg($path.'/game/csgo/addons/swiftlys2').' -maxdepth 2 -type f -name "*.log" -printf "%p\\n" 2>/dev/null; find '.escapeshellarg($path.'/game/bin/linuxsteamrt64').' -maxdepth 1 -type f -name "core*" -printf "%f %s\\n" 2>/dev/null')),
        'gameinfo'=>$clean((string)shell_exec('grep -E "Game[[:space:]_]|Game_LowViolence" '.escapeshellarg($path.'/game/csgo/gameinfo.gi').' 2>/dev/null')),
        'sourceSizes'=>trim((string)shell_exec('du -h --max-depth=2 /var/lib/pterodactyl/volumes/11110003-0000-4000-8000-000000000003/game 2>/dev/null | sort -h | tail -15'))];
    // Compact JSON keeps the growing module report within the SSH runner's scoped 32k
    // character limit. Never emit a partial JSON document as a successful status.
    $json=json_encode($report,JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE|JSON_INVALID_UTF8_SUBSTITUTE|JSON_THROW_ON_ERROR);
    if(strlen($json)>31000) {
        // Keep structured health and current menu/weapon responses intact. Older
        // optional diagnostic tails must not crowd the bounded transport envelope.
        $report['shortenedDiagnostics']=[];
        foreach(['sourceSizes','gameinfo','startupFiles','workshopLog','connectionLog','anticampLog','antiteamflashLog','chatguardLog','teamsLog','thirdpersonLog','demosLog','statisticsLog','weaponsLog','log'] as $field) {
            if(strlen($report[$field]??'')>250){$report[$field]=mb_substr($report[$field],-250);$report['shortenedDiagnostics'][]=$field;}
        }
        $json=json_encode($report,JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE|JSON_INVALID_UTF8_SUBSTITUTE|JSON_THROW_ON_ERROR);
    }
    if(strlen($json)>31000) {
        $report['bridge']['plugins']=array_map(static fn($p)=>array_intersect_key($p,array_flip(['id','version','status'])),$report['bridge']['plugins']);
        $report['shortenedDiagnostics'][]='plugin descriptions';
        $json=json_encode($report,JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE|JSON_INVALID_UTF8_SUBSTITUTE|JSON_THROW_ON_ERROR);
    }
    if(strlen($json)>31000) {
        // Growing Economy settings and probe evidence take priority over optional
        // log tails. Mark omissions explicitly and retain all structured health.
        foreach(['sourceSizes','gameinfo','startupFiles','workshopLog','connectionLog','anticampLog','antiteamflashLog','chatguardLog','teamsLog','thirdpersonLog','demosLog','statisticsLog','weaponsLog','nadeeffectsLog','fluchLog','log'] as $field) {
            if(strlen($json)<=31000)break;
            if(($report[$field]??'')!==''){
                $report[$field]='';$report['shortenedDiagnostics'][]=$field.' omitted';
                $json=json_encode($report,JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE|JSON_INVALID_UTF8_SUBSTITUTE|JSON_THROW_ON_ERROR);
            }
        }
    }
    if(strlen($json)>31000) {
        // Historical probe reports are also retained in the workspace evidence.
        // Keep the most recent probe in full; omit older probes only if needed.
        // Current module health, settings and the active release proof stay intact.
        $probeFields=array_values(array_filter(['economyProbe','bettingProbe','lotteryProbe','robberyProbe','titlesProbe','bonusProbe','bountyProbe','killRewardProbe'],static fn($field)=>is_array($report[$field]??null)));
        usort($probeFields,static fn($a,$b)=>(strtotime($report[$a]['updatedAt']??'1970-01-01')?:0)<=>(strtotime($report[$b]['updatedAt']??'1970-01-01')?:0));
        array_pop($probeFields);
        foreach($probeFields as $field) {
            if(strlen($json)<=31000)break;
            $report[$field]=null;$report['shortenedDiagnostics'][]=$field.' historical omitted';
            $json=json_encode($report,JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE|JSON_INVALID_UTF8_SUBSTITUTE|JSON_THROW_ON_ERROR);
        }
    }
    if(strlen($json)>31000) throw new LengthException('Status report exceeds transport budget');
    ob_end_clean(); echo $json,"\n";
} catch(Throwable $e) { if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Testserver-Status nicht lesbar','type'=>get_class($e)]),"\n"; exit(1); }





