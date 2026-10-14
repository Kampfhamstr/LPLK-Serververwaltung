<?php
namespace App\Services;

use App\Core\Config;
use App\Core\Crypto;
use App\Core\Database;

final class FastLiveService
{
    private const ONLINE_SECONDS = 12;
    public const REMOTE_CONFIG_PATH = 'game/csgo/addons/swiftlys2/data/serververwaltung_bridge/fastlive.json';

    public static function enabled(int $serverId): bool
    {
        $s=Database::connection()->prepare('SELECT bridge_fastlive_enabled,bridge_fastlive_secret_enc FROM game_servers WHERE id=?');
        $s->execute([$serverId]); $r=$s->fetch();
        return $r && !empty($r['bridge_fastlive_enabled']) && trim((string)$r['bridge_fastlive_secret_enc'])!=='';
    }

    public static function isRecent(int $serverId, int $seconds=self::ONLINE_SECONDS): bool
    {
        if (!self::enabled($serverId)) return false;
        $path=self::cachePath($serverId); $mtime=@filemtime($path);
        return $mtime && (time()-$mtime)<=$seconds;
    }

    public static function readStatus(int $serverId, int $maxAge=60): ?array
    {
        $path=self::cachePath($serverId); if(!is_file($path)) return null;
        $mtime=@filemtime($path); if(!$mtime || time()-$mtime>$maxAge) return null;
        try{
            $data=json_decode((string)file_get_contents($path),true,512,JSON_THROW_ON_ERROR);
            return is_array($data['status']??null)?$data['status']:null;
        }catch(\Throwable){return null;}
    }

    public static function receive(int $serverId, string $rawBody, string $timestamp, string $signature): array
    {
        self::verify($serverId,$rawBody,$timestamp,$signature);
        $body=json_decode($rawBody,true,512,JSON_THROW_ON_ERROR);
        if(!is_array($body)) throw new \RuntimeException('Ungültiger Fast-Live-Body.');
        $status=$body['status']??null;
        if(!is_array($status)) throw new \RuntimeException('Fast-Live-Status fehlt.');

        $chatMessages=is_array($status['chat_messages']??null)?$status['chat_messages']:[];
        ServerChatService::ingest($serverId,$chatMessages);
        // Der Ringpuffer gehört dauerhaft in die Datenbank, nicht in jeden lokalen
        // Statuscache. Das hält die Statusdatei klein und die Browser-Polls schnell.
        $cacheStatus=$status;
        unset($cacheStatus['chat_messages']);
        self::writeCache($serverId,$cacheStatus);
        $acked=self::ingestResults($serverId,is_array($body['results']??null)?$body['results']:[]);
        AntiAdvertisementService::ingestEvents($serverId,is_array($body['anti_advertisement_events']??null)?$body['anti_advertisement_events']:[]);
        $commands=self::deliverCommands($serverId,25);
        return ['ok'=>true,'commands'=>$commands,'acked_result_ids'=>$acked,'global_bans'=>GlobalBanService::activePayload(),'global_comm_sanctions'=>ModerationService::activeCommunicationPayload(),'global_anti_advertisement'=>AntiAdvertisementService::activeSnapshot(),'server_time'=>gmdate('c')];
    }

    public static function queueCommand(int $serverId, array $command): string
    {
        $id=(string)($command['id']??'');
        if($id==='') throw new \InvalidArgumentException('Command-ID fehlt.');
        $json=json_encode($command,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
        Database::connection()->prepare(
            'INSERT INTO bridge_command_queue(server_id,command_id,command_json,status,requested_at) VALUES(?,?,?,?,?)'
        )->execute([$serverId,$id,$json,'queued',date('c')]);
        return $id;
    }

    public static function result(int $serverId,string $commandId): ?array
    {
        $s=Database::connection()->prepare('SELECT result_json FROM bridge_command_queue WHERE server_id=? AND command_id=? AND result_json IS NOT NULL');
        $s->execute([$serverId,$commandId]); $raw=$s->fetchColumn();
        if(!$raw) return null;
        $data=json_decode((string)$raw,true); return is_array($data)?$data:null;
    }

    public static function recentCommands(int $serverId, int $limit=20): array
    {
        $limit=max(1,min(100,$limit));
        $s=Database::connection()->prepare('SELECT command_id,command_json,status,delivery_attempts,requested_at,delivered_at,completed_at,result_json FROM bridge_command_queue WHERE server_id=? ORDER BY id DESC LIMIT '.$limit);
        $s->execute([$serverId]);
        $rows=[];
        foreach($s->fetchAll() as $row){
            $cmd=json_decode((string)$row['command_json'],true); if(!is_array($cmd))$cmd=[];
            $result=$row['result_json']!==null?json_decode((string)$row['result_json'],true):null;
            $rows[]=[
                'command_id'=>(string)$row['command_id'],
                'action'=>(string)($cmd['action']??'unbekannt'),
                'target_steamid'=>(string)($cmd['target_steamid']??''),
                'requested_by'=>(string)($cmd['requested_by']??''),
                'status'=>(string)$row['status'],
                'delivery_attempts'=>(int)$row['delivery_attempts'],
                'requested_at'=>(string)$row['requested_at'],
                'delivered_at'=>$row['delivered_at']!==null?(string)$row['delivered_at']:null,
                'completed_at'=>$row['completed_at']!==null?(string)$row['completed_at']:null,
                'result'=>is_array($result)?$result:null,
            ];
        }
        return $rows;
    }

    public static function ensureEnabled(int $serverId): string
    {
        $pdo=Database::connection();
        $s=$pdo->prepare('SELECT bridge_fastlive_enabled,bridge_fastlive_secret_enc FROM game_servers WHERE id=?');
        $s->execute([$serverId]);
        $row=$s->fetch();
        if(!$row) throw new \RuntimeException('Server nicht gefunden.');

        $secret='';
        $regenerated=false;
        $enc=trim((string)($row['bridge_fastlive_secret_enc']??''));
        if($enc!==''){
            try{$secret=Crypto::decrypt($enc);}catch(\Throwable){$secret='';}
        }
        if($secret===''){
            $secret=rtrim(strtr(base64_encode(random_bytes(32)),'+/','-_'),'=');
            $enc=Crypto::encrypt($secret);
            $regenerated=true;
        }

        if(empty($row['bridge_fastlive_enabled']) || $regenerated || trim((string)($row['bridge_fastlive_secret_enc']??''))===''){
            $pdo->prepare('UPDATE game_servers SET bridge_fastlive_enabled=1,bridge_fastlive_secret_enc=?,updated_at=? WHERE id=?')
                ->execute([$enc,date('c'),$serverId]);
        }
        return $secret;
    }

    public static function rotateSecret(int $serverId): string
    {
        $secret=rtrim(strtr(base64_encode(random_bytes(32)),'+/','-_'),'=');
        Database::connection()->prepare('UPDATE game_servers SET bridge_fastlive_enabled=1,bridge_fastlive_secret_enc=?,updated_at=? WHERE id=?')
            ->execute([Crypto::encrypt($secret),date('c'),$serverId]);
        return $secret;
    }

    public static function provision(int $serverId): array
    {
        self::ensureEnabled($serverId);
        $s=Database::connection()->prepare('SELECT * FROM game_servers WHERE id=?');
        $s->execute([$serverId]);
        $server=$s->fetch();
        if(!$server) throw new \RuntimeException('Server nicht gefunden.');

        $connector=ConnectorFactory::make($server);
        $config=self::configSnippet($serverId);
        $panelUrl=trim((string)($config['panel_url']??''));
        if($panelUrl==='' || !filter_var($panelUrl,FILTER_VALIDATE_URL) || strtolower((string)parse_url($panelUrl,PHP_URL_SCHEME))!=='https'){
            throw new \RuntimeException('Für Fast Live muss config/app.php eine gültige HTTPS app_url enthalten.');
        }
        $json=json_encode($config,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR)."\n";

        $connector->write(self::REMOTE_CONFIG_PATH,$json);
        $verify=$connector->read(self::REMOTE_CONFIG_PATH);
        $decoded=json_decode($verify,true,512,JSON_THROW_ON_ERROR);
        if(!is_array($decoded)
            || empty($decoded['enabled'])
            || (int)($decoded['server_id']??0)!==$serverId
            || !hash_equals((string)$config['secret'],(string)($decoded['secret']??''))
            || rtrim((string)($decoded['panel_url']??''),'/')!==rtrim((string)$config['panel_url'],'/')){
            throw new \RuntimeException('Fast-Live-Konfiguration wurde geschrieben, konnte aber nicht verifiziert werden.');
        }

        return [
            'path'=>self::REMOTE_CONFIG_PATH,
            'connector'=>$connector->label(),
            'config'=>$config,
        ];
    }

    public static function disable(int $serverId): void
    {
        Database::connection()->prepare('UPDATE game_servers SET bridge_fastlive_enabled=0,updated_at=? WHERE id=?')->execute([date('c'),$serverId]);
    }

    public static function secret(int $serverId): string
    {
        $s=Database::connection()->prepare('SELECT bridge_fastlive_secret_enc FROM game_servers WHERE id=?');$s->execute([$serverId]);
        return Crypto::decrypt((string)($s->fetchColumn()?:''));
    }

    public static function lastSeenAt(int $serverId): ?string
    {
        $mtime=@filemtime(self::cachePath($serverId));
        return $mtime ? date('c',$mtime) : null;
    }

    public static function configSnippet(int $serverId): array
    {
        return [
            'enabled'=>true,
            'panel_url'=>rtrim((string)Config::get('app_url',''),'/'),
            'server_id'=>$serverId,
            'secret'=>self::secret($serverId),
            'push_interval_seconds'=>2,
        ];
    }

    private static function verify(int $serverId,string $body,string $timestamp,string $signature): void
    {
        if(!self::enabled($serverId)) throw new \RuntimeException('Fast Live ist für diesen Server nicht aktiviert.');
        if(!preg_match('/^\d{10}$/',$timestamp)) throw new \RuntimeException('Ungültiger Zeitstempel.');
        if(abs(time()-(int)$timestamp)>90) throw new \RuntimeException('Fast-Live-Zeitstempel außerhalb des zulässigen Fensters.');
        $secret=self::secret($serverId); if($secret==='') throw new \RuntimeException('Fast-Live-Geheimnis fehlt.');
        $expected=hash_hmac('sha256',$timestamp."\n".$body,$secret);
        if(!preg_match('/^[a-f0-9]{64}$/i',$signature)||!hash_equals($expected,strtolower($signature))) throw new \RuntimeException('Fast-Live-Signatur ungültig.');
    }

    private static function cacheDirectory(): string { return dirname(__DIR__,2).'/storage/cache/fast-live'; }
    private static function cachePath(int $serverId): string { return self::cacheDirectory().'/server-'.$serverId.'.json'; }
    private static function writeCache(int $serverId,array $status): void
    {
        $dir=self::cacheDirectory(); if(!is_dir($dir)&&!@mkdir($dir,0770,true)&&!is_dir($dir)) throw new \RuntimeException('Fast-Live-Cache konnte nicht angelegt werden.');
        $path=self::cachePath($serverId); $tmp=$path.'.'.bin2hex(random_bytes(4)).'.tmp';
        file_put_contents($tmp,json_encode(['received_at'=>microtime(true),'status'=>$status],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),LOCK_EX);
        rename($tmp,$path); @chmod($path,0660);
    }

    private static function ingestResults(int $serverId,array $results): array
    {
        $ack=[]; $pdo=Database::connection();
        $u=$pdo->prepare('UPDATE bridge_command_queue SET status=?,result_json=?,completed_at=? WHERE server_id=? AND command_id=?');
        foreach(array_slice($results,0,100) as $r){
            if(!is_array($r))continue; $id=trim((string)($r['id']??'')); if($id==='')continue;
            $ok=!empty($r['success']); $u->execute([$ok?'success':'failed',json_encode($r,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),date('c'),$serverId,$id]);
            if($u->rowCount()>0)$ack[]=$id;
        }
        return $ack;
    }

    private static function deliverCommands(int $serverId,int $limit): array
    {
        $cut=date('c',time()-15); $pdo=Database::connection();
        $s=$pdo->prepare("SELECT id,command_id,command_json,requested_at FROM bridge_command_queue WHERE server_id=? AND (status='queued' OR (status='delivered' AND (delivered_at IS NULL OR delivered_at<?))) ORDER BY id LIMIT {$limit}");
        $s->execute([$serverId,$cut]); $rows=$s->fetchAll(); $out=[];
        $u=$pdo->prepare("UPDATE bridge_command_queue SET status='delivered',delivery_attempts=delivery_attempts+1,delivered_at=? WHERE id=?");
        $fail=$pdo->prepare("UPDATE bridge_command_queue SET status='failed',result_json=?,completed_at=? WHERE id=?");
        foreach($rows as $r){
            // Do not replay old admin actions when a broken exchange recovers.
            if((strtotime((string)$r['requested_at'])?:0)<time()-120){
                $failure=['id'=>$r['command_id'],'success'=>false,'message'=>'Keine rechtzeitige Bridge-Bestätigung: Der Befehl ist abgelaufen und wird nicht erneut ausgeliefert.'];
                $fail->execute([json_encode($failure,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR),date('c'),(int)$r['id']]);
                continue;
            }
            // Keep JSON objects intact through the database round trip. Associative
            // decoding turns an empty payload {} into [], which the C# Dictionary
            // rejects and therefore prevents the entire exchange from being read.
            $cmd=json_decode((string)$r['command_json']);
            if(!$cmd instanceof \stdClass)continue;
            if(!isset($cmd->payload)||$cmd->payload===[])$cmd->payload=new \stdClass();
            $out[]=$cmd;
            $u->execute([date('c'),(int)$r['id']]);
        }
        return $out;
    }
}
