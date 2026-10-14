<?php
// Read-only allowlisted metadata. Never output environment secrets or account credentials.
ini_set('display_errors', '0'); ini_set('log_errors', '0'); ob_start();
try {
    require getcwd() . '/vendor/autoload.php';
    $app = require getcwd() . '/bootstrap/app.php';
    $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();
    $db = $app->make('db')->connection();
    $ref = $db->table('servers')->where('uuid', '11110003-0000-4000-8000-000000000003')->first([
        'id','uuid','uuidShort','name','owner_id','node_id','egg_id','nest_id','allocation_id','image','memory','swap','disk','io','cpu','threads','status'
    ]);
    if (!$ref) throw new RuntimeException('Reference missing');
    $egg = $db->table('eggs')->where('id', $ref->egg_id)->first(['id','name','startup','docker_images','script_container','script_entry']);
    $node = $db->table('nodes')->where('id', $ref->node_id)->first(['id','name','fqdn','memory','memory_overallocate','disk','disk_overallocate','daemonBase']);
    $safeValues = ['SRCDS_APPID','STEAM_APP_ID','SRCDS_MAP','SRCDS_MAXPLAYERS','MAX_PLAYERS','GAME_TYPE','GAME_MODE','SRCDS_GAME_TYPE','SRCDS_GAME_MODE','TICKRATE','AUTO_UPDATE','VALIDATE','UPDATE_ON_START','WORKSHOP_COLLECTION_ID','WORKSHOP_START_MAP'];
    $variables = [];
    foreach ($db->table('egg_variables')->where('egg_id', $ref->egg_id)->orderBy('id')->get(['id','name','env_variable','default_value','rules']) as $var) {
        $value = $db->table('server_variables')->where('server_id', $ref->id)->where('variable_id',$var->id)->value('variable_value');
        $variables[] = ['id'=>$var->id,'name'=>$var->name,'key'=>$var->env_variable,'rules'=>$var->rules,'hasValue'=>strlen((string)$value)>0,
            'value'=>in_array($var->env_variable,$safeValues,true)?$value:null,
            'default'=>in_array($var->env_variable,$safeValues,true)?$var->default_value:null];
    }
    $startupCollection=null;
    foreach ($db->table('egg_variables')->where('egg_id',$ref->egg_id)->get(['id','env_variable']) as $v) {
        if (!in_array($v->env_variable,['CUSTOM_PARAMS','WORKSHOP_COLLECTION_ID','WORKSHOP_COLLECTION'],true)) continue;
        $text=(string)$db->table('server_variables')->where('server_id',$ref->id)->where('variable_id',$v->id)->value('variable_value');
        if($v->env_variable==='CUSTOM_PARAMS' && preg_match('/\+host_workshop_collection\s+["\x27]?([1-9][0-9]{0,19})/',$text,$m)) $startupCollection=$m[1];
        elseif($v->env_variable!=='CUSTOM_PARAMS' && preg_match('/^[1-9][0-9]{0,19}$/D',$text)) $startupCollection=$text;
    }
    $deps = json_decode(file_get_contents('/var/lib/pterodactyl/volumes/'.$ref->uuid.'/game/csgo/addons/swiftlys2/bin/managed/SwiftlyS2.CS2.deps.json'),true);
    $report = ['startupCollection'=>$startupCollection,'panelVersion'=>config('app.version'),'installedSwiftly'=>array_values(array_filter(array_keys($deps['libraries']??[]),fn($x)=>str_starts_with($x,'SwiftlyS2.CS2/'))),'reference'=>$ref,'egg'=>$egg,'node'=>$node,'variables'=>$variables,
        'freeAllocations'=>$db->table('allocations')->where('node_id',$ref->node_id)->whereNull('server_id')->orderBy('port')->get(['id','ip','port']),
        'servers'=>$db->table('servers')->where('node_id',$ref->node_id)->get(['id','uuidShort','name','allocation_id','memory','disk','status','external_id']),
        'creationServiceParameters'=>array_map(fn($p)=>['name'=>$p->getName(),'type'=>(string)$p->getType()], (new ReflectionMethod(Pterodactyl\Services\Servers\ServerCreationService::class,'handle'))->getParameters())];
    ob_end_clean(); echo json_encode($report,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),"\n";
} catch (Throwable $e) {
    if(ob_get_level()) ob_end_clean(); echo json_encode(['error'=>'Testserver-Metadaten konnten nicht gelesen werden.','type'=>get_class($e)]),"\n"; exit(1);
}

