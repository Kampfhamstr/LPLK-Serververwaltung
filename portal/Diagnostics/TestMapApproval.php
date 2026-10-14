<?php
// User approved exactly the 90 reviewed proposals; no mode changes and no production writes.
ini_set('display_errors','0');ini_set('log_errors','0');ob_start();
try {
    $request=json_decode(base64_decode('__LPLK_REVIEW_BASE64__',true),true,512,JSON_THROW_ON_ERROR);
    $review=$request['proposals']??[];$apply=($request['apply']??false)===true;
    if(count($review)!==90||count(array_unique(array_column($review,'id')))!==90)throw new DomainException('Exactly 90 distinct reviewed maps required');
    require getcwd().'/vendor/autoload.php';$app=require getcwd().'/bootstrap/app.php';$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $server=Pterodactyl\Models\Server::query()->where('external_id','lplk-inner-circle-test-20261011')->firstOrFail();
    $allocation=$app->make('db')->table('allocations')->where('id',$server->allocation_id)->first();
    if($server->uuid!=='11110004-0000-4000-8000-000000000004'||$server->node_id!==2||$allocation->ip!=='203.0.113.10'||(int)$allocation->port!==27020)throw new DomainException('Test identity mismatch');
    $state=trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"));
    if($apply&&$state!=='exited')throw new DomainException('Stop isolated test server before applying');
    $root='/var/lib/pterodactyl/volumes/'.$server->uuid;
    $read=static function(string $path):string {
        if(!is_file($path)||is_link($path)||realpath($path)!==$path||filesize($path)>4*1024*1024)throw new DomainException('File path or size mismatch');
        return file_get_contents($path);
    };
    $configPath=$root.'/game/csgo/addons/swiftlys2/configs/plugins/LPLK_Maps/config.jsonc';
    $config=json_decode($read($configPath),true,512,JSON_THROW_ON_ERROR)['LPLK_Maps']??[];
    if(($config['MeasurementOnly']??null)!==true||($config['CollectionId']??'')!=='3500873770')throw new DomainException('Measurement-only collection required');
    $excluded=$config['ExcludedWorkshopIds']??[];
    if(!in_array('3797047889',$excluded,true))throw new DomainException('Sound exclusion must remain');
    $path=$root.'/game/csgo/addons/swiftlys2/data/LPLK_Maps/maps.json';$original=$read($path);
    $maps=json_decode($original,true,512,JSON_THROW_ON_ERROR);$lookup=[];
    foreach($maps as $index=>$map){$id=$map['Id']??'';if($id===''||isset($lookup[$id]))throw new DomainException('Duplicate or missing map ID');$lookup[$id]=$index;}
    $withheld=['3354923062','3733075447'];
    foreach($withheld as $id){$m=$maps[$lookup[$id]??throw new DomainException('Withheld map missing')];if(!empty($m['ApprovedLimitSignature'])||!empty($m['LimitsApprovedAt']))throw new DomainException('Withheld map unexpectedly approved');}
    $beforeHash=hash('sha256',$original);$new=0;$already=0;$now=gmdate('Y-m-d\TH:i:s\Z');
    foreach($review as $item){
        $id=$item['id'];if(in_array($id,$withheld,true)||in_array($id,$excluded,true)||!ctype_digit($id)||!isset($lookup[$id]))throw new DomainException('Requested map outside approved scope');
        $i=$lookup[$id];$m=$maps[$i];$p=$m['LimitProposal']??null;
        if(($m['InPool']??false)!==true||($m['IsMap']??false)!==true||($m['Enabled']??false)!==true||($m['Removed']??true)!==false||($m['MeasurementVersion']??0)!==2||empty($m['MeasuredAt'])||($m['ProbeError']??null)!==null||!is_array($p))throw new DomainException('Map eligibility or measurement changed: '.$id);
        $t=$m['TSpawns']??0;$ct=$m['CtSpawns']??0;$bomb=$m['BombSites']??-1;$hostages=$m['Hostages']??-1;
        if($t<1||$ct<1||$bomb<0||$hostages<0)throw new DomainException('Unconfirmed measurement: '.$id);
        $reserve=2*min($t,$ct)>2?2:0;$min=$hostages>0||$bomb>=2?6:1;$max=min($m['ConfiguredMaxPlayers']??128,128,2*min($t,$ct)-$reserve);
        $signature=implode(':',['v2',$id,$m['WorkshopUpdated'],$t,$ct,$bomb,$hostages,$m['ConfiguredMaxPlayers']??'',$min,$max,$reserve]);
        if($max<$min||$signature!==$item['signature']||($p['Signature']??'')!==$signature||$p['MinPlayers']!==$min||$p['MaxPlayers']!==$max||$p['Reserve']!==$reserve)throw new DomainException('Reviewed proposal changed: '.$id);
        $wasApproved=($m['ApprovedLimitSignature']??null)===$signature&&$m['MinPlayers']===$min&&$m['MaxPlayers']===$max;
        if($wasApproved){$already++;continue;}
        if(!empty($m['ApprovedLimitSignature'])||$m['MinPlayers']!==$item['beforeMin']||$m['MaxPlayers']!==$item['beforeMax'])throw new DomainException('Active limits changed: '.$id);
        $maps[$i]['MinPlayers']=$min;$maps[$i]['MaxPlayers']=$max;$maps[$i]['ApprovedLimitSignature']=$signature;$maps[$i]['LimitsApprovedAt']=$now;$new++;
    }
    if($apply&&$new>0){
        $backup=$path.'.before-reviewed-approval';
        if(!file_exists($backup)){$f=fopen($backup,'x');if(!$f)throw new DomainException('Backup unavailable');try{if(fwrite($f,$original)!==strlen($original))throw new DomainException('Backup incomplete');}finally{fclose($f);}chmod($backup,0600);}
        else if(hash('sha256',$read($backup))!==$beforeHash)throw new DomainException('Existing backup differs; inspect before retry');
        if(hash('sha256',$read($path))!==$beforeHash)throw new DomainException('Map file changed during review');
        if(trim((string)shell_exec("docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004"))!=='exited')throw new DomainException('Test server restarted during approval');
        $tmp=$path.'.'.bin2hex(random_bytes(8)).'.tmp';
        try{
            $json=json_encode($maps,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR)."\n";
            if(file_put_contents($tmp,$json,LOCK_EX)!==strlen($json)||!chown($tmp,fileowner($path))||!chgrp($tmp,filegroup($path))||!chmod($tmp,fileperms($path)&0777))throw new DomainException('Approval staging failed');
            if(!rename($tmp,$path))throw new DomainException('Atomic approval failed');
        }finally{if(file_exists($tmp))unlink($tmp);}
    }
    ob_end_clean();echo json_encode(['testServer'=>'11110004','applied'=>$apply,'reviewed'=>90,'newApprovals'=>$new,'alreadyApproved'=>$already,'withheld'=>$withheld,'measurementOnly'=>true,'beforeSha256'=>$beforeHash,'afterSha256'=>hash_file('sha256',$path)]),"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>$e instanceof DomainException?$e->getMessage():'Test map approval failed','type'=>get_class($e)]),"\n";exit(1);}
