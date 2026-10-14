<?php
namespace App\Services;

use App\Core\Crypto;
use App\Core\Database;

final class GitHubReleaseService
{
    public static function normalizeRepository(string $value): string
    {
        $value = trim($value);
        if ($value === '') {
            throw new \InvalidArgumentException('GitHub Repository darf nicht leer sein.');
        }

        if (preg_match('/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/', $value)) {
            return $value;
        }

        $parts = parse_url($value);
        if (!is_array($parts)) {
            throw new \InvalidArgumentException('Ungültige GitHub-URL.');
        }

        $scheme = strtolower((string)($parts['scheme'] ?? ''));
        $host = strtolower((string)($parts['host'] ?? ''));
        if ($scheme !== 'https' || !in_array($host, ['github.com', 'www.github.com'], true)) {
            throw new \InvalidArgumentException('Es werden nur HTTPS-URLs von github.com akzeptiert.');
        }

        $path = trim((string)($parts['path'] ?? ''), '/');
        $segments = array_values(array_filter(
            explode('/', $path),
            static fn(string $part): bool => $part !== ''
        ));

        if (count($segments) < 2) {
            throw new \InvalidArgumentException('GitHub-URL enthält kein Repository.');
        }

        $owner = rawurldecode($segments[0]);
        $repository = rawurldecode($segments[1]);

        if (str_ends_with(strtolower($repository), '.git')) {
            $repository = substr($repository, 0, -4);
        }

        $normalized = $owner . '/' . $repository;

        if (!preg_match('/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/', $normalized)) {
            throw new \InvalidArgumentException(
                'GitHub Repository muss als owner/repository oder vollständige github.com-URL angegeben werden.'
            );
        }

        return $normalized;
    }

    public static function repositoryFromWebsite(?string $website): ?string
    {
        $website = trim((string)$website);
        if ($website === '') return null;

        try {
            return self::normalizeRepository($website);
        } catch (\Throwable) {
            return null;
        }
    }

    public static function latest(string $repository,string $assetPattern='*.zip',string $channel='stable'): array
    {
        $repository=self::normalizeRepository($repository); [$owner,$repo]=explode('/',$repository,2);
        $assetPattern=trim($assetPattern)?:'*.zip'; $channel=$channel==='prerelease'?'prerelease':'stable';
        if(str_contains($assetPattern,'/')||str_contains($assetPattern,'\\\\')) throw new \InvalidArgumentException('Asset-Muster darf keinen Verzeichnispfad enthalten.');

        if($channel==='stable') $release=self::requestJson('https://api.github.com/repos/'.rawurlencode($owner).'/'.rawurlencode($repo).'/releases/latest');
        else {
            $releases=self::requestJson('https://api.github.com/repos/'.rawurlencode($owner).'/'.rawurlencode($repo).'/releases?per_page=20'); $release=null;
            foreach((array)$releases as $candidate){ if(is_array($candidate)&&empty($candidate['draft'])){$release=$candidate;break;} }
            if(!$release) throw new \RuntimeException('Kein veröffentlichtes GitHub Release gefunden.');
        }
        $tag=trim((string)($release['tag_name']??'')); $version=self::versionFromTag($tag); $selected=null;
        foreach((array)($release['assets']??[]) as $asset){
            if(!is_array($asset))continue; $name=(string)($asset['name']??'');
            $lower=strtolower($name);
            $supported=str_ends_with($lower,'.zip')||str_ends_with($lower,'.rar');
            if($name!==''&&fnmatch($assetPattern,$name,FNM_CASEFOLD)&&$supported){$selected=$asset;break;}
        }
        if(!$selected) throw new \RuntimeException('Kein unterstütztes ZIP-/RAR-Release-Asset passt zum Muster "'.$assetPattern.'".');
        $assetUrl=trim((string)($selected['browser_download_url']??'')); if(!self::isGitHubDownloadUrl($assetUrl)) throw new \RuntimeException('GitHub Release Asset hat keine zulässige Download-URL.');
        $digest=trim((string)($selected['digest']??'')); $sha=null; if(preg_match('/^sha256:([a-f0-9]{64})$/i',$digest,$m))$sha=strtolower($m[1]);
        return ['repository'=>$repository,'version'=>$version,'tag'=>$tag,'release_url'=>self::httpsUrl($release['html_url']??null),'prerelease'=>!empty($release['prerelease']),'published_at'=>$release['published_at']??null,'asset_name'=>(string)$selected['name'],'asset_url'=>$assetUrl,'asset_sha256'=>$sha,'asset_size'=>isset($selected['size'])?(int)$selected['size']:null];
    }

    public static function versionFromTag(string $tag): ?string
    {
        if(preg_match('/(?<!\\d)(\\d+\\.\\d+\\.\\d+(?:[-+][0-9A-Za-z.-]+)?)/',$tag,$m))return $m[1];
        if(preg_match('/(?<!\\d)(\\d+\\.\\d+)(?!\\d)/',$tag,$m))return $m[1]; return null;
    }
    public static function isNewer(?string $installed,?string $latest): bool
    { $installed=self::cleanVersion($installed);$latest=self::cleanVersion($latest);return $installed!==null&&$latest!==null&&version_compare($latest,$installed,'>'); }
    public static function tokenConfigured(): bool
    { $st=Database::connection()->prepare("SELECT setting_value FROM app_settings WHERE setting_key='github.api_token'");$st->execute();return trim((string)($st->fetchColumn()?:''))!==''; }
    public static function test(): array
    { $d=self::requestJson('https://api.github.com/rate_limit');$c=$d['resources']['core']??[];return ['limit'=>(int)($c['limit']??0),'remaining'=>(int)($c['remaining']??0),'reset'=>(int)($c['reset']??0),'authenticated'=>self::tokenConfigured()]; }
    private static function cleanVersion(?string $v): ?string
    { $v=trim((string)$v);return preg_match('/(\\d+\\.\\d+(?:\\.\\d+)?(?:[-+][0-9A-Za-z.-]+)?)/',$v,$m)?$m[1]:null; }
    private static function requestJson(string $url): array
    {
        $headers=['Accept: application/vnd.github+json','X-GitHub-Api-Version: 2026-03-10','User-Agent: LPLK-Serververwaltung/1.5.3'];$token=self::apiToken();if($token!==null)$headers[]='Authorization: Bearer '.$token;
        $ch=curl_init($url);curl_setopt_array($ch,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_FOLLOWLOCATION=>false,CURLOPT_CONNECTTIMEOUT=>8,CURLOPT_TIMEOUT=>20,CURLOPT_HTTPHEADER=>$headers]);$body=curl_exec($ch);
        if($body===false){$e=curl_error($ch);curl_close($ch);throw new \RuntimeException('GitHub-Verbindung fehlgeschlagen: '.$e);} $status=(int)curl_getinfo($ch,CURLINFO_RESPONSE_CODE);curl_close($ch);
        if($status<200||$status>=300){$d=json_decode((string)$body,true);$m=is_array($d)?(string)($d['message']??''):'';throw new \RuntimeException('GitHub API HTTP '.$status.($m!==''?': '.$m:'.'));}
        $d=json_decode((string)$body,true,512,JSON_THROW_ON_ERROR);if(!is_array($d))throw new \RuntimeException('GitHub API lieferte keine gültige JSON-Antwort.');return $d;
    }
    private static function apiToken(): ?string
    { $st=Database::connection()->prepare("SELECT setting_value,encrypted FROM app_settings WHERE setting_key='github.api_token'");$st->execute();$r=$st->fetch();if(!$r||trim((string)$r['setting_value'])==='')return null;$v=(string)$r['setting_value'];return (int)$r['encrypted']===1?Crypto::decrypt($v):$v; }
    private static function isGitHubDownloadUrl(string $url): bool
    { if(!filter_var($url,FILTER_VALIDATE_URL)||!str_starts_with($url,'https://'))return false;return strtolower((string)parse_url($url,PHP_URL_HOST))==='github.com'; }
    private static function httpsUrl(mixed $v): ?string
    { $u=trim((string)$v);return $u!==''&&str_starts_with($u,'https://')&&filter_var($u,FILTER_VALIDATE_URL)?$u:null; }
}
