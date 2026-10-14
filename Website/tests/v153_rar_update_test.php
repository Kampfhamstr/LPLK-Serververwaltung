<?php
$root=dirname(__DIR__);
$github=file_get_contents($root.'/src/Services/GitHubReleaseService.php');
$updates=file_get_contents($root.'/src/Services/PluginUpdateService.php');
$template=file_get_contents($root.'/templates/plugins.php');
$bridge=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/src/Plugin.cs');
$csproj=file_get_contents($root.'/bridge/SwiftlyS2/SerververwaltungBridge/SerververwaltungBridge.csproj');

function ok153(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok153(str_contains($github,'str_ends_with($lower,\'.rar\')'),'GitHub-Service akzeptiert RAR');
ok153(str_contains($github,'ZIP-/RAR-Release-Asset'),'Fehlermeldung nennt ZIP/RAR');
ok153(str_contains($updates,'str_contains($lowerPattern,\'.rar\')'),'RAR-Muster ist erlaubt');
ok153(str_contains($template,'Whitelist-v*.rar'),'RAR-Beispiel ist im Formular sichtbar');
ok153(str_contains($bridge,'extension is not ".zip" and not ".rar"'),'Bridge akzeptiert ZIP/RAR');
ok153(str_contains($bridge,'ReaderFactory.OpenReader'),'Bridge nutzt formatunabhängigen Reader');
ok153(str_contains($bridge,'entry.LinkTarget'),'Links/Symlinks werden geprüft');
ok153(str_contains($bridge,'Unsicherer Pfad im Release-Archiv'),'Path-Traversal-Schutz vorhanden');
ok153(str_contains($csproj,'SharpCompress" Version="0.50.4"'),'SharpCompress 0.50.4 eingebunden');

echo "\nv1.5.3 RAR-Update-Test erfolgreich.\n";
