<?php
$root=dirname(__DIR__);
$db=file_get_contents($root.'/src/Core/Database.php');
$auth=file_get_contents($root.'/src/Core/Auth.php');
$svc=file_get_contents($root.'/src/Services/PanelUserDirectoryService.php');
$js=file_get_contents($root.'/public/assets/app.js');
$css=file_get_contents($root.'/public/assets/app.css');
function ok188(bool $ok,string $msg):void{if(!$ok){fwrite(STDERR,"FEHLER: $msg\n");exit(1);}echo "OK  $msg\n";}
ok188(str_contains($svc,'private const MESSAGE_LIMIT = 4000;'),'Direktnachrichten bis 4.000 Zeichen');
ok188(str_contains($js,'maxlength="4000"'),'Composer akzeptiert 4.000 Zeichen');
ok188(str_contains($js,'panelDmDraft:'),'Entwurf wird sitzungsbezogen gespeichert');
ok188(str_contains($js,'isComposing'),'Auto-Refresh pausiert beim Schreiben');
ok188(str_contains($js,'captureDetailUiState'),'UI-Zustand wird vor Refresh erfasst');
ok188(str_contains($js,'detailScroll:detail.scrollTop'),'Drawer-Scrollposition wird erfasst');
ok188(str_contains($js,'composerFocused'),'Fokus/Cursor werden erhalten');
ok188(str_contains($css,'min-height:110px;max-height:260px;resize:vertical'),'Größeres verstellbares Nachrichtenfeld');
ok188(str_contains($svc,'SELECT 1 FROM webpanel_direct_messages'),'Read-Receipt prüft ungelesene Nachrichten vor Write');
ok188(str_contains($db,'PDO::ATTR_TIMEOUT => 15'),'PDO-Timeout auf 15 Sekunden erhöht');
ok188(str_contains($db,'PRAGMA busy_timeout = 15000'),'SQLite Busy-Timeout auf 15 Sekunden erhöht');
ok188(str_contains($db,'withBusyRetry'),'SQLite Busy-Retry vorhanden');
ok188(str_contains($auth,"_presence_touch']) >= 60") || str_contains($auth,'>= 60'),'Presence-Schreibfrequenz reduziert');
ok188(str_contains($auth,'Best effort'),'Presence-Lock kann UI nicht mehr abbrechen');
echo "v1.8.8 Direct-Message-/SQLite-Test erfolgreich.\n";
