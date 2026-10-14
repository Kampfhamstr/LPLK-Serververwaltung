<?php
namespace App\Services;

use App\Core\Auth;
use App\Core\Database;

final class PermissionCenterService
{
    public static function packages(): array
    {
        try {
            return Database::connection()->query(
                'SELECT p.*,u.name created_by_name FROM permission_packages p LEFT JOIN users u ON u.id=p.created_by ORDER BY p.active DESC,lower(p.name),p.id'
            )->fetchAll();
        } catch (\Throwable) { return []; }
    }

    public static function packageAssignments(): array
    {
        try {
            $rows=Database::connection()->query(
                'SELECT a.*,p.name package_name,p.permissions_json,u.name assigned_by_name
                 FROM user_permission_packages a JOIN permission_packages p ON p.id=a.package_id
                 LEFT JOIN users u ON u.id=a.assigned_by ORDER BY a.user_id,lower(p.name),a.id'
            )->fetchAll();
        } catch (\Throwable) { return []; }
        $out=[]; foreach($rows as $row)$out[(int)$row['user_id']][]=$row; return $out;
    }

    public static function temporaryPermissions(): array
    {
        try {
            $rows=Database::connection()->query(
                'SELECT t.*,u.name created_by_name FROM user_temporary_permissions t LEFT JOIN users u ON u.id=t.created_by ORDER BY t.user_id,t.expires_at,t.id'
            )->fetchAll();
        } catch (\Throwable) { return []; }
        $out=[]; foreach($rows as $row)$out[(int)$row['user_id']][]=$row; return $out;
    }

    public static function serverGroups(): array
    {
        try {
            $groups=Database::connection()->query(
                'SELECT g.*,u.name created_by_name FROM server_access_groups g LEFT JOIN users u ON u.id=g.created_by ORDER BY lower(g.name),g.id'
            )->fetchAll();
            $members=Database::connection()->query(
                'SELECT gm.group_id,s.id server_id,s.name server_name FROM server_group_members gm JOIN game_servers s ON s.id=gm.server_id ORDER BY gm.group_id,lower(s.name),s.id'
            )->fetchAll();
        } catch (\Throwable) { return []; }
        $by=[];
        foreach($groups as $g){$g['servers']=[];$by[(int)$g['id']]=$g;}
        foreach($members as $m)if(isset($by[(int)$m['group_id']]))$by[(int)$m['group_id']]['servers'][]=$m;
        return array_values($by);
    }

    public static function serverGroupAccess(): array
    {
        try {
            $rows=Database::connection()->query(
                'SELECT uga.*,g.name group_name,u.name assigned_by_name FROM user_server_group_access uga
                 JOIN server_access_groups g ON g.id=uga.group_id LEFT JOIN users u ON u.id=uga.assigned_by
                 ORDER BY uga.user_id,lower(g.name),uga.group_id'
            )->fetchAll();
        } catch (\Throwable) { return []; }
        $out=[];foreach($rows as $row)$out[(int)$row['user_id']][]=$row;return $out;
    }

    public static function reviewMetrics(array $users): array
    {
        $pdo=Database::connection();$now=time();$soon=date('c',$now+48*3600);$cutoff=date('c',$now-90*86400);
        $m=['temporary_active'=>0,'expires_48h'=>0,'stale_users'=>0,'inactive_with_access'=>0,'founders_active'=>0,'custom_users'=>0,'orphaned_permissions'=>0];
        try{
            $stmt=$pdo->prepare("SELECT COUNT(*) FROM user_temporary_permissions WHERE expires_at IS NULL OR expires_at='' OR expires_at>?");$stmt->execute([date('c')]);$m['temporary_active']=(int)$stmt->fetchColumn();
            $stmt=$pdo->prepare("SELECT COUNT(*) FROM user_temporary_permissions WHERE expires_at>? AND expires_at<=?");$stmt->execute([date('c'),$soon]);$m['expires_48h']=(int)$stmt->fetchColumn();
            $stmt=$pdo->prepare("SELECT COUNT(*) FROM users WHERE active=1 AND COALESCE(last_login_at,created_at)<?");$stmt->execute([$cutoff]);$m['stale_users']=(int)$stmt->fetchColumn();
            $m['inactive_with_access']=(int)$pdo->query('SELECT COUNT(DISTINCT u.id) FROM users u JOIN user_server_access a ON a.user_id=u.id WHERE u.active=0')->fetchColumn();
        }catch(\Throwable){}
        foreach($users as $u){
            if(Auth::normalizeWebRole((string)$u['web_role'])==='founder'&&(int)$u['active']===1)$m['founders_active']++;
            $raw=Auth::rawPermissionOverrides($u['permissions_json']??null);
            if($raw)$m['custom_users']++;
            $m['orphaned_permissions']+=count(Auth::orphanedPermissionOverrides($u['permissions_json']??null));
        }
        return $m;
    }

    public static function packagePermissions(array $package): array
    {
        $decoded=json_decode((string)($package['permissions_json']??'[]'),true);
        if(!is_array($decoded))return [];
        return array_values(array_filter($decoded,static fn($p)=>is_string($p)&&array_key_exists($p,Auth::PERMISSIONS)));
    }
}
