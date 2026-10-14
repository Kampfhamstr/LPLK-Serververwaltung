<?php
namespace App\Services;

use App\Core\Crypto;

final class ConnectorFactory
{
    public static function make(array $server): ServerConnectorInterface
    {
        $secret = Crypto::decrypt($server['secret_enc'] ?? '');
        return match ($server['connector_type']) {
            'pterodactyl' => new PterodactylConnector($server, $secret),
            'sftp' => new SftpConnector($server, $secret),
            'ftps' => new FtpConnector($server, $secret, true),
            'ftp' => new FtpConnector($server, $secret, false),
            default => throw new \RuntimeException('Unbekannter Verbindungstyp.'),
        };
    }
}
