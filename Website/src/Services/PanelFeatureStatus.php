<?php
namespace App\Services;
use App\Core\Auth;

final class PanelFeatureStatus
{
    public static function classify(string $feature, array $d, int $now): string
    {
        $at=is_string($d['updatedAt']??null)?strtotime($d['updatedAt']):false;
        if(!$at || $at>$now+60 || $now-$at>120) return 'stale';
        if($feature==='statistics') {
            if(!in_array($d['version']??null,['0.2.0','0.2.1'],true) || !is_bool($d['Enabled']??null)) return 'unknown';
            if(!$d['Enabled']) return 'disabled';
            if(($d['saveError']??null)!==null || ($d['reloadError']??null)!==null) return 'unknown';
            return 'active';
        }
        if(($d['version']??null)!=='0.9.0' || ($d['profile']??null)!=='standard-test-11110004') return 'unknown';
        if(($d['effective']??null)===false || ($d['features']['bank']??null)===false || ($d['web']['ExportEnabled']??null)===false) return 'disabled';
        return ($d['effective']??null)===true && ($d['features']['bank']??null)===true && ($d['web']['ExportEnabled']??null)===true && ($d['healthy']??null)===true && ($d['reloadError']??null)===null && ($d['web']['error']??null)===null ? 'active' : 'unknown';
    }

    public static function read(int $serverId): array
    {
        if($serverId<1 || !Auth::can('players.view') || !Auth::canServer($serverId,'view')) throw new \RuntimeException('Keine Berechtigung.');
        $server=LiveServerService::server($serverId);
        $result=['checkedAt'=>gmdate('c'),'features'=>['statistics'=>'unknown','economy'=>'unknown']];
        // This UI change does not expand the explicitly limited rollout.
        if(($server['server_identifier']??'')!=='11110004') { $result['features']=['statistics'=>'not_released','economy'=>'not_released']; return $result; }
        $dir=dirname(__DIR__,2).'/storage/cache/panel-features';
        if(!is_dir($dir) && !@mkdir($dir,0770,true) && !is_dir($dir)) return $result;
        $path=$dir.'/'.$serverId.'.json';
        $cached=is_file($path)?json_decode((string)@file_get_contents($path),true):null;
        if(is_array($cached) && (time()-(int)@filemtime($path))<60) return $cached;
        $lock=@fopen($path.'.lock','c');
        if(!$lock || !flock($lock,LOCK_EX|LOCK_NB)) { if($lock)fclose($lock); return $result; }
        try {
            $connector=ConnectorFactory::make($server);
            foreach(['statistics'=>'LPLK_Statistics','economy'=>'LPLK_Economy'] as $key=>$addon) {
                try {
                    $file='game/csgo/addons/swiftlys2/data/'.$addon.'/status.json';
                    if(!$connector->exists($file)) { $result['features'][$key]='not_installed'; continue; }
                    $raw=$connector->read($file);
                    if(strlen($raw)>32768) continue;
                    $data=json_decode($raw,true,32,JSON_THROW_ON_ERROR);
                    if(is_array($data)) $result['features'][$key]=self::classify($key,$data,time());
                } catch(\Throwable) { /* Unavailable is not disabled. */ }
            }
            $tmp=$path.'.tmp';
            if(file_put_contents($tmp,json_encode($result,JSON_THROW_ON_ERROR),LOCK_EX)!==false) rename($tmp,$path);
        } catch(\Throwable) { /* The page remains usable while a connector is unavailable. */ }
        finally { flock($lock,LOCK_UN); fclose($lock); }
        return $result;
    }
}
