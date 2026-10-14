<?php
// Evaluate only the pure diagnostic formatter, never the server maintenance code.
$source=file_get_contents(__DIR__.'/../portal/Diagnostics/ProductionSoundsDeploy.php');
if(!preg_match('/function soundDiagnosticLogTail\(string \$log\):string \{.*?\n\}/s',$source,$m))throw new RuntimeException('Formatter missing');
eval($m[0]);
$count=0;
$check=function(bool $ok,string $name)use(&$count){if(!$ok)throw new RuntimeException($name);$count++;};
$split="ä".str_repeat('A',4499);
try{json_encode(substr($split,-4500),JSON_THROW_ON_ERROR);$broken=false;}catch(JsonException $e){$broken=$e->getCode()===JSON_ERROR_UTF8;}
$check($broken,'Reproduce previous byte-cut failure');
foreach(['',"Kurzes gültiges Protokoll",str_repeat('A',5000),$split,"€".str_repeat('A',4498),"🚀".str_repeat('A',4497),"invalid \xFF end",str_repeat('ä🚀',1000)] as $log){
 $tail=soundDiagnosticLogTail($log);
 $check(mb_check_encoding($tail,'UTF-8'),'Valid UTF-8 output');
 $check(strlen($tail)<=4503,'Bounded tail with at most one leading complete character');
 $check(json_decode(json_encode(['log'=>$tail],JSON_THROW_ON_ERROR),true,512,JSON_THROW_ON_ERROR)['log']===$tail,'Strict report encoding succeeds');
}
$check(soundDiagnosticLogTail('ä')==='ä','Short Unicode stays exact');
$check(str_contains(soundDiagnosticLogTail("bad \xFF byte"),"\u{FFFD}"),'Invalid diagnostic bytes visibly substituted');
echo 'PASS: '.$count." sound diagnostic log checks\n";
