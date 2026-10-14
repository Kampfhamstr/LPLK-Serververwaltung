<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Database;
use App\Core\View;
use App\Core\Security;
use App\Core\Audit;
use App\Services\ConnectorFactory;
use App\Services\PlayerImportService;
use App\Services\SteamService;
use App\Services\PlayerAccountService;

final class PlayersController
{
    public const ROLES = ['founder','admin','member','vip','spieler'];
    public const ROLE_LABELS = ['founder'=>'Founder','admin'=>'Admin','member'=>'Member','vip'=>'VIP','spieler'=>'Spieler'];

    public function index(): void
    {
        Auth::requirePermission('players.view');
        $pdo = Database::connection();
        $q = trim((string)($_GET['q'] ?? ''));
        $role = strtolower(trim((string)($_GET['role'] ?? '')));
        if (!in_array($role, self::ROLES, true)) $role = '';
        $canContacts = Auth::can('players.contacts.view');

        $where = ['1=1']; $params = [];
        if ($role !== '') { $where[] = 'gp.community_role=?'; $params[] = $role; }
        if ($q !== '') {
            $like = '%' . $q . '%';
            $parts = [
                'gp.display_name LIKE ?','gp.steam_personaname LIKE ?','gp.steam_id64 LIKE ?',
                'main.display_name LIKE ?','main.steam_personaname LIKE ?','main.steam_id64 LIKE ?',
                'EXISTS (SELECT 1 FROM steam_name_history snh WHERE snh.player_id=gp.id AND snh.steam_name LIKE ?)'
            ];
            for ($i=0;$i<7;$i++) $params[]=$like;
            if ($canContacts) {
                $parts = array_merge($parts, ['gp.first_name LIKE ?','gp.last_name LIKE ?','gp.email LIKE ?','gp.phone LIKE ?','gp.discord_name LIKE ?','gp.discord_id LIKE ?']);
                for ($i=0;$i<6;$i++) $params[]=$like;
            }
            $where[] = '(' . implode(' OR ', $parts) . ')';
        }
        $sql = 'SELECT gp.*,main.display_name AS main_display_name,main.steam_personaname AS main_steam_personaname,main.steam_id64 AS main_steam_id64,' .
            '(SELECT COUNT(*) FROM game_players child WHERE child.main_player_id=gp.id) AS secondary_count ' .
            'FROM game_players gp LEFT JOIN game_players main ON main.id=gp.main_player_id WHERE ' . implode(' AND ', $where) .
            ' ORDER BY CASE gp.community_role WHEN \'founder\' THEN 5 WHEN \'admin\' THEN 4 WHEN \'member\' THEN 3 WHEN \'vip\' THEN 2 ELSE 1 END DESC,' .
            ' CASE WHEN gp.main_player_id IS NULL THEN gp.id ELSE gp.main_player_id END, gp.main_player_id IS NOT NULL,' .
            ' COALESCE(NULLIF(gp.steam_personaname,\'\'),gp.display_name) COLLATE NOCASE';
        $stmt = $pdo->prepare($sql);
        $stmt->execute($params);
        $players = $stmt->fetchAll();

        // Gruppenstatistik zählt Personen/Hauptaccounts, nicht zusätzliche Steam-Zweitaccounts.
        $counts = ['founder'=>0,'admin'=>0,'member'=>0,'vip'=>0,'spieler'=>0];
        foreach ($pdo->query("SELECT community_role,COUNT(*) c FROM game_players WHERE main_player_id IS NULL GROUP BY community_role")->fetchAll() as $row) {
            if (isset($counts[$row['community_role']])) $counts[$row['community_role']] = (int)$row['c'];
        }
        $secondaryCount = (int)$pdo->query('SELECT COUNT(*) FROM game_players WHERE main_player_id IS NOT NULL')->fetchColumn();

        $allServers = $pdo->query('SELECT id,name,framework,server_identifier FROM game_servers ORDER BY name')->fetchAll();
        $servers = array_values(array_filter($allServers, fn($s) => Auth::canServer((int)$s['id'], 'view')));
        $manageable = []; $maxRoles = [];
        foreach ($servers as $s) {
            $sid=(int)$s['id']; $manageable[$sid]=Auth::canServer($sid,'players'); $maxRoles[$sid]=Auth::serverMaxGameRole($sid);
        }
        $assignments=[];
        foreach ($pdo->query('SELECT * FROM server_player_groups')->fetchAll() as $a) {
            if (isset($manageable[(int)$a['server_id']])) $assignments[$a['player_id']][$a['server_id']]=$a['role_name'];
        }

        View::render('players', [
            'title'=>'Spieler & Personen','players'=>$players,'servers'=>$servers,'assignments'=>$assignments,
            'manageable'=>$manageable,'maxRoles'=>$maxRoles,'counts'=>$counts,'secondaryCount'=>$secondaryCount,'q'=>$q,'roleFilter'=>$role,
            'roles'=>self::ROLE_LABELS,'canManage'=>Auth::can('players.manage'),'maxCommunityRole'=>Auth::maxCommunityRole(),
            'canImport'=>Auth::can('players.import'),'canExport'=>Auth::can('players.export'),'canSteamSync'=>Auth::can('players.steam.sync'),'steamConfigured'=>SteamService::hasApiKey(),
        ]);
    }

    public function view(string $id): void
    {
        Auth::requirePermission('players.view');
        $pdo=Database::connection();
        $stmt=$pdo->prepare('SELECT gp.*,main.display_name AS main_display_name,main.steam_personaname AS main_steam_personaname,main.steam_id64 AS main_steam_id64 FROM game_players gp LEFT JOIN game_players main ON main.id=gp.main_player_id WHERE gp.id=?');
        $stmt->execute([(int)$id]);$player=$stmt->fetch();
        if(!$player){http_response_code(404);View::render('error',['title'=>'Person nicht gefunden','message'=>'Der angeforderte Datensatz existiert nicht.']);return;}
        $history=$pdo->prepare('SELECT * FROM steam_name_history WHERE player_id=? ORDER BY last_seen_at DESC');$history->execute([(int)$id]);
        $allServers=$pdo->query('SELECT id,name,framework,server_identifier FROM game_servers ORDER BY name')->fetchAll();
        $servers=array_values(array_filter($allServers,fn($s)=>Auth::canServer((int)$s['id'],'view')));
        $assignments=[];$stmt=$pdo->prepare('SELECT server_id,role_name FROM server_player_groups WHERE player_id=?');$stmt->execute([(int)$id]);foreach($stmt->fetchAll() as $a)$assignments[(int)$a['server_id']]=$a['role_name'];
        $manageable=[];$maxRoles=[];foreach($servers as $s){$sid=(int)$s['id'];$manageable[$sid]=Auth::canServer($sid,'players');$maxRoles[$sid]=Auth::serverMaxGameRole($sid);}
        $childrenStmt=$pdo->prepare('SELECT id,display_name,steam_id64,steam_personaname FROM game_players WHERE main_player_id=? ORDER BY COALESCE(NULLIF(steam_personaname,\'\'),display_name) COLLATE NOCASE');
        $childrenStmt->execute([(int)$id]);
        $secondaryAccounts=$childrenStmt->fetchAll();
        $potentialMainAccounts=$pdo->prepare('SELECT id,display_name,steam_id64,steam_personaname,community_role FROM game_players WHERE id<>? AND main_player_id IS NULL ORDER BY COALESCE(NULLIF(steam_personaname,\'\'),display_name) COLLATE NOCASE');
        $potentialMainAccounts->execute([(int)$id]);
        $potentialMainRows=$potentialMainAccounts->fetchAll();
        if(!Auth::isFounder()){$potentialMainRows=array_values(array_filter($potentialMainRows,static fn(array $candidate):bool=>Auth::canManageGameRoleTarget((string)($candidate['community_role']??'spieler'))));}
        View::render('player_view',[
            'title'=>$player['display_name'],'player'=>$player,'history'=>$history->fetchAll(),'servers'=>$servers,'assignments'=>$assignments,
            'manageable'=>$manageable,'maxRoles'=>$maxRoles,'roles'=>self::ROLE_LABELS,'canManage'=>Auth::can('players.manage'),
            'canContactsView'=>Auth::can('players.contacts.view'),'canContactsManage'=>Auth::can('players.contacts.manage'),
            'canSteamSync'=>Auth::can('players.steam.sync'),'steamConfigured'=>SteamService::hasApiKey(),'maxCommunityRole'=>Auth::maxCommunityRole(),
            'secondaryAccounts'=>$secondaryAccounts,'potentialMainAccounts'=>$potentialMainRows,'showcaseServers'=>$servers,
        ]);
    }

    public function create(): void
    {
        Auth::requirePermission('players.manage'); Security::verifyCsrf();
        $steam=trim((string)($_POST['steam_id64']??''));$role=(string)($_POST['community_role']??'vip');
        if(!preg_match('/^7656119\d{10}$/',$steam)||!Auth::canAssignCommunityRole($role)){View::flash('error','Gültige SteamID64 und zulässige Rolle angeben.');header('Location:/players');exit;}
        $name='Steam '.substr($steam,-6);
        try{
            Database::connection()->prepare('INSERT INTO game_players(display_name,steam_id64,community_role,status,active,created_at,updated_at) VALUES(?,?,?,?,1,?,?)')->execute([$name,$steam,$role,'active',date('c'),date('c')]);
            $id=(int)Database::connection()->lastInsertId();
            $steamUpdated=false;
            if(Auth::can('players.steam.sync')&&SteamService::hasApiKey()){
                try{$sync=SteamService::syncPlayers([$id]);$steamUpdated=((int)($sync['updated']??0))>0;}catch(\Throwable){}
            }
            Audit::log('player.create','player',(string)$id,['steam_id64'=>$steam,'community_role'=>$role,'steam_synced'=>$steamUpdated]);
            View::flash('success','Person wurde angelegt.'.($steamUpdated?' Steam-Profilname wurde übernommen.':''));header('Location:/players/'.$id);return;
        }catch(\Throwable $e){View::flash('error','Person konnte nicht gespeichert werden: '.$e->getMessage());header('Location:/players');}
    }

    public function update(string $id): void
    {
        Auth::requirePermission('players.manage');
        Security::verifyCsrf();
        $pdo=Database::connection();
        $stmt=$pdo->prepare('SELECT * FROM game_players WHERE id=?');
        $stmt->execute([(int)$id]);
        $player=$stmt->fetch();
        if(!$player) throw new \RuntimeException('Person nicht gefunden.');

        $display=trim((string)($player['steam_personaname']??''));
        if($display==='') $display=(string)$player['display_name'];
        $isSecondary=!empty($player['main_player_id']);
        if($isSecondary){
            $mainStmt=$pdo->prepare('SELECT community_role FROM game_players WHERE id=?');$mainStmt->execute([(int)$player['main_player_id']]);
            $role=(string)($mainStmt->fetchColumn()?:$player['community_role']);
        }else{
            $role=(string)($_POST['community_role']??$player['community_role']);
            if(!Auth::canAssignCommunityRole($role)&&$role!==$player['community_role']) throw new \RuntimeException('Diese Community-Rolle darf nicht vergeben werden.');
        }
        $status=(string)($_POST['status']??$player['status']);
        if(!in_array($status,['active','inactive','blocked','former'],true)) $status='active';
        $mirror=!$isSecondary&&!empty($_POST['mirror_community_to_servers']);
        $roleChanges=!$isSecondary&&$role!==(string)$player['community_role'];
        if($roleChanges||$mirror)$this->assertCommunityRoleTargetManageable((int)$id);

        $mirrorServers=[];
        if($mirror){
            foreach($pdo->query('SELECT id FROM game_servers ORDER BY id')->fetchAll() as $server){
                $serverId=(int)$server['id'];
                if(!Auth::canServer($serverId,'players')) continue;
                $this->assertServerRoleTargetManageable((int)$id,$serverId);
                if(!Auth::canAssignGameRole($serverId,$role)) throw new \RuntimeException('Die Community-Rolle '.self::ROLE_LABELS[$role].' darf nicht auf allen verwaltbaren Servern als Serverrolle vergeben werden.');
                $mirrorServers[]=$serverId;
            }
        }

        $fields=[$display,$role,$status,$status==='active'?1:0,date('c')];
        $sql='UPDATE game_players SET display_name=?,community_role=?,status=?,active=?,updated_at=?';
        if(Auth::can('players.contacts.manage')){
            $sql.=',first_name=?,last_name=?,email=?,phone=?,discord_name=?,discord_id=?,notes=?,joined_at=?,left_at=?';
            foreach(['first_name','last_name','email','phone','discord_name','discord_id','notes','joined_at','left_at'] as $key) $fields[]=trim((string)($_POST[$key]??$player[$key]??''))?:null;
        }
        $sql.=' WHERE id=?';
        $fields[]=(int)$id;

        $pdo->beginTransaction();
        try{
            $pdo->prepare($sql)->execute($fields);
            if(!$isSecondary) PlayerAccountService::propagateCommunityRole((int)$id,$role);
            if($mirrorServers){
                foreach($mirrorServers as $serverId) PlayerAccountService::propagateServerRole((int)$id,$serverId,$role);
            }
            $pdo->commit();
        }catch(\Throwable $e){
            $pdo->rollBack();
            throw $e;
        }
        Audit::log('player.update','player',$id,['community_role'=>$role,'status'=>$status,'secondary'=>$isSecondary,'mirrored_to_servers'=>$mirrorServers]);
        View::flash('success','Personendaten wurden gespeichert.'.($mirrorServers?' Die Community-Rolle wurde gleichzeitig auf '.count($mirrorServers).' Server als Serverrolle gesetzt.':''));
        header('Location:/players/'.$id);
    }

    public function changeCommunityRole(string $id): void
    {
        Auth::requirePermission('players.manage');
        Security::verifyCsrf();
        $playerId=(int)$id;
        $role=(string)($_POST['community_role']??'');
        $mirror=!empty($_POST['mirror_to_servers']);
        $return=(string)($_POST['_return']??'/players');
        if(!str_starts_with($return,'/players')) $return='/players';
        if(!Auth::canAssignCommunityRole($role)){
            View::flash('error','Diese Gruppe darf nicht vergeben werden.');header('Location:'.$return);return;
        }
        $pdo=Database::connection();
        try{$primaryId=$this->assertCommunityRoleTargetManageable($playerId);}catch(\Throwable $e){View::flash('error',$e->getMessage());header('Location:'.$return);return;}
        $targets=[];
        if($mirror){
            foreach($pdo->query('SELECT id FROM game_servers ORDER BY id')->fetchAll() as $server){
                $serverId=(int)$server['id'];
                if(!Auth::canServer($serverId,'players')) continue;
                try{$this->assertServerRoleTargetManageable($primaryId,$serverId);}catch(\Throwable $e){View::flash('error',$e->getMessage());header('Location:'.$return);return;}
                if(!Auth::canAssignGameRole($serverId,$role)){
                    View::flash('error','Die Community-Rolle '.self::ROLE_LABELS[$role].' darf nicht auf allen verwaltbaren Servern als Serverrolle vergeben werden.');header('Location:'.$return);return;
                }
                $targets[]=$serverId;
            }
        }
        $pdo->beginTransaction();
        try{
            $family=PlayerAccountService::propagateCommunityRole($primaryId,$role);
            foreach($targets as $serverId) PlayerAccountService::propagateServerRole($primaryId,$serverId,$role);
            $pdo->commit();
        }catch(\Throwable $e){$pdo->rollBack();throw $e;}
        Audit::log('player.community_role','player',(string)$primaryId,['role'=>$role,'family'=>$family,'mirrored_to_servers'=>$targets]);
        $extra=count($family)>1?' Haupt- und Zweitaccounts wurden gemeinsam aktualisiert.':'';
        View::flash('success','Person wurde nach '.self::ROLE_LABELS[$role].' verschoben.'.$extra.($targets?' Die Rolle wurde gleichzeitig auf '.count($targets).' Server übernommen.':''));
        header('Location:'.$return);
    }

    public function changeServerRole(string $id, string $server): void
    {
        Auth::requirePermission('players.manage');
        Security::verifyCsrf();
        $playerId=(int)$id;$serverId=(int)$server;
        $role=strtolower(trim((string)($_POST['server_role']??'')));
        $return=(string)($_POST['_return']??'/players');if(!str_starts_with($return,'/players'))$return='/players';
        if(!Auth::canServer($serverId,'players')){View::flash('error','Keine Berechtigung zur Rollenverwaltung auf diesem Server.');header('Location:'.$return);return;}
        if($role!==''&&!in_array($role,self::ROLES,true)){View::flash('error','Ungültige Serverrolle.');header('Location:'.$return);return;}
        if($role!==''&&!Auth::canAssignGameRole($serverId,$role)){View::flash('error','Diese Serverrolle darf nicht vergeben werden.');header('Location:'.$return);return;}
        try{
            $primaryId=$this->assertServerRoleTargetManageable($playerId,$serverId);
            $family=PlayerAccountService::propagateServerRole($primaryId,$serverId,$role?:null);
        }catch(\Throwable $e){View::flash('error',$e->getMessage());header('Location:'.$return);return;}
        Audit::log('player.server_role','player',(string)$primaryId,['server_id'=>$serverId,'role'=>$role?:null,'family'=>$family]);
        View::flash('success','Serverrolle wurde gespeichert'.(count($family)>1?' und auf '.count($family).' verknüpfte Accounts übernommen':'').'. Die Änderung wird beim nächsten Server-Sync übertragen.');
        header('Location:'.$return);
    }

    public function bulkUpdate(): void
    {
        Auth::requirePermission('players.manage');
        Security::verifyCsrf();
        $return=(string)($_POST['_return']??'/players');
        if(!str_starts_with($return,'/players')) $return='/players';

        $playerIds=[];
        foreach((array)($_POST['player_ids']??[]) as $raw){
            $id=(int)$raw;
            if($id>0) $playerIds[$id]=$id;
        }
        $playerIds=array_values($playerIds);
        if(!$playerIds){View::flash('warning','Bitte mindestens einen Spieler auswählen.');header('Location:'.$return);return;}

        $pdo=Database::connection();
        $placeholders=implode(',',array_fill(0,count($playerIds),'?'));
        $stmt=$pdo->prepare('SELECT id FROM game_players WHERE id IN ('.$placeholders.')');
        $stmt->execute($playerIds);
        $existing=array_map('intval',$stmt->fetchAll(\PDO::FETCH_COLUMN));
        if(!$existing){View::flash('error','Keine gültigen Spieler ausgewählt.');header('Location:'.$return);return;}
        $primaryIds=PlayerAccountService::normalizePrimaryIds($existing);
        try{foreach($primaryIds as $primaryId)$this->assertCommunityRoleTargetManageable((int)$primaryId);}catch(\Throwable $e){View::flash('error',$e->getMessage());header('Location:'.$return);return;}
        $familyIds=[];
        foreach($primaryIds as $primaryId){foreach(PlayerAccountService::familyIds($primaryId) as $familyId)$familyIds[$familyId]=$familyId;}
        $playerIds=array_values($familyIds);
        $familyCount=count($primaryIds);

        $community=(string)($_POST['community_role']??'__unchanged');
        $mirror=!empty($_POST['mirror_community']);
        $serverRole=(string)($_POST['server_role']??'__unchanged');
        $scope=(string)($_POST['scope']??'selected');

        if($community!=='__unchanged'&&!Auth::canAssignCommunityRole($community)){
            View::flash('error','Diese Community-Rolle darf nicht vergeben werden.');header('Location:'.$return);return;
        }
        if($mirror){
            if($community==='__unchanged'){
                View::flash('warning','Für „Community-Rolle auch als Serverrolle“ muss eine Community-Rolle ausgewählt werden.');header('Location:'.$return);return;
            }
            $serverRole=$community;
        }
        if($serverRole!=='__unchanged'&&$serverRole!=='__none'&&!in_array($serverRole,self::ROLES,true)){
            View::flash('error','Ungültige Serverrolle.');header('Location:'.$return);return;
        }
        if($community==='__unchanged'&&$serverRole==='__unchanged'){
            View::flash('warning','Es wurde keine Änderung ausgewählt.');header('Location:'.$return);return;
        }

        $targets=[];
        if($serverRole!=='__unchanged'){
            if($scope==='global'){
                foreach($pdo->query('SELECT id FROM game_servers ORDER BY id')->fetchAll() as $row){
                    $sid=(int)$row['id'];
                    if(Auth::canServer($sid,'players')) $targets[$sid]=$sid;
                }
            }else{
                foreach((array)($_POST['server_ids']??[]) as $raw){
                    $sid=(int)$raw;
                    if($sid>0&&Auth::canServer($sid,'players')) $targets[$sid]=$sid;
                }
            }
            $targets=array_values($targets);
            if(!$targets){View::flash('warning',$scope==='global'?'Keine verwaltbaren Server vorhanden.':'Bitte für die Serverrollen mindestens einen Server auswählen.');header('Location:'.$return);return;}
            try{foreach($primaryIds as $primaryId){foreach($targets as $sid)$this->assertServerRoleTargetManageable((int)$primaryId,(int)$sid);}}catch(\Throwable $e){View::flash('error',$e->getMessage());header('Location:'.$return);return;}
            if($serverRole!=='__none'){
                foreach($targets as $sid){
                    if(!Auth::canAssignGameRole($sid,$serverRole)){
                        View::flash('error','Die Serverrolle '.self::ROLE_LABELS[$serverRole].' darf nicht auf allen ausgewählten Servern vergeben werden.');header('Location:'.$return);return;
                    }
                }
            }
        }

        $pdo->beginTransaction();
        try{
            if($community!=='__unchanged'){
                $update=$pdo->prepare('UPDATE game_players SET community_role=?,updated_at=? WHERE id=?');
                foreach($playerIds as $playerId) $update->execute([$community,date('c'),$playerId]);
            }
            if($serverRole!=='__unchanged'){
                $managed=$pdo->prepare('INSERT OR IGNORE INTO server_managed_players(server_id,player_id,created_at) VALUES(?,?,?)');
                $up=$pdo->prepare('INSERT INTO server_player_groups(server_id,player_id,role_name,updated_at) VALUES(?,?,?,?) ON CONFLICT(server_id,player_id) DO UPDATE SET role_name=excluded.role_name,updated_at=excluded.updated_at');
                $del=$pdo->prepare('DELETE FROM server_player_groups WHERE server_id=? AND player_id=?');
                foreach($playerIds as $playerId){
                    foreach($targets as $sid){
                        $managed->execute([$sid,$playerId,date('c')]);
                        if($serverRole==='__none') $del->execute([$sid,$playerId]);
                        else $up->execute([$sid,$playerId,$serverRole,date('c')]);
                    }
                }
            }
            $pdo->commit();
        }catch(\Throwable $e){
            $pdo->rollBack();
            throw $e;
        }

        Audit::log('player.bulk_update','player',null,[
            'player_ids'=>$playerIds,
            'community_role'=>$community==='__unchanged'?null:$community,
            'server_role'=>$serverRole==='__unchanged'?null:($serverRole==='__none'?'removed':$serverRole),
            'servers'=>$targets,
            'scope'=>$scope,
            'mirror_community'=>$mirror,
        ]);
        $parts=[];
        if($community!=='__unchanged') $parts[]='Community-Rolle: '.self::ROLE_LABELS[$community];
        if($serverRole!=='__unchanged') $parts[]='Serverrolle: '.($serverRole==='__none'?'entfernt':self::ROLE_LABELS[$serverRole]).' auf '.count($targets).' Server(n)';
        View::flash('success','Massenänderung für '.$familyCount.' Person(en) / '.count($playerIds).' Steam-Account(s) gespeichert. '.implode(' · ',$parts).'. Serverdateien werden erst beim jeweiligen Server-Sync geschrieben.');
        header('Location:'.$return);
    }

    public function assign(string $id): void
    {
        Auth::requirePermission('players.manage'); Security::verifyCsrf();$playerId=(int)$id;$pdo=Database::connection();$roles=$_POST['role']??[];$servers=$pdo->query('SELECT id FROM game_servers')->fetchAll();
        $primaryId=$this->assertCommunityRoleTargetManageable($playerId);
        $pdo->beginTransaction();
        try{
            foreach($servers as $server){
                $serverId=(int)$server['id'];if(!Auth::canServer($serverId,'players'))continue;
                $this->assertServerRoleTargetManageable($primaryId,$serverId);
                $role=$roles[$serverId]??'';
                if($role!==''&&!Auth::canAssignGameRole($serverId,$role))throw new \RuntimeException('Die Gruppe '.$role.' darf auf diesem Server nicht vergeben werden.');
                PlayerAccountService::propagateServerRole($primaryId,$serverId,in_array($role,self::ROLES,true)?$role:null);
            }
            $pdo->commit();Audit::log('player.assign','player',(string)$primaryId,['roles'=>$roles,'family'=>PlayerAccountService::familyIds($primaryId)]);View::flash('success','Servergruppen wurden für Haupt- und verknüpfte Zweitaccounts gespeichert.');
        }catch(\Throwable $e){$pdo->rollBack();throw $e;}
        $return=(string)($_POST['_return']??'/players');if(!str_starts_with($return,'/players'))$return='/players';header('Location:'.$return);
    }

    public function quickAssign(string $id): void
    {
        Auth::requirePermission('players.manage');Security::verifyCsrf();
        $playerId=(int)$id;$role=strtolower(trim((string)($_POST['server_role']??'')));$scope=(string)($_POST['scope']??'selected');
        $return=(string)($_POST['_return']??'/players');if(!str_starts_with($return,'/players'))$return='/players';
        if($role!==''&&!in_array($role,self::ROLES,true)){View::flash('error','Ungültige Servergruppe.');header('Location:'.$return);return;}
        $pdo=Database::connection();
        try{$primaryId=$this->assertCommunityRoleTargetManageable($playerId);}catch(\Throwable $e){View::flash('error',$e->getMessage());header('Location:'.$return);return;}
        $targets=[];
        if($scope==='global'){
            foreach($pdo->query('SELECT id FROM game_servers ORDER BY id')->fetchAll() as $server){$serverId=(int)$server['id'];if(Auth::canServer($serverId,'players'))$targets[]=$serverId;}
        }else{
            foreach((array)($_POST['server_ids']??[]) as $raw){$serverId=(int)$raw;if($serverId>0&&Auth::canServer($serverId,'players'))$targets[$serverId]=$serverId;}$targets=array_values($targets);
        }
        if(!$targets){View::flash('warning',$scope==='global'?'Keine verwaltbaren Server vorhanden.':'Bitte mindestens einen Server auswählen.');header('Location:'.$return);return;}
        try{foreach($targets as $serverId)$this->assertServerRoleTargetManageable($primaryId,(int)$serverId);}catch(\Throwable $e){View::flash('error',$e->getMessage());header('Location:'.$return);return;}
        if($role!==''){foreach($targets as $serverId){if(!Auth::canAssignGameRole($serverId,$role)){View::flash('error','Die Gruppe '.self::ROLE_LABELS[$role].' darf nicht auf allen ausgewählten Servern vergeben werden.');header('Location:'.$return);return;}}}
        $pdo->beginTransaction();
        try{foreach($targets as $serverId)PlayerAccountService::propagateServerRole($primaryId,$serverId,$role?:null);$pdo->commit();}catch(\Throwable $e){$pdo->rollBack();throw $e;}
        $family=PlayerAccountService::familyIds($primaryId);
        Audit::log('player.quick_assign','player',(string)$primaryId,['role'=>$role?:null,'scope'=>$scope,'servers'=>$targets,'family'=>$family]);
        $label=$role===''?'Keine verwaltete Gruppe':self::ROLE_LABELS[$role];
        View::flash('success',$label.' wurde für '.count($targets).' Server und '.count($family).' verknüpfte(n) Steam-Account(s) gesetzt. Die Änderung wird beim nächsten Server-Sync übertragen.');
        header('Location:'.$return);
    }

    public function setMainAccount(string $id): void
    {
        Auth::requirePermission('players.manage');Security::verifyCsrf();
        $playerId=(int)$id;$raw=trim((string)($_POST['main_player_id']??''));$mainId=$raw===''?null:(int)$raw;
        try{
            $this->assertAllRoleTargetsManageable($playerId);
            if($mainId!==null)$this->assertAllRoleTargetsManageable($mainId);
            $result=PlayerAccountService::setMainAccount($playerId,$mainId);
            Audit::log('player.account_link','player',(string)$playerId,['main_player_id'=>$mainId,'result'=>$result]);
            View::flash('success',$mainId?'Account wurde als Zweitaccount verknüpft. Community- und Serverrechte entsprechen jetzt dem Hauptaccount.':'Account wurde vom bisherigen Hauptaccount gelöst und ist wieder eigenständig.');
        }catch(\Throwable $e){View::flash('error',$e->getMessage());}
        header('Location:/players/'.$playerId);
    }

    private function assertCommunityRoleTargetManageable(int $playerId): int
    {
        $primaryId=PlayerAccountService::primaryId($playerId);
        $stmt=Database::connection()->prepare('SELECT community_role FROM game_players WHERE id=?');
        $stmt->execute([$primaryId]);
        $role=(string)($stmt->fetchColumn()?:'spieler');
        if(!Auth::canManageGameRoleTarget($role)){
            throw new \RuntimeException('Diese Person besitzt die geschützte Rolle '.(self::ROLE_LABELS[$role]??$role).'. Admins dürfen nur Rollen unterhalb von Admin verändern.');
        }
        return $primaryId;
    }

    private function assertServerRoleTargetManageable(int $playerId,int $serverId): int
    {
        $primaryId=$this->assertCommunityRoleTargetManageable($playerId);
        $family=PlayerAccountService::familyIds($primaryId);
        if($family){
            $ph=implode(',',array_fill(0,count($family),'?'));
            $stmt=Database::connection()->prepare('SELECT role_name FROM server_player_groups WHERE server_id=? AND player_id IN ('.$ph.')');
            $stmt->execute(array_merge([$serverId],$family));
            foreach($stmt->fetchAll(\PDO::FETCH_COLUMN) as $role){
                $role=(string)$role;
                if($role!==''&&!Auth::canManageGameRoleTarget($role)){
                    throw new \RuntimeException('Auf diesem Server besitzt die Person bereits die geschützte Rolle '.(self::ROLE_LABELS[$role]??$role).'. Admins dürfen Admin/Founder nicht verändern oder entfernen.');
                }
            }
        }
        return $primaryId;
    }

    private function assertAllRoleTargetsManageable(int $playerId): int
    {
        $primaryId=$this->assertCommunityRoleTargetManageable($playerId);
        $family=PlayerAccountService::familyIds($primaryId);
        if($family){
            $ph=implode(',',array_fill(0,count($family),'?'));
            $stmt=Database::connection()->prepare('SELECT DISTINCT role_name FROM server_player_groups WHERE player_id IN ('.$ph.')');
            $stmt->execute($family);
            foreach($stmt->fetchAll(\PDO::FETCH_COLUMN) as $role){
                $role=(string)$role;
                if($role!==''&&!Auth::canManageGameRoleTarget($role)){
                    throw new \RuntimeException('Die Account-Zuordnung ist geschützt, weil mindestens eine Admin-/Founder-Serverrolle vorhanden ist.');
                }
            }
        }
        return $primaryId;
    }

    public function exportForm(): void
    {
        Auth::requirePermission('players.export');
        View::render('players_export', [
            'title' => 'Spielerliste exportieren',
            'roles' => self::ROLE_LABELS,
            'canContacts' => Auth::can('players.contacts.view'),
        ]);
    }

    public function exportDownload(): void
    {
        Auth::requirePermission('players.export');

        $format = strtolower(trim((string)($_GET['format'] ?? 'whitelist')));
        if (!in_array($format, ['whitelist','table'], true)) $format = 'whitelist';

        $minRole = strtolower(trim((string)($_GET['min_role'] ?? 'spieler')));
        if (!in_array($minRole, self::ROLES, true)) $minRole = 'spieler';

        $includeSecondary = ((string)($_GET['include_secondary'] ?? '1')) !== '0';
        $activeOnly = ((string)($_GET['active_only'] ?? '1')) !== '0';

        $levels = Auth::GAME_ROLE_LEVELS;
        $minimum = $levels[$minRole] ?? 1;
        $pdo = Database::connection();

        $sql =
            'SELECT gp.*,main.display_name AS main_display_name,main.steam_personaname AS main_steam_personaname,' .
            'main.steam_id64 AS main_steam_id64,main.community_role AS main_community_role ' .
            'FROM game_players gp LEFT JOIN game_players main ON main.id=gp.main_player_id WHERE 1=1';
        $params = [];

        if (!$includeSecondary) $sql .= ' AND gp.main_player_id IS NULL';
        if ($activeOnly) $sql .= ' AND gp.active=1';
        $sql .= ' ORDER BY COALESCE(main.id,gp.id), gp.main_player_id IS NOT NULL, COALESCE(NULLIF(gp.steam_personaname,\'\'),gp.display_name) COLLATE NOCASE';

        $stmt = $pdo->prepare($sql);
        $stmt->execute($params);

        $rows = [];
        foreach ($stmt->fetchAll() as $row) {
            $effectiveRole = (string)($row['main_community_role'] ?: $row['community_role'] ?: 'spieler');
            if (($levels[$effectiveRole] ?? 1) < $minimum) continue;
            $row['effective_role'] = $effectiveRole;
            $rows[] = $row;
        }

        $roleSlug = $minRole === 'founder' ? 'founder' : 'ab_' . $minRole;
        $timestamp = date('Ymd_His');

        if ($format === 'whitelist') {
            header('Content-Type: text/plain; charset=utf-8');
            header('Content-Disposition: attachment; filename="whitelist_' . $roleSlug . '_' . $timestamp . '.txt"');
            header('X-Content-Type-Options: nosniff');

            foreach ($rows as $row) {
                $steam = trim((string)$row['steam_id64']);
                if (preg_match('/^7656119\d{10}$/', $steam)) echo $steam . "\r\n";
            }

            Audit::log('player.export.whitelist', 'player', null, [
                'min_role'=>$minRole,
                'include_secondary'=>$includeSecondary,
                'active_only'=>$activeOnly,
                'rows'=>count($rows),
            ]);
            exit;
        }

        $allServers = $pdo->query('SELECT id,name FROM game_servers ORDER BY name')->fetchAll();
        $servers = array_values(array_filter($allServers, static fn(array $server): bool => Auth::canServer((int)$server['id'], 'view')));
        $groupRows = $pdo->query('SELECT server_id,player_id,role_name FROM server_player_groups')->fetchAll();
        $groups = [];
        foreach ($groupRows as $group) {
            $groups[(int)$group['player_id']][(int)$group['server_id']] = (string)$group['role_name'];
        }

        header('Content-Type: text/csv; charset=utf-8');
        header('Content-Disposition: attachment; filename="spieler_tabelle_' . $roleSlug . '_' . $timestamp . '.csv"');
        header('X-Content-Type-Options: nosniff');

        $out = fopen('php://output', 'wb');
        if (!$out) throw new \RuntimeException('Exportstream konnte nicht geöffnet werden.');

        // Excel-kompatibles UTF-8.
        fwrite($out, "\xEF\xBB\xBF");

        $header = [
            'SteamID64','Steamname','Accounttyp','Hauptaccount SteamID64','Hauptaccount Name',
            'Community-Rolle','Status'
        ];
        if (Auth::can('players.contacts.view')) {
            $header = array_merge($header, ['Vorname','Nachname','E-Mail','Telefon','Discord-Name','Discord-ID']);
        }
        foreach ($servers as $server) $header[] = 'Server: ' . (string)$server['name'];
        fputcsv($out, $header, ';');

        foreach ($rows as $row) {
            $isSecondary = !empty($row['main_player_id']);
            $line = [
                (string)$row['steam_id64'],
                (string)($row['steam_personaname'] ?: $row['display_name']),
                $isSecondary ? 'Zweitaccount' : 'Hauptaccount',
                $isSecondary ? (string)$row['main_steam_id64'] : '',
                $isSecondary ? (string)($row['main_steam_personaname'] ?: $row['main_display_name']) : '',
                self::ROLE_LABELS[(string)$row['effective_role']] ?? (string)$row['effective_role'],
                (string)$row['status'],
            ];

            if (Auth::can('players.contacts.view')) {
                $line = array_merge($line, [
                    (string)$row['first_name'],
                    (string)$row['last_name'],
                    (string)$row['email'],
                    (string)$row['phone'],
                    (string)$row['discord_name'],
                    (string)$row['discord_id'],
                ]);
            }

            $primaryId = $isSecondary ? (int)$row['main_player_id'] : (int)$row['id'];
            foreach ($servers as $server) {
                $sid = (int)$server['id'];
                $role = $groups[(int)$row['id']][$sid] ?? ($groups[$primaryId][$sid] ?? '');
                $line[] = $role !== '' ? (self::ROLE_LABELS[$role] ?? $role) : '';
            }
            fputcsv($out, $line, ';');
        }

        fclose($out);
        Audit::log('player.export.table', 'player', null, [
            'min_role'=>$minRole,
            'include_secondary'=>$includeSecondary,
            'active_only'=>$activeOnly,
            'rows'=>count($rows),
            'servers'=>count($servers),
        ]);
        exit;
    }

    public function importForm(): void
    {
        Auth::requirePermission('players.import');$all=Database::connection()->query('SELECT id,name FROM game_servers ORDER BY name')->fetchAll();$servers=array_values(array_filter($all,fn($s)=>Auth::canServer((int)$s['id'],'players')));
        View::render('players_import',['title'=>'Whitelist / SteamIDs importieren','servers'=>$servers,'roles'=>self::ROLE_LABELS,'steamConfigured'=>SteamService::hasApiKey()]);
    }

    public function import(): void
    {
        Auth::requirePermission('players.import');Security::verifyCsrf();$content=trim((string)($_POST['content']??''));
        if(isset($_FILES['import_file'])&&is_uploaded_file($_FILES['import_file']['tmp_name']??'')){
            if((int)($_FILES['import_file']['size']??0)>2_000_000){View::flash('error','Importdatei ist größer als 2 MB.');header('Location:/players/import');exit;}
            $content.="\n".(string)file_get_contents($_FILES['import_file']['tmp_name']);
        }
        if($content===''){View::flash('error','Keine Importdaten angegeben.');header('Location:/players/import');exit;}
        $defaultRole=(string)($_POST['default_role']??'spieler');if(!Auth::canAssignCommunityRole($defaultRole))$defaultRole='spieler';
        $serverId=(int)($_POST['server_id']??0);$serverRole=(string)($_POST['server_role']??'');if(!$serverId||!Auth::canServer($serverId,'players')||!Auth::canAssignGameRole($serverId,$serverRole)){$serverId=0;$serverRole=null;}
        $result=PlayerImportService::import($content,$defaultRole,$serverId?:null,$serverRole?:null);$sync='';
        if(!empty($_POST['sync_steam'])&&Auth::can('players.steam.sync')&&SteamService::hasApiKey()&&$result['player_ids']){$r=SteamService::syncPlayers($result['player_ids']);$sync=' Steam: '.$r['updated'].' Profile aktualisiert.';}
        Audit::log('player.import','player',null,['found'=>$result['found'],'created'=>$result['created'],'existing'=>$result['existing']]);
        View::flash('success',$result['found'].' SteamIDs erkannt, '.$result['created'].' neu angelegt, '.$result['existing'].' bereits vorhanden.'.$sync);header('Location:/players');
    }

    public function importRemote(): void
    {
        Auth::requirePermission('players.import');Security::verifyCsrf();$serverId=(int)($_POST['server_id']??0);if(!$serverId||!Auth::canServer($serverId,'players'))throw new \RuntimeException('Kein Zugriff auf diesen Server.');
        $path=ltrim(trim((string)($_POST['remote_path']??'')),'/');if($path===''||str_contains($path,'..')){View::flash('error','Ungültiger Remote-Pfad.');header('Location:/players/import');exit;}
        $stmt=Database::connection()->prepare('SELECT * FROM game_servers WHERE id=?');$stmt->execute([$serverId]);$server=$stmt->fetch();if(!$server)throw new \RuntimeException('Server nicht gefunden.');$connector=ConnectorFactory::make($server);if(!$connector->exists($path))throw new \RuntimeException('Datei auf dem Server nicht gefunden: '.$path);$content=$connector->read($path);if(strlen($content)>2_000_000)throw new \RuntimeException('Remote-Datei ist größer als 2 MB.');
        $defaultRole=(string)($_POST['default_role']??'spieler');if(!Auth::canAssignCommunityRole($defaultRole))$defaultRole='spieler';$serverRole=(string)($_POST['server_role']??'spieler');if(!Auth::canAssignGameRole($serverId,$serverRole))$serverRole='spieler';
        $result=PlayerImportService::import($content,$defaultRole,$serverId,$serverRole);if(!empty($_POST['sync_steam'])&&Auth::can('players.steam.sync')&&SteamService::hasApiKey())SteamService::syncPlayers($result['player_ids']);
        Audit::log('player.import.remote','server',(string)$serverId,['path'=>$path,'found'=>$result['found']]);View::flash('success',$result['found'].' SteamIDs aus '.$path.' erkannt; '.$result['created'].' neu angelegt.');header('Location:/players');
    }

    public function syncOne(string $id): void
    {
        Auth::requirePermission('players.steam.sync');Security::verifyCsrf();$r=SteamService::syncPlayers([(int)$id]);Audit::log('player.steam.sync','player',$id,$r);View::flash($r['updated']?'success':'error',$r['updated']?'Steam-Profil wurde aktualisiert.':'Steam-Profil konnte nicht aktualisiert werden.');header('Location:/players/'.$id);
    }

    public function syncAll(): void
    {
        Auth::requirePermission('players.steam.sync');Security::verifyCsrf();$r=SteamService::syncAll();Audit::log('player.steam.sync_all','player',null,$r);$type=$r['errors']?'warning':'success';View::flash($type,$r['updated'].' Steam-Profile aktualisiert, '.$r['missing'].' ohne API-Ergebnis'.($r['errors']?', Fehler: '.implode(' | ',array_slice($r['errors'],0,2)):'.'));header('Location:/players');
    }

    public function avatar(string $id): void
    {
        Auth::requirePermission('players.view');
        $stmt=Database::connection()->prepare('SELECT steam_id64,steam_avatar_full_url,steam_avatar_medium_url,steam_avatar_url FROM game_players WHERE id=?');
        $stmt->execute([(int)$id]);
        $row=$stmt->fetch();
        if(!$row){http_response_code(404);exit;}

        $steam=(string)$row['steam_id64'];
        $remote=(string)($row['steam_avatar_full_url'] ?: ($row['steam_avatar_medium_url'] ?: $row['steam_avatar_url']));
        $path=SteamService::ensureAvatarCached($steam,$remote);
        if(!$path){http_response_code(404);exit;}

        $mime=SteamService::avatarMimeType($path);
        if(!in_array($mime,['image/jpeg','image/png','image/webp'],true)){http_response_code(415);exit;}
        header('Content-Type: '.$mime);
        header('Content-Length: '.filesize($path));
        header('Cache-Control: private, max-age=86400');
        header('X-Content-Type-Options: nosniff');
        readfile($path);exit;
    }
}
