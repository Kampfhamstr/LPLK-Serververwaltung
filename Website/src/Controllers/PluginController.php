<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Security;
use App\Core\View;
use App\Services\GitHubReleaseService;
use App\Services\LiveServerService;
use App\Services\PluginUpdateService;

final class PluginController
{
    public function index(): void
    {
        Auth::requirePermission('plugins.view');
        $servers=[];
        foreach(PluginUpdateService::visibleServers() as $server){
            if(!Auth::canServer((int)$server['id'],'view')) continue;
            try{$inv=PluginUpdateService::inventoryForServer((int)$server['id']);}
            catch(\Throwable $e){$inv=['bridge_online'=>false,'bridge_version'=>null,'reason'=>$e->getMessage(),'plugins'=>[]];}
            $server['inventory']=$inv; $servers[]=$server;
        }

        $catalog=[]; $updateCount=0;
        foreach($servers as $server){
            foreach((array)($server['inventory']['plugins']??[]) as $p){
                if(!is_array($p))continue;
                $pid=(string)($p['id']??''); if($pid==='')continue;
                if(!empty($p['update_available']))$updateCount++;
                if(!isset($catalog[$pid])){
                    $source=$p['update_source']??null; $suggested=$p['suggested_repository']??null;
                    $self=$pid==='lplk.serververwaltung.bridge';
                    $repoState=$self?'manual':($source?'configured':($suggested?'detected':'missing'));
                    $catalog[$pid]=[
                        'id'=>$pid,'name'=>(string)($p['name']??$pid),'author'=>(string)($p['author']??''),
                        'source'=>$source,'suggested_repository'=>$suggested,'repository_url'=>(string)($p['github_repository_url']??''),
                        'repo_state'=>$repoState,'installations'=>[],'versions'=>[],'update_available'=>false,
                    ];
                }
                $catalog[$pid]['installations'][]=[
                    'server_id'=>(int)$server['id'],'server_name'=>(string)$server['name'],
                    'version'=>(string)($p['version']??''),'status'=>(string)($p['status']??'Indeterminate'),
                    'update_available'=>!empty($p['update_available']),
                ];
                $v=(string)($p['version']??''); if($v!=='')$catalog[$pid]['versions'][$v]=true;
                if(!empty($p['update_available']))$catalog[$pid]['update_available']=true;
            }
        }
        foreach($catalog as &$item){$item['version_conflict']=count($item['versions'])>1;$item['versions']=array_keys($item['versions']);}
        unset($item);
        uasort($catalog,static fn(array $a,array $b):int=>strnatcasecmp($a['name'],$b['name']));
        $repoCounts=['configured'=>0,'detected'=>0,'missing'=>0,'manual'=>0];
        foreach($catalog as $item)$repoCounts[$item['repo_state']]++;

        View::render('plugins',[
            'title'=>'Plugins & Updates','servers'=>$servers,'catalog'=>array_values($catalog),
            'repoCounts'=>$repoCounts,'sources'=>PluginUpdateService::sourcesByPluginId(),
            'recentRuns'=>PluginUpdateService::recentRuns(30),'updateCount'=>$updateCount,
            'githubTokenConfigured'=>GitHubReleaseService::tokenConfigured(),
        ]);
    }

    public function saveSource(string $pluginId):void{Auth::requirePermission('plugins.repository.manage');Security::verifyCsrf();try{PluginUpdateService::saveSource($pluginId,$_POST);PluginUpdateService::checkSource($pluginId);View::flash('success','GitHub-Updatequelle wurde gespeichert und geprüft.');}catch(\Throwable $e){View::flash('error','Updatequelle konnte nicht gespeichert/geprüft werden: '.$e->getMessage());}header('Location: /plugins#plugin-'.rawurlencode($pluginId));}
    public function deleteSource(string $pluginId):void{Auth::requirePermission('plugins.repository.manage');Security::verifyCsrf();try{PluginUpdateService::deleteSource($pluginId);View::flash('success','Updatequelle wurde entfernt.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}header('Location: /plugins');}
    public function checkSource(string $pluginId):void{Auth::requirePermission('plugins.repository.manage');Security::verifyCsrf();try{$l=PluginUpdateService::checkSource($pluginId);View::flash('success','GitHub geprüft: '.$l['tag'].' · Asset '.$l['asset_name'].'.');}catch(\Throwable $e){View::flash('error','GitHub-Prüfung fehlgeschlagen: '.$e->getMessage());}header('Location: /plugins#plugin-'.rawurlencode($pluginId));}
    public function checkAll():void{Auth::requirePermission('plugins.repository.manage');Security::verifyCsrf();$r=PluginUpdateService::checkAllSources();View::flash($r['errors']?'warning':'success',$r['ok'].' von '.$r['checked'].' GitHub-Quellen erfolgreich geprüft'.($r['errors']?' · '.count($r['errors']).' Fehler.':'.'));header('Location: /plugins');}
    public function install(string $serverId,string $pluginId):void{Auth::requirePermission('plugins.update');Security::verifyCsrf();$sid=(int)$serverId;Auth::requireServer($sid,'live');try{$cid=PluginUpdateService::queueUpdate($sid,$pluginId,'manual',(int)Auth::user()['id']);View::flash('success','Plugin-Update wurde an die LiveBridge übermittelt. Command: '.$cid);}catch(\Throwable $e){View::flash('error','Plugin-Update konnte nicht gestartet werden: '.$e->getMessage());}header('Location: /plugins#plugin-'.rawurlencode($pluginId));}
    public function rollback(string $serverId,string $pluginId):void{Auth::requirePermission('plugins.rollback');Security::verifyCsrf();$sid=(int)$serverId;Auth::requireServer($sid,'live');try{$cid=PluginUpdateService::queueRollback($sid,$pluginId,trim((string)($_POST['backup_id']??'')),(int)Auth::user()['id']);View::flash('success','Rollback wurde an die LiveBridge übermittelt. Command: '.$cid);}catch(\Throwable $e){View::flash('error','Rollback konnte nicht gestartet werden: '.$e->getMessage());}header('Location: /plugins#plugin-'.rawurlencode($pluginId));}
    public function rescan(string $serverId):void{Auth::requirePermission('plugins.rescan');Security::verifyCsrf();$sid=(int)$serverId;Auth::requireServer($sid,'live');try{LiveServerService::queue($sid,'plugin_rescan');View::flash('success','Plugin-Inventar wird neu eingelesen.');}catch(\Throwable $e){View::flash('error',$e->getMessage());}header('Location: /plugins');}
}
