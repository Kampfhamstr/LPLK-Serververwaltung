<?php
$root=dirname(__DIR__);

function ok171(bool $condition,string $message): void {
    if (!$condition) {
        fwrite(STDERR,"FEHLER: {$message}\n");
        exit(1);
    }
    echo "OK  {$message}\n";
}

$service=file_get_contents($root.'/src/Services/WhitelistLiveService.php');
$controller=file_get_contents($root.'/src/Controllers/LiveController.php');
$template=file_get_contents($root.'/templates/server_live.php');
$css=file_get_contents($root.'/public/assets/app.css');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
$bridgeProj=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/SerververwaltungBridge.csproj');

ok171(str_contains($service,"bridgeSupportsWhitelistSync"),'Webpanel erkennt Bridge-Support für synchronen Whitelist-Abgleich');
ok171(str_contains($service,"'action'=>'whitelist_remove_sync'"),'Webpanel sendet gekoppelte Whitelist-Remove-Aktion');
ok171(str_contains($service,'reconcileRemovals'),'Webpanel besitzt zusätzlichen Dateifallback für Entfernen');
ok171(str_contains($service,'$backup=self::backup'),'Entfernen legt vor dem Umschreiben ein Backup an');
ok171(str_contains($service,'sortEntries($entries)'),'Aktuelle Whitelist wird sortiert');
ok171(str_contains($service,"WHEN 'founder' THEN 1 WHEN 'admin' THEN 2 WHEN 'member' THEN 3 WHEN 'vip' THEN 4 ELSE 5"),'Kandidaten sind nach Rollenpriorität sortiert');
ok171(str_contains($service,"'founder'=>1") && str_contains($service,"'admin'=>2") && str_contains($service,"'member'=>3") && str_contains($service,"'vip'=>4"),'Datei-/UI-Sortierung besitzt feste Rollenreihenfolge');
ok171(str_contains($service,'strnatcasecmp'),'Alphabetische A-Z-Sortierung ist implementiert');
ok171(str_contains($template,'whitelist-role-group'),'Whitelist zeigt Rollen-Gruppenüberschriften');
ok171(str_contains($css,'.whitelist-role-group'),'Rollen-Gruppen sind gestylt');
ok171(str_contains($controller,'Bridge-Sync aktiv'),'Webpanel meldet aktiven sicheren Bridge-Abgleich');

ok171(str_contains($bridge,'case "whitelist_remove_sync"'),'Bridge kennt whitelist_remove_sync');
ok171(str_contains($bridge,'ExecuteWhitelistRemoveSync'),'Bridge führt !uwl und Dateiabgleich gekoppelt aus');
ok171(str_contains($bridge,'ReconcileWhitelistFile'),'Bridge schreibt whitelist.txt nach !uwl neu');
ok171(str_contains($bridge,'ResolveWhitelistListPath'),'Bridge validiert den Whitelist-Dateipfad');
ok171(str_contains($bridge,'File.Move(tmp, path, true)'),'Bridge ersetzt whitelist.txt atomar');
ok171(str_contains($bridgeProj,'<Version>1.5.5</Version>'),'Bridge-Projektversion ist aktueller Stand 1.5.5');
ok171(str_contains($bridge,'Version = "1.5.5"'),'Bridge-Metadatenversion ist aktueller Stand 1.5.5');

echo "\nv1.7.1 Whitelist-Sync-/Sortier-Test erfolgreich.\n";
