<?php
// Fixed-target, bounded read-only attribution of power actions. Never return credentials or request payloads.
ini_set('display_errors','0'); ini_set('log_errors','0'); ob_start();
try {
 require getcwd().'/vendor/autoload.php'; $app=require getcwd().'/bootstrap/app.php';
 $app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap(); $db=$app->make('db'); $schema=$db->getSchemaBuilder();
 $uuid='11110003-0000-4000-8000-000000000003';
 $s=$db->table('servers')->where('uuid',$uuid)->first(['id','allocation_id']);
 if(!$s || (int)$db->table('allocations')->where('id',$s->allocation_id)->value('port')!==27019)throw new RuntimeException('Target mismatch');
 $report=['server'=>'11110003','readOnly'=>true,'checkedAt'=>gmdate('c'),'schema'=>[],'activity'=>[],'schedules'=>[]];
 foreach(['activity_logs','activity_log_subjects','schedules','tasks'] as $table)if($schema->hasTable($table))$report['schema'][$table]=$schema->getColumnListing($table);
 if(isset($report['schema']['activity_logs'],$report['schema']['activity_log_subjects'])) {
  $columns=array_values(array_intersect(['id','event','timestamp','actor_id','actor_type','api_key_id','ip','properties'],$report['schema']['activity_logs']));
  $type=(new Pterodactyl\Models\Server)->getMorphClass();$report['serverSubjectType']=$type;
  $ids=$db->table('activity_log_subjects')->where('subject_id',$s->id)->whereIn('subject_type',[$type,'Pterodactyl\\Models\\Server'])->select('activity_log_id');
  foreach($db->table('activity_logs')->whereIn('id',$ids)->where('timestamp','>=',date('Y-m-d H:i:s',time()-10800))->where(function($q){$q->where('event','like','server:power%')->orWhere('event','like','server:schedule%');})->orderByDesc('timestamp')->limit(40)->get($columns) as $row) {
   $v=(array)$row; $props=json_decode($v['properties']??'{}',true); unset($v['properties']);
   $ip=$v['ip']??''; unset($v['ip']);
   $v['origin']=in_array($ip,['127.0.0.1','::1'],true)?'loopback':($ip===''?'unknown':(filter_var($ip,FILTER_VALIDATE_IP,FILTER_FLAG_NO_PRIV_RANGE|FILTER_FLAG_NO_RES_RANGE)?'public':'private'));
   $v['originFingerprint']=$ip===''?null:substr(hash('sha256',$uuid.'|'.$ip),0,12);
   foreach(['signal','action'] as $key)if(isset($props[$key])&&in_array($props[$key],['start','stop','restart','kill'],true))$v[$key]=$props[$key];
   if(isset($props['schedule_id'])&&is_numeric($props['schedule_id']))$v['schedule_id']=(int)$props['schedule_id'];
   $report['activity'][]=$v;
  }
 }
 if(isset($report['schema']['schedules'])) {
  $cols=array_values(array_intersect(['id','is_active','is_processing','last_run_at','next_run_at','cron_minute','cron_hour','cron_day_of_month','cron_month','cron_day_of_week'],$report['schema']['schedules']));
  foreach($db->table('schedules')->where('server_id',$s->id)->limit(30)->get($cols) as $row) {
   $v=(array)$row;$v['powerTasks']=[];
   if(isset($report['schema']['tasks']))foreach($db->table('tasks')->where('schedule_id',$row->id)->where('action','power')->limit(20)->get(['id','sequence_id','time_offset','payload']) as $task) {
    $t=(array)$task;if(!in_array($t['payload'],['start','stop','restart','kill'],true))$t['payload']='[unknown power action]';$v['powerTasks'][]=$t;
   }
   $report['schedules'][]=$v;
  }
 }
 $keyIds=array_values(array_unique(array_filter(array_column($report['activity'],'api_key_id'))));
 $report['apiKeys']=[];
 if($keyIds&&$schema->hasTable('api_keys'))foreach($db->table('api_keys')->whereIn('id',$keyIds)->limit(10)->get(array_values(array_intersect(['id','user_id','key_type','created_at','last_used_at'],$schema->getColumnListing('api_keys')))) as $row)$report['apiKeys'][]=(array)$row;
 $web='/var/www/serververwaltung';
 spl_autoload_register(static function($class)use($web){if(str_starts_with($class,'App\\')){$p=$web.'/src/'.str_replace('\\','/',substr($class,4)).'.php';if(is_file($p))require $p;}});
 App\Core\Config::load($web.'/config/app.php');$dbPath=(string)App\Core\Config::get('database.path');
 if(!is_file($dbPath)||is_link($dbPath)||!str_starts_with((string)realpath($dbPath),$web.'/storage/'))throw new RuntimeException('Unsafe database path');$dbPath=realpath($dbPath);
 $pdo=new PDO('sqlite:file:'.$dbPath.'?mode=ro',null,null,[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION]);$pdo->exec('PRAGMA query_only=ON');
 $q=$pdo->prepare('SELECT id FROM game_servers WHERE server_identifier IN (?,?)');$q->execute(['11110003',$uuid]);$webId=$q->fetchColumn();
 $report['webAudit']=[];
 if($webId!==false) {
  $q=$pdo->prepare("SELECT a.id,a.user_id,u.name AS account_name,a.action,a.target_type,a.target_id,a.created_at,a.details_json FROM audit_log a LEFT JOIN users u ON u.id=a.user_id WHERE a.target_id=? AND a.target_type IN ('server','game_server') AND a.action LIKE 'server.power.%' AND julianday(a.created_at) >= julianday(?) ORDER BY a.id DESC LIMIT 30");
  $q->execute([(string)$webId,gmdate('c',time()-10800)]);
  foreach($q->fetchAll(PDO::FETCH_ASSOC) as $r){$props=json_decode($r['details_json']??'{}',true);unset($r['details_json']);foreach(['signal','action'] as $key)if(isset($props[$key])&&in_array($props[$key],['start','stop','restart','kill'],true))$r[$key]=$props[$key];$report['webAudit'][]=$r;}
 }
 $json=json_encode($report,JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);if(strlen($json)>24000)throw new LengthException('Report budget');
 ob_end_clean();echo $json,"\n";
}catch(Throwable $e){if(ob_get_level())ob_end_clean();echo json_encode(['error'=>'Power audit failed','type'=>get_class($e),'line'=>$e->getLine()]),"\n";exit(1);}
