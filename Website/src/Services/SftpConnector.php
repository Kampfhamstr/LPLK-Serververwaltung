<?php
namespace App\Services;

final class SftpConnector implements ServerConnectorInterface
{
    private $connection;
    private $sftp;

    public function __construct(private array $server, private string $password)
    {
        if (!function_exists('ssh2_connect')) throw new \RuntimeException('PHP-Erweiterung ssh2 ist nicht installiert.');
        $this->connection = @ssh2_connect((string)$server['host'], (int)($server['port'] ?: 22));
        if (!$this->connection) throw new \RuntimeException('SFTP-Verbindung konnte nicht hergestellt werden.');
        if (!@ssh2_auth_password($this->connection, (string)$server['username'], $password)) throw new \RuntimeException('SFTP-Anmeldung fehlgeschlagen.');
        $this->sftp = ssh2_sftp($this->connection);
        if (!$this->sftp) throw new \RuntimeException('SFTP-Subsystem konnte nicht gestartet werden.');
    }

    public function label(): string { return 'SFTP'; }

    public function list(string $path): array
    {
        $remote = $this->path($path);
        $dir = @opendir('ssh2.sftp://' . intval($this->sftp) . $remote);
        if (!$dir) throw new \RuntimeException('Verzeichnis nicht lesbar: ' . $remote);
        $items=[];
        while (($name = readdir($dir)) !== false) {
            if ($name === '.' || $name === '..') continue;
            $stat = @ssh2_sftp_stat($this->sftp, rtrim($remote,'/') . '/' . $name);
            $mode = $stat['mode'] ?? 0;
            $isFile = (($mode & 0170000) === 0100000);
            $items[]=['name'=>$name,'is_file'=>$isFile];
        }
        closedir($dir);
        return $items;
    }

    public function read(string $path): string
    {
        $stream = @fopen('ssh2.sftp://' . intval($this->sftp) . $this->path($path), 'rb');
        if (!$stream) throw new \RuntimeException('Datei nicht lesbar: ' . $path);
        $data = stream_get_contents($stream); fclose($stream);
        if ($data === false) throw new \RuntimeException('Datei konnte nicht gelesen werden.');
        return $data;
    }

    public function write(string $path, string $content): void
    {
        $stream = @fopen('ssh2.sftp://' . intval($this->sftp) . $this->path($path), 'wb');
        if (!$stream) throw new \RuntimeException('Datei nicht schreibbar: ' . $path);
        $written = fwrite($stream, $content); fclose($stream);
        if ($written === false || $written < strlen($content)) throw new \RuntimeException('Datei wurde nicht vollständig geschrieben.');
    }

    public function exists(string $path): bool { return @ssh2_sftp_stat($this->sftp, $this->path($path)) !== false; }

    private function path(string $path): string
    {
        $root = '/' . trim((string)($this->server['root_path'] ?: '/'), '/');
        if ($root === '//') $root = '/';
        return rtrim($root, '/') . '/' . ltrim($path, '/');
    }
}
