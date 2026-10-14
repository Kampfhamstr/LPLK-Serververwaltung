<?php
namespace App\Services;

final class FtpConnector implements ServerConnectorInterface
{
    private \FTP\Connection $connection;

    public function __construct(private array $server, private string $password, private bool $tls = false)
    {
        if (!function_exists('ftp_connect')) {
            throw new \RuntimeException('PHP-Erweiterung ftp ist nicht installiert.');
        }
        $host = (string)$server['host'];
        $port = (int)($server['port'] ?: 21);
        if ($this->tls) {
            if (!function_exists('ftp_ssl_connect')) {
                throw new \RuntimeException('FTPS wird von der installierten PHP-FTP-Erweiterung nicht unterstützt.');
            }
            $connection = @ftp_ssl_connect($host, $port, 10);
        } else {
            $connection = @ftp_connect($host, $port, 10);
        }
        if (!$connection) {
            throw new \RuntimeException(($this->tls ? 'FTPS' : 'FTP') . '-Verbindung konnte nicht hergestellt werden.');
        }
        $this->connection = $connection;
        if (!@ftp_login($this->connection, (string)$server['username'], $password)) {
            throw new \RuntimeException(($this->tls ? 'FTPS' : 'FTP') . '-Anmeldung fehlgeschlagen.');
        }
        @ftp_pasv($this->connection, true);
    }

    public function __destruct()
    {
        if (isset($this->connection)) {
            @ftp_close($this->connection);
        }
    }

    public function label(): string
    {
        return $this->tls ? 'FTPS' : 'FTP';
    }

    public function list(string $path): array
    {
        $remote = $this->path($path);
        $items = [];
        if (function_exists('ftp_mlsd')) {
            $rows = @ftp_mlsd($this->connection, $remote);
            if ($rows !== false) {
                foreach ($rows as $row) {
                    $name = (string)($row['name'] ?? '');
                    if ($name === '' || $name === '.' || $name === '..') continue;
                    $type = strtolower((string)($row['type'] ?? ''));
                    $items[] = ['name' => $name, 'is_file' => $type !== 'dir' && $type !== 'cdir' && $type !== 'pdir'];
                }
                return $items;
            }
        }

        $names = @ftp_nlist($this->connection, $remote);
        if ($names === false) throw new \RuntimeException('FTP-Verzeichnis nicht lesbar: ' . $remote);
        foreach ($names as $entry) {
            $name = basename((string)$entry);
            if ($name === '' || $name === '.' || $name === '..') continue;
            $full = rtrim($remote, '/') . '/' . $name;
            $isFile = @ftp_size($this->connection, $full) >= 0;
            $items[] = ['name' => $name, 'is_file' => $isFile];
        }
        return $items;
    }

    public function read(string $path): string
    {
        $stream = fopen('php://temp', 'w+b');
        if ($stream === false) throw new \RuntimeException('Temporärer Speicher konnte nicht geöffnet werden.');
        try {
            if (!@ftp_fget($this->connection, $stream, $this->path($path), FTP_BINARY)) {
                throw new \RuntimeException('FTP-Datei nicht lesbar: ' . $path);
            }
            rewind($stream);
            $content = stream_get_contents($stream);
            if ($content === false) throw new \RuntimeException('FTP-Datei konnte nicht gelesen werden.');
            return $content;
        } finally {
            fclose($stream);
        }
    }

    public function write(string $path, string $content): void
    {
        $stream = fopen('php://temp', 'w+b');
        if ($stream === false) throw new \RuntimeException('Temporärer Speicher konnte nicht geöffnet werden.');
        try {
            if (fwrite($stream, $content) !== strlen($content)) {
                throw new \RuntimeException('Temporäre Datei konnte nicht vollständig geschrieben werden.');
            }
            rewind($stream);
            if (!@ftp_fput($this->connection, $this->path($path), $stream, FTP_BINARY)) {
                throw new \RuntimeException('FTP-Datei konnte nicht geschrieben werden: ' . $path);
            }
        } finally {
            fclose($stream);
        }
    }

    public function exists(string $path): bool
    {
        $remote = $this->path($path);
        if (@ftp_size($this->connection, $remote) >= 0) return true;
        $cwd = @ftp_pwd($this->connection);
        if ($cwd === false) return false;
        if (@ftp_chdir($this->connection, $remote)) {
            @ftp_chdir($this->connection, $cwd);
            return true;
        }
        return false;
    }

    private function path(string $path): string
    {
        $root = '/' . trim((string)($this->server['root_path'] ?: '/'), '/');
        if ($root === '//') $root = '/';
        return rtrim($root, '/') . '/' . ltrim($path, '/');
    }
}
