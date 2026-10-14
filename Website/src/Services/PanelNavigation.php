<?php
namespace App\Services;

use App\Core\Auth;
use App\Core\Config;

/** Navigation uses the same permissions as the destinations; never grants access. */
final class PanelNavigation
{
    public static function groups(): array
    {
        $definitions = [
            'Übersicht' => [['/dashboard','Meine Übersicht','dashboard.view']],
            'Server' => [['/servers','Serverübersicht','servers.view'],['/plugins','Addons & Updates','plugins.view']],
            'Spieler' => [['/players','Spieler finden','players.view'],['/bans','Sperren','moderation.view'],['/moderation','Moderationsfälle','moderation.view']],
            'Community' => [['/forum','Forum','community.forum.view'],['/pets','Meine Pets','pets.use']],
            'Verwaltung' => [['/users','Panelnutzer','users.view'],['/invitations','Einladungen','users.invite'],['/audit','Änderungsverlauf','audit.view'],['/settings','Systemeinstellungen','settings.view']],
        ];
        $groups = [];
        foreach ($definitions as $label => $items) {
            $items = array_values(array_filter($items, static fn($i) => Auth::can($i[2]) && ($i[0] !== '/pets' || Config::get('pets.enabled', true))));
            if ($items) $groups[$label] = $items;
        }
        return $groups;
    }

    public static function active(string $path, string $prefix): bool
    { return $path === $prefix || str_starts_with($path, $prefix . '/'); }

    public static function context(string $path, ?array $player, ?array $server, int $selected = 0): ?array
    {
        if (preg_match('~^/players/([1-9][0-9]*)(?:/|$)~D', $path, $m) && $player && (int)$player['id'] === (int)$m[1] && Auth::can('players.view')) {
            $base = '/players/'.(int)$player['id'];
            $query = $selected > 0 && Auth::canServer($selected,'view') ? '?server='.$selected : '';
            $links = [[$base,'Übersicht'],[$base.'/statistics'.$query,'Statistiken','statistics'],[$base.'/economy'.$query,'Bank','economy']];
            if (Auth::can('moderation.view')) $links[] = ['/moderation?q='.rawurlencode((string)$player['steam_id64']),'Moderation'];
            if (Auth::can('players.manage') || Auth::can('players.contacts.view')) $links[] = [$base.'#verwaltung','Verwaltung'];
            $links[] = [$base.'#rechte','Rechte'];
            return ['kind'=>'player','id'=>(int)$player['id'],'name'=>(string)($player['steam_personaname']??$player['display_name']), 'parent'=>'/players','parentLabel'=>'Spieler','base'=>$base,'links'=>$links,'server'=>$selected];
        }
        if (preg_match('~^/servers/([1-9][0-9]*)(?:/|$)~D',$path,$m) && $server && (int)$server['id'] === (int)$m[1] && Auth::canServer((int)$m[1],'view')) {
            $sid=(int)$server['id']; $base='/servers/'.$sid; $links=[];
            if (Auth::can('servers.view')) $links[]=[$base,'Übersicht'];
            if (Auth::can('live.view') && Auth::canServer($sid,'live')) { $links[]=[$base.'/live','Spieler & Live']; $links[]=[$base.'/chat','Chat']; }
            if (Auth::can('players.view')) $links[]=[$base.'/ranking','Ranglisten','statistics'];
            if (Auth::can('servers.view')) { $links[]=[$base.'#addons','Addons']; $links[]=[$base.'#rechte','Rechte']; }
            if (Auth::can('servers.manage') && Auth::canServer($sid,'permissions')) $links[]=[$base.'#einstellungen','Einstellungen'];
            return ['kind'=>'server','id'=>$sid,'name'=>(string)$server['name'],'parent'=>'/servers','parentLabel'=>'Server','base'=>$base,'links'=>$links,'server'=>$sid];
        }
        return null;
    }

    public static function commandStatus(string $state): string
    {
        return match($state) {
            'queued'=>'Gespeichert · Übertragung ausstehend',
            'delivered'=>'Übertragen · Bestätigung ausstehend',
            'success'=>'Vom Server bestätigt',
            'failed'=>'Vom Server abgelehnt',
            default=>'Status noch unklar',
        };
    }
}
