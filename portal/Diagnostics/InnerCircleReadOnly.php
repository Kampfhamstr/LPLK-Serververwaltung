<?php
// Fixed production target. Read-only observations; no game commands or configuration writes.
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
 require getcwd().'/vendor/autoload.php'; $app=require getcwd().'/bootstrap/app.php';
 $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap(); $db=$app->make('db');
 $uuid='11110003-0000-4000-8000-000000000003';
 $s=$db->table('servers')->where('uuid',$uuid)->first(['id','uuid','name','allocation_id']);
 if(!$s)throw new RuntimeException('Target missing');
 $a=$db->table('allocations')->where('id',$s->allocation_id)->first(['ip','port']);
 if($a->ip!=='203.0.113.10'||(int)$a->port!==27019)throw new RuntimeException('Target mismatch');
 $root='/var/lib/pterodactyl/volumes/'.$uuid; $swift=$root.'/game/csgo/addons/swiftlys2';
 if(realpath($swift)!==$swift)throw new RuntimeException('Unsafe path');
 $secrets=$db->table('server_variables as sv')->join('egg_variables as ev','ev.id','=','sv.variable_id')->where('sv.server_id',$s->id)->where(function($q){$q->where('ev.env_variable','like','%TOKEN%')->orWhere('ev.env_variable','like','%PASSWORD%')->orWhere('ev.env_variable','like','%SECRET%')->orWhere('ev.env_variable','like','%RCON%');})->pluck('sv.variable_value')->all();
 $clean=static function($text)use($secrets){
  foreach($secrets as $secret)if(strlen((string)$secret)>3)$text=str_replace((string)$secret,'[secret]',$text);
  return preg_replace(['/\x1b\[[0-9;]*m/','/\b[0-9a-fA-F]{32,}\b/','/\b7656119\d{10}\b/','/\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?\b/','/\b(?:ghp_|github_pat_)[A-Za-z0-9_]+/','/((?:password|passwd|token|secret|rcon_password)\s*[=:]\s*)\S+/i'],['','[secret]','[steamid]','[ip]','[secret]','$1[secret]'],(string)$text);
 };
 $readJson=static function($path){if(!is_file($path)||is_link($path)||realpath($path)!==$path||filesize($path)>4194304)return null;return json_decode(file_get_contents($path),true);};
 $bridge=$readJson($swift.'/data/serververwaltung_bridge/status.json');
 $startupFlags=[];
 foreach($db->table('server_variables as sv')->join('egg_variables as ev','ev.id','=','sv.variable_id')->where('sv.server_id',$s->id)->whereIn('ev.env_variable',['PRERELEASE','INSTALL_SWIFTLY','AUTO_UPDATE','UPDATE_ON_START'])->get(['ev.env_variable','sv.variable_value']) as $row){
  $startupFlags[$row->env_variable]=preg_match('/^(0|1|true|false)$/i',(string)$row->variable_value)?$row->variable_value:'[unexpected value]';
 }
 $runningFlags=trim((string)shell_exec('timeout 5 docker inspect --format '.escapeshellarg('{{range .Config.Env}}{{println .}}{{end}}').' '.$uuid.' 2>/dev/null | grep -E '.escapeshellarg('^(PRERELEASE|INSTALL_SWIFTLY|AUTO_UPDATE|UPDATE_ON_START)=(0|1|true|false)$')));
 $launcherExcerpt=(string)shell_exec('timeout 5 docker exec '.$uuid.' sh -c '.escapeshellarg('for f in /entrypoint.sh /entrypoint /home/container/entrypoint.sh /usr/local/bin/entrypoint.sh; do if [ -f "$f" ]; then echo "$f"; head -100 "$f"; fi; done').' 2>/dev/null');
 $deps=$readJson($swift.'/bin/managed/SwiftlyS2.CS2.deps.json');
 $frameworkSelection=['panelFlags'=>$startupFlags,'runningFlags'=>$runningFlags,'managed'=>array_values(array_filter(array_keys($deps['libraries']??[]),fn($v)=>str_starts_with($v,'SwiftlyS2.'))),'launcherExcerpt'=>mb_substr($clean($launcherExcerpt),0,10000),'startupTemplate'=>$clean($db->table('servers')->where('id',$s->id)->value('startup'))];
 $players=$bridge['players']??[]; $pings=[];foreach($players as $p)if(!($p['is_bot']??false)&&isset($p['ping'])&&is_numeric($p['ping']))$pings[]=(int)$p['ping'];
 $inspect=json_decode((string)shell_exec('timeout 5 docker inspect --format '.escapeshellarg('{{json .State}}').' '.$uuid.' 2>/dev/null'),true);
 if(!is_array($inspect))throw new RuntimeException('Container state unavailable');
 $limits=json_decode((string)shell_exec('timeout 5 docker inspect --format '.escapeshellarg('{"memory":{{.HostConfig.Memory}},"nanoCpus":{{.HostConfig.NanoCpus}},"cpuQuota":{{.HostConfig.CpuQuota}},"cpuPeriod":{{.HostConfig.CpuPeriod}},"cpuset":"{{.HostConfig.CpusetCpus}}","restartCount":{{.RestartCount}}}').' '.$uuid.' 2>/dev/null'),true);
 $stats=[];for($i=0;$i<3;$i++){
  $row=json_decode(trim((string)shell_exec('timeout 5 docker stats --no-stream --format '.escapeshellarg('{{json .}}').' '.$uuid.' 2>/dev/null')),true);
  $stats[]=['at'=>gmdate('c'),'cpu'=>$row['CPUPerc']??null,'memory'=>$row['MemUsage']??null,'memoryPercent'=>$row['MemPerc']??null,'netIO'=>$row['NetIO']??null,'blockIO'=>$row['BlockIO']??null,'pids'=>$row['PIDs']??null];
 }
 $log=(string)shell_exec('timeout 8 docker logs --timestamps --since 3h --tail 12000 '.$uuid.' 2>&1 | tail -c 3000000');
 $patterns=['exceptions'=>'/exception|segmentation fault|segfault|fatal|assertion|\bpanic\b/i','tickDelays'=>'/slow server frame|long frame|hitch|spike|fell behind|tick.*(?:slow|overrun)|simulation.*(?:slow|behind)/i','connection'=>'/timed? out|timeout|connection.*(?:fail|lost)|Steam.*(?:fail|disconnect)|No Steam logon|network.*(?:error|loss)/i','resources'=>'/failed to (?:load|find)|error loading resource|missing.*(?:resource|file)|no space left|out of memory/i','pluginErrors'=>'/\[error\]|\berror\b.*(?:plugin|swiftly)|(?:plugin|swiftly).*\berror\b/i'];
 $groups=[];foreach($patterns as $key=>$pattern)$groups[$key]=['count'=>0,'firstAt'=>null,'lastAt'=>null,'samples'=>[]];
 $lines=explode("\n",$log);$lastTimestamp=null;$firstTimestamp=null;
 $runtimeVersionLines=[];foreach($lines as $line)if(preg_match('/SwiftlyS2.*(?:version|v?1\.[45]\.\d)|SwiftlyS2 is (?:up-to-date|at a newer version)|SwiftlyS2 (?:updated|installed)/i',$line))$runtimeVersionLines[]=mb_substr($clean($line),0,450);
 $frameworkSelection['startupVersionLines']=array_slice($runtimeVersionLines,-12);
 foreach($lines as $line){
  $stamp=preg_match('/^(\d{4}-\d\d-\d\dT\S+)/',$line,$m)?$m[1]:null;if($stamp){$firstTimestamp??=$stamp;$lastTimestamp=$stamp;}
  // Never export chat or player identity lines.
  if(preg_match('/say_team|\bsay\b|STEAM USERID|connected, address|namechange/i',$line))continue;
  foreach($patterns as $key=>$pattern)if(preg_match($pattern,$line)){
   $groups[$key]['count']++;$groups[$key]['firstAt']??=$stamp;$groups[$key]['lastAt']=$stamp;
   $groups[$key]['samples'][]=mb_substr($clean($line),0,500);if(count($groups[$key]['samples'])>7)array_shift($groups[$key]['samples']);
  }
 }
 $contexts=[];
 foreach($lines as $i=>$line)if(preg_match('/Failed to load plugin|GetAvailableAddonMaps failed/',$line)){
  $context=[];foreach(array_slice($lines,max(0,$i-3),9) as $near)if(!preg_match('/say_team|\bsay\b|connected, address/i',$near))$context[]=mb_substr($clean($near),0,700);
  $contexts[]= $context;
 }
 $suspectPaths=[];
 foreach(['AntiCamp','SerververwaltungBridge_v1.6.1_Source','SerververwaltungBridge','SLAYER_AntiCamp'] as $name){
  $dir=$swift.'/plugins/'.$name;$files=[];
  if(is_dir($dir)&&realpath($dir)===$dir)foreach(new DirectoryIterator($dir) as $f)if($f->isFile()&&!$f->isLink())$files[]=['name'=>$f->getFilename(),'bytes'=>$f->getSize(),'modified'=>gmdate('c',$f->getMTime())];
  $suspectPaths[$name]=['exists'=>is_dir($dir),'files'=>array_slice($files,0,14)];
 }
 $logFiles=[];$fileErrors=[];
 foreach([$swift.'/logs',$root.'/game/csgo/logs'] as $dir)if(is_dir($dir)&&realpath($dir)===$dir){
  $files=[];foreach(new DirectoryIterator($dir) as $f)if($f->isFile()&&!$f->isLink())$files[]=['path'=>$f->getPathname(),'modified'=>$f->getMTime(),'bytes'=>$f->getSize()];
  usort($files,fn($a,$b)=>$b['modified']<=>$a['modified']);
  foreach(array_slice($files,0,3) as $f){
   $logFiles[]=['name'=>str_replace($root.'/','',$f['path']),'modified'=>gmdate('c',$f['modified']),'bytes'=>$f['bytes']];
   if($f['modified']<time()-10800)continue;
   $fp=fopen($f['path'],'rb');if(!$fp)continue;fseek($fp,max(0,$f['bytes']-196608));$tail=fread($fp,196608);fclose($fp);
   foreach(explode("\n",$tail) as $line)if(preg_match('/exception|\[error\]|fatal|slow server frame|segfault/i',$line)&&!preg_match('/say_team|\bsay\b/i',$line))$fileErrors[]=['file'=>basename($f['path']),'line'=>mb_substr($clean($line),0,650)];
  }
 }
 $eventFormat='{"time":"{{.Time}}","action":"{{.Action}}","exitCode":"{{index .Actor.Attributes "exitCode"}}","signal":"{{index .Actor.Attributes "signal"}}"}';
 $eventRaw=(string)shell_exec('timeout 6 docker events --since 30m --until '.escapeshellarg(gmdate('Y-m-d\TH:i:s\Z')).' --filter type=container --filter container='.$uuid.' --format '.escapeshellarg($eventFormat).' 2>/dev/null | tail -60');
 $events=[];foreach(explode("\n",trim($eventRaw)) as $line){$e=json_decode($line,true);if(is_array($e)&&in_array($e['action']??'', ['start','stop','die','kill','oom','restart','destroy','create'],true)){$e['at']=gmdate('c',(int)$e['time']);$events[]=$e;}}
 $wings=(string)shell_exec('timeout 6 journalctl -u wings --since "30 minutes ago" -n 2500 --no-pager -o short-iso 2>/dev/null | grep -F '.$uuid.' | tail -18');
 $wingsLines=[];foreach(explode("\n",trim($wings)) as $line)if($line!=='')$wingsLines[]=mb_substr($clean($line),0,700);
 $host=[];foreach(['cpu','memory','io'] as $p)$host[$p.'Pressure']=trim((string)@file_get_contents('/proc/pressure/'.$p));
 $host['load']=sys_getloadavg();$host['cpuCount']=(int)trim((string)shell_exec('getconf _NPROCESSORS_ONLN'));
 $host['memory']=trim((string)shell_exec("free -m | head -3"));$host['disk']=trim((string)shell_exec('df -h / | tail -1'));
 $report=['checkedAt'=>gmdate('c'),'server'=>'11110003','name'=>$s->name,'readOnly'=>true,'frameworkSelection'=>$frameworkSelection,'container'=>array_intersect_key($inspect,array_flip(['Status','Running','Restarting','OOMKilled','ExitCode','StartedAt','FinishedAt'])),'containerEvents'=>$events,'wingsEvents'=>$wingsLines,'limits'=>$limits,'samples'=>$stats,'host'=>$host,
  'bridge'=>['updatedAt'=>$bridge['updated_at']??null,'ageSeconds'=>isset($bridge['updated_at'])?time()-strtotime($bridge['updated_at']):null,'map'=>$bridge['map']??null,'humans'=>count(array_filter($players,fn($p)=>!($p['is_bot']??false))),'bots'=>count(array_filter($players,fn($p)=>($p['is_bot']??false))),'pingMin'=>$pings?min($pings):null,'pingMax'=>$pings?max($pings):null,'plugins'=>array_map(fn($p)=>array_intersect_key($p,array_flip(['id','version','status'])),$bridge['plugins']??[])],
  'logs'=>['window'=>'up to 3 hours, at most 12000 lines / 3 MB','lines'=>count($lines),'firstAt'=>$firstTimestamp,'lastAt'=>$lastTimestamp,'groups'=>$groups,'loadFailureContexts'=>array_slice($contexts,-4),'suspectPaths'=>$suspectPaths,'files'=>$logFiles,'fileErrors'=>array_slice($fileErrors,-14)]];
 $json=json_encode($report,JSON_UNESCAPED_SLASHES|JSON_INVALID_UTF8_SUBSTITUTE|JSON_THROW_ON_ERROR);if(strlen($json)>31000)throw new LengthException('Report budget exceeded');
 ob_end_clean();echo $json,"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Inner Circle read-only diagnosis failed','type'=>get_class($e),'line'=>$e->getLine()]),"\n";exit(1);}
