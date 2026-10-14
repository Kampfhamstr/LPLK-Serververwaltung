<?php
namespace App\Core;

final class Config
{
    private static array $config = [];

    public static function load(string $file): void
    {
        if (!is_file($file)) {
            throw new \RuntimeException('Konfigurationsdatei fehlt: ' . $file);
        }
        $cfg = require $file;
        if (!is_array($cfg)) {
            throw new \RuntimeException('Ungültige Konfiguration.');
        }
        self::$config = $cfg;
        date_default_timezone_set((string)self::get('timezone', 'Europe/Berlin'));
    }

    public static function get(string $key, mixed $default = null): mixed
    {
        $value = self::$config;
        foreach (explode('.', $key) as $part) {
            if (!is_array($value) || !array_key_exists($part, $value)) {
                return $default;
            }
            $value = $value[$part];
        }
        return $value;
    }
}
