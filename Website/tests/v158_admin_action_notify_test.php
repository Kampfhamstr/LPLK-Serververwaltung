<?php
$root=dirname(__DIR__);
$live=file_get_contents($root.'/src/Services/LiveServerService.php');
$whitelist=file_get_contents($root.'/src/Services/WhitelistLiveService.php');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');

function ok158(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok158(str_contains($live,"'requested_by' => \$requesterName"),'Zentrale Live-Befehle enthalten requested_by');
ok158(str_contains($live,"'requested_by_role' => \$requesterRole"),'Zentrale Live-Befehle enthalten requested_by_role');
ok158(str_contains($whitelist,"'requested_by'=>\$requesterName"),'Whitelist-Befehle enthalten requested_by');
ok158(str_contains($bridge,'public string? RequestedBy { get; set; }'),'BridgeCommand kennt RequestedBy');
ok158(str_contains($bridge,'public string? RequestedByRole { get; set; }'),'BridgeCommand kennt RequestedByRole');
ok158(str_contains($bridge,'admin_actions.log'),'Admin-Aktionslog vorhanden');
ok158(str_contains($bridge,'"dd.MM.yy HH:mm:ss"'),'Zeitformat TT.MM.JJ HH:MM:ss vorhanden');
ok158(str_contains($bridge,'RefreshAdminNotifyRecipients'),'Admin/Founder-Empfänger werden geladen');
ok158(str_contains($bridge,'permissions.jsonc'),'Empfängerquelle permissions.jsonc vorhanden');
ok158(str_contains($bridge,'ResolveHighestGameRole') && str_contains($bridge,'if (set.Contains("admin")) return "admin";'),'Admin-Gruppe erkannt');
ok158(str_contains($bridge,'ResolveHighestGameRole') && str_contains($bridge,'if (set.Contains("founder")) return "founder";'),'Founder-Gruppe erkannt');
ok158(str_contains($bridge,'player.SendChat(chatMessage)'),'Ingame-Nachricht an Empfänger vorhanden');
ok158(str_contains($bridge,'RecordAdminAction(command, success: true'),'Erfolgreiche Aktionen werden geloggt/gemeldet');
ok158(str_contains($bridge,'RecordAdminAction(command, success: false'),'Fehler werden ebenfalls geloggt');
ok158(str_contains($bridge,'notifyInGame: false'),'Fehler erzeugen keine Erfolgsmeldung im Spiel');

echo "\nv1.5.8 Admin-Action-Notify-Test erfolgreich.\n";
