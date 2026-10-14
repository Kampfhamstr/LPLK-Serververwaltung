<?php
$root=dirname(__DIR__);
$service=file_get_contents($root.'/src/Services/PluginUpdateService.php');
$template=file_get_contents($root.'/templates/plugins.php');

function ok165(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok165(str_contains($service,'githubRepositoryUrl'),'Repository-URL-Helfer vorhanden');
ok165(str_contains($service,'GitHubReleaseService::normalizeRepository'),'GitHub-Normalisierung wird wiederverwendet');
ok165(str_contains($service,"'https://github.com/'"),'Repository-Wurzel wird erzeugt');
ok165(str_contains($service,'github_repository_url'),'Inventory enthält Repository-URL');
ok165(str_contains($template,'GitHub Repository'),'Repository-Link wird angezeigt');
ok165(str_contains($template,'Letztes Release'),'Release-Link wird angezeigt');
ok165(str_contains($template,'target="_blank"'),'Links öffnen neuen Tab');
ok165(str_contains($template,'rel="noopener noreferrer"'),'Externe Links sind abgesichert');

echo "\nv1.6.5 Plugin-Repo-Link-Test erfolgreich.\n";
