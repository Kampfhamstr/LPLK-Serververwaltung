<?php
namespace App\Controllers;

use App\Core\Auth;
use App\Core\Database;
use App\Core\View;
use App\Core\Security;
use App\Core\Crypto;
use App\Core\Audit;
use App\Core\Mailer;
use App\Services\SteamService;
use App\Services\GitHubReleaseService;

final class SettingsController
{
    public function index(): void
    {
        Auth::requirePermission('settings.view');
        $rows=Database::connection()->query("SELECT setting_key,setting_value,encrypted FROM app_settings WHERE setting_key LIKE 'smtp.%' OR setting_key LIKE 'steam.%' OR setting_key LIKE 'github.%' OR setting_key LIKE 'live.admin_action_notify.%'")->fetchAll();
        $s=[];
        foreach($rows as $r){$v=$r['setting_value'];if((int)$r['encrypted']===1)$v=$v?'********':'';$s[$r['setting_key']]=$v;}
        View::render('settings',['title'=>'Einstellungen','settings'=>$s]);
    }

    public function save(): void
    {
        Auth::requirePermission('settings.manage');Security::verifyCsrf();$pdo=Database::connection();
        $up=$pdo->prepare('INSERT INTO app_settings(setting_key,setting_value,encrypted,updated_at) VALUES(?,?,?,?) ON CONFLICT(setting_key) DO UPDATE SET setting_value=excluded.setting_value,encrypted=excluded.encrypted,updated_at=excluded.updated_at');
        $section=(string)($_POST['section']??'smtp');
        if($section==='steam'){
            $key=trim((string)($_POST['steam_api_key']??''));
            if($key!==''&&$key!=='********')$up->execute(['steam.api_key',Crypto::encrypt($key),1,date('c')]);
            Audit::log('settings.steam.update');View::flash('success','Steam-Web-API-Einstellung wurde gespeichert.');header('Location:/settings');return;
        }
        if($section==='github'){
            $token=trim((string)($_POST['github_api_token']??''));
            if($token!==''&&$token!=='********')$up->execute(['github.api_token',Crypto::encrypt($token),1,date('c')]);
            Audit::log('settings.github.update');View::flash('success','GitHub-API-Einstellung wurde gespeichert.');header('Location:/settings#github');return;
        }
        if($section==='admin_action_notify'){
            $scope=strtolower(trim((string)($_POST['notify_scope']??'admins')));
            if(!in_array($scope,['admins','all'],true))$scope='admins';
            $alwaysTarget=isset($_POST['notify_always_target'])&&((string)$_POST['notify_always_target']==='1');
            $up->execute(['live.admin_action_notify.scope',$scope,0,date('c')]);
            $up->execute(['live.admin_action_notify.always_target',$alwaysTarget?'1':'0',0,date('c')]);
            Audit::log('settings.admin_action_notify.update',null,null,[
                'scope'=>$scope,
                'always_target'=>$alwaysTarget,
            ]);
            View::flash('success','Adminaktions-Meldungen wurden gespeichert.');
            header('Location:/settings#admin-action-notify');
            return;
        }
        $plain=['smtp.host','smtp.port','smtp.encryption','smtp.username','smtp.from_email','smtp.from_name'];
        foreach($plain as $k)$up->execute([$k,trim((string)($_POST[str_replace('.','_',$k)]??'')),0,date('c')]);
        $pw=(string)($_POST['smtp_password']??'');if($pw!==''&&$pw!=='********')$up->execute(['smtp.password',Crypto::encrypt($pw),1,date('c')]);
        Audit::log('settings.smtp.update');View::flash('success','SMTP-Einstellungen wurden gespeichert.');header('Location:/settings');
    }

    public function test(): void
    {
        Auth::requirePermission('settings.manage');Security::verifyCsrf();
        try{Mailer::send((string)Auth::user()['email'],'Test der Serververwaltung','<p>Der SMTP-Versand der Serververwaltung funktioniert.</p>','Der SMTP-Versand der Serververwaltung funktioniert.');View::flash('success','Test-E-Mail wurde versendet.');}
        catch(\Throwable $e){View::flash('error','Test-E-Mail fehlgeschlagen: '.$e->getMessage());}
        header('Location:/settings');
    }

    public function testSteam(): void
    {
        Auth::requirePermission('settings.manage');Security::verifyCsrf();
        try{SteamService::testKey();View::flash('success','Steam Web API ist erreichbar und der hinterlegte Schlüssel wurde akzeptiert.');}
        catch(\Throwable $e){View::flash('error','Steam-Web-API-Test fehlgeschlagen: '.$e->getMessage());}
        header('Location:/settings');
    }
    public function testGitHub(): void
    {
        Auth::requirePermission('settings.manage');Security::verifyCsrf();
        try{$r=GitHubReleaseService::test();View::flash('success','GitHub API erreichbar. Restlimit: '.$r['remaining'].' / '.$r['limit'].($r['authenticated']?' · Token aktiv':' · ohne Token'));}
        catch(\Throwable $e){View::flash('error','GitHub-API-Test fehlgeschlagen: '.$e->getMessage());}
        header('Location:/settings#github');
    }
}
