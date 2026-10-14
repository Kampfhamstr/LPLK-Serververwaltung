<?php
namespace App\Core;

final class Jsonc
{
    public static function decode(string $jsonc): array
    {
        $clean = self::stripComments($jsonc);
        $data = json_decode($clean, true, 512, JSON_THROW_ON_ERROR);
        if (!is_array($data)) throw new \RuntimeException('JSONC enthält kein Objekt.');
        return $data;
    }

    public static function encode(array $data): string
    {
        return json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE) . "\n";
    }

    private static function stripComments(string $input): string
    {
        $out = '';
        $len = strlen($input);
        $inString = false; $escape = false; $line = false; $block = false;
        for ($i=0; $i<$len; $i++) {
            $c = $input[$i]; $n = $i+1<$len ? $input[$i+1] : '';
            if ($line) {
                if ($c === "\n") { $line=false; $out .= $c; }
                continue;
            }
            if ($block) {
                if ($c === '*' && $n === '/') { $block=false; $i++; }
                elseif ($c === "\n") $out .= "\n";
                continue;
            }
            if ($inString) {
                $out .= $c;
                if ($escape) { $escape=false; continue; }
                if ($c === '\\') { $escape=true; continue; }
                if ($c === '"') $inString=false;
                continue;
            }
            if ($c === '"') { $inString=true; $out .= $c; continue; }
            if ($c === '/' && $n === '/') { $line=true; $i++; continue; }
            if ($c === '/' && $n === '*') { $block=true; $i++; continue; }
            $out .= $c;
        }
        return $out;
    }
}
