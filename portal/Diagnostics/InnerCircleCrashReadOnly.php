<?php
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
 $uuid='11110003-0000-4000-8000-000000000003';
 $root='/var/lib/pterodactyl/volumes/'.$uuid;
 if(realpath($root)!==$root)throw new RuntimeException('Unsafe root');
 $clean=static function($s){return preg_replace(['/\x1b\[[0-9;]*m/','/\b[0-9a-fA-F]{32,}\b/','/\b7656119\d{10}\b/','/\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?\b/','/((?:password|token|secret|rcon_password)\s*[=:]\s*)\S+/i'],['','[secret]','[steamid]','[ip]','$1[secret]'],(string)$s);};
 $kernel=shell_exec("timeout 6 journalctl -k --since '45 minutes ago' --no-pager -o short-iso 2>/dev/null | grep -Ei 'cs2|segfault|out of memory|oom-kill|killed process|trap|general protection' | tail -30");
 $cores=shell_exec("timeout 6 sh -c 'if command -v coredumpctl >/dev/null; then coredumpctl --no-pager --since \"45 minutes ago\" list 2>/dev/null; fi' | tail -15");
 $files=[];foreach([$root,$root.'/game/csgo',$root.'/game/bin/linuxsteamrt64',$root.'/game/csgo/addons/swiftlys2',$root.'/game/csgo/addons/swiftlys2/logs',$root.'/game/csgo/addons/swiftlys2/logs/core',$root.'/game/csgo/addons/swiftlys2/logs/console',$root.'/game/csgo/addons/swiftlys2/dumps',$root.'/game/csgo/addons/swiftlys2/dumps/crashreport'] as $dir){
  if(!is_dir($dir)||realpath($dir)!==$dir)continue;
  foreach(new DirectoryIterator($dir) as $f)if(!$f->isDot()&&!$f->isLink()&&(preg_match('/log|dump|crash|core|console/i',$f->getFilename())||str_ends_with($dir,'/console')||str_contains($dir,'/dumps')))$files[]=['path'=>str_replace($root.'/','',$f->getPathname()),'directory'=>$f->isDir(),'bytes'=>$f->isFile()?$f->getSize():null,'modified'=>gmdate('c',$f->getMTime())];
 }
 $tmp=shell_exec('timeout 6 docker exec '.$uuid.' sh -c '.escapeshellarg('for d in /tmp/dumps /tmp /home/container/game/bin/linuxsteamrt64; do if [ -d "$d" ]; then find "$d" -maxdepth 1 -type f \( -name "*.dmp" -o -name "core*" -o -name "*crash*" \) -printf "%p %s bytes %TY-%Tm-%TdT%TH:%TM:%TS\n"; fi; done').' 2>/dev/null');
 usort($files,fn($a,$b)=>strcmp($b['modified'],$a['modified']));
 $tail=[];foreach(array_slice($files,0,12) as $f)if(!$f['directory']&&preg_match('/\.log$|\.txt$/i',$f['path'])){
  $path=$root.'/'.$f['path'];if(!is_file($path)||is_link($path))continue;$fp=fopen($path,'rb');fseek($fp,max(0,$f['bytes']-262144));$text=fread($fp,262144);fclose($fp);
  foreach(explode("\n",$text) as $line)if(preg_match('/exception|fatal|assert|segfault|crash|\.cs:line|at Swiftly|at .*Plugin|\[error\]|shutdown|shutting down|terminated|abort|signal|backtrace/i',$line)&&!preg_match('/\bsay\b/i',$line))$tail[]=['file'=>$f['path'],'line'=>mb_substr($clean($line),0,900)];
 }
 $crashContents=[];foreach(array_slice(array_values(array_filter($files,fn($f)=>$f['directory']&&preg_match('~/crashreport/[a-f0-9-]{36}$~',$f['path']))),0,4) as $folder){
  $dir=$root.'/'.$folder['path'];if(realpath($dir)!==$dir)continue;
  foreach(new DirectoryIterator($dir) as $f)if($f->isFile()&&!$f->isLink()){
   $item=['path'=>$folder['path'].'/'.$f->getFilename(),'bytes'=>$f->getSize(),'modifiedAt'=>gmdate('c',$f->getMTime())];
   if($f->getFilename()==='crashinfo.json'&&$f->getSize()<2097152){
    $j=json_decode(file_get_contents($f->getPathname()),true);
    $process=$j['process']??[];$item['crash']=$process['crash']??null;$item['native']=$j['native']??null;
    $threads=$process['threads']??[];
    $crashThread=$process['crash']['thread_id']??null;
    $item['faultFrames']=array_map(fn($frame)=>array_intersect_key($frame,array_flip(['index','module','function','function_offset','address'])),array_slice($threads[$crashThread]['frames']??[],0,28));
    $item['faultRegisters']=array_intersect_key($threads[$crashThread]['frames'][0]['registers']??[],array_flip(['rip','rax','rbx','rcx','rdx','rsi','rdi','rsp','rbp','r8','r9','r10','r11','r12','r13','r14','r15']));
    $item['processKeys']=array_keys($process);
    $item['reportKeys']=array_keys($j);
   }
   $crashContents[]=$item;
  }
 }
 $consoleEnds=[];foreach(array_slice(array_values(array_filter($files,fn($f)=>!$f['directory']&&str_contains($f['path'],'/logs/console/')&&str_ends_with($f['path'],'.log'))),0,3) as $f){
  $fp=fopen($root.'/'.$f['path'],'rb');fseek($fp,max(0,$f['bytes']-24000));$data=fread($fp,24000);fclose($fp);$end=[];
  foreach(array_slice(explode("\n",$data),-38) as $line)if(!preg_match('/\bsay\b|say_team/i',$line))$end[]=mb_substr($clean(preg_replace('/\x27[^\x27]*\x27/','[quoted]',$line)),0,500);
  $consoleEnds[]=['file'=>$f['path'],'lines'=>$end];
 }
 $swift=$root.'/game/csgo/addons/swiftlys2';$binary=null;$symbols=null;$disassembly=null;
 foreach([$swift.'/bin/linuxsteamrt64/swiftlys2.so',$swift.'/bin/linux64/swiftlys2.so',$swift.'/bin/swiftlys2.so',$swift.'/swiftlys2.so'] as $path)if(is_file($path)&&realpath($path)===$path){$binary=['path'=>str_replace($root.'/','',$path),'sha256'=>hash_file('sha256',$path),'modified'=>gmdate('c',filemtime($path))];$symbols=shell_exec('timeout 8 addr2line -Cfie '.escapeshellarg($path).' 0x342d4f 0x345cc0 0x34883f 0x3ede14 2>/dev/null');$disassembly=shell_exec('timeout 8 objdump -d -C --start-address=0x342d10 --stop-address=0x342d80 '.escapeshellarg($path).' 2>/dev/null | tail -35');break;}
 $elfSymbols=[];$faultBytes=null;
 // These four offsets belong only to the diagnosed beta. Never symbolize old dumps against a new binary.
 if($binary&&$binary['sha256']==='c51c4a7962fee5efabbaff2789c247cf64897d5d69e22f30bc98b53d1cc34594'){
  $fp=fopen($root.'/'.$binary['path'],'rb');$size=filesize($root.'/'.$binary['path']);
  $read=static function($off,$len)use($fp,$size){if($off<0||$len<0||$off+$len>$size||$len>33554432)throw new RuntimeException('ELF bounds');fseek($fp,$off);return fread($fp,$len);};
  $u64=static fn($data,$off)=>unpack('P',substr($data,$off,8))[1];$u32=static fn($data,$off)=>unpack('V',substr($data,$off,4))[1];
  $header=$read(0,64);
  if(substr($header,0,6)==="\x7fELF\x02\x01"){
   $sectionOffset=$u64($header,40);$sectionSize=unpack('v',substr($header,58,2))[1];$sectionCount=unpack('v',substr($header,60,2))[1];
   if($sectionSize===64&&$sectionCount<4096){$sections=[];for($i=0;$i<$sectionCount;$i++)$sections[]=$read($sectionOffset+$i*64,64);
    foreach($sections as $section){$address=$u64($section,16);$length=$u64($section,32);if($address<=0x342d10&&$address+$length>0x342d80)$faultBytes=['address'=>'342d10','hex'=>bin2hex($read($u64($section,24)+0x342d10-$address,0x70))];}
    foreach($sections as $section)if(in_array($u32($section,4),[2,11],true)&&$u64($section,56)===24){
     $data=$read($u64($section,24),$u64($section,32));$stringSection=$sections[$u32($section,40)];$strings=$read($u64($stringSection,24),$u64($stringSection,32));
     for($i=0;$i+24<=strlen($data);$i+=24){if((ord($data[$i+4])&15)!==2)continue;$start=$u64($data,$i+8);$length=$u64($data,$i+16);
      foreach([0x342d4f,0x345cc0,0x34883f,0x3ede14] as $address)if($start<=$address&&$start+$length>$address){$nameOffset=$u32($data,$i);$end=strpos($strings,"\0",$nameOffset);$elfSymbols[dechex($address)]=['symbol'=>substr($strings,$nameOffset,min(800,$end-$nameOffset)),'start'=>dechex($start),'offset'=>dechex($address-$start),'size'=>$length];}
     }
    }
   }
  }fclose($fp);
 }
 $result=['at'=>gmdate('c'),'server'=>'11110003','readOnly'=>true,'kernel'=>$clean($kernel),'coredumps'=>$clean($cores),'paths'=>array_slice($files,0,5),'crashContents'=>$crashContents,'binary'=>$binary,'elfSymbols'=>$elfSymbols,'faultBytes'=>$faultBytes,'symbols'=>$symbols,'disassembly'=>$disassembly,'containerDumpFiles'=>$clean($tmp),'consoleEnds'=>array_map(fn($x)=>['file'=>$x['file'],'lines'=>array_slice($x['lines'],-20)],$consoleEnds)];
 ob_end_clean();echo json_encode($result,JSON_UNESCAPED_SLASHES|JSON_INVALID_UTF8_SUBSTITUTE|JSON_THROW_ON_ERROR),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Crash read-only inspection failed','type'=>get_class($e),'line'=>$e->getLine()]);exit(1);}
