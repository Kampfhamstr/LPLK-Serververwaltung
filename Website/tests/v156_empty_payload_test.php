<?php
$root=dirname(__DIR__);
$service=file_get_contents($root.'/src/Services/LiveServerService.php');

function ok156(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

$needle="'payload' => \$payload === [] ? new \\stdClass() : \$payload";
ok156(
    str_contains($service,$needle),
    'Leere Payload wird im zentralen Queue-Pfad als stdClass geschrieben'
);

$json=json_encode([
    'id'=>'test',
    'action'=>'plugin_rescan',
    'target_steamid'=>null,
    'payload'=>new stdClass(),
    'requested_at'=>gmdate('c'),
],JSON_THROW_ON_ERROR);

ok156(str_contains($json,'"payload":{}'),'Leere Payload wird als JSON-Objekt serialisiert');

$arrayJson=json_encode(['payload'=>[]],JSON_THROW_ON_ERROR);
ok156(str_contains($arrayJson,'"payload":[]'),'Alter Fehlerfall [] ist reproduzierbar');

echo "\nv1.5.6 Empty-Payload-Test erfolgreich.\n";
