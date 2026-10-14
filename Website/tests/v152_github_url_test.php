<?php
require dirname(__DIR__).'/src/Services/GitHubReleaseService.php';

use App\Services\GitHubReleaseService;

function ok152(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

$cases=[
    'K4ryuu/K4-BombWires-SwiftlyS2' => 'K4ryuu/K4-BombWires-SwiftlyS2',
    'https://github.com/K4ryuu/K4-BombWires-SwiftlyS2' => 'K4ryuu/K4-BombWires-SwiftlyS2',
    'https://github.com/K4ryuu/K4-BombWires-SwiftlyS2/releases' => 'K4ryuu/K4-BombWires-SwiftlyS2',
    'https://github.com/K4ryuu/K4-BombWires-SwiftlyS2/releases/latest' => 'K4ryuu/K4-BombWires-SwiftlyS2',
    'https://github.com/K4ryuu/K4-BombWires-SwiftlyS2/releases/tag/v1.0.1' => 'K4ryuu/K4-BombWires-SwiftlyS2',
    'https://www.github.com/K4ryuu/K4-BombWires-SwiftlyS2/releases/tag/v1.0.1?x=1#asset' => 'K4ryuu/K4-BombWires-SwiftlyS2',
];

foreach($cases as $input=>$expected){
    $actual=GitHubReleaseService::normalizeRepository($input);
    ok152($actual===$expected,$input.' -> '.$actual);
}

foreach([
    'http://github.com/K4ryuu/K4-BombWires-SwiftlyS2',
    'https://example.com/K4ryuu/K4-BombWires-SwiftlyS2',
    'https://github.com/K4ryuu',
    ''
] as $input){
    $thrown=false;
    try{GitHubReleaseService::normalizeRepository($input);}
    catch(Throwable){$thrown=true;}
    ok152($thrown,'Ungültige Eingabe abgelehnt: '.($input!==''?$input:'<leer>'));
}

echo "\nv1.5.2 GitHub-URL-Test erfolgreich.\n";
