<?php
namespace App\Controllers;
use App\Core\Auth; use App\Core\Security; use App\Core\View; use App\Core\Database; use App\Core\Crypto; use App\Core\TOTP; use App\Core\WebAuthn; use App\Core\Audit; use App\Core\Mailer; use App\Core\Config;
final class AuthController {
 public function loginForm():void
 {
  if(Auth::check()){header('Location: /dashboard');exit;}
  Auth::cancelPendingTwoFactor();
  View::render('login',['title'=>'Anmelden']);
 }

 public function login():void
 {
  Security::verifyCsrf();

  $result=Auth::beginLogin(
   (string)($_POST['email']??''),
   (string)($_POST['password']??'')
  );

  if($result==='2fa_required'){
   header('Location: /login/2fa');
   exit;
  }

  if($result!=='authenticated'){
   View::flash('error','Anmeldung fehlgeschlagen. Zugangsdaten oder Rate-Limit prüfen.');
   header('Location: /login');
   exit;
  }

  header('Location: /dashboard');
 }

 public function loginTwoFactorForm():void
 {
  if(Auth::check()){header('Location: /dashboard');exit;}

  if(!Auth::pendingTwoFactor()){
   View::flash('error','Die Zwei-Faktor-Anmeldung ist abgelaufen. Bitte erneut anmelden.');
   header('Location: /login');
   exit;
  }

  $pendingUser=Auth::pendingTwoFactorUser();
  View::render('login_2fa',['title'=>'Zwei-Faktor-Authentifizierung','passkeyAvailable'=>$pendingUser?WebAuthn::credentialCount((int)$pendingUser['id'])>0:false]);
 }

 public function loginTwoFactor():void
 {
  Security::verifyCsrf();

  if(!Auth::pendingTwoFactor()){
   View::flash('error','Die Zwei-Faktor-Anmeldung ist abgelaufen. Bitte erneut anmelden.');
   header('Location: /login');
   exit;
  }

  if(!Auth::completeTwoFactor((string)($_POST['totp']??''))){
   if(Auth::pendingTwoFactor()){
    View::flash('error','Der Authenticator- oder Recovery-Code ist nicht gültig.');
    header('Location: /login/2fa');
   }else{
    View::flash('error','Zu viele ungültige Codes oder die Anmeldung ist abgelaufen. Bitte erneut anmelden.');
    header('Location: /login');
   }
   exit;
  }

  if(isset($_SESSION['_2fa_recovery_notice'])){
   $remaining=(int)$_SESSION['_2fa_recovery_notice'];
   unset($_SESSION['_2fa_recovery_notice']);
   View::flash($remaining<=3?'warning':'info','Anmeldung mit Recovery-Code erfolgreich. Verbleibende Recovery-Codes: '.$remaining.'.');
  }
  header('Location: /dashboard');
 }


 public function cancelTwoFactor():void
 {
  Security::verifyCsrf();
  Auth::cancelPendingTwoFactor();
  header('Location: /login');
 }

 public function logout():void { Security::verifyCsrf(); Auth::logout(); header('Location: /login'); }

 public function profile():void
 {
  Auth::requireLogin();
  $user=Auth::user();
  $totpProvisioningUri=null;
  $totpSetupKey=null;

  if(!(int)$user['totp_enabled'] && !empty($_SESSION['pending_totp'])){
   $secret=(string)$_SESSION['pending_totp'];
   $issuer=trim((string)Config::get('app_name','Serververwaltung')) ?: 'Serververwaltung';

   $totpProvisioningUri=TOTP::provisioningUri(
    $secret,
    (string)$user['email'],
    $issuer
   );
   $totpSetupKey=TOTP::formatSecret($secret);
  }

  if(!empty($_SESSION['_new_recovery_codes_created_at']) && (int)$_SESSION['_new_recovery_codes_created_at'] < time()-900){
   unset($_SESSION['_new_recovery_codes'],$_SESSION['_new_recovery_codes_created_at']);
  }
  $newRecoveryCodes=is_array($_SESSION['_new_recovery_codes']??null)?$_SESSION['_new_recovery_codes']:[];
  $recoveryCount=TOTP::recoveryCount(isset($user['totp_recovery_codes_json'])?(string)$user['totp_recovery_codes_json']:null);
  $passkeys=WebAuthn::credentials((int)$user['id']);
  $passkeySupported=WebAuthn::supported();

  View::render('profile',[
   'title'=>'Profil',
   'user'=>$user,
   'totpProvisioningUri'=>$totpProvisioningUri,
   'totpSetupKey'=>$totpSetupKey,
   'newRecoveryCodes'=>$newRecoveryCodes,
   'recoveryCount'=>$recoveryCount,
   'passkeys'=>$passkeys,
   'passkeySupported'=>$passkeySupported,
  ]);
 }

 public function enableTotp():void
 {
  Auth::requireLogin();
  Security::verifyCsrf();
  $u=Auth::user();

  if(!empty($_POST['confirm_code']) && !empty($_SESSION['pending_totp'])){
   $digits=preg_replace('/\D+/','',(string)$_POST['confirm_code'])??'';
   if(!TOTP::verify((string)$_SESSION['pending_totp'],$digits)){
    View::flash('error','Der 2FA-Code ist nicht gültig. Bitte den aktuell angezeigten 6-stelligen Code prüfen.');
    header('Location:/profile');exit;
   }

   $codes=TOTP::generateRecoveryCodes(10);
   $newVersion=max(1,(int)($u['auth_version']??1))+1;
   Database::connection()->prepare(
    'UPDATE users SET totp_secret_enc=?,totp_recovery_codes_json=?,totp_enabled=1,auth_version=?,updated_at=? WHERE id=?'
   )->execute([
    Crypto::encrypt((string)$_SESSION['pending_totp']),
    TOTP::encodeRecoveryHashes($codes),
    $newVersion,
    date('c'),
    $u['id']
   ]);

   $_SESSION['auth_version']=$newVersion;
   $_SESSION['_new_recovery_codes']=$codes;
   $_SESSION['_new_recovery_codes_created_at']=time();
   unset($_SESSION['pending_totp']);

   Audit::log('auth.2fa.enabled','user',(string)$u['id'],[
    'recovery_codes'=>count($codes),
    'founder_unlocked'=>Auth::isFounderAccount($u),
   ]);
   View::flash('success',Auth::isFounderAccount($u)
    ?'2FA wurde aktiviert. Founder-Vollzugriff ist jetzt freigeschaltet. Bitte die Recovery-Codes sicher speichern.'
    :'Zwei-Faktor-Authentifizierung wurde aktiviert. Bitte die Recovery-Codes sicher speichern.');
   header('Location:/profile');exit;
  }

  if((int)($u['totp_enabled']??0)===1){
   View::flash('info','2FA ist bereits aktiv.');
   header('Location:/profile');exit;
  }

  $_SESSION['pending_totp']=TOTP::generateSecret();
  header('Location:/profile');
 }

 public function disableTotp():void
 {
  Auth::requireLogin();
  Security::verifyCsrf();
  $u=Auth::user();
  $password=(string)($_POST['current_password']??'');
  $factor=(string)($_POST['second_factor']??'');

  if(!password_verify($password,(string)$u['password_hash'])){
   View::flash('error','2FA wurde nicht deaktiviert: aktuelles Passwort ist falsch.');
   header('Location:/profile');exit;
  }
  $verified=$this->verifyCurrentSecondFactor($u,$factor);
  if(!$verified['valid']){
   View::flash('error','2FA wurde nicht deaktiviert: Authenticator- oder Recovery-Code ist ungültig.');
   header('Location:/profile');exit;
  }

  $newVersion=max(1,(int)($u['auth_version']??1))+1;
  Database::connection()->prepare(
   'UPDATE users SET totp_secret_enc=NULL,totp_recovery_codes_json=NULL,totp_enabled=0,auth_version=?,updated_at=? WHERE id=?'
  )->execute([$newVersion,date('c'),$u['id']]);
  $_SESSION['auth_version']=$newVersion;
  unset($_SESSION['_new_recovery_codes'],$_SESSION['_new_recovery_codes_created_at']);

  $hasPasskey=WebAuthn::credentialCount((int)$u['id'])>0;
  Audit::log('auth.2fa.disabled','user',(string)$u['id'],[
   'founder_admin_cap_enabled'=>Auth::isFounderAccount($u) && !$hasPasskey,
   'passkey_still_active'=>$hasPasskey,
  ]);
  $founderLoses=Auth::isFounderAccount($u) && !$hasPasskey;
  View::flash($founderLoses?'warning':'success',$founderLoses
   ?'Authenticator wurde deaktiviert. Das Founder-Konto arbeitet nur noch mit Admin-Rechten, bis wieder TOTP oder ein Passkey eingerichtet ist.'
   :($hasPasskey?'Authenticator wurde deaktiviert. Dein Passkey bleibt als zweiter Faktor aktiv.':'Zwei-Faktor-Authentifizierung wurde deaktiviert.'));
  header('Location:/profile');
 }

 public function regenerateRecoveryCodes():void
 {
  Auth::requireLogin();
  Security::verifyCsrf();
  $u=Auth::user();
  if((int)($u['totp_enabled']??0)!==1){
   View::flash('error','Recovery-Codes können nur bei aktiver 2FA erzeugt werden.');
   header('Location:/profile');exit;
  }
  if(!password_verify((string)($_POST['current_password']??''),(string)$u['password_hash'])){
   View::flash('error','Recovery-Codes wurden nicht erneuert: aktuelles Passwort ist falsch.');
   header('Location:/profile');exit;
  }
  $verified=$this->verifyCurrentSecondFactor($u,(string)($_POST['second_factor']??''));
  if(!$verified['valid']){
   View::flash('error','Recovery-Codes wurden nicht erneuert: 2FA-Code ist ungültig.');
   header('Location:/profile');exit;
  }

  $codes=TOTP::generateRecoveryCodes(10);
  Database::connection()->prepare(
   'UPDATE users SET totp_recovery_codes_json=?,updated_at=? WHERE id=?'
  )->execute([TOTP::encodeRecoveryHashes($codes),date('c'),$u['id']]);
  $_SESSION['_new_recovery_codes']=$codes;
  $_SESSION['_new_recovery_codes_created_at']=time();
  Audit::log('auth.2fa.recovery_regenerated','user',(string)$u['id'],['recovery_codes'=>count($codes)]);
  View::flash('success','Neue Recovery-Codes wurden erzeugt. Alle bisherigen Recovery-Codes sind jetzt ungültig.');
  header('Location:/profile');
 }

 public function downloadRecoveryCodes():void
 {
  Auth::requireLogin();
  $codes=is_array($_SESSION['_new_recovery_codes']??null)?$_SESSION['_new_recovery_codes']:[];
  if(!$codes){
   http_response_code(404);
   View::render('error',['title'=>'Recovery-Codes nicht verfügbar','message'=>'Recovery-Codes werden aus Sicherheitsgründen nur direkt nach ihrer Erzeugung angezeigt.']);
   return;
  }
  header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
  header('Pragma: no-cache');
  header('Content-Type: text/plain; charset=utf-8');
  header('Content-Disposition: attachment; filename="serververwaltung-2fa-recovery-'.date('Ymd-His').'.txt"');
  echo "Serververwaltung · 2FA Recovery-Codes\n";
  echo "Erzeugt: ".date('d.m.Y H:i:s')."\n\n";
  echo implode("\n",$codes)."\n\n";
  echo "Jeder Code kann nur einmal verwendet werden. Sicher und getrennt vom Passwort aufbewahren.\n";
  exit;
 }

 private function verifyCurrentSecondFactor(array $user,string $input):array
 {
  $raw=trim($input);
  $digits=preg_replace('/\D+/','',$raw)??'';
  try{
   if(preg_match('/^\d{6}$/',$digits) && strlen(preg_replace('/\s+/','',$raw)??'')<=7){
    $secret=Crypto::decrypt((string)($user['totp_secret_enc']??''));
    return ['valid'=>TOTP::verify($secret,$digits),'used_recovery'=>false];
   }
   $recovery=TOTP::consumeRecoveryCode(isset($user['totp_recovery_codes_json'])?(string)$user['totp_recovery_codes_json']:null,$raw);
   if(!empty($recovery['valid'])){
    Database::connection()->prepare('UPDATE users SET totp_recovery_codes_json=?,updated_at=? WHERE id=?')
      ->execute([(string)$recovery['json'],date('c'),$user['id']]);
    Audit::log('auth.2fa.recovery_used','user',(string)$user['id'],['remaining'=>(int)$recovery['remaining'],'context'=>'profile_security']);
    return ['valid'=>true,'used_recovery'=>true,'remaining'=>(int)$recovery['remaining']];
   }
  }catch(\Throwable){ }
  return ['valid'=>false,'used_recovery'=>false];
 }

 public function changeTheme():void
 {
  Auth::requireLogin();
  Security::verifyCsrf();
  $u=Auth::user();
  $theme=strtolower(trim((string)($_POST['theme']??'system')));
  $allowed=['light','dark','system'];

  header('Content-Type: application/json; charset=utf-8');
  header('Cache-Control: no-store');

  if(!in_array($theme,$allowed,true)){
   http_response_code(422);
   echo json_encode(['ok'=>false,'error'=>'Ungültige Designauswahl.'],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
   return;
  }

  Database::connection()->prepare('UPDATE users SET ui_theme=?,updated_at=? WHERE id=?')
   ->execute([$theme,date('c'),(int)$u['id']]);
  Audit::log('auth.ui_theme.changed','user',(string)$u['id'],['theme'=>$theme]);
  echo json_encode(['ok'=>true,'theme'=>$theme],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
 }

 public function changePassword():void
 {
  Auth::requireLogin(); Security::verifyCsrf();
  $u=Auth::user();
  $current=(string)($_POST['current_password']??'');
  $new=(string)($_POST['new_password']??'');
  $confirm=(string)($_POST['new_password_confirm']??'');

  if(!password_verify($current,$u['password_hash'])||strlen($new)<12||$new!==$confirm){
   View::flash('error','Passwort konnte nicht geändert werden. Aktuelles Passwort prüfen und mindestens 12 identische neue Zeichen verwenden.');
   header('Location:/profile');exit;
  }

  $pdo=Database::connection();
  $newVersion=max(1,(int)($u['auth_version']??1))+1;
  $pdo->prepare('UPDATE users SET password_hash=?,auth_version=?,updated_at=? WHERE id=?')
      ->execute([password_hash($new,PASSWORD_ARGON2ID),$newVersion,date('c'),$u['id']]);

  // Diese aktuelle, bewusst authentifizierte Sitzung bleibt gültig.
  // Alle anderen Sessions mit der alten Version werden ungültig.
  $_SESSION['auth_version']=$newVersion;

  Audit::log('auth.password.changed','user',(string)$u['id'],['all_other_sessions_invalidated'=>true]);
  View::flash('success','Passwort wurde geändert. Andere bestehende Sitzungen wurden abgemeldet.');
  header('Location:/profile');
 }


 public function passkeyRegisterOptions():void
 {
  Auth::requireLogin(); Security::verifyCsrf(); header('Content-Type: application/json; charset=utf-8'); header('Cache-Control: no-store');
  try{
   $u=Auth::user();
   if(!password_verify((string)($_POST['current_password']??''),(string)$u['password_hash'])) throw new \RuntimeException('Aktuelles Passwort ist falsch.');
   $options=WebAuthn::registrationOptions($u,(string)($_POST['label']??'Passkey'));
   echo json_encode(['ok'=>true,'public_key'=>$options],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
  }catch(\Throwable $e){http_response_code(400);echo json_encode(['ok'=>false,'error'=>$e->getMessage()],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);}
 }

 public function passkeyRegisterFinish():void
 {
  Auth::requireLogin(); Security::verifyCsrf(); header('Content-Type: application/json; charset=utf-8'); header('Cache-Control: no-store');
  try{
   $body=json_decode((string)file_get_contents('php://input'),true,512,JSON_THROW_ON_ERROR);
   $credential=is_array($body['credential']??null)?$body['credential']:[];
   $result=WebAuthn::finishRegistration(Auth::user(),$credential);
   Audit::log('auth.passkey.registered','user',(string)Auth::user()['id'],['credential_id'=>$result['id'],'label'=>$result['label'],'founder_unlocked'=>Auth::isFounderAccount(Auth::user())]);
   echo json_encode(['ok'=>true,'credential'=>$result,'founder_unlocked'=>!Auth::founderTwoFactorLocked()],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
  }catch(\Throwable $e){http_response_code(400);echo json_encode(['ok'=>false,'error'=>$e->getMessage()],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);}
 }

 public function passkeyDelete(string $id):void
 {
  Auth::requireLogin(); Security::verifyCsrf(); $u=Auth::user();
  if(!password_verify((string)($_POST['current_password']??''),(string)$u['password_hash'])){View::flash('error','Passkey wurde nicht entfernt: aktuelles Passwort ist falsch.');header('Location:/profile#passkeys');exit;}
  if(!WebAuthn::deleteCredential((int)$u['id'],(int)$id)){View::flash('error','Passkey wurde nicht gefunden.');header('Location:/profile#passkeys');exit;}
  $newVersion=max(1,(int)($u['auth_version']??1))+1;
  Database::connection()->prepare('UPDATE users SET auth_version=?,updated_at=? WHERE id=?')->execute([$newVersion,date('c'),$u['id']]); $_SESSION['auth_version']=$newVersion;
  Audit::log('auth.passkey.deleted','user',(string)$u['id'],['credential_id'=>(int)$id,'remaining'=>WebAuthn::credentialCount((int)$u['id'])]);
  View::flash(Auth::founderTwoFactorLocked()?'warning':'success',Auth::founderTwoFactorLocked()?'Letzter Passkey entfernt. Founder-Vollzugriff bleibt gesperrt, bis wieder Passkey oder TOTP aktiv ist.':'Passkey wurde entfernt.');
  header('Location:/profile#passkeys');
 }

 public function loginPasskeyOptions():void
 {
  header('Content-Type: application/json; charset=utf-8'); header('Cache-Control: no-store'); Security::verifyCsrf();
  try{$u=Auth::pendingTwoFactorUser();if(!$u)throw new \RuntimeException('2FA-Anmeldung ist abgelaufen.');echo json_encode(['ok'=>true,'public_key'=>WebAuthn::authenticationOptions((int)$u['id'])],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);}catch(\Throwable $e){http_response_code(400);echo json_encode(['ok'=>false,'error'=>$e->getMessage()],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);}
 }

 public function loginPasskeyFinish():void
 {
  header('Content-Type: application/json; charset=utf-8'); header('Cache-Control: no-store'); Security::verifyCsrf();
  try{$u=Auth::pendingTwoFactorUser();if(!$u)throw new \RuntimeException('2FA-Anmeldung ist abgelaufen.');$body=json_decode((string)file_get_contents('php://input'),true,512,JSON_THROW_ON_ERROR);WebAuthn::finishAuthentication((int)$u['id'],is_array($body['credential']??null)?$body['credential']:[]);if(!Auth::completePasskeyTwoFactor((int)$u['id']))throw new \RuntimeException('Passkey konnte die Anmeldung nicht abschließen.');Audit::log('auth.2fa.passkey_success','user',(string)$u['id']);echo json_encode(['ok'=>true,'redirect'=>'/dashboard'],JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);}catch(\Throwable $e){Auth::failPendingTwoFactor('passkey');http_response_code(400);echo json_encode(['ok'=>false,'error'=>$e->getMessage(),'pending'=>Auth::pendingTwoFactor()],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);}
 }

 public function forgotPasswordForm():void
 {
  if(Auth::check()){header('Location: /profile');exit;}
  View::render('forgot_password',['title'=>'Passwort vergessen']);
 }

 public function forgotPassword():void
 {
  Security::verifyCsrf();

  $email=strtolower(trim((string)($_POST['email']??'')));
  $ip=Security::clientIp();
  $pdo=Database::connection();
  $now=date('c');
  $emailHash=hash('sha256',$email);

  // Alte Request-Metadaten regelmäßig entfernen.
  $pdo->prepare('DELETE FROM password_reset_requests WHERE created_at < ?')
      ->execute([date('c',time()-7*86400)]);

  $hourAgo=date('c',time()-3600);

  $ipStmt=$pdo->prepare('SELECT COUNT(*) FROM password_reset_requests WHERE ip_address=? AND created_at>=?');
  $ipStmt->execute([$ip,$hourAgo]);
  $ipLimited=(int)$ipStmt->fetchColumn()>=5;

  $mailStmt=$pdo->prepare('SELECT COUNT(*) FROM password_reset_requests WHERE email_hash=? AND created_at>=?');
  $mailStmt->execute([$emailHash,$hourAgo]);
  $mailLimited=(int)$mailStmt->fetchColumn()>=3;

  $user=null;
  if(filter_var($email,FILTER_VALIDATE_EMAIL)){
   $stmt=$pdo->prepare('SELECT id,email,name,active FROM users WHERE lower(email)=lower(?) LIMIT 1');
   $stmt->execute([$email]);
   $candidate=$stmt->fetch();
   if($candidate && (int)$candidate['active']===1) $user=$candidate;
  }

  $token=null;
  $tokenHash=null;
  $expires=date('c',time()+3600);

  if(!$ipLimited && !$mailLimited && $user){
   // Frühere noch offene Links für diesen Nutzer sofort ungültig machen.
   $pdo->prepare('UPDATE password_reset_requests SET used_at=? WHERE user_id=? AND used_at IS NULL')
       ->execute([$now,(int)$user['id']]);

   $token=bin2hex(random_bytes(32));
   $tokenHash=hash('sha256',$token);
  }

  $pdo->prepare('INSERT INTO password_reset_requests(user_id,email_hash,token_hash,ip_address,expires_at,created_at) VALUES(?,?,?,?,?,?)')
      ->execute([$user?(int)$user['id']:null,$emailHash,$tokenHash,$ip,$expires,$now]);
  $requestId=(int)$pdo->lastInsertId();

  if($token!==null && $user){
   $url=rtrim((string)Config::get('app_url',''),'/').'/reset-password/'.$token;
   $appName=(string)Config::get('app_name','Serververwaltung');

   $safeUrl=htmlspecialchars($url,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
   $safeApp=htmlspecialchars($appName,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');

   $html='<!doctype html><html><body style="margin:0;background:#f3f5f7;font-family:Arial,Helvetica,sans-serif;color:#20242a">'.
    '<div style="max-width:640px;margin:32px auto;background:#fff;border:1px solid #dfe3e8;border-radius:8px;overflow:hidden">'.
    '<div style="padding:24px 28px;background:#171a1f;color:#fff"><div style="font-size:20px;font-weight:700">'.$safeApp.'</div>'.
    '<div style="margin-top:5px;color:#b8c0ca;font-size:13px">Passwort zurücksetzen</div></div>'.
    '<div style="padding:28px"><p style="margin-top:0">Für Ihr Webpanel-Konto wurde das Zurücksetzen des Passworts angefordert.</p>'.
    '<p style="margin:24px 0"><a href="'.$safeUrl.'" style="display:inline-block;background:#20252c;color:#fff;text-decoration:none;padding:12px 18px;border-radius:5px;font-weight:700">Neues Passwort festlegen</a></p>'.
    '<p style="font-size:13px;color:#626b76">Der Link ist 60 Minuten gültig und kann nur einmal verwendet werden. Falls Sie diese Anfrage nicht gestellt haben, ignorieren Sie diese E-Mail. Ihr bisheriges Passwort bleibt dann unverändert.</p>'.
    '</div><div style="padding:16px 28px;border-top:1px solid #e5e8ec;color:#7a838e;font-size:12px">'.$safeApp.'</div></div></body></html>';

   $text=$appName."\n\nFür Ihr Webpanel-Konto wurde ein Passwort-Reset angefordert.\n\n".
     "Neues Passwort festlegen:\n".$url."\n\n".
     "Der Link ist 60 Minuten gültig und einmal verwendbar. Falls Sie die Anfrage nicht gestellt haben, ignorieren Sie diese E-Mail.";

   try{
    Mailer::send((string)$user['email'],'Passwort für '.$appName.' zurücksetzen',$html,$text);
    Audit::log('auth.password.reset_requested','user',(string)$user['id'],['request_id'=>$requestId]);
   }catch(\Throwable $e){
    // Ein nicht versendbarer Link soll später nicht versehentlich nutzbar bleiben.
    $pdo->prepare('UPDATE password_reset_requests SET used_at=? WHERE id=?')->execute([date('c'),$requestId]);
    Audit::log('auth.password.reset_mail_failed','user',(string)$user['id'],['request_id'=>$requestId]);

    $logDir=dirname(__DIR__,2).'/storage/logs';
    if(!is_dir($logDir)) @mkdir($logDir,0770,true);
    @file_put_contents($logDir.'/app.log','['.date('c').'] Passwort-Reset-Mail fehlgeschlagen: '.$e->getMessage().PHP_EOL,FILE_APPEND|LOCK_EX);
   }
  }

  // Absichtlich immer dieselbe Meldung, unabhängig davon ob die Adresse existiert,
  // SMTP funktioniert oder ein Rate-Limit gegriffen hat.
  View::flash('success','Falls für diese E-Mail-Adresse ein aktives Webpanel-Konto existiert, wurde ein Passwort-Reset-Link versendet.');
  header('Location: /login');
 }

 public function resetPasswordForm(string $token):void
 {
  if(Auth::check()){header('Location: /profile');exit;}
  $this->findResetRequest($token);
  View::render('reset_password',['title'=>'Neues Passwort festlegen','token'=>$token]);
 }

 public function resetPassword(string $token):void
 {
  Security::verifyCsrf();
  $request=$this->findResetRequest($token);

  $password=(string)($_POST['password']??'');
  $confirm=(string)($_POST['password_confirm']??'');

  if(strlen($password)<12 || $password!==$confirm){
   View::flash('error','Bitte ein identisches neues Passwort mit mindestens 12 Zeichen verwenden.');
   header('Location: /reset-password/'.rawurlencode($token));
   exit;
  }

  $pdo=Database::connection();
  $pdo->beginTransaction();

  try{
   $userStmt=$pdo->prepare('SELECT id,email,active,auth_version FROM users WHERE id=?');
   $userStmt->execute([(int)$request['user_id']]);
   $user=$userStmt->fetch();

   if(!$user || (int)$user['active']!==1){
    throw new \RuntimeException('Konto nicht verfügbar.');
   }

   $newVersion=max(1,(int)($user['auth_version']??1))+1;
   $now=date('c');

   $pdo->prepare('UPDATE users SET password_hash=?,auth_version=?,updated_at=? WHERE id=?')
       ->execute([password_hash($password,PASSWORD_ARGON2ID),$newVersion,$now,(int)$user['id']]);

   // Sämtliche offenen Reset-Tokens dieses Kontos verbrauchen.
   $pdo->prepare('UPDATE password_reset_requests SET used_at=? WHERE user_id=? AND used_at IS NULL')
       ->execute([$now,(int)$user['id']]);

   // Ein Reset soll nicht an vorherigen fehlgeschlagenen Logins hängen bleiben.
   $pdo->prepare('DELETE FROM login_attempts WHERE lower(email)=lower(?)')
       ->execute([(string)$user['email']]);

   $pdo->commit();

   Audit::log('auth.password.reset_completed','user',(string)$user['id'],[
    'all_sessions_invalidated'=>true,
    'request_id'=>(int)$request['id'],
   ]);

   $this->sendPasswordChangedNotice((string)$user['email']);

   View::flash('success','Passwort wurde geändert. Alle bisherigen Sitzungen wurden beendet. Sie können sich jetzt mit dem neuen Passwort anmelden.');
   header('Location: /login');
  }catch(\Throwable $e){
   if($pdo->inTransaction()) $pdo->rollBack();
   View::flash('error','Der Passwort-Reset konnte nicht abgeschlossen werden. Bitte einen neuen Reset-Link anfordern.');
   header('Location: /forgot-password');
  }
 }

 private function findResetRequest(string $token):array
 {
  if(!preg_match('/^[a-f0-9]{64}$/',$token)){
   $this->invalidResetLink();
  }

  $hash=hash('sha256',$token);
  $stmt=Database::connection()->prepare(
   'SELECT r.* FROM password_reset_requests r JOIN users u ON u.id=r.user_id '.
   'WHERE r.token_hash=? AND r.used_at IS NULL AND u.active=1 LIMIT 1'
  );
  $stmt->execute([$hash]);
  $request=$stmt->fetch();

  if(!$request || strtotime((string)$request['expires_at'])<time()){
   $this->invalidResetLink();
  }

  return $request;
 }

 private function invalidResetLink():never
 {
  http_response_code(410);
  View::render('error',[
   'title'=>'Reset-Link ungültig',
   'message'=>'Dieser Passwort-Reset-Link ist abgelaufen, wurde bereits verwendet oder ist ungültig. Bitte fordern Sie einen neuen Link an.',
  ]);
  exit;
 }

 private function sendPasswordChangedNotice(string $email):void
 {
  try{
   $appName=(string)Config::get('app_name','Serververwaltung');
   $safeApp=htmlspecialchars($appName,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
   $html='<!doctype html><html><body style="margin:0;background:#f3f5f7;font-family:Arial,Helvetica,sans-serif;color:#20242a">'.
    '<div style="max-width:640px;margin:32px auto;background:#fff;border:1px solid #dfe3e8;border-radius:8px;overflow:hidden">'.
    '<div style="padding:24px 28px;background:#171a1f;color:#fff;font-size:20px;font-weight:700">'.$safeApp.'</div>'.
    '<div style="padding:28px"><p style="margin-top:0">Das Passwort Ihres Webpanel-Kontos wurde erfolgreich geändert.</p>'.
    '<p>Alle vorherigen Sitzungen wurden ungültig gemacht. Die Zwei-Faktor-Authentifizierung bleibt, falls aktiviert, unverändert aktiv.</p>'.
    '<p style="font-size:13px;color:#626b76">Falls Sie diese Änderung nicht durchgeführt haben, wenden Sie sich bitte unmittelbar an einen Founder.</p></div></div></body></html>';
   $text=$appName."\n\nDas Passwort Ihres Webpanel-Kontos wurde geändert.\nAlle vorherigen Sitzungen wurden beendet. Eine aktivierte Zwei-Faktor-Authentifizierung bleibt aktiv.\n\nFalls Sie diese Änderung nicht durchgeführt haben, wenden Sie sich bitte unmittelbar an einen Founder.";
   Mailer::send($email,'Passwort in '.$appName.' wurde geändert',$html,$text);
  }catch(\Throwable $e){
   $logDir=dirname(__DIR__,2).'/storage/logs';
   if(!is_dir($logDir)) @mkdir($logDir,0770,true);
   @file_put_contents($logDir.'/app.log','['.date('c').'] Passwortänderungs-Hinweis-Mail fehlgeschlagen: '.$e->getMessage().PHP_EOL,FILE_APPEND|LOCK_EX);
  }
 }
}
