<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Database;
use App\Core\View;
use App\Core\Security;
use App\Core\Audit;
use App\Core\Mailer;
use App\Core\Config;

final class InvitationsController
{
    public function index(): void
    {
        Auth::requirePermission('users.invite');
        $pdo = Database::connection();
        $stmt = $pdo->query('SELECT i.*,u.name inviter FROM invitations i JOIN users u ON u.id=i.invited_by ORDER BY i.id DESC');
        $servers = $pdo->query('SELECT id,name FROM game_servers ORDER BY lower(name),id')->fetchAll();
        View::render('invitations', [
            'title' => 'Einladungen',
            'invitations' => $stmt->fetchAll(),
            'invitableRoles'=>Auth::invitableWebRoles(),
            'servers'=>$servers,
        ]);
    }

    public function create(): void
    {
        Auth::requirePermission('users.invite');
        Security::verifyCsrf();
        $email = strtolower(trim((string)($_POST['email'] ?? '')));
        if (!filter_var($email, FILTER_VALIDATE_EMAIL)) {
            View::flash('error', 'Bitte eine gültige E-Mail-Adresse eingeben.');
            header('Location: /invitations');
            return;
        }

        $role = Auth::normalizeWebRole((string)($_POST['web_role'] ?? 'spieler'));
        if (!Auth::canInviteWebRole($role)) {
            View::flash('error','Diese Rolle darf nicht eingeladen werden. Außer Founder dürfen Panelnutzer ausschließlich Rollen unterhalb ihrer eigenen Rolle einladen.');
            header('Location: /invitations');
            return;
        }

        $pdo = Database::connection();
        $exists = $pdo->prepare('SELECT COUNT(*) FROM users WHERE lower(email)=lower(?)');
        $exists->execute([$email]);
        if ((int)$exists->fetchColumn() > 0) {
            View::flash('error', 'Für diese E-Mail-Adresse existiert bereits ein Benutzerkonto.');
            header('Location: /invitations');
            return;
        }

        $perms = null;
        $serverAccess = [];
        $accessReason=trim((string)($_POST['access_reason']??''));
        if (Auth::can('users.permissions')) {
            $overrides=[];
            foreach((array)($_POST['perm_override']??[]) as $encoded=>$mode){
                $permission=str_replace('__','.',(string)$encoded);
                if(!array_key_exists($permission,Auth::PERMISSIONS))continue;
                $mode=(string)$mode;
                if($mode==='inherit')continue;
                $allowed=$mode==='allow';
                if($allowed&&!Auth::isFounder()&&!Auth::can($permission)){
                    View::flash('error','Die Einladung darf kein Recht erhalten, das dem einladenden Konto selbst fehlt: '.Auth::PERMISSIONS[$permission]);
                    header('Location: /invitations'); return;
                }
                $overrides[$permission]=$allowed;
            }
            $perms=$overrides?json_encode($overrides,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES):null;

            $modes=(array)($_POST['server_mode']??[]);
            $caps=(array)($_POST['server_cap']??[]);
            $maxRoles=(array)($_POST['server_max_game_role']??[]);
            $serverIds=$pdo->query('SELECT id FROM game_servers ORDER BY id')->fetchAll(\PDO::FETCH_COLUMN);
            foreach($serverIds as $rawId){
                $sid=(int)$rawId;
                if((string)($modes[$sid]??'inherit')!=='custom') continue;
                $row=['view'=>false,'players'=>false,'permissions'=>false,'sync'=>false,'live'=>false,'max_game_role'=>'spieler'];
                foreach(['view','players','permissions','sync','live'] as $cap){
                    $allow=isset($caps[$sid][$cap]);
                    if($allow&&!Auth::isFounder()&&!Auth::canServer($sid,$cap)){
                        View::flash('error','Eine Einladung kann auf einem Server keine höheren Rechte erhalten als das einladende Konto selbst besitzt.');
                        header('Location: /invitations');
                        return;
                    }
                    $row[$cap]=$allow;
                }
                $max=strtolower(trim((string)($maxRoles[$sid]??'spieler')));
                if(!array_key_exists($max,Auth::GAME_ROLE_LEVELS))$max='spieler';
                $actorMax=Auth::GAME_ROLE_LEVELS[Auth::serverMaxGameRole($sid)]??1;
                if(!Auth::isFounder()&&(Auth::GAME_ROLE_LEVELS[$max]??1)>$actorMax){
                    View::flash('error','Die maximale CS2-Rolle der Einladung darf nicht höher als die eigene Serverfreigabe sein.');
                    header('Location: /invitations');
                    return;
                }
                $row['max_game_role']=$max;
                $serverAccess[(string)$sid]=$row;
            }
        }

        if(($perms!==null||$serverAccess)&&$accessReason===''){
            View::flash('error','Bitte für individuelle Einladungsrechte/Serverfreigaben einen kurzen Grund angeben.');
            header('Location: /invitations'); return;
        }

        $token = bin2hex(random_bytes(32));
        $hash = hash('sha256', $token);
        $expires = date('c', time() + 48 * 3600);
        $pdo->prepare('INSERT INTO invitations(email,web_role,permissions_json,server_access_json,token_hash,invited_by,expires_at,created_at) VALUES(?,?,?,?,?,?,?,?)')
            ->execute([$email, $role, $perms, json_encode($serverAccess,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES), $hash, Auth::user()['id'], $expires, date('c')]);
        $id = (int)$pdo->lastInsertId();
        $url = rtrim((string)Config::get('app_url'), '/') . '/invite/' . $token;
        $roleLabel = Auth::webRoleLabel($role);
        $appName = (string)Config::get('app_name', 'LPLK Serververwaltung');
        $serverText = $serverAccess
            ? count($serverAccess).' Server mit individuellen Freigaben; übrige Server verwenden die Rollenvorlage.'
            : 'Serverzugriffe werden aus der Rollenvorlage '.$roleLabel.' übernommen.';
        $safeUrl = htmlspecialchars($url, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
        $safeApp = htmlspecialchars($appName, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
        $safeRole = htmlspecialchars($roleLabel, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
        $safeServers = htmlspecialchars($serverText, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
        $html = '<!doctype html><html><body style="margin:0;background:#f3f5f7;font-family:Arial,Helvetica,sans-serif;color:#20242a">' .
            '<div style="max-width:640px;margin:32px auto;background:#ffffff;border:1px solid #dfe3e8;border-radius:8px;overflow:hidden">' .
            '<div style="padding:24px 28px;background:#171a1f;color:#ffffff"><div style="font-size:20px;font-weight:700">' . $safeApp . '</div><div style="margin-top:5px;color:#b8c0ca;font-size:13px">Einladung zur Verwaltungsoberfläche</div></div>' .
            '<div style="padding:28px"><p style="margin-top:0">Sie wurden eingeladen, ein Benutzerkonto für die ' . $safeApp . ' einzurichten.</p>' .
            '<table style="width:100%;border-collapse:collapse;margin:20px 0;background:#f7f8fa"><tr><td style="padding:10px 12px;color:#68717d;width:150px">Vorgesehene Rolle</td><td style="padding:10px 12px;font-weight:700">' . $safeRole . '</td></tr>' .
            '<tr><td style="padding:10px 12px;color:#68717d;border-top:1px solid #e1e5ea">Serverzugriff</td><td style="padding:10px 12px;border-top:1px solid #e1e5ea">' . $safeServers . '</td></tr></table>' .
            '<p>Über den folgenden Link legen Sie Ihr persönliches Passwort fest und aktivieren den Account:</p>' .
            '<p style="margin:24px 0"><a href="' . $safeUrl . '" style="display:inline-block;background:#20252c;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:5px;font-weight:700">Einladung annehmen</a></p>' .
            '<p style="font-size:13px;color:#626b76">Der Link ist 48 Stunden gültig, nur einmal verwendbar und sollte nicht weitergegeben werden.</p>' .
            '</div><div style="padding:16px 28px;border-top:1px solid #e5e8ec;color:#7a838e;font-size:12px">' . $safeApp . '</div></div></body></html>';
        $text = $appName . "\n\nSie wurden eingeladen, ein Benutzerkonto einzurichten.\n\nVorgesehene Rolle: " . $roleLabel . "\nServerzugriff: " . $serverText . "\n\nEinladung annehmen:\n" . $url . "\n\nDer Link ist 48 Stunden gültig und nur einmal verwendbar.";

        try {
            Mailer::send($email,'Einladung zur ' . $appName,$html,$text);
            View::flash('success', 'Einladung wurde erstellt und per E-Mail versendet.');
        } catch (\Throwable $e) {
            View::flash('warning', 'Einladung wurde erstellt, der E-Mail-Versand ist jedoch fehlgeschlagen: ' . $e->getMessage());
        }

        Audit::log('invitation.create', 'invitation', (string)$id, [
            'email' => $email,'role' => $role,'permission_overrides' => $perms?json_decode($perms,true):[],'server_access' => $serverAccess,'reason'=>$accessReason,
        ]);
        header('Location: /invitations');
    }

    public function revoke(string $id): void
    {
        Auth::requirePermission('users.invite');
        Security::verifyCsrf();
        $pdo = Database::connection();
        $stmt = $pdo->prepare('SELECT id,web_role,accepted_at FROM invitations WHERE id=?');
        $stmt->execute([(int)$id]);
        $invitation = $stmt->fetch();
        if (!$invitation) { http_response_code(404); exit('Einladung nicht gefunden.'); }
        if (!Auth::canInviteWebRole((string)$invitation['web_role']) && !Auth::isFounder()) {
            http_response_code(403);
            View::flash('error','Einladung für eine gleiche oder höhere Rolle darf nicht widerrufen werden.');
            header('Location: /invitations');
            return;
        }
        $pdo->prepare('UPDATE invitations SET revoked_at=? WHERE id=? AND accepted_at IS NULL')->execute([date('c'), (int)$id]);
        Audit::log('invitation.revoke', 'invitation', $id);
        View::flash('success', 'Einladung wurde widerrufen.');
        header('Location: /invitations');
    }

    public function acceptForm(string $token): void
    {
        $inv = $this->find($token);
        View::render('invite_accept', ['title' => 'Einladung annehmen', 'invitation' => $inv, 'token' => $token]);
    }

    public function accept(string $token): void
    {
        Security::verifyCsrf();
        $inv = $this->find($token);
        $name = trim((string)($_POST['name'] ?? ''));
        $password = (string)($_POST['password'] ?? '');
        $passwordConfirm = (string)($_POST['password_confirm'] ?? '');
        if ($name === '' || strlen($password) < 12 || $password !== $passwordConfirm) {
            View::flash('error', 'Name angeben und ein identisches Passwort mit mindestens 12 Zeichen verwenden.');
            header('Location: /invite/' . $token);
            return;
        }

        $pdo = Database::connection();
        $pdo->beginTransaction();
        try {
            $hash = password_hash($password, PASSWORD_ARGON2ID);
            $role = Auth::normalizeWebRole((string)$inv['web_role']);
            $permissionsJson = is_string($inv['permissions_json'] ?? null) && trim((string)$inv['permissions_json']) !== '' ? (string)$inv['permissions_json'] : null;
            $pdo->prepare('INSERT INTO users(name,email,password_hash,web_role,permissions_json,active,created_at,updated_at) VALUES(?,?,?,?,?,1,?,?)')
                ->execute([$name, $inv['email'], $hash, $role, $permissionsJson, date('c'), date('c')]);
            $uid = (int)$pdo->lastInsertId();

            $access = json_decode((string)($inv['server_access_json'] ?? '{}'), true);
            if (is_array($access)) {
                $up = $pdo->prepare(
                    'INSERT INTO user_server_access(user_id,server_id,can_view,can_manage_players,can_manage_permissions,can_sync,can_live_admin,max_game_role,updated_at)
                     VALUES(?,?,?,?,?,?,?,?,?)
                     ON CONFLICT(user_id,server_id) DO UPDATE SET can_view=excluded.can_view,can_manage_players=excluded.can_manage_players,can_manage_permissions=excluded.can_manage_permissions,can_sync=excluded.can_sync,can_live_admin=excluded.can_live_admin,max_game_role=excluded.max_game_role,updated_at=excluded.updated_at'
                );
                foreach ($access as $sid=>$row) {
                    if (!is_array($row) || (int)$sid<=0) continue;
                    $max=strtolower(trim((string)($row['max_game_role']??'spieler')));
                    if(!array_key_exists($max,Auth::GAME_ROLE_LEVELS))$max='spieler';
                    $up->execute([
                        $uid,(int)$sid,!empty($row['view'])?1:0,!empty($row['players'])?1:0,!empty($row['permissions'])?1:0,
                        !empty($row['sync'])?1:0,!empty($row['live'])?1:0,$max,date('c'),
                    ]);
                }
            }

            $pdo->prepare('UPDATE invitations SET accepted_at=? WHERE id=?')->execute([date('c'), $inv['id']]);
            $pdo->commit();
            Audit::log('invitation.accept', 'user', (string)$uid, ['email' => $inv['email'],'role'=>$role]);
            View::flash('success', 'Account wurde erstellt. Sie können sich jetzt anmelden.');
            header('Location: /login');
        } catch (\Throwable $e) {
            if ($pdo->inTransaction()) $pdo->rollBack();
            View::flash('error', 'Account konnte nicht erstellt werden. Bitte eine neue Einladung anfordern.');
            header('Location: /invite/' . $token);
        }
    }

    private function find(string $token): array
    {
        $hash = hash('sha256', $token);
        $stmt = Database::connection()->prepare('SELECT * FROM invitations WHERE token_hash=? AND accepted_at IS NULL AND revoked_at IS NULL');
        $stmt->execute([$hash]);
        $inv = $stmt->fetch();
        if (!$inv || strtotime($inv['expires_at']) < time()) {
            http_response_code(410);
            View::render('error', ['title' => 'Einladung ungültig', 'message' => 'Diese Einladung ist abgelaufen, wurde widerrufen oder bereits verwendet.']);
            exit;
        }
        return $inv;
    }
}
