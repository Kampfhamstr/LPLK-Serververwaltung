<?php
require_once dirname(__DIR__).'/src/Services/ServerJoinService.php';
use App\Services\ServerJoinService;
$checks=0;
$check=static function(bool $ok,string $label)use(&$checks){if(!$ok)throw new RuntimeException($label);$checks++;};
$details=static fn(array $rows)=>['relationships'=>['allocations'=>['data'=>array_map(static fn($a)=>['attributes'=>$a],$rows)]]];
$a=['ip'=>'203.0.113.10','port'=>27020,'is_default'=>true];
$result=ServerJoinService::fromDetails($details([$a,['ip'=>'203.0.113.10','port'=>27019,'is_default'=>false]]));
$check($result===['address'=>'203.0.113.10:27020','url'=>'steam://connect/203.0.113.10:27020'],'Default game allocation, not first/SFTP port');
$check(ServerJoinService::fromDetails($details([['ip'=>'2606:4700::1111','port'=>'27015','is_default'=>true]]))['address']==='[2606:4700::1111]:27015','IPv6 bracketed');
foreach([[],[$a,$a],[array_replace($a,['is_default'=>false])],[array_replace($a,['port'=>0])],[array_replace($a,['port'=>65536])],[array_replace($a,['port'=>'22;quit'])],[array_replace($a,['ip'=>'0.0.0.0'])],[array_replace($a,['ip'=>'127.0.0.1'])],[array_replace($a,['ip'=>'10.1.1.1'])],[array_replace($a,['ip'=>'203.0.113.10/secret'])],[array_replace($a,['ip'=>'example.org\r\nLocation: javascript:alert(1)'])]] as $rows){
    try{ServerJoinService::fromDetails($details($rows));$check(false,'Invalid allocation accepted');}catch(RuntimeException $e){if($e->getMessage()==='Invalid allocation accepted')throw $e;$checks++;}
}
$a['ip_alias']='javascript:alert(1)';$a['password']='must-not-escape';
$check(ServerJoinService::fromDetails($details([$a]))===$result,'Aliases and secrets never enter join link');
echo "Join allocation checks: $checks passed\n";
