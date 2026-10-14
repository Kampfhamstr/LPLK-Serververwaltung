<?php
$root=dirname(__DIR__);
$db=file_get_contents($root.'/src/Core/Database.php');
$chat=file_get_contents($root.'/src/Services/ServerChatService.php');
function ok177(bool $ok,string $msg):void{if(!$ok){fwrite(STDERR,"FEHLER: $msg\n");exit(1);}echo "OK  $msg\n";}
preg_match('/PDO::ATTR_TIMEOUT\s*=>\s*(\d+)/',$db,$pdoMatch);
preg_match('/PRAGMA busy_timeout\s*=\s*(\d+)/',$db,$busyMatch);
$pdoTimeout=(int)($pdoMatch[1]??0); $busyTimeout=(int)($busyMatch[1]??0);
ok177($pdoTimeout>=5,'PDO-Timeout mindestens 5 Sekunden');
ok177($busyTimeout>=5000,'SQLite Busy-Timeout mindestens 5 Sekunden');
ok177(strpos($db,'PRAGMA busy_timeout') < strpos($db,'PRAGMA journal_mode = WAL'),'Busy-Timeout vor WAL-Pragma');
ok177(str_contains($db,'PRAGMA synchronous = NORMAL'),'WAL synchronous NORMAL');
ok177(str_contains($chat,'if (!$pending) return 0;'),'Kein Write-Transaction ohne neue Chatnachrichten');
ok177(str_contains($chat,'foreach ($pending as $row)'),'Nur neue Nachrichten werden geschrieben');
echo "v1.7.7+ Performance-/SQLite-Test erfolgreich.\n";
