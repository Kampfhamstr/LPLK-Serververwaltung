<?php
namespace App\Services;

final class FrameworkDetector
{
    public static function detect(ServerConnectorInterface $connector): array
    {
        $swiftly = $connector->exists('game/csgo/addons/swiftlys2');
        $css = $connector->exists('game/csgo/addons/counterstrikesharp');
        $framework = $swiftly && $css ? 'both' : ($swiftly ? 'swiftlys2' : ($css ? 'counterstrikesharp' : 'unknown'));
        $plugins = [];
        if ($swiftly) $plugins = array_merge($plugins, self::scanPlugins($connector, 'swiftlys2', 'game/csgo/addons/swiftlys2/plugins'));
        if ($css) $plugins = array_merge($plugins, self::scanPlugins($connector, 'counterstrikesharp', 'game/csgo/addons/counterstrikesharp/plugins'));
        return ['framework' => $framework, 'plugins' => $plugins];
    }

    private static function scanPlugins(ServerConnectorInterface $connector, string $framework, string $path): array
    {
        $out=[];
        try {
            foreach ($connector->list($path) as $item) {
                if ($item['name'] === '' || $item['name'][0] === '.') continue;
                $out[]=['framework'=>$framework,'name'=>$item['name'],'path'=>$path . '/' . $item['name']];
            }
        } catch (\Throwable) {}
        return $out;
    }
}
