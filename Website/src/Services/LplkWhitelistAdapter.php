<?php
namespace App\Services;

final class LplkWhitelistAdapter
{
    public const ID='LPLK_Whitelist';
    public const DIRECTORY='game/csgo/addons/swiftlys2/plugins/LPLK_Whitelist';
    public const LIST=self::DIRECTORY.'/whitelist.txt';
    public const STATUS='game/csgo/addons/swiftlys2/data/LPLK_Whitelist/status.json';
    public const ADD='sw_lplk_wl_add';
    public const REMOVE='sw_lplk_wl_remove';

    public static function parse(string $text):array
    {
        if(strlen($text)>4*1024*1024)throw new \RuntimeException('Whitelist-Datei ist zu groß.');
        $ids=[];
        foreach(explode("\n",$text) as $line){
            $id=trim($line);if($id===''||str_starts_with($id,'#')||str_starts_with($id,'//'))continue;
            if(!self::validId($id))throw new \RuntimeException('Whitelist enthält eine ungültige SteamID64; keine Änderung ausgeführt.');
            $ids[$id]=true;
        }
        if(count($ids)>100000)throw new \RuntimeException('Whitelist enthält zu viele Einträge.');
        return array_map('strval',array_keys($ids));
    }

    public static function validId(string $id):bool
    {
        return preg_match('/^[0-9]{17}$/D',$id)===1 && strcmp($id,'76561197960265728')>=0 && strcmp($id,'76561202255233023')<=0;
    }

    public static function metadata(ServerConnectorInterface $connector,array $server,array $bridge,array $config,int $count):array
    {
        $result=['lplk'=>true,'editable'=>false,'enabled'=>null,'runtime_updated_at'=>null,'state_error'=>null];
        try {
            if(($server['server_identifier']??'')!=='11110004')throw new \RuntimeException('LPLK-Whitelist ist zunächst nur für den Testserver freigegeben.');
            if(empty($bridge['bridge_online'])||!is_numeric($bridge['status_age_seconds']??null)||$bridge['status_age_seconds']<0||$bridge['status_age_seconds']>15)
                throw new \RuntimeException('Keine aktuelle Verbindung zum Gameserver; Änderungen sind gesperrt.');
            if(empty($config['_loaded']))throw new \RuntimeException('Whitelist-Konfiguration konnte nicht gelesen werden.');
            $raw=$connector->read(self::STATUS);if(strlen($raw)>32768)throw new \RuntimeException('Ungültiger Pluginstatus.');
            $runtime=json_decode($raw,true,16,JSON_THROW_ON_ERROR);
            foreach(['Enabled','AdminBypass'] as $key)if(!is_bool($runtime[$key]??null)||!is_bool($config[$key]??null)||$runtime[$key]!==$config[$key])throw new \RuntimeException('Konfiguration und aktiver Pluginzustand stimmen noch nicht überein.');
            if(!in_array($runtime['Mode']??null,[1,2],true)||($config['Mode']??null)!==$runtime['Mode'])throw new \RuntimeException('Listenmodus ist noch nicht bestätigt.');
            $result['enabled']=$runtime['Enabled'];
            if(!array_key_exists('reloadError',$runtime)||$runtime['reloadError']!==null)throw new \RuntimeException('Das Addon meldet einen Ladefehler; Änderungen sind gesperrt.');
            if(($runtime['entries']??null)!==$count)throw new \RuntimeException('Listendatei und Pluginzustand werden noch abgeglichen.');
            $at=strtotime((string)($runtime['updatedAt']??''));if(!$at||$at>time()+60)throw new \RuntimeException('Ungültiger Pluginzeitstempel.');
            // The addon writes on changes, not as a heartbeat. Bridge freshness proves liveness.
            $result['runtime_updated_at']=$at;$result['editable']=true;
        }catch(\Throwable $e){$result['state_error']=$e instanceof \RuntimeException?$e->getMessage():'Pluginstatus derzeit nicht lesbar.';}
        return $result;
    }
}
