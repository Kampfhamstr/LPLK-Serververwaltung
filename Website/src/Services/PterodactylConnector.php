<?php
namespace App\Services;

final class PterodactylConnector implements ServerConnectorInterface
{
    public function __construct(private array $server, private string $apiKey) {}

    public function label(): string { return 'Pterodactyl API'; }

    public function list(string $path): array
    {
        $data = $this->request('GET', '/files/list?directory=' . rawurlencode($this->path($path)));
        $json = json_decode($data, true, 512, JSON_THROW_ON_ERROR);
        $items = [];
        foreach (($json['data'] ?? []) as $item) {
            $a = $item['attributes'] ?? [];
            $items[] = ['name' => $a['name'] ?? '', 'is_file' => (bool)($a['is_file'] ?? false)];
        }
        return $items;
    }

    public function read(string $path): string
    {
        return $this->request('GET', '/files/contents?file=' . rawurlencode($this->path($path)), null, false);
    }

    public function write(string $path, string $content): void
    {
        $this->request('POST', '/files/write?file=' . rawurlencode($this->path($path)), $content, false, 'text/plain');
    }

    public function exists(string $path): bool
    {
        $parent = dirname($this->path($path));
        $name = basename($this->path($path));
        try {
            foreach ($this->list($parent === '.' ? '/' : $parent) as $item) if ($item['name'] === $name) return true;
        } catch (\Throwable) {}
        return false;
    }


    public function sendCommand(string $command): void
    {
        $this->request('POST', '/command', json_encode(['command' => $command], JSON_THROW_ON_ERROR));
    }

    public function power(string $signal): void
    {
        if (!in_array($signal, ['start','stop','restart','kill'], true)) {
            throw new \InvalidArgumentException('Ungültiges Pterodactyl-Powersignal.');
        }
        $this->request('POST', '/power', json_encode(['signal' => $signal], JSON_THROW_ON_ERROR));
    }

    public function resources(): array
    {
        $raw = $this->request('GET', '/resources');
        $json = json_decode($raw, true, 512, JSON_THROW_ON_ERROR);
        return is_array($json['attributes'] ?? null) ? $json['attributes'] : [];
    }

    /**
     * Liefert Serverdetails inklusive Limits aus der Pterodactyl Client API.
     * Wird für RAM-/Disk-/CPU-Prozentwerte der Liveverwaltung verwendet.
     */
    public function serverDetails(): array
    {
        $raw = $this->request('GET', '');
        $json = json_decode($raw, true, 512, JSON_THROW_ON_ERROR);
        return is_array($json['attributes'] ?? null) ? $json['attributes'] : [];
    }

    private function path(string $path): string
    {
        $root = trim((string)($this->server['root_path'] ?? '/'));
        $root = $root === '/' ? '' : '/' . trim($root, '/');
        return $root . '/' . ltrim($path, '/');
    }

    private function request(string $method, string $endpoint, ?string $body = null, bool $json = true, string $contentType = 'application/json'): string
    {
        $base = rtrim((string)$this->server['base_url'], '/');
        $id = rawurlencode((string)$this->server['server_identifier']);
        $url = $base . '/api/client/servers/' . $id . $endpoint;
        $ch = curl_init($url);
        $headers = [
            'Authorization: Bearer ' . $this->apiKey,
            'Accept: Application/vnd.pterodactyl.v1+json',
            'Content-Type: ' . $contentType,
            'User-Agent: Serververwaltung/1.8.3',
        ];
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_HTTPHEADER => $headers,
            CURLOPT_CONNECTTIMEOUT => 2,
            CURLOPT_TIMEOUT => 4,
            CURLOPT_FOLLOWLOCATION => false,
        ]);
        if ($body !== null) curl_setopt($ch, CURLOPT_POSTFIELDS, $body);
        $response = curl_exec($ch);
        if ($response === false) {
            $e = curl_error($ch); curl_close($ch); throw new \RuntimeException('Pterodactyl-Verbindung fehlgeschlagen: ' . $e);
        }
        $code = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);
        if ($code < 200 || $code >= 300) throw new \RuntimeException('Pterodactyl API HTTP ' . $code . ': ' . substr($response, 0, 500));
        return (string)$response;
    }
}
