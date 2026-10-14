<?php
namespace App\Services;

final class ServerJoinService
{
    // Only the default game allocation is eligible; never the connector/SFTP port.
    public static function fromDetails(array $details): array
    {
        $allocations = $details['relationships']['allocations']['data'] ?? [];
        $defaults = array_values(array_filter($allocations, static fn($a) => ($a['attributes']['is_default'] ?? false) === true));
        if (count($defaults) !== 1) throw new \RuntimeException('Keine eindeutige Spieladresse vorhanden.');
        $a = $defaults[0]['attributes'];
        $ip = (string)($a['ip'] ?? '');
        $port = filter_var($a['port'] ?? null, FILTER_VALIDATE_INT, ['options'=>['min_range'=>1,'max_range'=>65535]]);
        if (!filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE) || $port === false)
            throw new \RuntimeException('Keine öffentliche Spieladresse vorhanden.');
        $address = (str_contains($ip, ':') ? '['.$ip.']' : $ip).':'.$port;
        return ['address'=>$address, 'url'=>'steam://connect/'.$address];
    }

    public static function resolve(array $server): array
    {
        $connector = ConnectorFactory::make($server);
        if (!$connector instanceof PterodactylConnector) throw new \RuntimeException('Spieladresse nicht verfügbar.');
        // Fetch only on demand, not on every dashboard poll. No credentials in cache or response.
        $dir = dirname(__DIR__, 2).'/storage/cache/join';
        $key = hash('sha256', (string)$server['base_url'].'|'.(string)$server['server_identifier']);
        $path = $dir.'/server-'.(int)$server['id'].'.json';
        $old = is_file($path) ? json_decode((string)file_get_contents($path), true) : null;
        if (is_array($old) && ($old['key'] ?? '') === $key && ($old['expires'] ?? 0) > time())
            return self::fromDetails($old['details'] ?? []);
        $details = $connector->serverDetails();
        $result = self::fromDetails($details);
        // Whitelist only the selected allocation, never write API details containing secrets.
        $selected = array_values(array_filter($details['relationships']['allocations']['data'], static fn($a) => ($a['attributes']['is_default'] ?? false) === true))[0]['attributes'];
        $safe = ['relationships'=>['allocations'=>['data'=>[['attributes'=>['ip'=>$selected['ip'],'port'=>$selected['port'],'is_default'=>true]]]]]];
        if (is_dir($dir) || @mkdir($dir, 0770, true)) {
            $tmp = $path.'.'.bin2hex(random_bytes(4)).'.tmp';
            if (@file_put_contents($tmp, json_encode(['key'=>$key,'expires'=>time()+60,'details'=>$safe]), LOCK_EX) !== false) {
                @chmod($tmp,0660); if (!@rename($tmp,$path)) @unlink($tmp);
            }
        }
        return $result;
    }
}
