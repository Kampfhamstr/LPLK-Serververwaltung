<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Database;
use App\Core\View;
use App\Core\Security;
use App\Core\Audit;
use App\Services\PermissionCenterService;

final class UsersController
{
    private const SERVER_CAP_COLUMNS = [
        'view'=>'can_view',
        'players'=>'can_manage_players',
        'permissions'=>'can_manage_permissions',
        'sync'=>'can_sync',
        'live'=>'can_live_admin',
    ];

    public function index(): void
    {
        Auth::requirePermission('users.view');
        $pdo=Database::connection();
        $users=$pdo->query(
            'SELECT id,name,email,web_role,permissions_json,active,auth_version,totp_enabled,created_at,last_login_at,last_seen_at,steam_id64,steam_personaname,steam_verified_at
             FROM users ORDER BY CASE lower(web_role) WHEN "founder" THEN 5 WHEN "admin" THEN 4 WHEN "member" THEN 3 WHEN "vip" THEN 2 ELSE 1 END DESC, lower(name), id'
        )->fetchAll();
        $servers=$pdo->query('SELECT id,name FROM game_servers ORDER BY lower(name),id')->fetchAll();
        $accessRows=$pdo->query('SELECT * FROM user_server_access ORDER BY user_id,server_id')->fetchAll();
        $access=[];foreach($accessRows as $row)$access[(int)$row['user_id']][(int)$row['server_id']]=$row;
        $roleCounts=[];foreach($users as $u){$r=Auth::normalizeWebRole((string)$u['web_role']);$roleCounts[$r]=($roleCounts[$r]??0)+1;}

        View::render('users',[
            'title'=>'Benutzer & Rechte',
            'users'=>$users,
            'servers'=>$servers,
            'serverAccess'=>$access,
            'roleTemplates'=>Auth::allRoleTemplates(),
            'roleCounts'=>$roleCounts,
            'permissionPackages'=>PermissionCenterService::packages(),
            'packageAssignments'=>PermissionCenterService::packageAssignments(),
            'temporaryPermissions'=>PermissionCenterService::temporaryPermissions(),
            'serverGroups'=>PermissionCenterService::serverGroups(),
            'serverGroupAccess'=>PermissionCenterService::serverGroupAccess(),
            'reviewMetrics'=>PermissionCenterService::reviewMetrics($users),
        ]);
    }

    public function update(string $id): void
    {
        Auth::requirePermission('users.manage');Security::verifyCsrf();
        $uid=(int)$id;$pdo=Database::connection();$target=$this->target($uid);
        $currentRole=Auth::normalizeWebRole((string)$target['web_role']);$me=Auth::user();$viewerId=(int)$me['id'];
        if(!Auth::canManageWebRoleTarget($currentRole)&&$uid!==$viewerId)$this->fail('Nutzer mit gleicher oder höherer Rolle dürfen nicht bearbeitet werden.');
        if($uid===$viewerId&&!Auth::isFounder())$this->fail('Die eigene Rolle und die eigenen Rechte können nicht selbst verändert werden.');

        $role=Auth::normalizeWebRole((string)($_POST['web_role']??$currentRole));
        if($role!==$currentRole&&!Auth::canAssignWebRole($role))$this->fail('Diese Webpanel-Rolle darf nicht vergeben werden. Es dürfen nur Rollen unterhalb der eigenen Rolle vergeben werden.');
        if($role===$currentRole&&!Auth::isFounder()&&Auth::webRoleLevel($role)>=Auth::webRoleLevel(Auth::effectiveWebRoleForUser($me)))$this->fail('Diese Webpanel-Rolle darf nicht verwaltet werden.');

        $active=isset($_POST['active'])?1:0;
        if($uid===$viewerId&&!$active)$this->fail('Das eigene Konto kann nicht gesperrt werden.');
        if($currentRole==='founder'&&($role!=='founder'||!$active)){
            $count=$pdo->prepare("SELECT COUNT(*) FROM users WHERE lower(web_role)='founder' AND active=1 AND id<>?");$count->execute([$uid]);
            if((int)$count->fetchColumn()===0)$this->fail('Der letzte aktive Founder kann weder herabgestuft noch gesperrt werden.');
        }

        $reason=trim((string)($_POST['change_reason']??''));
        $permissionsJson=$target['permissions_json'];$managePermissions=Auth::can('users.permissions')&&($uid!==$viewerId||Auth::isFounder());
        $oldRaw=Auth::rawPermissionOverrides($target['permissions_json']??null);
        if($managePermissions&&$role!=='founder'){
            $overrides=Auth::orphanedPermissionOverrides($target['permissions_json']??null); // verwaiste Schlüssel sichtbar erhalten
            foreach((array)($_POST['perm_override']??[]) as $encoded=>$mode){
                $permission=str_replace('__','.',(string)$encoded);if(!isset(Auth::PERMISSIONS[$permission]))continue;
                $mode=(string)$mode;if($mode==='inherit'){unset($overrides[$permission]);continue;}
                $allowed=$mode==='allow';
                if($allowed&&!Auth::isFounder()&&!Auth::can($permission))$this->fail('Ein Recht kann nicht weitergegeben werden, wenn es dem bearbeitenden Konto selbst fehlt: '.Auth::PERMISSIONS[$permission]);
                $overrides[$permission]=$allowed;
            }
            $permissionsJson=$overrides?json_encode($overrides,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES):null;
        }elseif($role==='founder')$permissionsJson=null;

        $newRaw=Auth::rawPermissionOverrides($permissionsJson);$permissionChanged=$oldRaw!==$newRaw;
        $roleChanged=$role!==$currentRole;$activeChanged=(int)$target['active']!==$active;
        $serverChanged=false;
        if(($roleChanged||$permissionChanged||$activeChanged)&&$reason==='')$this->fail('Bitte für Rollen-, Rechte- oder Statusänderungen einen kurzen Änderungsgrund angeben.');

        $pdo->beginTransaction();
        try{
            $newAuthVersion=max(1,(int)($target['auth_version']??1))+($roleChanged?1:0);
            $pdo->prepare('UPDATE users SET web_role=?,permissions_json=?,active=?,auth_version=?,updated_at=? WHERE id=?')
                ->execute([$role,$permissionsJson,$active,$newAuthVersion,date('c'),$uid]);

            if($managePermissions){
                $servers=$pdo->query('SELECT id FROM game_servers ORDER BY id')->fetchAll(\PDO::FETCH_COLUMN);
                $modes=(array)($_POST['server_mode']??[]);$capsPost=(array)($_POST['server_cap']??[]);$maxPost=(array)($_POST['server_max_game_role']??[]);$expiresPost=(array)($_POST['server_expires_at']??[]);
                $delete=$pdo->prepare('DELETE FROM user_server_access WHERE user_id=? AND server_id=?');
                $upsert=$pdo->prepare(
                    'INSERT INTO user_server_access(user_id,server_id,can_view,can_manage_players,can_manage_permissions,can_sync,can_live_admin,max_game_role,expires_at,reason,updated_by,updated_at)
                     VALUES(?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(user_id,server_id) DO UPDATE SET can_view=excluded.can_view,can_manage_players=excluded.can_manage_players,can_manage_permissions=excluded.can_manage_permissions,can_sync=excluded.can_sync,can_live_admin=excluded.can_live_admin,max_game_role=excluded.max_game_role,expires_at=excluded.expires_at,reason=excluded.reason,updated_by=excluded.updated_by,updated_at=excluded.updated_at'
                );
                foreach($servers as $serverIdRaw){
                    $sid=(int)$serverIdRaw;$mode=(string)($modes[$sid]??'inherit');$existing=$this->serverAccessRow($uid,$sid);
                    if($mode!=='custom'||$role==='founder'){
                        if($existing){$delete->execute([$uid,$sid]);$serverChanged=true;}continue;
                    }
                    $postedCaps=(array)($capsPost[$sid]??[]);$values=[];
                    foreach(self::SERVER_CAP_COLUMNS as $cap=>$column){$allow=isset($postedCaps[$cap]);if($allow&&!Auth::isFounder()&&!Auth::canServer($sid,$cap))throw new \RuntimeException('Serverrecht "'.$cap.'" darf nicht weitergegeben werden, weil es dem bearbeitenden Konto dort selbst fehlt.');$values[$cap]=$allow?1:0;}
                    $maxRole=strtolower(trim((string)($maxPost[$sid]??'spieler')));if(!isset(Auth::GAME_ROLE_LEVELS[$maxRole]))$maxRole='spieler';
                    $actorMax=Auth::GAME_ROLE_LEVELS[Auth::serverMaxGameRole($sid)]??1;if(!Auth::isFounder()&&(Auth::GAME_ROLE_LEVELS[$maxRole]??1)>$actorMax)throw new \RuntimeException('Die maximale verwaltbare CS2-Rolle darf nicht höher als die eigene Freigabe sein.');
                    $expires=$this->normalizeDateTime((string)($expiresPost[$sid]??''));
                    $new=[$values['view'],$values['players'],$values['permissions'],$values['sync'],$values['live'],$maxRole,$expires];
                    $old=$existing?[(int)$existing['can_view'],(int)$existing['can_manage_players'],(int)$existing['can_manage_permissions'],(int)$existing['can_sync'],(int)$existing['can_live_admin'],(string)$existing['max_game_role'],(string)($existing['expires_at']??'')]:null;
                    if($old!==$new)$serverChanged=true;
                    $upsert->execute([$uid,$sid,$values['view'],$values['players'],$values['permissions'],$values['sync'],$values['live'],$maxRole,$expires?:null,$reason?:null,(int)$me['id'],date('c')]);
                }
            }
            $pdo->commit();
            if($uid===$viewerId&&$roleChanged)$_SESSION['auth_version']=$newAuthVersion;
        }catch(\Throwable $e){if($pdo->inTransaction())$pdo->rollBack();$this->fail($e->getMessage());}

        Audit::log('user.update','user',(string)$uid,['from_role'=>$currentRole,'role'=>$role,'active'=>$active,'permission_changed'=>$permissionChanged,'server_changed'=>$serverChanged,'reason'=>$reason]);
        View::flash('success','Benutzerrolle, individuelle Rechte und Serverfreigaben wurden gespeichert. Änderungen gelten spätestens beim nächsten Request.');header('Location: /users#user-'.$uid);
    }

    public function updateRoleTemplate(string $role): void
    {
        Auth::requirePermission('roles.templates.manage');Security::verifyCsrf();$role=Auth::normalizeWebRole($role);
        if(!Auth::canEditRoleTemplate($role))$this->fail('Diese Rollenvorlage darf nicht bearbeitet werden.','/users#role-'.$role,403);
        $reason=trim((string)($_POST['change_reason']??''));if($reason==='')$this->fail('Bitte einen Änderungsgrund für die Rollenvorlage angeben.','/users#role-'.$role);
        if($role==='founder'){
            $permissions=['*'];$serverCaps=['view'=>true,'players'=>true,'permissions'=>true,'sync'=>true,'live'=>true];$maxGameRole='founder';$require2fa=1;
        }else{
            $selected=(array)($_POST['template_permissions']??[]);$permissions=[];
            foreach($selected as $encoded=>$value){if((string)$value!=='1')continue;$permission=str_replace('__','.',(string)$encoded);if(!isset(Auth::PERMISSIONS[$permission]))continue;if(!Auth::isFounder()&&!Auth::can($permission))$this->fail('Die Vorlage darf kein Recht erhalten, das dem bearbeitenden Konto selbst fehlt: '.Auth::PERMISSIONS[$permission],'/users#role-'.$role);$permissions[]=$permission;}
            $actorTemplate=Auth::roleTemplate(Auth::effectiveWebRole());$postedCaps=(array)($_POST['template_server_caps']??[]);$serverCaps=[];
            foreach(array_keys(self::SERVER_CAP_COLUMNS) as $cap){$allow=isset($postedCaps[$cap]);if($allow&&!Auth::isFounder()&&empty($actorTemplate['server_caps'][$cap]))$this->fail('Die Rollenvorlage darf kein Serverrecht erhalten, das die eigene Rollenvorlage nicht besitzt: '.$cap,'/users#role-'.$role);$serverCaps[$cap]=$allow;}
            $maxGameRole=strtolower(trim((string)($_POST['max_game_role']??'spieler')));if(!isset(Auth::GAME_ROLE_LEVELS[$maxGameRole]))$maxGameRole='spieler';$actorMax=Auth::GAME_ROLE_LEVELS[Auth::maxCommunityRole()]??1;if(!Auth::isFounder()&&(Auth::GAME_ROLE_LEVELS[$maxGameRole]??1)>$actorMax)$this->fail('Die Vorlage darf keine höhere CS2-Rolle verwalten als das eigene Konto.','/users#role-'.$role);
            $require2fa=isset($_POST['require_2fa'])?1:0;
        }
        Database::connection()->prepare(
            'INSERT INTO web_role_templates(role_name,permissions_json,server_caps_json,max_game_role,require_2fa,updated_at) VALUES(?,?,?,?,?,?)
             ON CONFLICT(role_name) DO UPDATE SET permissions_json=excluded.permissions_json,server_caps_json=excluded.server_caps_json,max_game_role=excluded.max_game_role,require_2fa=excluded.require_2fa,updated_at=excluded.updated_at'
        )->execute([$role,json_encode(array_values(array_unique($permissions)),JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),json_encode($serverCaps,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$maxGameRole,$require2fa,date('c')]);
        Audit::log('web_role_template.update','web_role',$role,['permissions'=>$permissions,'server_caps'=>$serverCaps,'max_game_role'=>$maxGameRole,'require_2fa'=>$require2fa,'reason'=>$reason]);
        View::flash('success','Rollenvorlage '.Auth::webRoleLabel($role).' wurde gespeichert. Nutzer ohne individuelle Abweichung übernehmen sie sofort.');header('Location: /users#role-'.$role);
    }

    public function createPackage(): void
    {
        Auth::requirePermission('roles.packages.manage');Security::verifyCsrf();$pdo=Database::connection();$name=trim((string)($_POST['name']??''));$description=trim((string)($_POST['description']??''));$reason=trim((string)($_POST['change_reason']??''));
        if($name===''||mb_strlen($name)>80)$this->fail('Bitte einen Paketnamen mit maximal 80 Zeichen angeben.');if($reason==='')$this->fail('Bitte einen Änderungsgrund angeben.');$permissions=$this->selectedPermissions('package_permissions');
        $pdo->prepare('INSERT INTO permission_packages(name,description,permissions_json,active,created_by,created_at,updated_at) VALUES(?,?,?,?,?,?,?)')->execute([$name,$description?:null,json_encode($permissions,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),1,(int)Auth::user()['id'],date('c'),date('c')]);$id=(int)$pdo->lastInsertId();
        Audit::log('permission_package.create','permission_package',(string)$id,['name'=>$name,'permissions'=>$permissions,'reason'=>$reason]);View::flash('success','Rechtepaket wurde erstellt.');header('Location:/users#permission-packages');
    }

    public function updatePackage(string $id): void
    {
        Auth::requirePermission('roles.packages.manage');Security::verifyCsrf();$pid=(int)$id;$pdo=Database::connection();$stmt=$pdo->prepare('SELECT * FROM permission_packages WHERE id=?');$stmt->execute([$pid]);$package=$stmt->fetch();if(!$package)$this->fail('Rechtepaket nicht gefunden.');
        $name=trim((string)($_POST['name']??$package['name']));$description=trim((string)($_POST['description']??''));$active=isset($_POST['active'])?1:0;$reason=trim((string)($_POST['change_reason']??''));if($name===''||$reason==='')$this->fail('Paketname und Änderungsgrund sind erforderlich.');$permissions=$this->selectedPermissions('package_permissions');
        $pdo->prepare('UPDATE permission_packages SET name=?,description=?,permissions_json=?,active=?,updated_at=? WHERE id=?')->execute([$name,$description?:null,json_encode($permissions,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$active,date('c'),$pid]);
        Audit::log('permission_package.update','permission_package',(string)$pid,['name'=>$name,'active'=>$active,'permissions'=>$permissions,'reason'=>$reason]);View::flash('success','Rechtepaket wurde aktualisiert.');header('Location:/users#permission-packages');
    }

    public function assignPackage(string $id): void
    {
        Auth::requirePermission('roles.packages.manage');Security::verifyCsrf();$uid=(int)$id;$target=$this->manageableTarget($uid);$pdo=Database::connection();$pid=(int)($_POST['package_id']??0);$stmt=$pdo->prepare('SELECT * FROM permission_packages WHERE id=? AND active=1');$stmt->execute([$pid]);$package=$stmt->fetch();if(!$package)$this->fail('Aktives Rechtepaket nicht gefunden.');
        foreach(PermissionCenterService::packagePermissions($package) as $permission)if(!Auth::isFounder()&&!Auth::can($permission))$this->fail('Das Paket enthält ein Recht, das das eigene Konto nicht vergeben darf: '.Auth::PERMISSIONS[$permission]);
        $expires=$this->normalizeDateTime((string)($_POST['expires_at']??''));$reason=trim((string)($_POST['reason']??''));if($reason==='')$this->fail('Bitte einen Grund für die Paketzuweisung angeben.');
        $pdo->prepare('INSERT INTO user_permission_packages(user_id,package_id,expires_at,reason,assigned_by,created_at) VALUES(?,?,?,?,?,?) ON CONFLICT(user_id,package_id) DO UPDATE SET expires_at=excluded.expires_at,reason=excluded.reason,assigned_by=excluded.assigned_by,created_at=excluded.created_at')->execute([$uid,$pid,$expires?:null,$reason,(int)Auth::user()['id'],date('c')]);
        Audit::log('permission_package.assign','user',(string)$uid,['package_id'=>$pid,'package_name'=>$package['name'],'expires_at'=>$expires,'reason'=>$reason]);View::flash('success','Rechtepaket wurde zugewiesen.');header('Location:/users#user-'.$uid);
    }

    public function removePackage(string $id,string $assignmentId): void
    {
        Auth::requirePermission('roles.packages.manage');Security::verifyCsrf();$uid=(int)$id;$this->manageableTarget($uid);$aid=(int)$assignmentId;$pdo=Database::connection();$stmt=$pdo->prepare('SELECT a.*,p.name FROM user_permission_packages a JOIN permission_packages p ON p.id=a.package_id WHERE a.id=? AND a.user_id=?');$stmt->execute([$aid,$uid]);$row=$stmt->fetch();if(!$row)$this->fail('Paketzuweisung nicht gefunden.');$pdo->prepare('DELETE FROM user_permission_packages WHERE id=?')->execute([$aid]);Audit::log('permission_package.remove','user',(string)$uid,['package'=>$row['name']]);View::flash('success','Rechtepaket wurde entfernt.');header('Location:/users#user-'.$uid);
    }

    public function addTemporaryPermission(string $id): void
    {
        Auth::requirePermission('users.permissions');Security::verifyCsrf();$uid=(int)$id;$this->manageableTarget($uid);$permission=(string)($_POST['permission_key']??'');$effect=(string)($_POST['effect']??'allow');if(!isset(Auth::PERMISSIONS[$permission]))$this->fail('Unbekanntes Recht.');if($effect!=='deny')$effect='allow';if($effect==='allow'&&!Auth::isFounder()&&!Auth::can($permission))$this->fail('Dieses Recht darf nicht weitergegeben werden.');
        $expires=$this->normalizeDateTime((string)($_POST['expires_at']??''));if(!$expires)$this->fail('Für ein temporäres Recht muss ein zukünftiges Ablaufdatum angegeben werden.');$reason=trim((string)($_POST['reason']??''));if($reason==='')$this->fail('Bitte einen Grund angeben.');
        $pdo=Database::connection();$pdo->prepare('INSERT INTO user_temporary_permissions(user_id,permission_key,effect,expires_at,reason,created_by,created_at) VALUES(?,?,?,?,?,?,?)')->execute([$uid,$permission,$effect,$expires,$reason,(int)Auth::user()['id'],date('c')]);$tid=(int)$pdo->lastInsertId();Audit::log('permission.temporary.add','user',(string)$uid,['temporary_id'=>$tid,'permission'=>$permission,'effect'=>$effect,'expires_at'=>$expires,'reason'=>$reason]);View::flash('success','Temporäres Recht wurde gespeichert.');header('Location:/users#user-'.$uid);
    }

    public function removeTemporaryPermission(string $id,string $temporaryId): void
    {
        Auth::requirePermission('users.permissions');Security::verifyCsrf();$uid=(int)$id;$this->manageableTarget($uid);$tid=(int)$temporaryId;$pdo=Database::connection();$stmt=$pdo->prepare('SELECT * FROM user_temporary_permissions WHERE id=? AND user_id=?');$stmt->execute([$tid,$uid]);$row=$stmt->fetch();if(!$row)$this->fail('Temporäres Recht nicht gefunden.');$pdo->prepare('DELETE FROM user_temporary_permissions WHERE id=?')->execute([$tid]);Audit::log('permission.temporary.remove','user',(string)$uid,['permission'=>$row['permission_key'],'effect'=>$row['effect']]);View::flash('success','Temporäres Recht wurde entfernt.');header('Location:/users#user-'.$uid);
    }

    public function saveServerGroup(?string $id=null): void
    {
        Auth::requirePermission('servers.access_groups.manage');Security::verifyCsrf();$pdo=Database::connection();$gid=(int)($id??0);$name=trim((string)($_POST['name']??''));$description=trim((string)($_POST['description']??''));$reason=trim((string)($_POST['change_reason']??''));if($name===''||$reason==='')$this->fail('Name und Änderungsgrund sind erforderlich.');$serverIds=array_values(array_unique(array_map('intval',(array)($_POST['server_ids']??[]))));
        foreach($serverIds as $sid)if($sid>0&&!Auth::isFounder()&&!Auth::canServer($sid,'view'))$this->fail('Ein Server darf nur gruppiert werden, wenn das eigene Konto darauf Zugriff besitzt.');
        $pdo->beginTransaction();try{if($gid>0){$stmt=$pdo->prepare('SELECT id FROM server_access_groups WHERE id=?');$stmt->execute([$gid]);if(!$stmt->fetchColumn())throw new \RuntimeException('Servergruppe nicht gefunden.');$pdo->prepare('UPDATE server_access_groups SET name=?,description=?,updated_at=? WHERE id=?')->execute([$name,$description?:null,date('c'),$gid]);}else{$pdo->prepare('INSERT INTO server_access_groups(name,description,created_by,created_at,updated_at) VALUES(?,?,?,?,?)')->execute([$name,$description?:null,(int)Auth::user()['id'],date('c'),date('c')]);$gid=(int)$pdo->lastInsertId();}$pdo->prepare('DELETE FROM server_group_members WHERE group_id=?')->execute([$gid]);$ins=$pdo->prepare('INSERT INTO server_group_members(group_id,server_id) VALUES(?,?)');foreach($serverIds as $sid)if($sid>0)$ins->execute([$gid,$sid]);$pdo->commit();}catch(\Throwable $e){if($pdo->inTransaction())$pdo->rollBack();$this->fail($e->getMessage());}
        Audit::log('server_access_group.save','server_access_group',(string)$gid,['name'=>$name,'servers'=>$serverIds,'reason'=>$reason]);View::flash('success','Servergruppe wurde gespeichert.');header('Location:/users#server-groups');
    }

    public function deleteServerGroup(string $id): void
    {
        Auth::requirePermission('servers.access_groups.manage');Security::verifyCsrf();$gid=(int)$id;$pdo=Database::connection();$stmt=$pdo->prepare('SELECT name FROM server_access_groups WHERE id=?');$stmt->execute([$gid]);$name=$stmt->fetchColumn();if($name===false)$this->fail('Servergruppe nicht gefunden.');$pdo->prepare('DELETE FROM server_access_groups WHERE id=?')->execute([$gid]);Audit::log('server_access_group.delete','server_access_group',(string)$gid,['name'=>$name]);View::flash('success','Servergruppe wurde gelöscht.');header('Location:/users#server-groups');
    }

    public function assignServerGroup(string $id): void
    {
        Auth::requirePermission('users.permissions');Security::verifyCsrf();$uid=(int)$id;$this->manageableTarget($uid);$pdo=Database::connection();$gid=(int)($_POST['group_id']??0);$stmt=$pdo->prepare('SELECT * FROM server_access_groups WHERE id=?');$stmt->execute([$gid]);$group=$stmt->fetch();if(!$group)$this->fail('Servergruppe nicht gefunden.');$serverIds=$pdo->prepare('SELECT server_id FROM server_group_members WHERE group_id=?');$serverIds->execute([$gid]);$memberIds=array_map('intval',$serverIds->fetchAll(\PDO::FETCH_COLUMN));
        $posted=(array)($_POST['server_cap']??[]);$values=[];foreach(self::SERVER_CAP_COLUMNS as $cap=>$col){$allow=isset($posted[$cap]);if($allow&&!Auth::isFounder())foreach($memberIds as $sid)if(!Auth::canServer($sid,$cap))$this->fail('Die Servergruppe würde ein höheres Recht vergeben als das eigene Konto auf mindestens einem enthaltenen Server besitzt: '.$cap);$values[$cap]=$allow?1:0;}
        $max=strtolower(trim((string)($_POST['max_game_role']??'spieler')));if(!isset(Auth::GAME_ROLE_LEVELS[$max]))$max='spieler';if(!Auth::isFounder())foreach($memberIds as $sid)if((Auth::GAME_ROLE_LEVELS[$max]??1)>(Auth::GAME_ROLE_LEVELS[Auth::serverMaxGameRole($sid)]??1))$this->fail('Die maximale CS2-Rolle ist auf mindestens einem Server höher als die eigene Freigabe.');$expires=$this->normalizeDateTime((string)($_POST['expires_at']??''));$reason=trim((string)($_POST['reason']??''));if($reason==='')$this->fail('Bitte einen Grund für die Servergruppen-Freigabe angeben.');
        $pdo->prepare('INSERT INTO user_server_group_access(user_id,group_id,can_view,can_manage_players,can_manage_permissions,can_sync,can_live_admin,max_game_role,expires_at,reason,assigned_by,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(user_id,group_id) DO UPDATE SET can_view=excluded.can_view,can_manage_players=excluded.can_manage_players,can_manage_permissions=excluded.can_manage_permissions,can_sync=excluded.can_sync,can_live_admin=excluded.can_live_admin,max_game_role=excluded.max_game_role,expires_at=excluded.expires_at,reason=excluded.reason,assigned_by=excluded.assigned_by,updated_at=excluded.updated_at')->execute([$uid,$gid,$values['view'],$values['players'],$values['permissions'],$values['sync'],$values['live'],$max,$expires?:null,$reason,(int)Auth::user()['id'],date('c')]);Audit::log('server_access_group.assign','user',(string)$uid,['group_id'=>$gid,'group_name'=>$group['name'],'expires_at'=>$expires,'reason'=>$reason]);View::flash('success','Servergruppe wurde dem Benutzer zugewiesen.');header('Location:/users#user-'.$uid);
    }

    public function removeServerGroup(string $id,string $groupId): void
    {
        Auth::requirePermission('users.permissions');Security::verifyCsrf();$uid=(int)$id;$this->manageableTarget($uid);$gid=(int)$groupId;$pdo=Database::connection();$pdo->prepare('DELETE FROM user_server_group_access WHERE user_id=? AND group_id=?')->execute([$uid,$gid]);Audit::log('server_access_group.remove','user',(string)$uid,['group_id'=>$gid]);View::flash('success','Servergruppen-Freigabe wurde entfernt.');header('Location:/users#user-'.$uid);
    }

    public function preview(string $id): void
    {
        Auth::requirePermission('users.preview');$uid=(int)$id;$target=$this->target($uid);if(!Auth::isFounder()&&!Auth::canManageWebRoleTarget((string)$target['web_role'])&&(int)Auth::user()['id']!==$uid)$this->fail('Diese Rechtevorschau ist nicht erlaubt.','/users',403);$pdo=Database::connection();$servers=$pdo->query('SELECT id,name FROM game_servers ORDER BY lower(name),id')->fetchAll();$decisions=Auth::effectivePermissionDecisionsForUser($target);$serverDecisions=[];foreach($servers as $s){$sid=(int)$s['id'];foreach(array_keys(self::SERVER_CAP_COLUMNS) as $cap)$serverDecisions[$sid][$cap]=Auth::serverAccessDecisionForUser($target,$sid,$cap);$serverDecisions[$sid]['max_game_role']=Auth::serverMaxGameRoleForUser($target,$sid);}View::render('user_permissions_preview',['title'=>'Effektive Rechte · '.Auth::displayName($target),'target'=>$target,'decisions'=>$decisions,'servers'=>$servers,'serverDecisions'=>$serverDecisions]);
    }

    public function bulkUpdate(): void
    {
        Auth::requirePermission('users.bulk');Security::verifyCsrf();$ids=array_values(array_unique(array_filter(array_map('intval',(array)($_POST['user_ids']??[])))));if(!$ids)$this->fail('Bitte mindestens einen Benutzer auswählen.');$action=(string)($_POST['bulk_action']??'');$reason=trim((string)($_POST['reason']??''));if($reason==='')$this->fail('Bitte einen Grund für die Massenänderung angeben.');$pdo=Database::connection();$done=0;
        foreach($ids as $uid){$target=$this->target($uid);if((int)Auth::user()['id']===$uid||(!Auth::isFounder()&&!Auth::canManageWebRoleTarget((string)$target['web_role'])))continue;if($action==='activate'||$action==='deactivate'){$active=$action==='activate'?1:0;$pdo->prepare('UPDATE users SET active=?,updated_at=? WHERE id=?')->execute([$active,date('c'),$uid]);$done++;}elseif($action==='role'){$role=Auth::normalizeWebRole((string)($_POST['bulk_role']??'spieler'));if(!Auth::canAssignWebRole($role))continue;$pdo->prepare('UPDATE users SET web_role=?,auth_version=auth_version+1,updated_at=? WHERE id=?')->execute([$role,date('c'),$uid]);$done++;}elseif($action==='package'){$pid=(int)($_POST['bulk_package_id']??0);$stmt=$pdo->prepare('SELECT * FROM permission_packages WHERE id=? AND active=1');$stmt->execute([$pid]);$package=$stmt->fetch();if(!$package)continue;foreach(PermissionCenterService::packagePermissions($package) as $permission)if(!Auth::isFounder()&&!Auth::can($permission))continue 2;$pdo->prepare('INSERT INTO user_permission_packages(user_id,package_id,reason,assigned_by,created_at) VALUES(?,?,?,?,?) ON CONFLICT(user_id,package_id) DO UPDATE SET reason=excluded.reason,assigned_by=excluded.assigned_by,created_at=excluded.created_at')->execute([$uid,$pid,$reason,(int)Auth::user()['id'],date('c')]);$done++;}}
        Audit::log('users.bulk_update','users',null,['action'=>$action,'requested_ids'=>$ids,'changed'=>$done,'reason'=>$reason]);View::flash('success',$done.' Benutzer wurden aktualisiert.');header('Location:/users');
    }

    public function exportRoles(): void
    {
        Auth::requirePermission('roles.export_import');$templates=Auth::allRoleTemplates();$payload=['format'=>'serververwaltung-role-templates','version'=>'1.7.9','exported_at'=>date('c'),'templates'=>[]];foreach($templates as $role=>$t)$payload['templates'][$role]=['permissions'=>$t['permissions'],'server_caps'=>$t['server_caps'],'max_game_role'=>$t['max_game_role'],'require_2fa'=>(bool)($t['require_2fa']??false)];header('Content-Type: application/json; charset=utf-8');header('Content-Disposition: attachment; filename="serververwaltung-role-templates-'.date('Ymd-His').'.json"');echo json_encode($payload,JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);exit;
    }

    public function importRoles(): void
    {
        Auth::requirePermission('roles.export_import');Security::verifyCsrf();$raw=trim((string)($_POST['role_templates_json']??''));$reason=trim((string)($_POST['change_reason']??''));if($raw===''||$reason==='')$this->fail('JSON und Änderungsgrund sind erforderlich.');$payload=json_decode($raw,true);if(!is_array($payload)||($payload['format']??'')!=='serververwaltung-role-templates'||!is_array($payload['templates']??null))$this->fail('Das JSON ist keine gültige Rollenvorlagen-Datei.');$pdo=Database::connection();$changed=[];
        foreach($payload['templates'] as $roleRaw=>$t){$role=Auth::normalizeWebRole((string)$roleRaw);if(!Auth::canEditRoleTemplate($role))continue;if($role==='founder')continue;$permissions=[];foreach((array)($t['permissions']??[]) as $permission){if(!is_string($permission)||!isset(Auth::PERMISSIONS[$permission]))continue;if(!Auth::isFounder()&&!Auth::can($permission))continue;$permissions[]=$permission;}$caps=[];foreach(array_keys(self::SERVER_CAP_COLUMNS) as $cap)$caps[$cap]=!empty($t['server_caps'][$cap])&&(Auth::isFounder()||!empty(Auth::roleTemplate(Auth::effectiveWebRole())['server_caps'][$cap]));$max=Auth::normalizeGameRoleForImport((string)($t['max_game_role']??'spieler'));$actorMax=Auth::GAME_ROLE_LEVELS[Auth::maxCommunityRole()]??1;if(!Auth::isFounder()&&(Auth::GAME_ROLE_LEVELS[$max]??1)>$actorMax)$max=Auth::maxCommunityRole();$require2fa=!empty($t['require_2fa'])?1:0;$pdo->prepare('UPDATE web_role_templates SET permissions_json=?,server_caps_json=?,max_game_role=?,require_2fa=?,updated_at=? WHERE role_name=?')->execute([json_encode(array_values(array_unique($permissions)),JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),json_encode($caps,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$max,$require2fa,date('c'),$role]);$changed[]=$role;}
        Audit::log('web_role_templates.import','web_role',null,['roles'=>$changed,'reason'=>$reason]);View::flash('success',count($changed).' Rollenvorlagen wurden importiert. Founder wurde aus Sicherheitsgründen nicht verändert.');header('Location:/users#role-templates');
    }

    private function selectedPermissions(string $field): array
    {
        $selected=(array)($_POST[$field]??[]);$out=[];foreach($selected as $encoded=>$value){if((string)$value!=='1')continue;$permission=str_replace('__','.',(string)$encoded);if(!isset(Auth::PERMISSIONS[$permission]))continue;if(!Auth::isFounder()&&!Auth::can($permission))$this->fail('Dieses Recht darf nicht weitergegeben werden: '.Auth::PERMISSIONS[$permission]);$out[]=$permission;}return array_values(array_unique($out));
    }

    private function target(int $uid): array
    {
        $stmt=Database::connection()->prepare('SELECT * FROM users WHERE id=?');$stmt->execute([$uid]);$target=$stmt->fetch();if(!$target)$this->fail('Nutzer nicht gefunden.','/users',404);return $target;
    }

    private function manageableTarget(int $uid): array
    {
        $target=$this->target($uid);if($uid===(int)Auth::user()['id'])$this->fail('Eigene Sonderrechte dürfen auf diesem Weg nicht verändert werden.');if(!Auth::isFounder()&&!Auth::canManageWebRoleTarget((string)$target['web_role']))$this->fail('Gleiche oder höhere Rollen sind geschützt.','/users',403);return $target;
    }

    private function serverAccessRow(int $uid,int $sid): ?array
    {
        $stmt=Database::connection()->prepare('SELECT * FROM user_server_access WHERE user_id=? AND server_id=?');$stmt->execute([$uid,$sid]);$row=$stmt->fetch();return $row?:null;
    }

    private function normalizeDateTime(string $value): string
    {
        $value=trim($value);if($value==='')return '';$ts=strtotime($value);if($ts===false||$ts<=time())$this->fail('Das Ablaufdatum muss in der Zukunft liegen.');return date('c',$ts);
    }

    private function fail(string $message,string $location='/users',int $status=0): never
    {
        if($status>0)http_response_code($status);View::flash('error',$message);header('Location: '.$location);exit;
    }
}
