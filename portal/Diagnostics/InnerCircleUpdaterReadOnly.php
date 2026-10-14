<?php
// Only read fixed launcher source files in the already approved target container.
ini_set('display_errors','0'); ini_set('log_errors','0');
$uuid='11110003-0000-4000-8000-000000000003';
$files=[];
foreach(['/scripts/updaters/swiftlys2.sh','/utils/updater_common.sh'] as $path){
 $raw=(string)shell_exec('timeout 5 docker exec '.$uuid.' head -c 14000 '.escapeshellarg($path).' 2>/dev/null');
 $files[$path]=$raw;
}
echo json_encode(['server'=>'11110003','readOnly'=>true,'checkedAt'=>date('c'),'sources'=>$files],JSON_UNESCAPED_SLASHES|JSON_INVALID_UTF8_SUBSTITUTE),"\n";
