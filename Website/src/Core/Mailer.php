<?php
namespace App\Core;

final class Mailer
{
    public static function send(string $to, string $subject, string $html, string $text): void
    {
        $settings = self::settings();
        if (!$settings['host']) throw new \RuntimeException('SMTP ist noch nicht konfiguriert.');
        $host = $settings['host'];
        $port = (int)$settings['port'];
        $encryption = $settings['encryption'];
        $remote = ($encryption === 'ssl' ? 'ssl://' : '') . $host . ':' . $port;
        $fp = @stream_socket_client($remote, $errno, $errstr, 15, STREAM_CLIENT_CONNECT);
        if (!$fp) throw new \RuntimeException('SMTP-Verbindung fehlgeschlagen: ' . $errstr);
        stream_set_timeout($fp, 15);
        self::expect($fp, [220]);
        self::cmd($fp, 'EHLO ' . ($_SERVER['SERVER_NAME'] ?? 'localhost'), [250]);
        if ($encryption === 'tls') {
            self::cmd($fp, 'STARTTLS', [220]);
            if (!stream_socket_enable_crypto($fp, true, STREAM_CRYPTO_METHOD_TLS_CLIENT)) {
                throw new \RuntimeException('SMTP TLS konnte nicht aktiviert werden.');
            }
            self::cmd($fp, 'EHLO ' . ($_SERVER['SERVER_NAME'] ?? 'localhost'), [250]);
        }
        if ($settings['username'] !== '') {
            self::cmd($fp, 'AUTH LOGIN', [334]);
            self::cmd($fp, base64_encode($settings['username']), [334]);
            self::cmd($fp, base64_encode($settings['password']), [235]);
        }
        $from = $settings['from_email'];
        $fromName = $settings['from_name'] ?: Config::get('app_name', 'Serververwaltung');
        self::cmd($fp, 'MAIL FROM:<' . $from . '>', [250]);
        self::cmd($fp, 'RCPT TO:<' . $to . '>', [250,251]);
        self::cmd($fp, 'DATA', [354]);
        $boundary = 'b_' . bin2hex(random_bytes(12));
        $headers = [
            'From: ' . self::encodeHeader($fromName) . ' <' . $from . '>',
            'To: <' . $to . '>',
            'Subject: ' . self::encodeHeader($subject),
            'MIME-Version: 1.0',
            'Content-Type: multipart/alternative; boundary="' . $boundary . '"',
            'Date: ' . date(DATE_RFC2822),
            'Message-ID: <' . bin2hex(random_bytes(12)) . '@' . ($_SERVER['SERVER_NAME'] ?? 'localhost') . '>',
        ];
        $body = implode("\r\n", $headers) . "\r\n\r\n";
        $body .= '--' . $boundary . "\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: 8bit\r\n\r\n" . $text . "\r\n";
        $body .= '--' . $boundary . "\r\nContent-Type: text/html; charset=UTF-8\r\nContent-Transfer-Encoding: 8bit\r\n\r\n" . $html . "\r\n";
        $body .= '--' . $boundary . "--\r\n";
        foreach (preg_split('/\r\n|\n|\r/', $body) as $line) {
            if (str_starts_with($line, '.')) $line = '.' . $line;
            fwrite($fp, $line . "\r\n");
        }
        fwrite($fp, ".\r\n");
        self::expect($fp, [250]);
        self::cmd($fp, 'QUIT', [221]);
        fclose($fp);
    }

    private static function settings(): array
    {
        $stmt = Database::connection()->query("SELECT setting_key, setting_value, encrypted FROM app_settings WHERE setting_key LIKE 'smtp.%'");
        $map = [];
        foreach ($stmt->fetchAll() as $row) {
            $value = $row['setting_value'] ?? '';
            if ((int)$row['encrypted'] === 1 && $value !== '') $value = Crypto::decrypt($value);
            $map[$row['setting_key']] = $value;
        }
        return [
            'host' => $map['smtp.host'] ?? '',
            'port' => $map['smtp.port'] ?? '587',
            'encryption' => $map['smtp.encryption'] ?? 'tls',
            'username' => $map['smtp.username'] ?? '',
            'password' => $map['smtp.password'] ?? '',
            'from_email' => $map['smtp.from_email'] ?? '',
            'from_name' => $map['smtp.from_name'] ?? Config::get('app_name', 'Serververwaltung'),
        ];
    }

    private static function cmd($fp, string $cmd, array $codes): void
    {
        fwrite($fp, $cmd . "\r\n");
        self::expect($fp, $codes);
    }

    private static function expect($fp, array $codes): void
    {
        $response = '';
        while (($line = fgets($fp, 515)) !== false) {
            $response .= $line;
            if (strlen($line) < 4 || $line[3] === ' ') break;
        }
        $code = (int)substr($response, 0, 3);
        if (!in_array($code, $codes, true)) {
            throw new \RuntimeException('SMTP-Fehler: ' . trim($response));
        }
    }

    private static function encodeHeader(string $value): string
    {
        return '=?UTF-8?B?' . base64_encode($value) . '?=';
    }
}
