<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Audit;
use App\Core\Database;
use App\Core\Security;
use App\Core\View;
use App\Services\LiveServerService;
use App\Services\AdminActionLogService;
use App\Services\WhitelistLiveService;
use App\Services\ServerChatService;
use App\Services\FastLiveService;
use App\Services\PterodactylLiveService;
use App\Services\LiveLayoutService;

final class LiveController
{
    private const MESSAGE_TYPES = ['chat','center','alert','notify'];
    private const TEAMS = ['spectator','t','ct'];
    private const WEAPONS = [
        'weapon_glock','weapon_hkp2000','weapon_usp_silencer','weapon_p250','weapon_fiveseven','weapon_tec9','weapon_cz75a','weapon_elite','weapon_deagle','weapon_revolver',
        'weapon_mac10','weapon_mp9','weapon_mp7','weapon_mp5sd','weapon_ump45','weapon_p90','weapon_bizon',
        'weapon_galilar','weapon_famas','weapon_ak47','weapon_m4a1','weapon_m4a1_silencer','weapon_aug','weapon_sg556',
        'weapon_ssg08','weapon_awp','weapon_scar20','weapon_g3sg1',
        'weapon_nova','weapon_xm1014','weapon_mag7','weapon_sawedoff','weapon_m249','weapon_negev',
        'weapon_hegrenade','weapon_flashbang','weapon_smokegrenade','weapon_molotov','weapon_incgrenade','weapon_decoy',
        'weapon_taser','weapon_knife',
    ];

    public function index(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.view');
        $server = LiveServerService::server($sid);
        $status = LiveServerService::status($sid);
        $currentUser = Auth::user();
        $liveLayout = LiveLayoutService::get((int)($currentUser['id'] ?? 0), $sid);
        View::render('server_live', [
            'title' => $server['name'] . ' · Live-Verwaltung',
            'server' => $server,
            'status' => $status,
            'weapons' => self::weaponOptions(),
            'mapOptions' => LiveServerService::mapOptions($sid),
            'whitelistManager' => WhitelistLiveService::state($sid, $status),
            'whitelistPending' => $this->whitelistPendingForServer($sid),
            'adminActionLog' => AdminActionLogService::latest($sid, 100),
            'serverChat' => ServerChatService::latest($sid, 30),
            'serverChatTotal' => ServerChatService::count($sid),
            'rulePresets' => $this->rulePresets($sid),
            'recentCommands' => FastLiveService::recentCommands($sid, 20),
            'liveLayout' => $liveLayout,
        ]);
    }

    public function status(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.view');
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        echo json_encode(LiveServerService::status($sid), JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    }

    /** v1.8.0: CPU/RAM/Disk/Netzwerk/Laufzeit aus der Pterodactyl Client API. */
    public function pterodactylStatus(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'server.resources.view');
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        try {
            echo json_encode(PterodactylLiveService::snapshot($sid), JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
        } catch (\Throwable $e) {
            http_response_code(503);
            echo json_encode(['available'=>false,'error'=>$e->getMessage()], JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
        }
    }

    /** v1.8.0: granulare Pterodactyl-Poweraktionen mit exakter Permission. */
    public function power(string $id): void
    {
        $sid = (int)$id;
        Security::verifyCsrf();
        $signal = strtolower(trim((string)($_POST['signal'] ?? '')));
        $permission = match ($signal) {
            'start' => 'server.power.start',
            'restart' => 'server.power.restart',
            'stop' => 'server.power.stop',
            'kill' => 'server.power.kill',
            default => '',
        };
        if ($permission === '') { http_response_code(400); exit('Ungueltige Power-Aktion.'); }
        $this->requireLive($sid, $permission);

        if ($signal === 'kill' && strtoupper(trim((string)($_POST['confirm_kill'] ?? ''))) !== 'KILL') {
            View::flash('error', 'Hard Kill nicht ausgefuehrt. Zur Bestaetigung muss KILL eingegeben werden.');
            header('Location: /servers/' . $sid . '/live#pterodactyl-control');
            return;
        }

        try {
            PterodactylLiveService::power($sid, $signal);
            Audit::log('server.power.' . $signal, 'server', (string)$sid, [
                'connector'=>'pterodactyl',
                'source'=>'live-serververwaltung',
            ]);
            $label = match ($signal) {
                'start'=>'Start', 'restart'=>'Neustart', 'stop'=>'Stop', 'kill'=>'Hard Kill', default=>$signal,
            };
            View::flash('success', $label . ' wurde an Pterodactyl uebermittelt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Pterodactyl-Poweraktion fehlgeschlagen: ' . $e->getMessage());
        }
        header('Location: /servers/' . $sid . '/live#pterodactyl-control');
    }

    public function player(string $id, string $steamid): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.view');
        if (!$this->validSteamId($steamid)) { http_response_code(400); exit('Ungültige SteamID64.'); }
        $server = LiveServerService::server($sid);
        $livePlayer = LiveServerService::player($sid, $steamid);
        if (!$livePlayer) {
            View::flash('warning', 'Der Spieler ist aktuell nicht mehr online.');
            header('Location: /servers/' . $sid . '/live');
            exit;
        }
        $status = LiveServerService::status($sid);
        $person = LiveServerService::localPerson($steamid);

        // v1.7.4: Teleport darf – mit live.teleport – zwischen beliebigen aktuell
        // verbundenen menschlichen Spielern erfolgen. Das eigene verifizierte
        // Steam-Konto des Webpanel-Nutzers wird als Quelle vorausgewählt, wenn es
        // auf diesem Server online ist.
        $teleportPlayers = array_values(array_filter(
            (array)($status['players'] ?? []),
            static fn(array $p): bool =>
                empty($p['is_bot'])
                && preg_match('/^7656119\d{10}$/', (string)($p['steam_id64'] ?? '')) === 1
        ));
        usort($teleportPlayers, static function(array $a, array $b): int {
            $rank = ['founder'=>0,'admin'=>1,'member'=>2,'vip'=>3,'spieler'=>4];
            $ra = $rank[strtolower((string)($a['community_role'] ?? 'spieler'))] ?? 9;
            $rb = $rank[strtolower((string)($b['community_role'] ?? 'spieler'))] ?? 9;
            return $ra <=> $rb ?: strcasecmp((string)($a['name'] ?? ''), (string)($b['name'] ?? ''));
        });
        $ownSteamId = trim((string)(Auth::user()['steam_id64'] ?? ''));
        $ownSteamOnline = $this->validSteamId($ownSteamId)
            && array_filter($teleportPlayers, static fn(array $p): bool => (string)($p['steam_id64'] ?? '') === $ownSteamId) !== [];

        View::render('server_live_player', [
            'title' => $server['name'] . ' · ' . ((string)($livePlayer['name'] ?? $steamid)),
            'server' => $server,
            'player' => $livePlayer,
            'person' => $person,
            'weapons' => self::weaponOptions(),
            'teleportPlayers' => $teleportPlayers,
            'ownSteamId' => $ownSteamOnline ? $ownSteamId : '',
            'nicknameChoices' => $this->nicknameChoices($person, $livePlayer),
        ]);
    }

    public function playerAction(string $id, string $steamid): void
    {
        $sid = (int)$id;
        Security::verifyCsrf();
        if (!$this->validSteamId($steamid)) { http_response_code(400); exit('Ungültige SteamID64.'); }
        $action = (string)($_POST['action'] ?? '');
        $payload = [];
        $permission = match ($action) {
            'kick' => 'live.kick',
            'ban','unban' => 'live.ban',
            'mute','unmute' => 'live.mute',
            'gag','ungag' => 'live.gag',
            'silence','unsilence' => 'live.silence',
            'slay' => 'live.slay',
            'freeze' => 'live.freeze',
            'respawn' => 'live.respawn',
            'health' => 'live.health',
            'armor' => 'live.armor',
            'team' => 'live.team',
            'noclip' => 'live.noclip',
            'godmode' => 'live.godmode',
            'player_speed' => 'live.speed',
            'player_gravity' => 'live.gravity',
            'goto_player','bring_player','teleport_player' => 'live.teleport',
            'give_weapon','remove_weapons' => 'live.weapons',
            'nickname_set','nickname_restore' => 'live.nickname',
            default => '',
        };
        if ($permission === '') { http_response_code(400); exit('Unbekannte Live-Aktion.'); }
        $this->requireLive($sid, $permission);

        switch ($action) {
            case 'kick':
                $payload['reason'] = $this->cleanText((string)($_POST['reason'] ?? 'Von der Serververwaltung entfernt.'), 160);
                break;
            case 'ban':
                $minutes = $this->punishmentMinutes($_POST['minutes'] ?? 60);
                $payload['minutes'] = $minutes;
                $payload['reason'] = $this->cleanText((string)($_POST['reason'] ?? 'Serverban'), 160);
                break;
            case 'mute':
            case 'gag':
            case 'silence':
                $payload['minutes'] = $this->punishmentMinutes($_POST['minutes'] ?? 60);
                $payload['reason'] = $this->cleanText((string)($_POST['reason'] ?? 'Kommunikation gesperrt'), 160);
                break;
            case 'freeze':
                $payload['frozen'] = ((string)($_POST['frozen'] ?? '1')) === '1';
                $seconds = (int)($_POST['seconds'] ?? 0);
                $payload['seconds'] = in_array($seconds, [0,5,10,30,60,300], true) ? $seconds : 0;
                break;
            case 'health':
                $payload['value'] = max(1, min(500, (int)($_POST['value'] ?? 100)));
                break;
            case 'armor':
                $payload['value'] = max(0, min(500, (int)($_POST['value'] ?? 100)));
                break;
            case 'team':
                $team = (string)($_POST['team'] ?? 'spectator');
                if (!in_array($team, self::TEAMS, true)) $team = 'spectator';
                $payload['team'] = $team;
                break;
            case 'noclip':
                $payload['enabled'] = ((string)($_POST['enabled'] ?? '1')) === '1';
                break;
            case 'godmode':
                $payload['enabled'] = ((string)($_POST['enabled'] ?? '1')) === '1';
                break;
            case 'player_speed':
                $speed = (float)($_POST['value'] ?? 1.0);
                $payload['value'] = max(0.25, min(3.0, round($speed, 2)));
                break;
            case 'player_gravity':
                $gravity = (float)($_POST['value'] ?? 1.0);
                $payload['value'] = max(0.1, min(3.0, round($gravity, 2)));
                break;
            case 'goto_player':
            case 'bring_player':
                // Legacy-Kompatibilität für ältere Formulare/Links.
                $adminSteam = trim((string)($_POST['admin_steamid'] ?? ''));
                if (!$this->validSteamId($adminSteam) || $adminSteam === $steamid) {
                    View::flash('error', 'Bitte einen anderen aktuell verbundenen Spieler auswählen.');
                    header('Location: /servers/' . $sid . '/live/player/' . rawurlencode($steamid));
                    exit;
                }
                $anchorPlayer = LiveServerService::player($sid, $adminSteam);
                if (!$anchorPlayer || !empty($anchorPlayer['is_bot'])) {
                    View::flash('error', 'Der ausgewählte Teleport-Spieler ist nicht mehr online.');
                    header('Location: /servers/' . $sid . '/live/player/' . rawurlencode($steamid));
                    exit;
                }
                $payload['admin_steamid'] = $adminSteam;
                break;

            case 'teleport_player':
                $sourceSteam = trim((string)($_POST['source_steamid'] ?? ''));
                $destinationSteam = trim((string)($_POST['destination_steamid'] ?? ''));
                if (!$this->validSteamId($sourceSteam) || !$this->validSteamId($destinationSteam) || $sourceSteam === $destinationSteam) {
                    View::flash('error', 'Quelle und Ziel müssen zwei verschiedene aktuell verbundene Spieler sein.');
                    header('Location: /servers/' . $sid . '/live/player/' . rawurlencode($steamid));
                    exit;
                }
                $sourcePlayer = LiveServerService::player($sid, $sourceSteam);
                $destinationPlayer = LiveServerService::player($sid, $destinationSteam);
                if (!$sourcePlayer || !$destinationPlayer || !empty($sourcePlayer['is_bot']) || !empty($destinationPlayer['is_bot'])) {
                    View::flash('error', 'Quelle oder Ziel ist nicht mehr als menschlicher Spieler auf diesem Server online.');
                    header('Location: /servers/' . $sid . '/live/player/' . rawurlencode($steamid));
                    exit;
                }
                $payload['source_steamid'] = $sourceSteam;
                $payload['destination_steamid'] = $destinationSteam;
                break;
            case 'give_weapon':
                $weapon = (string)($_POST['weapon'] ?? '');
                if (!in_array($weapon, self::WEAPONS, true)) { http_response_code(400); exit('Nicht freigegebene Waffe.'); }
                $payload['weapon'] = $weapon;
                break;
            case 'nickname_set':
                $name = $this->cleanText((string)($_POST['nickname'] ?? ''), 48);
                if ($name === '') {
                    View::flash('error', 'Bitte einen Server-Nickname auswählen oder eingeben.');
                    header('Location: /servers/' . $sid . '/live/player/' . rawurlencode($steamid));
                    exit;
                }
                $payload['nickname'] = $name;
                $personForName = LiveServerService::localPerson($steamid);
                $payload['steam_name'] = $this->cleanText((string)($personForName['steam_personaname'] ?? ''), 48);
                break;
            case 'nickname_restore':
                $personForName = LiveServerService::localPerson($steamid);
                $steamName = $this->cleanText((string)($personForName['steam_personaname'] ?? ''), 48);
                if ($steamName === '') {
                    View::flash('error', 'Kein aktueller Steam-Profilname gespeichert. Bitte zuerst das Steam-Profil synchronisieren.');
                    header('Location: /servers/' . $sid . '/live/player/' . rawurlencode($steamid));
                    exit;
                }
                $payload['steam_name'] = $steamName;
                break;
        }

        try {
            $commandTarget = $action === 'teleport_player' ? (string)($payload['source_steamid'] ?? $steamid) : $steamid;
            $commandId = LiveServerService::queue($sid, $action, $payload, $commandTarget);
            Audit::log('live.' . $action, 'server_player', $steamid, ['server_id'=>$sid,'payload'=>$this->auditPayload($action,$payload),'command_id'=>$commandId]);
            View::flash('success', 'Aktion wurde an den Gameserver übermittelt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Live-Aktion fehlgeschlagen: ' . $e->getMessage());
        }
        header('Location: /servers/' . $sid . '/live/player/' . rawurlencode($steamid));
    }

    public function privateMessage(string $id, string $steamid): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.message');
        Security::verifyCsrf();
        if (!$this->validSteamId($steamid)) { http_response_code(400); exit('Ungültige SteamID64.'); }
        $type = (string)($_POST['type'] ?? 'chat');
        if (!in_array($type, self::MESSAGE_TYPES, true)) $type = 'chat';
        $message = $this->cleanText((string)($_POST['message'] ?? ''), 500, true);
        if ($message === '') {
            View::flash('error', 'Die Nachricht darf nicht leer sein.');
        } else {
            try {
                $cid = LiveServerService::queue($sid, 'private_message', ['type'=>$type,'message'=>$message], $steamid);
                Audit::log('live.private_message', 'server_player', $steamid, ['server_id'=>$sid,'type'=>$type,'message'=>$message,'command_id'=>$cid]);
                View::flash('success', 'Persönliche Nachricht wurde an den Server übermittelt.');
            } catch (\Throwable $e) { View::flash('error', 'Nachricht konnte nicht gesendet werden: ' . $e->getMessage()); }
        }
        header('Location: /servers/' . $sid . '/live/player/' . rawurlencode($steamid));
    }

    public function broadcast(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.message');
        Security::verifyCsrf();
        $type = (string)($_POST['type'] ?? 'chat');
        if (!in_array($type, self::MESSAGE_TYPES, true)) $type = 'chat';
        $message = $this->cleanText((string)($_POST['message'] ?? ''), 500, true);
        if ($message === '') {
            View::flash('error', 'Die Servernachricht darf nicht leer sein.');
        } else {
            try {
                $cid = LiveServerService::queue($sid, 'broadcast', ['type'=>$type,'message'=>$message]);
                Audit::log('live.broadcast', 'server', (string)$sid, ['type'=>$type,'message'=>$message,'command_id'=>$cid]);
                View::flash('success', 'Servernachricht wurde übermittelt.');
            } catch (\Throwable $e) { View::flash('error', 'Servernachricht fehlgeschlagen: ' . $e->getMessage()); }
        }
        header('Location: /servers/' . $sid . '/live#messages');
    }

    public function giveWeaponGroup(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.weapons');
        Security::verifyCsrf();

        $weapon = (string)($_POST['weapon'] ?? '');
        $scope = strtolower((string)($_POST['scope'] ?? 'all'));
        $notify = !empty($_POST['notify']);
        $type = (string)($_POST['notify_type'] ?? 'chat');
        $message = $this->cleanText((string)($_POST['notify_message'] ?? 'Serververwaltung: {weapon} wurde ausgegeben.'), 180, true);

        if (!in_array($weapon, self::WEAPONS, true)) {
            View::flash('error', 'Nicht freigegebene Waffe.');
            header('Location: /servers/' . $sid . '/live#team-weapons');
            return;
        }
        if (!in_array($scope, ['ct','t','all'], true)) $scope = 'all';
        if (!in_array($type, self::MESSAGE_TYPES, true)) $type = 'chat';

        try {
            $cid = LiveServerService::queue($sid, 'give_weapon_group', [
                'weapon'=>$weapon,
                'scope'=>$scope,
                'notify'=>$notify,
                'notify_type'=>$type,
                'notify_message'=>$message,
            ]);
            Audit::log('live.give_weapon_group','server',(string)$sid,[
                'weapon'=>$weapon,'scope'=>$scope,'notify'=>$notify,'command_id'=>$cid
            ]);
            View::flash('success', 'Waffenvergabe an ' . strtoupper($scope === 'all' ? 'alle T/CT-Spieler' : $scope) . ' wurde übermittelt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Gruppen-Waffenvergabe fehlgeschlagen: ' . $e->getMessage());
        }

        header('Location: /servers/' . $sid . '/live#team-weapons');
    }

    public function mapChange(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.map');
        Security::verifyCsrf();
        $manualMap = trim((string)($_POST['map'] ?? ''));
        $selectedMap = trim((string)($_POST['map_choice'] ?? ''));
        $map = $manualMap !== '' ? $manualMap : $selectedMap;

        if (!preg_match('/^(?:[a-zA-Z0-9_\-\.]{2,80}|[0-9]{6,20})$/', $map)) {
            View::flash('error', 'Ungültiger Mapname bzw. Workshop-ID.');
            header('Location: /servers/' . $sid . '/live#server-actions'); exit;
        }

        // Bei Auswahl einer installierten Workshopmap kommen Anzeigename und
        // source_folder direkt aus dem lokalen Bridge-Scan (wie HeadshotOnly),
        // niemals aus MapChooser.
        $mapName = $map;
        $sourceFolder = '';
        try {
            $installed = LiveServerService::installedWorkshopMaps($sid);
            if (isset($installed[$map])) {
                $mapName = (string)$installed[$map]['title'];
                $sourceFolder = (string)$installed[$map]['source_folder'];
            }
        } catch (\Throwable) {
            // Manuelle normale Mapnamen bleiben weiterhin möglich.
        }

        $seconds = $this->countdown($_POST['seconds'] ?? 5);
        $template = $this->cleanText((string)($_POST['message'] ?? 'Mapwechsel zu {map} in {seconds} Sekunden.'), 240, true);
        try {
            $cid = LiveServerService::queue($sid, 'map_change', [
                'map'=>$map,
                'map_name'=>$mapName,
                'source_folder'=>$sourceFolder,
                'countdown_seconds'=>$seconds,
                'message'=>$template,
                'type'=>'center'
            ]);
            Audit::log('live.map_change', 'server', (string)$sid, ['map'=>$map,'map_name'=>$mapName,'source_folder'=>$sourceFolder,'seconds'=>$seconds,'command_id'=>$cid]);
            View::flash('success', $seconds > 0 ? 'Mapwechsel wurde angekündigt und geplant.' : 'Mapwechsel wurde an den Server übermittelt.');
        } catch (\Throwable $e) { View::flash('error', 'Mapwechsel fehlgeschlagen: ' . $e->getMessage()); }
        header('Location: /servers/' . $sid . '/live#server-actions');
    }

    public function restart(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'server.power.restart');
        Security::verifyCsrf();
        $seconds = $this->restartCountdown($_POST['seconds'] ?? 5);
        $message = $this->cleanText((string)($_POST['message'] ?? 'Serverneustart in {seconds} Sekunden.'), 240, true);
        try {
            Audit::log('server.power.restart.request', 'server', (string)$sid, ['seconds'=>$seconds,'message'=>$message]);
            LiveServerService::restart($sid, $seconds, $message, 'center');
            Audit::log('server.power.restart.execute', 'server', (string)$sid, ['seconds'=>$seconds]);
            View::flash('success', 'Serverneustart wurde ausgeführt.');
        } catch (\Throwable $e) { View::flash('error', 'Serverneustart fehlgeschlagen: ' . $e->getMessage()); }
        header('Location: /servers/' . $sid . '/live#server-actions');
    }

    public function separatePlayers(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.teleport');
        Security::verifyCsrf();

        try {
            $cid = LiveServerService::queue($sid, 'separate_players');
            Audit::log('live.separate_players', 'server', (string)$sid, [
                'command_id'=>$cid,
            ]);
            View::flash(
                'success',
                'Kollisionsprüfung wurde an den Gameserver übermittelt. Überlappende Spieler werden auf freie Positionen nebeneinander versetzt.'
            );
        } catch (\Throwable $e) {
            View::flash('error', 'Spieler konnten nicht getrennt werden: ' . $e->getMessage());
        }

        header('Location: /servers/' . $sid . '/live#server-actions');
    }

    public function roundRestart(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.map');
        Security::verifyCsrf();
        $seconds = $this->countdown($_POST['seconds'] ?? 5);
        try {
            $cid = LiveServerService::queue($sid, 'round_restart', ['countdown_seconds'=>$seconds,'message'=>'Rundenneustart in {seconds} Sekunden.','type'=>'center']);
            Audit::log('live.round_restart', 'server', (string)$sid, ['seconds'=>$seconds,'command_id'=>$cid]);
            View::flash('success', 'Rundenneustart wurde angekündigt und geplant.');
        } catch (\Throwable $e) { View::flash('error', 'Rundenneustart fehlgeschlagen: ' . $e->getMessage()); }
        header('Location: /servers/' . $sid . '/live#server-actions');
    }

    public function whitelistAction(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.whitelist');
        Security::verifyCsrf();

        $operation=strtolower(trim((string)($_POST['operation'] ?? '')));
        try {
            $result=match($operation) {
                'add_one'=>WhitelistLiveService::addOne($sid,(string)($_POST['steam_id64'] ?? '')),
                'add_selected'=>WhitelistLiveService::addMany($sid,(array)($_POST['steam_ids'] ?? [])),
                'remove_selected'=>WhitelistLiveService::removeMany($sid,(array)($_POST['steam_ids'] ?? [])),
                'replace'=>WhitelistLiveService::replaceWith($sid,(string)($_POST['list_text'] ?? '')),
                default=>throw new \InvalidArgumentException('Unbekannte Whitelist-Aktion.'),
            };

            Audit::log('live.whitelist.'.$operation,'server',(string)$sid,[
                'added'=>$result['added'] ?? [],
                'removed'=>$result['removed'] ?? [],
                'command_ids'=>$result['commands'] ?? [],
                'backup'=>$result['backup'] ?? null,
            ]);

            $parts=[];
            if (!empty($result['added'])) $parts[]=count($result['added']).' hinzugefügt';
            if (!empty($result['removed'])) $parts[]=count($result['removed']).' entfernt';
            if (!$parts) $parts[]='keine Änderung erforderlich';

            $message='Whitelist-Aktion übermittelt: '.implode(', ',$parts).'.';
            if (!empty($result['bridge_sync'])) {
                $message.=' Bridge-Sync aktiv: !uwl wird ausgeführt und whitelist.txt anschließend ohne die entfernten SteamIDs neu geschrieben.';
            }
            if (!empty($result['commands'])) {
                $this->rememberWhitelistPending($sid,$operation,$result);
                $message.=' Das Panel wartet jetzt auf die direkte Bestätigung aus der echten Listendatei.';
            }
            if (!empty($result['backup'])) $message.=' Vorherige Liste wurde gesichert.';
            View::flash('success',$message);
        } catch (\Throwable $e) {
            View::flash('error','Whitelist-Verwaltung fehlgeschlagen: '.$e->getMessage());
        }

        header('Location: /servers/'.$sid.'/live#whitelist');
    }

    public function whitelistVerify(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.whitelist');

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');

        $token=strtolower(trim((string)($_GET['token'] ?? '')));
        if (!preg_match('/^[a-f0-9]{24}$/',$token)) {
            http_response_code(400);
            echo json_encode(['ok'=>false,'error'=>'Ungültige Bestätigungs-ID.'],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
            return;
        }

        $pending=$_SESSION['_whitelist_pending'][$sid][$token] ?? null;
        if (!is_array($pending)) {
            http_response_code(404);
            echo json_encode(['ok'=>false,'error'=>'Diese Whitelist-Bestätigung ist nicht mehr aktiv.'],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
            return;
        }

        try {
            $result=WhitelistLiveService::verifyPending($sid,$pending);

            if (in_array((string)($result['state'] ?? ''),['confirmed','failed','timeout'],true)) {
                unset($_SESSION['_whitelist_pending'][$sid][$token]);
                if (empty($_SESSION['_whitelist_pending'][$sid])) {
                    unset($_SESSION['_whitelist_pending'][$sid]);
                }
            }

            echo json_encode(
                ['ok'=>true,'token'=>$token] + $result,
                JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR
            );
        } catch (\Throwable $e) {
            http_response_code(500);
            echo json_encode(
                ['ok'=>false,'error'=>$e->getMessage()],
                JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR
            );
        }
    }

    private function rememberWhitelistPending(int $serverId,string $operation,array $result): void
    {
        $commands=array_values(array_filter(
            array_map('strval',(array)($result['commands'] ?? [])),
            static fn(string $id):bool=>preg_match('/^[A-Za-z0-9_-]{1,100}$/',$id)===1
        ));

        if (!$commands) return;

        $token=bin2hex(random_bytes(12));
        $_SESSION['_whitelist_pending'] ??= [];
        $_SESSION['_whitelist_pending'][$serverId] ??= [];

        foreach ($_SESSION['_whitelist_pending'][$serverId] as $key=>$pending) {
            if (!is_array($pending) || time()-(int)($pending['created_at'] ?? 0)>120) {
                unset($_SESSION['_whitelist_pending'][$serverId][$key]);
            }
        }

        $_SESSION['_whitelist_pending'][$serverId][$token]=[
            'token'=>$token,
            'operation'=>$operation,
            'created_at'=>time(),
            'added'=>array_values((array)($result['added'] ?? [])),
            'removed'=>array_values((array)($result['removed'] ?? [])),
            'commands'=>$commands,
            'command_map'=>array_values((array)($result['command_map'] ?? [])),
            'list_path'=>(string)($result['list_path'] ?? ''),
            'mode_label'=>(string)($result['mode_label'] ?? 'Whitelist'),
        ];

        if (count($_SESSION['_whitelist_pending'][$serverId])>8) {
            uasort(
                $_SESSION['_whitelist_pending'][$serverId],
                static fn(array $a,array $b):int=>(int)($a['created_at'] ?? 0)<=>(int)($b['created_at'] ?? 0)
            );
            while (count($_SESSION['_whitelist_pending'][$serverId])>8) {
                array_shift($_SESSION['_whitelist_pending'][$serverId]);
            }
        }
    }

    private function whitelistPendingForServer(int $serverId): array
    {
        $all=(array)($_SESSION['_whitelist_pending'][$serverId] ?? []);
        $out=[];

        foreach ($all as $token=>$pending) {
            if (!is_array($pending) || time()-(int)($pending['created_at'] ?? 0)>120) {
                unset($_SESSION['_whitelist_pending'][$serverId][$token]);
                continue;
            }
            $out[]=$pending;
        }

        if (empty($_SESSION['_whitelist_pending'][$serverId])) {
            unset($_SESSION['_whitelist_pending'][$serverId]);
        }

        usort($out,static fn(array $a,array $b):int=>(int)$a['created_at']<=>(int)$b['created_at']);
        return $out;
    }

    public function serverCommand(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'server.console.execute');
        Security::verifyCsrf();

        $command = $this->cleanText((string)($_POST['command'] ?? ''), 240);
        if ($command === '') {
            View::flash('error', 'Der Serverbefehl darf nicht leer sein.');
        } else {
            try {
                $cid = LiveServerService::queue($sid, 'server_command', ['command'=>$command]);
                Audit::log('server.console.execute', 'server', (string)$sid, ['command'=>$command,'command_id'=>$cid]);
                View::flash('success', 'Serverbefehl wurde an die Bridge übermittelt.');
            } catch (\Throwable $e) {
                View::flash('error', 'Serverbefehl fehlgeschlagen: ' . $e->getMessage());
            }
        }
        header('Location: /servers/' . $sid . '/live#advanced');
    }

    public function cvarSet(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.cvar');
        Security::verifyCsrf();

        $name = trim((string)($_POST['name'] ?? ''));
        $value = $this->cleanText((string)($_POST['value'] ?? ''), 120);
        if (!preg_match('/^[A-Za-z0-9_.-]{1,80}$/', $name)) {
            View::flash('error', 'Ungültiger ConVar-Name.');
        } else {
            try {
                $cid = LiveServerService::queue($sid, 'cvar_set', ['name'=>$name,'value'=>$value]);
                Audit::log('live.cvar_set', 'server', (string)$sid, ['name'=>$name,'value'=>$value,'command_id'=>$cid]);
                View::flash('success', 'ConVar wurde an die Bridge übermittelt.');
            } catch (\Throwable $e) {
                View::flash('error', 'ConVar konnte nicht gesetzt werden: ' . $e->getMessage());
            }
        }
        header('Location: /servers/' . $sid . '/live#advanced');
    }

    public function botAction(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.bots');
        Security::verifyCsrf();
        $op=strtolower(trim((string)($_POST['operation']??'')));
        try {
            $action=''; $payload=[];
            switch($op){
                case 'kick': $action='bot_kick'; break;
                case 'add_t': $action='bot_add'; $payload=['team'=>'t']; break;
                case 'add_ct': $action='bot_add'; $payload=['team'=>'ct']; break;
                case 'quota': $action='bot_quota'; $payload=['value'=>max(0,min(30,(int)($_POST['value']??0)))]; break;
                case 'stop': $action='bot_stop'; $payload=['enabled'=>((string)($_POST['enabled']??'1'))==='1']; break;
                case 'difficulty': $action='bot_difficulty'; $payload=['value'=>max(0,min(5,(int)($_POST['value']??2)))]; break;
                default: throw new \InvalidArgumentException('Unbekannte Bot-Aktion.');
            }
            $cid=LiveServerService::queue($sid,$action,$payload);
            Audit::log('live.'.$action,'server',(string)$sid,['payload'=>$payload,'command_id'=>$cid]);
            View::flash('success','Bot-Aktion wurde an den Gameserver übermittelt.');
        } catch(\Throwable $e){ View::flash('error','Bot-Aktion fehlgeschlagen: '.$e->getMessage()); }
        header('Location: /servers/'.$sid.'/live#bots-rules');
    }

    public function botPlayerAction(string $id): void
    {
        $sid = (int)$id;
        Security::verifyCsrf();
        $operation = strtolower(trim((string)($_POST['operation'] ?? '')));
        $permission = match ($operation) {
            'kick' => 'live.bot.kick',
            'slay' => 'live.bot.slay',
            'team' => 'live.bot.team',
            default => '',
        };
        if ($permission === '') { http_response_code(400); exit('Ungültige Bot-Aktion.'); }
        $this->requireLive($sid, $permission);

        $userId = (int)($_POST['user_id'] ?? 0);
        $playerId = (int)($_POST['player_id'] ?? -1);
        if ($userId <= 0 || $playerId < 0) {
            View::flash('error', 'Bot ist nicht mehr eindeutig identifizierbar. Live-Status bitte aktualisieren.');
            header('Location: /servers/' . $sid . '/live#players');
            return;
        }

        try {
            $action = match ($operation) {
                'kick' => 'bot_player_kick',
                'slay' => 'bot_player_slay',
                'team' => 'bot_player_team',
            };
            $payload = ['user_id'=>$userId, 'player_id'=>$playerId];
            if ($operation === 'team') {
                $team = strtolower(trim((string)($_POST['team'] ?? 'spectator')));
                if (!in_array($team, self::TEAMS, true)) throw new \InvalidArgumentException('Ungültiges Bot-Team.');
                $payload['team'] = $team;
            }
            $cid = LiveServerService::queue($sid, $action, $payload);
            Audit::log('live.' . $action, 'server', (string)$sid, [
                'bot_user_id'=>$userId,
                'bot_player_id'=>$playerId,
                'team'=>$payload['team'] ?? null,
                'command_id'=>$cid,
            ]);
            View::flash('success', 'Bot-Aktion wurde an den Gameserver übermittelt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Bot-Aktion fehlgeschlagen: ' . $e->getMessage());
        }
        header('Location: /servers/' . $sid . '/live#players');
    }

    public function layout(string $id): void
    {
        $sid = (int)$id;
        Security::verifyCsrf();
        $this->requireLive($sid, 'live.view');
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        $user = Auth::user();
        $userId = (int)($user['id'] ?? 0);
        $operation = strtolower(trim((string)($_POST['operation'] ?? 'save')));
        $allServers = ((string)($_POST['all_servers'] ?? '0')) === '1';
        try {
            if ($operation === 'reset') {
                LiveLayoutService::reset($userId, $sid, $allServers);
                $layout = LiveLayoutService::get($userId, $sid);
                echo json_encode(['ok'=>true,'layout'=>$layout], JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);
                return;
            }
            if ($operation !== 'save') throw new \InvalidArgumentException('Ungültige Layout-Aktion.');
            $decoded = json_decode((string)($_POST['layout_json'] ?? '{}'), true, 32, JSON_THROW_ON_ERROR);
            if (!is_array($decoded)) throw new \InvalidArgumentException('Ungültiges Layout.');
            $layout = LiveLayoutService::save($userId, $sid, $decoded, $allServers);
            echo json_encode(['ok'=>true,'layout'=>$layout], JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);
        } catch (\Throwable $e) {
            http_response_code(400);
            echo json_encode(['ok'=>false,'error'=>$e->getMessage()], JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE);
        }
    }

    public function rulesAction(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.rules');
        Security::verifyCsrf();
        $group=strtolower(trim((string)($_POST['group']??'')));
        $payload=[];
        try {
            if($group==='physics'){
                if(!Auth::can('live.gravity') || !Auth::can('live.speed')) throw new \RuntimeException('Keine Berechtigung für Gravity/Speed.');
                $payload=[
                    'gravity'=>max(100,min(2000,(int)($_POST['gravity']??800))),
                    'global_speed'=>max(0.25,min(3.0,round((float)($_POST['global_speed']??1.0),2))),
                ];
            } elseif($group==='gameplay'){
                $payload=[
                    'friendly_fire'=>!empty($_POST['friendly_fire']),
                    'infinite_ammo'=>max(0,min(2,(int)($_POST['infinite_ammo']??0))),
                    'buy_anywhere'=>max(0,min(3,(int)($_POST['buy_anywhere']??0))),
                    'bunnyhop'=>!empty($_POST['bunnyhop']),
                    'auto_bhop'=>!empty($_POST['auto_bhop']),
                    'airaccelerate'=>max(1.0,min(2000.0,(float)($_POST['airaccelerate']??12))),
                    'fall_damage_scale'=>max(0.0,min(5.0,(float)($_POST['fall_damage_scale']??1))),
                    'damage_scale'=>max(0.0,min(10.0,(float)($_POST['damage_scale']??1))),
                ];
            } elseif($group==='match'){
                $payload=[
                    'freeze_time'=>max(0,min(60,(int)($_POST['freeze_time']??15))),
                    'round_time'=>max(0.5,min(60.0,(float)($_POST['round_time']??1.92))),
                    'buy_time'=>max(0,min(9999,(int)($_POST['buy_time']??20))),
                    'start_money'=>max(0,min(65535,(int)($_POST['start_money']??800))),
                    'max_money'=>max(0,min(65535,(int)($_POST['max_money']??16000))),
                    'max_rounds'=>max(0,min(100,(int)($_POST['max_rounds']??24))),
                    'auto_team_balance'=>!empty($_POST['auto_team_balance']),
                    'limitteams'=>max(0,min(10,(int)($_POST['limitteams']??2))),
                ];
            } elseif($group==='headshot'){
                $this->requireLive($sid,'live.weapon_mode');
                $enabled=((string)($_POST['enabled']??'0'))==='1';
                $cid=LiveServerService::queue($sid,'headshot_only',['enabled'=>$enabled]);
                Audit::log('live.headshot_only','server',(string)$sid,['enabled'=>$enabled,'command_id'=>$cid]);
                View::flash('success','Headshot-Only wurde an den Gameserver übermittelt.');
                header('Location: /servers/'.$sid.'/live#weapon-modes');
                return;
            } else {
                throw new \InvalidArgumentException('Unbekannte Regelgruppe.');
            }
            $cid=LiveServerService::queue($sid,'server_rules',$payload);
            Audit::log('live.server_rules','server',(string)$sid,['group'=>$group,'payload'=>$payload,'command_id'=>$cid]);
            View::flash('success','Spielregeln wurden an den Gameserver übermittelt.');
        } catch(\Throwable $e){ View::flash('error','Spielregeln konnten nicht gesetzt werden: '.$e->getMessage()); }
        header('Location: /servers/'.$sid.'/live#bots-rules');
    }

    public function quickRule(string $id): void
    {
        $sid = (int)$id;
        $this->requireLive($sid, 'live.rules');
        Security::verifyCsrf();
        $key = strtolower(trim((string)($_POST['key'] ?? '')));
        $value = (string)($_POST['value'] ?? '');

        try {
            $action = 'server_rules';
            $payload = [];
            if ($key === 'gravity') {
                $this->requireLive($sid, 'live.gravity');
                $payload = ['gravity'=>max(100,min(2000,(int)$value))];
            } elseif ($key === 'global_speed') {
                $this->requireLive($sid, 'live.speed');
                $payload = ['global_speed'=>max(0.25,min(3.0,round((float)$value,2)))];
            } elseif ($key === 'friendly_fire') {
                $payload = ['friendly_fire'=>$value === '1'];
            } elseif ($key === 'infinite_ammo') {
                $payload = ['infinite_ammo'=>max(0,min(2,(int)$value))];
            } elseif ($key === 'headshot_only') {
                $this->requireLive($sid, 'live.weapon_mode');
                $action = 'headshot_only';
                $payload = ['enabled'=>$value === '1'];
            } elseif ($key === 'weapon_mode') {
                $this->requireLive($sid, 'live.weapon_mode');
                $allowed=['normal','pistols_only','deagle_only','awp_only','scout_only','rifles_only','smg_only','shotguns_only','snipers_only','knife_only','zeus_only','grenades_only'];
                $mode = in_array($value,$allowed,true) ? $value : 'normal';
                $status = LiveServerService::status($sid);
                $rules = (array)($status['rules'] ?? []);
                $action = 'weapon_mode';
                $payload = [
                    'mode'=>$mode,
                    'custom_allowed'=>[],
                    'team_limits'=>(array)($rules['weapon_team_limits'] ?? []),
                    'team_limits_include_bots'=>!empty($rules['weapon_team_limits_include_bots']),
                    'apply_now'=>true,
                ];
            } else {
                throw new \InvalidArgumentException('Unbekannte Schnellregel.');
            }

            $cid = LiveServerService::queue($sid, $action, $payload);
            Audit::log('live.rule_quick', 'server', (string)$sid, ['key'=>$key,'value'=>$value,'action'=>$action,'payload'=>$payload,'command_id'=>$cid]);
            View::flash('success', 'Live-Regel wurde an den Gameserver übermittelt.');
        } catch (\Throwable $e) {
            View::flash('error', 'Live-Regel konnte nicht geändert werden: ' . $e->getMessage());
        }
        header('Location: /servers/' . $sid . '/live#active-rules');
    }

    public function weaponMode(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.weapon_mode');
        Security::verifyCsrf();
        $allowedModes=['normal','pistols_only','deagle_only','awp_only','scout_only','rifles_only','smg_only','shotguns_only','snipers_only','knife_only','zeus_only','grenades_only','custom'];
        $mode=strtolower(trim((string)($_POST['mode']??'normal')));
        if(!in_array($mode,$allowedModes,true)) $mode='normal';
        $custom=array_values(array_intersect(array_map('strval',(array)($_POST['allowed_weapons']??[])),self::WEAPONS));
        $rawLimits=(array)($_POST['team_limits']??[]);
        $teamLimits=[];
        foreach($rawLimits as $weapon=>$rawLimit){
            $weapon=(string)$weapon;
            if(!in_array($weapon,self::WEAPONS,true)) continue;
            $limit=max(0,min(10,(int)$rawLimit));
            if($limit>0) $teamLimits[$weapon]=$limit;
        }
        try {
            $payload=[
                'mode'=>$mode,
                'custom_allowed'=>$custom,
                'apply_now'=>!empty($_POST['apply_now']),
                'team_limits'=>$teamLimits,
                'team_limits_include_bots'=>!empty($_POST['team_limits_include_bots']),
            ];
            $cid=LiveServerService::queue($sid,'weapon_mode',$payload);
            Audit::log('live.weapon_mode','server',(string)$sid,['payload'=>$payload,'command_id'=>$cid]);
            View::flash('success','Waffenmodus wurde an den Gameserver übermittelt.');
        } catch(\Throwable $e){ View::flash('error','Waffenmodus fehlgeschlagen: '.$e->getMessage()); }
        header('Location: /servers/'.$sid.'/live#weapon-modes');
    }

    public function scopeAction(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.rules');
        Security::verifyCsrf();
        $scope=strtolower((string)($_POST['scope']??'all'));
        if(!in_array($scope,['t','ct','all'],true)) $scope='all';
        $operation=strtolower((string)($_POST['operation']??''));
        $allowed=['respawn','freeze','unfreeze','health','armor','godmode_on','godmode_off'];
        if(!in_array($operation,$allowed,true)){ http_response_code(400); exit('Ungültige Gruppenaktion.'); }
        $permission=match($operation){
            'respawn'=>'live.respawn','freeze','unfreeze'=>'live.freeze','health'=>'live.health','armor'=>'live.armor','godmode_on','godmode_off'=>'live.godmode',default=>'live.rules'
        };
        $this->requireLive($sid,$permission);
        $payload=['scope'=>$scope,'operation'=>$operation];
        if(in_array($operation,['health','armor'],true)) $payload['value']=max(0,min(500,(int)($_POST['value']??100)));
        try{
            $cid=LiveServerService::queue($sid,'scope_action',$payload);
            Audit::log('live.scope_action','server',(string)$sid,['payload'=>$payload,'command_id'=>$cid]);
            View::flash('success','Gruppenaktion wurde an den Gameserver übermittelt.');
        }catch(\Throwable $e){ View::flash('error','Gruppenaktion fehlgeschlagen: '.$e->getMessage()); }
        header('Location: /servers/'.$sid.'/live#group-actions');
    }

    public function roundControl(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.rules');
        Security::verifyCsrf();
        $operation=strtolower((string)($_POST['operation']??''));
        if(!in_array($operation,['swap_teams','scramble_teams'],true)){ http_response_code(400); exit('Ungültige Rundenaktion.'); }
        try{
            $cid=LiveServerService::queue($sid,'round_control',['operation'=>$operation]);
            Audit::log('live.round_control','server',(string)$sid,['operation'=>$operation,'command_id'=>$cid]);
            View::flash('success','Runden-/Teamaktion wurde übermittelt.');
        }catch(\Throwable $e){ View::flash('error','Rundenaktion fehlgeschlagen: '.$e->getMessage()); }
        header('Location: /servers/'.$sid.'/live#round-control');
    }

    public function warmupControl(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.rules');
        Security::verifyCsrf();
        $operation=strtolower((string)($_POST['operation']??''));
        if(!in_array($operation,['start','end'],true)){ http_response_code(400); exit('Ungültige Warmup-Aktion.'); }
        try{
            $cid=LiveServerService::queue($sid,'warmup_control',['operation'=>$operation]);
            Audit::log('live.warmup_control','server',(string)$sid,['operation'=>$operation,'command_id'=>$cid]);
            View::flash('success','Warmup-Aktion wurde übermittelt.');
        }catch(\Throwable $e){ View::flash('error','Warmup-Aktion fehlgeschlagen: '.$e->getMessage()); }
        header('Location: /servers/'.$sid.'/live#round-control');
    }

    public function resetRules(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.rules');
        Security::verifyCsrf();
        try{
            $cid=LiveServerService::queue($sid,'rules_reset',['reset_players'=>!empty($_POST['reset_players'])]);
            Audit::log('live.rules_reset','server',(string)$sid,['command_id'=>$cid,'reset_players'=>!empty($_POST['reset_players'])]);
            View::flash('success','Standardregeln werden wiederhergestellt.');
        }catch(\Throwable $e){ View::flash('error','Reset fehlgeschlagen: '.$e->getMessage()); }
        header('Location: /servers/'.$sid.'/live#active-rules');
    }

    public function presetAction(string $id): void
    {
        $sid=(int)$id;
        $this->requireLive($sid,'live.presets');
        Security::verifyCsrf();
        $action=strtolower(trim((string)($_POST['preset_action']??'apply')));
        try{
            if($action==='save_current'){
                $name=$this->cleanText((string)($_POST['name']??''),48);
                if($name==='') throw new \InvalidArgumentException('Bitte einen Preset-Namen angeben.');
                $status=LiveServerService::status($sid);
                $rules=(array)($status['rules']??[]);
                if(!$rules) throw new \RuntimeException('Die Bridge hat noch keinen Live-Regelstatus geliefert.');
                $this->saveRulePreset($sid,$name,$rules);
                Audit::log('live.preset_save','server',(string)$sid,['name'=>$name]);
                View::flash('success','Aktueller Live-Regelstatus wurde als Preset gespeichert.');
            } elseif($action==='delete'){
                $key=preg_replace('/[^a-z0-9_-]/','',strtolower((string)($_POST['preset']??'')))??'';
                $this->deleteRulePreset($sid,$key);
                Audit::log('live.preset_delete','server',(string)$sid,['preset'=>$key]);
                View::flash('success','Eigenes Preset wurde gelöscht.');
            } else {
                $key=(string)($_POST['preset']??'');
                $preset=$this->findRulePreset($sid,$key);
                if(!$preset) throw new \InvalidArgumentException('Preset nicht gefunden.');
                $duration=(string)($_POST['duration']??'manual');
                if(!in_array($duration,['manual','map','rounds','minutes'],true)) $duration='manual';
                $payload=[
                    'preset_name'=>(string)$preset['name'],
                    'rules'=>(array)$preset['rules'],
                    'duration'=>$duration,
                    'rounds'=>max(1,min(30,(int)($_POST['rounds']??1))),
                    'minutes'=>max(1,min(240,(int)($_POST['minutes']??10))),
                ];
                $cid=LiveServerService::queue($sid,'preset_apply',$payload);
                Audit::log('live.preset_apply','server',(string)$sid,['preset'=>$key,'duration'=>$duration,'command_id'=>$cid]);
                View::flash('success','Preset wurde an den Gameserver übermittelt.');
            }
        }catch(\Throwable $e){ View::flash('error','Preset-Aktion fehlgeschlagen: '.$e->getMessage()); }
        header('Location: /servers/'.$sid.'/live#presets');
    }

    private function rulePresets(int $serverId): array
    {
        return array_merge($this->builtInRulePresets(),$this->customRulePresets($serverId));
    }

    private function builtInRulePresets(): array
    {
        return [
            ['key'=>'competitive','name'=>'Competitive / Standard','builtin'=>true,'rules'=>['reset'=>true]],
            ['key'=>'training','name'=>'Training','builtin'=>true,'rules'=>['infinite_ammo'=>2,'buy_anywhere'=>1,'headshot_only'=>false,'weapon_mode'=>'normal','weapon_team_limits'=>[]]],
            ['key'=>'headshot_training','name'=>'Headshot Training','builtin'=>true,'rules'=>['infinite_ammo'=>2,'buy_anywhere'=>1,'headshot_only'=>true,'weapon_mode'=>'normal','weapon_team_limits'=>[]]],
            ['key'=>'awp_training','name'=>'AWP Training','builtin'=>true,'rules'=>['infinite_ammo'=>2,'buy_anywhere'=>1,'weapon_mode'=>'awp_only','weapon_team_limits'=>[]]],
            ['key'=>'pistols_only','name'=>'Pistols Only','builtin'=>true,'rules'=>['weapon_mode'=>'pistols_only','weapon_team_limits'=>[]]],
            ['key'=>'knife_round','name'=>'Knife Only','builtin'=>true,'rules'=>['weapon_mode'=>'knife_only','weapon_team_limits'=>[]]],
            ['key'=>'low_gravity','name'=>'Low Gravity Fun','builtin'=>true,'rules'=>['gravity'=>400,'global_speed'=>1.25]],
        ];
    }

    private function customRulePresets(int $serverId): array
    {
        $key='live.presets.server.'.$serverId;
        $stmt=Database::connection()->prepare('SELECT setting_value FROM app_settings WHERE setting_key=?');
        $stmt->execute([$key]);
        $raw=(string)($stmt->fetchColumn()?:'');
        if($raw==='') return [];
        $decoded=json_decode($raw,true);
        if(!is_array($decoded)) return [];
        $out=[];
        foreach($decoded as $row){
            if(!is_array($row) || empty($row['key']) || empty($row['name']) || !is_array($row['rules']??null)) continue;
            $row['builtin']=false; $out[]=$row;
        }
        return $out;
    }

    private function saveRulePreset(int $serverId,string $name,array $rules): void
    {
        $presets=$this->customRulePresets($serverId);
        $slug=strtolower(trim(preg_replace('/[^A-Za-z0-9_-]+/','-', $name)??'','-'));
        if($slug==='') $slug='preset-'.bin2hex(random_bytes(3));
        $slug='custom-'.$slug;
        $presets=array_values(array_filter($presets,static fn(array $p):bool=>(string)$p['key']!==$slug));
        $presets[]=['key'=>$slug,'name'=>$name,'builtin'=>false,'rules'=>$rules];
        if(count($presets)>30) $presets=array_slice($presets,-30);
        $settingKey='live.presets.server.'.$serverId;
        Database::connection()->prepare('INSERT INTO app_settings(setting_key,setting_value,encrypted,updated_at) VALUES(?,?,0,?) ON CONFLICT(setting_key) DO UPDATE SET setting_value=excluded.setting_value,updated_at=excluded.updated_at')
            ->execute([$settingKey,json_encode($presets,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),date('c')]);
    }

    private function deleteRulePreset(int $serverId,string $key): void
    {
        if(!str_starts_with($key,'custom-')) throw new \InvalidArgumentException('Nur eigene Presets können gelöscht werden.');
        $presets=array_values(array_filter($this->customRulePresets($serverId),static fn(array $p):bool=>(string)$p['key']!==$key));
        $settingKey='live.presets.server.'.$serverId;
        Database::connection()->prepare('INSERT INTO app_settings(setting_key,setting_value,encrypted,updated_at) VALUES(?,?,0,?) ON CONFLICT(setting_key) DO UPDATE SET setting_value=excluded.setting_value,updated_at=excluded.updated_at')
            ->execute([$settingKey,json_encode($presets,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR),date('c')]);
    }

    private function findRulePreset(int $serverId,string $key): ?array
    {
        foreach($this->rulePresets($serverId) as $preset){ if((string)($preset['key']??'')===$key) return $preset; }
        return null;
    }

    private function requireLive(int $serverId, string $permission): void
    {
        Auth::requirePermission($permission);
        Auth::requireServer($serverId, 'live');
    }

    /**
     * @return array<int,array{name:string,source:string}>
     */
    private function nicknameChoices(?array $person, array $livePlayer): array
    {
        $choices = [];
        $seen = [];

        $add = static function(?string $name, string $source) use (&$choices, &$seen): void {
            $name = trim((string)$name);
            if ($name === '') return;
            $key = function_exists('mb_strtolower') ? mb_strtolower($name, 'UTF-8') : strtolower($name);
            if (isset($seen[$key])) return;
            $seen[$key] = true;
            $choices[] = ['name'=>$name, 'source'=>$source];
        };

        $add((string)($person['steam_personaname'] ?? ''), 'Aktueller Steam-Profilname');
        $add((string)($livePlayer['original_name'] ?? ''), 'Gespeicherter Steamname der Bridge');

        foreach ((array)($livePlayer['nickname_history'] ?? []) as $name) {
            $add((string)$name, 'Server-Namenshistorie');
        }

        if (!empty($person['id'])) {
            $stmt = Database::connection()->prepare(
                'SELECT steam_name FROM steam_name_history WHERE player_id=? ORDER BY last_seen_at DESC'
            );
            $stmt->execute([(int)$person['id']]);
            foreach ($stmt->fetchAll() as $row) {
                $add((string)($row['steam_name'] ?? ''), 'Steam-Namenshistorie');
            }
        }

        $add((string)($livePlayer['nickname_override'] ?? ''), 'Aktueller Server-Nickname');
        $add((string)($livePlayer['name'] ?? ''), 'Aktuell auf dem Server sichtbar');

        return $choices;
    }

    private function validSteamId(string $steamId): bool
    {
        return (bool)preg_match('/^7656119\d{10}$/', $steamId);
    }

    private function cleanText(string $value, int $maxLength, bool $allowNewlines = false): string
    {
        $value = trim($value);
        if (!$allowNewlines) $value = preg_replace('/[\r\n\t]+/', ' ', $value) ?? $value;
        $value = preg_replace('/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/u', '', $value) ?? $value;
        return function_exists('mb_substr') ? mb_substr($value, 0, $maxLength) : substr($value, 0, $maxLength);
    }

    private function punishmentMinutes(mixed $value): int
    {
        $minutes = (int)$value;
        return in_array($minutes, [10,30,60,360,1440,10080,43200,0], true) ? $minutes : 60;
    }

    private function countdown(mixed $value): int
    {
        $seconds = (int)$value;
        return in_array($seconds, [0,5,10,30,60], true) ? $seconds : 5;
    }

    private function restartCountdown(mixed $value): int
    {
        $seconds = (int)$value;
        return in_array($seconds, [0,5,10,30], true) ? $seconds : 5;
    }

    private function auditPayload(string $action, array $payload): array
    {
        if ($action === 'nickname_set') return ['nickname'=>$payload['nickname'] ?? '','steam_name'=>$payload['steam_name'] ?? ''];
        if ($action === 'nickname_restore') return ['steam_name'=>$payload['steam_name'] ?? ''];
        if (in_array($action, ['goto_player','bring_player'], true)) return ['admin_steamid'=>$payload['admin_steamid'] ?? ''];
        if ($action === 'teleport_player') return ['source_steamid'=>$payload['source_steamid'] ?? '','destination_steamid'=>$payload['destination_steamid'] ?? ''];
        return $payload;
    }

    public static function weaponOptions(): array
    {
        return [
            'Pistolen' => [
                'weapon_glock'=>'Glock-18','weapon_hkp2000'=>'P2000','weapon_usp_silencer'=>'USP-S','weapon_p250'=>'P250','weapon_fiveseven'=>'Five-SeveN','weapon_tec9'=>'Tec-9','weapon_cz75a'=>'CZ75-Auto','weapon_elite'=>'Dual Berettas','weapon_deagle'=>'Desert Eagle','weapon_revolver'=>'R8 Revolver',
            ],
            'SMGs' => [
                'weapon_mac10'=>'MAC-10','weapon_mp9'=>'MP9','weapon_mp7'=>'MP7','weapon_mp5sd'=>'MP5-SD','weapon_ump45'=>'UMP-45','weapon_p90'=>'P90','weapon_bizon'=>'PP-Bizon',
            ],
            'Gewehre' => [
                'weapon_galilar'=>'Galil AR','weapon_famas'=>'FAMAS','weapon_ak47'=>'AK-47','weapon_m4a1'=>'M4A4','weapon_m4a1_silencer'=>'M4A1-S','weapon_aug'=>'AUG','weapon_sg556'=>'SG 553',
            ],
            'Sniper' => [
                'weapon_ssg08'=>'SSG 08','weapon_awp'=>'AWP','weapon_scar20'=>'SCAR-20','weapon_g3sg1'=>'G3SG1',
            ],
            'Schwere Waffen' => [
                'weapon_nova'=>'Nova','weapon_xm1014'=>'XM1014','weapon_mag7'=>'MAG-7','weapon_sawedoff'=>'Sawed-Off','weapon_m249'=>'M249','weapon_negev'=>'Negev',
            ],
            'Granaten' => [
                'weapon_hegrenade'=>'HE-Granate','weapon_flashbang'=>'Flashbang','weapon_smokegrenade'=>'Smoke','weapon_molotov'=>'Molotov','weapon_incgrenade'=>'Brandgranate','weapon_decoy'=>'Decoy',
            ],
            'Spezial' => [
                'weapon_taser'=>'Zeus x27','weapon_knife'=>'Messer',
            ],
        ];
    }
}
