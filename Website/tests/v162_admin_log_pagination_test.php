<?php
$root=dirname(__DIR__);
$template=file_get_contents($root.'/templates/server_live.php');
$js=file_get_contents($root.'/public/assets/app.js');

function ok162(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok162(str_contains($template,'10 Einträge pro Seite'),'10 Einträge pro Seite sichtbar');
ok162(str_contains($template,'data-admin-log-newer'),'Neuere-Schaltfläche vorhanden');
ok162(str_contains($template,'data-admin-log-older'),'Ältere-Schaltfläche vorhanden');
ok162(str_contains($template,'data-admin-log-page-info'),'Seitenanzeige vorhanden');
ok162(str_contains($js,'const pageSize=10'),'Seitengröße ist exakt 10');
ok162(str_contains($js,'let currentPage=1'),'Start auf Seite 1');
ok162(str_contains($js,'matches.slice(start,start+pageSize)'),'Nur eine 10er-Seite wird eingeblendet');
ok162(str_contains($js,'resetAndRender'),'Filter setzt auf Seite 1 zurück');
ok162(str_contains($js,'currentPage++'),'Blättern zu älteren Einträgen');
ok162(str_contains($js,'currentPage--'),'Blättern zu neueren Einträgen');

echo "\nv1.6.2 Admin-Log-Pagination-Test erfolgreich.\n";
