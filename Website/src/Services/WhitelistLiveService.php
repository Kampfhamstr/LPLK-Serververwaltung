<?php
namespace App\Services;

use App\Core\Auth;
use App\Core\Database;
use App\Core\Jsonc;

final class WhitelistLiveService
{
    private const SUPPORTED_ID = 'Whitelist';
    private const DEFAULT_ADD = 'wl';
    private const DEFAULT_REMOVE = 'uwl';
    private const MAX_BULK = 500;

    public static function state(int $serverId, ?array $status = null): array
    {
        if(!Auth::can('live.whitelist')||!Auth::canServer($serverId,'live'))return ['detected'=>false,'supported'=>false,'entries'=>[],'candidates'=>[],'steam_ids'=>[],'plugin'=>null,'mode'=>1,'mode_label'=>'Whitelist'];
        $server = LiveServerService::server($serverId);
        $status ??= LiveServerService::status($serverId);
        $plugin = self::detectPlugin((array)($status['plugins'] ?? []));

        if (!$plugin) {
            return [
                'detected'=>false,'supported'=>false,'mode'=>1,'mode_label'=>'Whitelist',
                'entries'=>[],'candidates'=>[],'steam_ids'=>[],'plugin'=>null,
            ];
        }

        $lplk=strcasecmp((string)($plugin['id']??''),LplkWhitelistAdapter::ID)===0;
        if($lplk&&($server['server_identifier']??'')!=='11110004')return ['detected'=>false,'supported'=>false,'entries'=>[],'candidates'=>[],'steam_ids'=>[],'plugin'=>null,'mode'=>1,'mode_label'=>'Whitelist'];
        $connector = ConnectorFactory::make($server);
        $pluginPath = self::relativePluginPath((string)($plugin['path'] ?? ''));
        if($lplk)$pluginPath=LplkWhitelistAdapter::DIRECTORY;
        if ($pluginPath === '') $pluginPath = 'game/csgo/addons/swiftlys2/plugins/Whitelist';

        $listPath = rtrim($pluginPath,'/') . '/whitelist.txt';
        $config = self::readConfig($connector,$plugin);

        $mode = (int)($config['Mode'] ?? 1);
        if (!in_array($mode,[1,2],true)) $mode = 1;

        $addCommand = self::commandName((string)($config['AddCommand'] ?? self::DEFAULT_ADD),self::DEFAULT_ADD);
        $removeCommand = self::commandName((string)($config['RemoveCommand'] ?? self::DEFAULT_REMOVE),self::DEFAULT_REMOVE);
        if($lplk){$addCommand=LplkWhitelistAdapter::ADD;$removeCommand=LplkWhitelistAdapter::REMOVE;}

        $fileExists=false;$readError=null;$steamIds=[];
        try {
            $raw=$connector->read($listPath);
            $steamIds=$lplk?LplkWhitelistAdapter::parse($raw):self::parseSteamIds($raw);
            $fileExists=true;
        } catch (\Throwable $e) {
            $message=$e->getMessage();
            $missing=str_contains($message,'HTTP 404')
                || str_contains(strtolower($message),'nicht gefunden')
                || str_contains(strtolower($message),'not found');
            if (!$missing || $lplk) $readError=$lplk?'Listendatei fehlt, ist ungültig oder kann nicht gelesen werden.':$message;
        }

        $online=[];
        foreach ((array)($status['players'] ?? []) as $live) {
            $sid=trim((string)($live['steam_id64'] ?? ''));
            if (self::validSteamId($sid)) $online[$sid]=$live;
        }

        $known=self::peopleBySteamIds($serverId,$steamIds);
        $entries=[];
        foreach ($steamIds as $sid) {
            $person=$known[$sid] ?? null;
            $role=strtolower(trim((string)($person['server_role'] ?? $person['community_role'] ?? '')));
            if ($role==='') $role='spieler';
            $entries[]=[
                'steam_id64'=>$sid,
                'display_name'=>self::displayName($person,$online[$sid] ?? null,$sid),
                'community_role'=>$role,
                'role'=>$role,
                'online'=>isset($online[$sid]),
            ];
        }
        self::sortEntries($entries);

        $candidates=self::serverCandidates($serverId,array_fill_keys($steamIds,true),$online);

        $metadata=$lplk?LplkWhitelistAdapter::metadata($connector,$server,$status,$config,count($steamIds)):['lplk'=>false,'editable'=>true];
        if($readError!==null)$metadata['editable']=false;
        return $metadata+[
            'detected'=>true,
            'supported'=>true,
            'plugin'=>$plugin,
            'plugin_path'=>$pluginPath,
            'list_path'=>$listPath,
            'file_exists'=>$fileExists,
            'read_error'=>$readError,
            'mode'=>$mode,
            'mode_label'=>$mode===2?'Blacklist':'Whitelist',
            'add_command'=>$addCommand,
            'remove_command'=>$removeCommand,
            'permission'=>(string)($config['PermissionForCommands'] ?? 'wl.admin'),
            'config_loaded'=>!empty($config['_loaded']),
            'bridge_version'=>(string)($status['version'] ?? ''),
            'entries'=>$entries,
            'steam_ids'=>$steamIds,
            'candidates'=>$candidates,
            'count'=>count($steamIds),
        ];
    }

    public static function addOne(int $serverId,string $steamId): array
    {
        $steamId=self::steamId($steamId);
        $state=self::requireState($serverId);

        if (in_array($steamId,$state['steam_ids'],true)) {
            return [
                'added'=>[],'removed'=>[],'commands'=>[],'backup'=>null,'unchanged'=>[$steamId],
                'list_path'=>$state['list_path'],'mode_label'=>$state['mode_label'],'command_map'=>[]
            ];
        }

        $commands=self::queueCommands($serverId,[$state['add_command'].' '.$steamId]);
        return [
            'added'=>[$steamId],'removed'=>[],'commands'=>$commands,'backup'=>null,'unchanged'=>[],
            'list_path'=>$state['list_path'],'mode_label'=>$state['mode_label'],
            'command_map'=>self::commandMap($commands,['add'],[$steamId]),
        ];
    }

    public static function addMany(int $serverId,array $steamIds): array
    {
        $state=self::requireState($serverId);
        $requested=self::normalizeSteamIds($steamIds);
        $current=array_fill_keys($state['steam_ids'],true);
        $add=array_values(array_filter($requested,static fn(string $sid):bool=>!isset($current[$sid])));

        if (!$add) return [
            'added'=>[],'removed'=>[],'commands'=>[],'backup'=>null,'unchanged'=>$requested,
            'list_path'=>$state['list_path'],'mode_label'=>$state['mode_label'],'command_map'=>[]
        ];

        $backup=count($add)>1?self::backup($serverId,$state):null;
        $commands=self::queueCommands($serverId,array_map(
            static fn(string $sid):string=>$state['add_command'].' '.$sid,$add
        ));

        return [
            'added'=>$add,'removed'=>[],'commands'=>$commands,'backup'=>$backup,
            'unchanged'=>array_values(array_intersect($requested,$state['steam_ids'])),
            'list_path'=>$state['list_path'],'mode_label'=>$state['mode_label'],
            'command_map'=>self::commandMap(
                $commands,array_fill(0,count($add),'add'),$add
            ),
        ];
    }

    public static function removeMany(int $serverId,array $steamIds): array
    {
        $state=self::requireState($serverId);
        $requested=self::normalizeSteamIds($steamIds);
        $current=array_fill_keys($state['steam_ids'],true);
        $remove=array_values(array_filter($requested,static fn(string $sid):bool=>isset($current[$sid])));

        if (!$remove) return [
            'added'=>[],'removed'=>[],'commands'=>[],'backup'=>null,'unchanged'=>$requested,
            'list_path'=>$state['list_path'],'mode_label'=>$state['mode_label'],'command_map'=>[]
        ];

        $backup=self::backup($serverId,$state);
        $syncCapable=empty($state['lplk'])&&self::bridgeSupportsWhitelistSync((string)($state['bridge_version'] ?? ''));
        $commands=$syncCapable
            ? self::queueWhitelistRemoval($serverId,$state,$remove)
            : self::queueCommands($serverId,array_map(
                static fn(string $sid):string=>$state['remove_command'].' '.$sid,$remove
            ));

        $commandMap=$syncCapable
            ? self::commandMap($commands,['remove_sync'],[$remove[0] ?? ''])
            : self::commandMap($commands,array_fill(0,count($remove),'remove'),$remove);

        return [
            'added'=>[],'removed'=>$remove,'commands'=>$commands,'backup'=>$backup,'unchanged'=>[],
            'list_path'=>$state['list_path'],'mode_label'=>$state['mode_label'],
            'command_map'=>$commandMap,'bridge_sync'=>$syncCapable,
        ];
    }

    public static function replaceWith(int $serverId,string $text): array
    {
        $state=self::requireState($serverId);
        $desired=!empty($state['lplk'])?LplkWhitelistAdapter::parse($text):self::parseSteamIds($text);
        if (count($desired)>self::MAX_BULK) throw new \InvalidArgumentException('Maximal '.self::MAX_BULK.' SteamIDs pro Abgleich.');

        $current=$state['steam_ids'];
        $add=array_values(array_diff($desired,$current));
        $remove=array_values(array_diff($current,$desired));

        if (!$add && !$remove) return [
            'added'=>[],'removed'=>[],'commands'=>[],'backup'=>null,'unchanged'=>$desired,
            'list_path'=>$state['list_path'],'mode_label'=>$state['mode_label'],'command_map'=>[]
        ];

        $backup=self::backup($serverId,$state);
        $ids=[];
        $commandMap=[];

        if ($add) {
            $addIds=self::queueCommands($serverId,array_map(
                static fn(string $sid):string=>$state['add_command'].' '.$sid,$add
            ));
            $ids=array_merge($ids,$addIds);
            $commandMap=array_merge($commandMap,self::commandMap(
                $addIds,array_fill(0,count($add),'add'),$add
            ));
        }

        $syncCapable=$remove && empty($state['lplk']) && self::bridgeSupportsWhitelistSync((string)($state['bridge_version'] ?? ''));
        if ($remove) {
            if ($syncCapable) {
                $removeIds=self::queueWhitelistRemoval($serverId,$state,$remove);
                $ids=array_merge($ids,$removeIds);
                $commandMap=array_merge($commandMap,self::commandMap(
                    $removeIds,['remove_sync'],[$remove[0] ?? '']
                ));
            } else {
                $removeIds=self::queueCommands($serverId,array_map(
                    static fn(string $sid):string=>$state['remove_command'].' '.$sid,$remove
                ));
                $ids=array_merge($ids,$removeIds);
                $commandMap=array_merge($commandMap,self::commandMap(
                    $removeIds,array_fill(0,count($remove),'remove'),$remove
                ));
            }
        }

        return [
            'added'=>$add,'removed'=>$remove,'commands'=>$ids,'backup'=>$backup,'unchanged'=>[],
            'list_path'=>$state['list_path'],'mode_label'=>$state['mode_label'],
            'command_map'=>$commandMap,'bridge_sync'=>(bool)$syncCapable,
        ];
    }

    public static function verifyPending(int $serverId,array $pending): array
    {
        self::requireAccess($serverId);
        $createdAt=(int)($pending['created_at'] ?? 0);
        $age=$createdAt>0?max(0,time()-$createdAt):0;

        $expectedAdd=self::normalizeSteamIds((array)($pending['added'] ?? []));
        $expectedRemove=self::normalizeSteamIds((array)($pending['removed'] ?? []));
        $listPath=trim((string)($pending['list_path'] ?? ''));
        if($listPath===LplkWhitelistAdapter::LIST)return self::verifyLplk($serverId,$pending,$expectedAdd,$expectedRemove,$age);

        if ($listPath==='' || !str_starts_with($listPath,'game/csgo/addons/swiftlys2/plugins/')) {
            throw new \RuntimeException('Ungültiger Whitelist-Prüfpfad.');
        }

        $server=LiveServerService::server($serverId);
        $connector=ConnectorFactory::make($server);

        try {
            // Entscheidend ist die echte Plugin-Datei. Ein Bridge-Result "OK"
            // bestätigt nur, dass ExecuteCommandWithBuffer angenommen wurde.
            $current=self::parseSteamIds($connector->read($listPath));
        } catch (\Throwable $e) {
            if ($age>=20) {
                return [
                    'state'=>'timeout','confirmed'=>false,'age_seconds'=>$age,
                    'message'=>'Die Listendatei konnte innerhalb von 20 Sekunden nicht bestätigt werden: '.$e->getMessage(),
                ];
            }

            return [
                'state'=>'pending','confirmed'=>false,'age_seconds'=>$age,
                'message'=>'Warte auf lesbare Listendatei des Gameservers.',
                'added_confirmed'=>0,'removed_confirmed'=>0,
                'added_total'=>count($expectedAdd),'removed_total'=>count($expectedRemove),
            ];
        }

        $progress=self::confirmationProgress($current,$expectedAdd,$expectedRemove);
        if ($progress['complete']) {
            // Nach jeder bestätigten Änderung wird die echte Datei stabil normalisiert:
            // Rollenreihenfolge Founder -> Admin -> Member -> VIP -> Spieler,
            // innerhalb der Rolle alphabetisch nach Anzeigename.
            self::normalizeListFile($serverId,$connector,$listPath,$current);

            return [
                'state'=>'confirmed','confirmed'=>true,'age_seconds'=>$age,
                'message'=>'Änderung wurde durch die echte '.(string)($pending['mode_label'] ?? 'Whitelist').'-Datei des Gameservers bestätigt und die Liste nach Rolle/Name sortiert.',
                'added_confirmed'=>$progress['added_confirmed'],'removed_confirmed'=>$progress['removed_confirmed'],
                'added_total'=>count($expectedAdd),'removed_total'=>count($expectedRemove),
            ];
        }

        // Fehlerresultate können früh sichtbar gemacht werden. Ein positives Result
        // allein ist absichtlich noch keine Bestätigung der Dateipersistenz.
        $failedMessage=self::failedCommandMessage($serverId,(array)($pending['command_map'] ?? []),$age);
        if ($failedMessage!==null) {
            return [
                'state'=>'failed','confirmed'=>false,'age_seconds'=>$age,
                'message'=>$failedMessage,
                'added_confirmed'=>$progress['added_confirmed'],'removed_confirmed'=>$progress['removed_confirmed'],
                'added_total'=>count($expectedAdd),'removed_total'=>count($expectedRemove),
            ];
        }

        // v1.7.1: Manche Whitelist-Versionen akzeptieren !uwl im laufenden Plugin,
        // persistieren die entfernte SteamID aber nicht zuverlässig in whitelist.txt.
        // Nach kurzer Schonfrist ersetzen wir deshalb die echte Datei atomar durch
        // ihren aktuellen Inhalt OHNE die angeforderten SteamIDs. Damit bleiben
        // parallel hinzugekommene Spieler erhalten und nur die ausgewählten IDs
        // werden entfernt.
        if ($expectedRemove && $age>=3) {
            try {
                $reconciled=self::reconcileRemovals($serverId,$connector,$listPath,$current,$expectedRemove);
                $after=self::parseSteamIds($connector->read($listPath));
                $afterProgress=self::confirmationProgress($after,$expectedAdd,$expectedRemove);

                if ($afterProgress['complete']) {
                    self::normalizeListFile($serverId,$connector,$listPath,$after);
                    return [
                        'state'=>'confirmed','confirmed'=>true,'age_seconds'=>$age,
                        'message'=>$reconciled
                            ?'!uwl wurde ausgeführt; die echte Listendatei wurde zusätzlich ohne die entfernte SteamID neu geschrieben und nach Rolle/Name sortiert.'
                            :'Entfernung wurde bestätigt; die echte Listendatei ist nach Rolle/Name sortiert.',
                        'added_confirmed'=>$afterProgress['added_confirmed'],'removed_confirmed'=>$afterProgress['removed_confirmed'],
                        'added_total'=>count($expectedAdd),'removed_total'=>count($expectedRemove),
                        'reconciled'=>$reconciled,
                    ];
                }
            } catch (\Throwable $e) {
                if ($age>=20) {
                    return [
                        'state'=>'timeout','confirmed'=>false,'age_seconds'=>$age,
                        'message'=>'!uwl wurde übermittelt, aber die Listendatei konnte nicht bereinigt werden: '.$e->getMessage(),
                        'added_confirmed'=>$progress['added_confirmed'],'removed_confirmed'=>$progress['removed_confirmed'],
                        'added_total'=>count($expectedAdd),'removed_total'=>count($expectedRemove),
                    ];
                }
            }
        }

        if ($age>=20) {
            return [
                'state'=>'timeout','confirmed'=>false,'age_seconds'=>$age,
                'message'=>'Die Bridge hat die Änderung angenommen, aber nach 20 Sekunden ist sie noch nicht in der echten Listendatei sichtbar.',
                'added_confirmed'=>$progress['added_confirmed'],'removed_confirmed'=>$progress['removed_confirmed'],
                'added_total'=>count($expectedAdd),'removed_total'=>count($expectedRemove),
            ];
        }

        return [
            'state'=>'pending','confirmed'=>false,'age_seconds'=>$age,
            'message'=>$expectedRemove
                ?'!uwl ist übermittelt; das Panel prüft die echte Datei und bereinigt sie bei Bedarf automatisch.'
                :'Änderung ist übermittelt; warte auf Bestätigung durch die echte Listendatei.',
            'added_confirmed'=>$progress['added_confirmed'],'removed_confirmed'=>$progress['removed_confirmed'],
            'added_total'=>count($expectedAdd),'removed_total'=>count($expectedRemove),
        ];
    }

    private static function confirmationProgress(array $current,array $expectedAdd,array $expectedRemove): array
    {
        $listed=array_fill_keys($current,true);
        $addedConfirmed=0;
        foreach ($expectedAdd as $sid) if (isset($listed[$sid])) $addedConfirmed++;

        $removedConfirmed=0;
        foreach ($expectedRemove as $sid) if (!isset($listed[$sid])) $removedConfirmed++;

        return [
            'added_confirmed'=>$addedConfirmed,
            'removed_confirmed'=>$removedConfirmed,
            'complete'=>$addedConfirmed===count($expectedAdd) && $removedConfirmed===count($expectedRemove),
        ];
    }

    private static function failedCommandMessage(int $serverId,array $commandMap,int $age): ?string
    {
        if ($age<2) return null;
        foreach (array_slice($commandMap,0,12) as $item) {
            if (!is_array($item)) continue;
            $cid=trim((string)($item['id'] ?? ''));
            if ($cid==='') continue;

            try {
                $result=LiveServerService::result($serverId,$cid);
            } catch (\Throwable) {
                $result=null;
            }

            if (is_array($result) && array_key_exists('success',$result) && !$result['success']) {
                return 'Bridge-Befehl fehlgeschlagen: '.trim((string)($result['message'] ?? 'Unbekannter Fehler'));
            }
        }
        return null;
    }

    private static function reconcileRemovals(
        int $serverId,
        ServerConnectorInterface $connector,
        string $listPath,
        array $current,
        array $remove
    ): bool {
        $removeSet=array_fill_keys($remove,true);
        $desired=array_values(array_filter(
            self::normalizeSteamIds($current),
            static fn(string $sid):bool=>!isset($removeSet[$sid])
        ));

        if (count($desired)===count($current)) return false;

        $desired=self::sortSteamIdsForServer($serverId,$desired);
        $connector->write($listPath,self::renderSteamIdList($desired));
        return true;
    }

    private static function normalizeListFile(
        int $serverId,
        ServerConnectorInterface $connector,
        string $listPath,
        array $current
    ): void {
        $normalized=self::normalizeSteamIds($current);
        $sorted=self::sortSteamIdsForServer($serverId,$normalized);
        if ($sorted===$normalized) return;
        $connector->write($listPath,self::renderSteamIdList($sorted));
    }

    private static function renderSteamIdList(array $steamIds): string
    {
        return $steamIds ? implode("\n",$steamIds)."\n" : '';
    }

    private static function commandMap(array $ids,array $operations,array $steamIds): array
    {
        $out=[];
        foreach ($ids as $index=>$id) {
            $out[]=[
                'id'=>(string)$id,
                'operation'=>(string)($operations[$index] ?? ''),
                'steam_id64'=>(string)($steamIds[$index] ?? ''),
            ];
        }
        return $out;
    }

    private static function requireState(int $serverId): array
    {
        self::requireAccess($serverId);
        $state=self::state($serverId);
        if (empty($state['detected']) || empty($state['supported'])) {
            throw new \RuntimeException('Das unterstützte SwiftlyS2-Whitelist-Plugin wurde auf diesem Server nicht erkannt.');
        }
        if (!empty($state['read_error'])) {
            throw new \RuntimeException('Whitelist-Datei konnte nicht gelesen werden: '.$state['read_error']);
        }
        if(isset($state['editable'])&&!$state['editable'])throw new \RuntimeException((string)($state['state_error']??'Whitelist kann derzeit nicht bearbeitet werden.'));
        return $state;
    }

    private static function requireAccess(int $serverId):void
    {
        if(!Auth::can('live.whitelist')||!Auth::canServer($serverId,'live'))throw new \RuntimeException('Keine Berechtigung für die Whitelist dieses Servers.');
    }

    private static function verifyLplk(int $serverId,array $pending,array $add,array $remove,int $age):array
    {
        $state=self::state($serverId);
        $progress=self::confirmationProgress((array)($state['steam_ids']??[]),$add,$remove);
        $fresh=!empty($state['lplk'])&&!empty($state['editable'])&&empty($state['read_error'])
            &&($state['runtime_updated_at']??0)>=(int)($pending['created_at']??PHP_INT_MAX);
        // LPLK owns atomic persistence. Verification must never rewrite or sort its file.
        if($fresh&&$progress['complete'])return ['state'=>'confirmed','confirmed'=>true,'age_seconds'=>$age,
            'message'=>'Änderung dauerhaft gespeichert und vom aktiven LPLK-Whitelist-Addon bestätigt.']+[
            'added_confirmed'=>$progress['added_confirmed'],'removed_confirmed'=>$progress['removed_confirmed'],
            'added_total'=>count($add),'removed_total'=>count($remove)];
        $failed=self::failedCommandMessage($serverId,(array)($pending['command_map']??[]),$age);
        return ['state'=>$failed?'failed':($age>=20?'timeout':'pending'),'confirmed'=>false,'age_seconds'=>$age,
            'message'=>$failed??($age>=20?'Änderung wurde vom Addon nicht bestätigt; die Liste wurde vom Panel nicht überschrieben.':($state['state_error']??'Warte auf gespeicherte Liste und Bestätigung des Addons.')),
            'added_confirmed'=>$progress['added_confirmed'],'removed_confirmed'=>$progress['removed_confirmed'],
            'added_total'=>count($add),'removed_total'=>count($remove)];
    }

    private static function detectPlugin(array $plugins): ?array
    {
        foreach($plugins as $p)if(is_array($p)&&strcasecmp((string)($p['id']??''),LplkWhitelistAdapter::ID)===0&&strcasecmp((string)($p['status']??''),'Loaded')===0)return $p;
        foreach ($plugins as $plugin) {
            if (!is_array($plugin)) continue;
            if (strcasecmp(trim((string)($plugin['id'] ?? '')),self::SUPPORTED_ID)!==0) continue;

            $author=strtolower(trim((string)($plugin['author'] ?? '')));
            $name=strtolower(trim((string)($plugin['name'] ?? '')));
            $path=strtolower(str_replace('\\','/',(string)($plugin['path'] ?? '')));

            if ($author==='verneri' || $name==='whitelist' || str_contains($path,'/whitelist')) return $plugin;
        }
        return null;
    }

    private static function readConfig(ServerConnectorInterface $connector,array $plugin): array
    {
        $ids=strcasecmp((string)($plugin['id']??''),LplkWhitelistAdapter::ID)===0?[LplkWhitelistAdapter::ID]:array_values(array_unique(array_filter([
            trim((string)($plugin['id'] ?? '')),
            trim((string)($plugin['name'] ?? '')),
            basename(str_replace('\\','/',trim((string)($plugin['path'] ?? '')))),
        ])));

        foreach ($ids as $id) {
            if (!preg_match('/^[A-Za-z0-9_.-]{1,100}$/',$id)) continue;
            $path='game/csgo/addons/swiftlys2/configs/plugins/'.$id.'/config.jsonc';
            try {
                $decoded=Jsonc::decode($connector->read($path));
                $section=$decoded['Whitelist'] ?? $decoded;
                if (!is_array($section)) continue;
                $section['_loaded']=true;
                return $section;
            } catch (\Throwable) {}
        }

        return [
            'Mode'=>1,'AddCommand'=>self::DEFAULT_ADD,'RemoveCommand'=>self::DEFAULT_REMOVE,
            'PermissionForCommands'=>'wl.admin','_loaded'=>false,
        ];
    }

    private static function serverCandidates(int $serverId,array $listed,array $online): array
    {
        $stmt=Database::connection()->prepare(
            "SELECT gp.id,gp.display_name,gp.steam_personaname,gp.steam_id64,gp.community_role,
                    COALESCE(spg.role_name,gp.community_role,'spieler') AS server_role
             FROM server_managed_players smp
             JOIN game_players gp ON gp.id=smp.player_id
             LEFT JOIN server_player_groups spg ON spg.server_id=smp.server_id AND spg.player_id=gp.id
             WHERE smp.server_id=? AND gp.active=1
             ORDER BY
                CASE LOWER(COALESCE(spg.role_name,gp.community_role,'spieler'))
                    WHEN 'founder' THEN 1 WHEN 'admin' THEN 2 WHEN 'member' THEN 3 WHEN 'vip' THEN 4 ELSE 5
                END,
                COALESCE(NULLIF(gp.steam_personaname,''),gp.display_name) COLLATE NOCASE"
        );
        $stmt->execute([$serverId]);

        $out=[];
        foreach ($stmt->fetchAll() as $row) {
            $sid=trim((string)$row['steam_id64']);
            if (!self::validSteamId($sid) || isset($listed[$sid])) continue;
            $out[]=[
                'steam_id64'=>$sid,
                'display_name'=>trim((string)($row['steam_personaname'] ?: $row['display_name'])) ?: $sid,
                'role'=>(string)($row['server_role'] ?? 'spieler'),
                'online'=>isset($online[$sid]),
            ];
        }
        return $out;
    }

    private static function peopleBySteamIds(int $serverId,array $steamIds): array
    {
        if (!$steamIds) return [];
        $ph=implode(',',array_fill(0,count($steamIds),'?'));
        $stmt=Database::connection()->prepare(
            "SELECT gp.id,gp.display_name,gp.steam_personaname,gp.steam_id64,gp.community_role,
                    COALESCE(spg.role_name,gp.community_role,'spieler') AS server_role
             FROM game_players gp
             LEFT JOIN server_player_groups spg ON spg.server_id=? AND spg.player_id=gp.id
             WHERE gp.steam_id64 IN ($ph)"
        );
        $stmt->execute(array_merge([$serverId],$steamIds));
        $out=[];
        foreach ($stmt->fetchAll() as $row) $out[(string)$row['steam_id64']]=$row;
        return $out;
    }

    private static function sortEntries(array &$entries): void
    {
        usort($entries,static function(array $a,array $b): int {
            $roleCompare=self::roleWeight((string)($a['role'] ?? $a['community_role'] ?? ''))
                <=> self::roleWeight((string)($b['role'] ?? $b['community_role'] ?? ''));
            if ($roleCompare!==0) return $roleCompare;

            $nameCompare=strnatcasecmp((string)($a['display_name'] ?? ''),(string)($b['display_name'] ?? ''));
            if ($nameCompare!==0) return $nameCompare;
            return strcmp((string)($a['steam_id64'] ?? ''),(string)($b['steam_id64'] ?? ''));
        });
    }

    private static function sortSteamIdsForServer(int $serverId,array $steamIds): array
    {
        $steamIds=self::normalizeSteamIds($steamIds);
        if (count($steamIds)<2) return $steamIds;
        $people=self::peopleBySteamIds($serverId,$steamIds);

        usort($steamIds,static function(string $a,string $b) use ($people): int {
            $pa=$people[$a] ?? null;
            $pb=$people[$b] ?? null;

            // Unbekannte IDs stehen hinter bekannten Panel-Spielern, damit die
            // sichtbare Rollenreihenfolge nicht durch reine SteamIDs unterbrochen wird.
            $wa=$pa ? self::roleWeight((string)($pa['server_role'] ?? $pa['community_role'] ?? 'spieler')) : 99;
            $wb=$pb ? self::roleWeight((string)($pb['server_role'] ?? $pb['community_role'] ?? 'spieler')) : 99;
            if ($wa!==$wb) return $wa<=>$wb;

            $na=$pa ? self::displayName($pa,null,$a) : $a;
            $nb=$pb ? self::displayName($pb,null,$b) : $b;
            $nameCompare=strnatcasecmp($na,$nb);
            return $nameCompare!==0 ? $nameCompare : strcmp($a,$b);
        });
        return $steamIds;
    }

    private static function roleWeight(string $role): int
    {
        return match(strtolower(trim($role))) {
            'founder'=>1,
            'admin'=>2,
            'member'=>3,
            'vip'=>4,
            'spieler','player'=>5,
            default=>6,
        };
    }

    private static function displayName(?array $person,?array $live,string $fallback): string
    {
        foreach ([$live['name'] ?? null,$person['steam_personaname'] ?? null,$person['display_name'] ?? null] as $value) {
            $value=trim((string)$value);
            if ($value!=='') return $value;
        }
        return $fallback;
    }

    private static function backup(int $serverId,array $state): ?string
    {
        if (empty($state['file_exists'])) return null;
        $server=LiveServerService::server($serverId);
        $connector=ConnectorFactory::make($server);
        $source=(string)$state['list_path'];
        $backup=rtrim((string)$state['plugin_path'],'/').'/whitelist.serververwaltung-last.bak';
        $connector->write($backup,$connector->read($source));
        return $backup;
    }

    private static function bridgeSupportsWhitelistSync(string $version): bool
    {
        $version=trim($version);
        return $version!=='' && version_compare($version,'1.5.0','>=');
    }

    private static function queueWhitelistRemoval(int $serverId,array $state,array $steamIds): array
    {
        $steamIds=self::normalizeSteamIds($steamIds);
        if (!$steamIds) return [];

        $server=LiveServerService::server($serverId);
        $status=LiveServerService::status($serverId);
        if (empty($status['bridge_online'])) {
            throw new \RuntimeException((string)($status['reason'] ?? 'SerververwaltungBridge ist aktuell nicht online.'));
        }

        $listPath=trim((string)($state['list_path'] ?? ''));
        if ($listPath==='' || !str_starts_with($listPath,'game/csgo/addons/swiftlys2/plugins/') || !str_ends_with(strtolower($listPath),'/whitelist.txt')) {
            throw new \RuntimeException('Ungültiger Whitelist-Dateipfad für den synchronen Abgleich.');
        }

        $removeCommand=self::commandName((string)($state['remove_command'] ?? self::DEFAULT_REMOVE),self::DEFAULT_REMOVE);
        $connector=ConnectorFactory::make($server);
        $requester=Auth::user();
        $requesterName=trim((string)($requester['steam_personaname'] ?? ''));
        if ($requesterName==='') $requesterName=trim((string)($requester['name'] ?? ''));
        if ($requesterName==='') $requesterName='Webpanel/System';
        $requesterRole=strtolower(trim((string)($requester['web_role'] ?? 'system')));
        $notificationPolicy=LiveServerService::adminActionNotificationPolicy();

        $id=LiveServerService::newCommandId();
        $payload=[
            'id'=>$id,
            'action'=>'whitelist_remove_sync',
            'target_steamid'=>null,
            'requested_by'=>$requesterName,
            'requested_by_role'=>$requesterRole,
            'notify_scope'=>$notificationPolicy['scope'],
            'notify_target'=>$notificationPolicy['always_target'],
            'payload'=>[
                'remove_command'=>$removeCommand,
                'steam_ids'=>$steamIds,
                'list_path'=>$listPath,
                'settle_milliseconds'=>1400,
            ],
            'requested_at'=>gmdate('c'),
        ];

        $connector->write(
            LiveServerService::COMMANDS_PATH.'/'.$id.'.json',
            json_encode($payload,JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR)."\n"
        );
        return [$id];
    }

    private static function queueCommands(int $serverId,array $commands): array
    {
        if (count($commands)>self::MAX_BULK*2) throw new \InvalidArgumentException('Zu viele Whitelist-Befehle in einer Aktion.');

        $server=LiveServerService::server($serverId);
        $status=LiveServerService::status($serverId);
        if (empty($status['bridge_online'])) {
            throw new \RuntimeException((string)($status['reason'] ?? 'SerververwaltungBridge ist aktuell nicht online.'));
        }

        $connector=ConnectorFactory::make($server);
        $requester=Auth::user();
        $requesterName=trim((string)($requester['steam_personaname'] ?? ''));
        if ($requesterName==='') $requesterName=trim((string)($requester['name'] ?? ''));
        if ($requesterName==='') $requesterName='Webpanel/System';
        $requesterRole=strtolower(trim((string)($requester['web_role'] ?? 'system')));
        $notificationPolicy=LiveServerService::adminActionNotificationPolicy();

        $ids=[];
        foreach ($commands as $command) {
            $command=trim((string)$command);
            if ($command==='' || strlen($command)>240 || preg_match('/[\r\n\0]/',$command)) {
                throw new \InvalidArgumentException('Ungültiger Whitelist-Befehl.');
            }

            $id=LiveServerService::newCommandId();
            $payload=[
                'id'=>$id,
                'action'=>'server_command',
                'target_steamid'=>null,
                'requested_by'=>$requesterName,
                'requested_by_role'=>$requesterRole,
                'notify_scope'=>$notificationPolicy['scope'],
                'notify_target'=>$notificationPolicy['always_target'],
                'payload'=>['command'=>$command],
                'requested_at'=>gmdate('c'),
            ];
            $connector->write(
                LiveServerService::COMMANDS_PATH.'/'.$id.'.json',
                json_encode($payload,JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR)."\n"
            );
            $ids[]=$id;
        }
        return $ids;
    }

    private static function relativePluginPath(string $path): string
    {
        $path=str_replace('\\','/',trim($path));
        if ($path==='') return '';
        if (str_starts_with($path,'/home/container/')) return trim(substr($path,strlen('/home/container/')),'/');
        $pos=strpos($path,'game/csgo/');
        if ($pos!==false) return trim(substr($path,$pos),'/');
        if (!str_starts_with($path,'/') && !preg_match('/^[A-Za-z]:\//',$path)) return trim($path,'/');
        return '';
    }

    private static function commandName(string $value,string $fallback): string
    {
        $value=trim($value);
        $value=preg_match('/^[A-Za-z0-9_.!-]{1,40}$/D',$value)?$value:$fallback;
        return str_starts_with($value,'sw_')?$value:'sw_'.$value;
    }

    private static function normalizeSteamIds(array $values): array
    {
        $out=[];
        foreach ($values as $value) {
            $sid=trim((string)$value);
            if ($sid==='') continue;
            $sid=self::steamId($sid);
            $out[$sid]=true;
        }
        if (count($out)>self::MAX_BULK) throw new \InvalidArgumentException('Maximal '.self::MAX_BULK.' SteamIDs pro Aktion.');
        return array_map('strval',array_keys($out));
    }

    private static function parseSteamIds(string $text): array
    {
        $parts=preg_split('/[\s,;]+/',trim($text)) ?: [];
        $out=[];
        foreach ($parts as $part) {
            $part=trim($part);
            if ($part==='' || !self::validSteamId($part)) continue;
            $out[$part]=true;
        }
        return array_map('strval',array_keys($out));
    }

    private static function steamId(string $steamId): string
    {
        $steamId=trim($steamId);
        if (!self::validSteamId($steamId)) {
            throw new \InvalidArgumentException('Ungültige SteamID64. Erwartet wird eine 17-stellige SteamID64.');
        }
        return $steamId;
    }

    private static function validSteamId(string $steamId): bool
    {
        return preg_match('/^7656119\d{10}$/',$steamId)===1;
    }
}
