<?php
namespace App\Core;

/**
 * Kleine, dependency-freie WebAuthn/Passkey-Implementierung für das Panel.
 * Unterstützt Passkeys/FIDO2 mit ES256 (P-256) und RS256.
 *
 * Sicherheitsmodell:
 * - Registrierung nur in einer bereits angemeldeten Sitzung + aktuelles Passwort.
 * - userVerification=required (Windows Hello/Touch ID/Android PIN/Biometrie/FIDO2-PIN).
 * - Challenge, Origin und RP-ID werden serverseitig geprüft.
 * - Private Schlüssel verlassen den Authenticator nie; gespeichert wird nur der Public Key.
 * - Anmeldung per Passkey ist hier bewusst der zweite Faktor NACH korrektem Passwort.
 */
final class WebAuthn
{
    public static function supported(): bool
    {
        try {
            $origin = self::origin();
            $parts = parse_url($origin);
            if (!is_array($parts) || empty($parts['host'])) return false;
            $scheme = strtolower((string)($parts['scheme'] ?? ''));
            $host = strtolower((string)$parts['host']);
            return $scheme === 'https' || in_array($host, ['localhost','127.0.0.1','::1'], true);
        } catch (\Throwable) {
            return false;
        }
    }

    public static function rpId(): string
    {
        $configured = trim((string)Config::get('webauthn.rp_id', ''));
        if ($configured !== '') return strtolower($configured);
        $parts = parse_url(self::origin());
        $host = is_array($parts) ? strtolower((string)($parts['host'] ?? '')) : '';
        if ($host === '') throw new \RuntimeException('WebAuthn-RP-ID konnte nicht aus app_url ermittelt werden.');
        return $host;
    }

    public static function origin(): string
    {
        $configured = trim((string)Config::get('webauthn.origin', ''));
        $url = $configured !== '' ? $configured : trim((string)Config::get('app_url', ''));
        if ($url === '') {
            $https = (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off')
                || strtolower((string)($_SERVER['HTTP_X_FORWARDED_PROTO'] ?? '')) === 'https';
            $host = trim((string)($_SERVER['HTTP_HOST'] ?? ''));
            if ($host === '') throw new \RuntimeException('WebAuthn-Origin ist nicht konfiguriert.');
            $url = ($https ? 'https' : 'http') . '://' . $host;
        }
        $parts = parse_url($url);
        if (!is_array($parts) || empty($parts['scheme']) || empty($parts['host'])) {
            throw new \RuntimeException('Ungültige app_url/WebAuthn-Origin-Konfiguration.');
        }
        $origin = strtolower((string)$parts['scheme']) . '://' . strtolower((string)$parts['host']);
        if (isset($parts['port'])) $origin .= ':' . (int)$parts['port'];
        return $origin;
    }

    public static function rpName(): string
    {
        return trim((string)Config::get('webauthn.rp_name', (string)Config::get('app_name', 'Serververwaltung'))) ?: 'Serververwaltung';
    }

    public static function credentialCount(int $userId): int
    {
        if ($userId <= 0) return 0;
        try {
            $stmt = Database::connection()->prepare('SELECT COUNT(*) FROM webauthn_credentials WHERE user_id=?');
            $stmt->execute([$userId]);
            return (int)$stmt->fetchColumn();
        } catch (\Throwable) {
            return 0;
        }
    }

    public static function credentials(int $userId): array
    {
        if ($userId <= 0) return [];
        try {
            $stmt = Database::connection()->prepare(
                'SELECT id,user_id,credential_id_b64,algorithm,sign_count,label,transports_json,aaguid,created_at,last_used_at '
                . 'FROM webauthn_credentials WHERE user_id=? ORDER BY id'
            );
            $stmt->execute([$userId]);
            $rows = $stmt->fetchAll();
            foreach ($rows as &$row) {
                $transports = json_decode((string)($row['transports_json'] ?? '[]'), true);
                $row['transports'] = is_array($transports) ? array_values(array_filter($transports, 'is_string')) : [];
            }
            unset($row);
            return $rows;
        } catch (\Throwable) {
            return [];
        }
    }

    public static function registrationOptions(array $user, string $label): array
    {
        if (!self::supported()) throw new \RuntimeException('Passkeys benötigen HTTPS und eine gültige app_url.');
        $userId = (int)($user['id'] ?? 0);
        if ($userId <= 0) throw new \RuntimeException('Ungültiger Benutzer.');

        $challenge = self::randomB64(32);
        $_SESSION['_webauthn_register'] = [
            'user_id'=>$userId,
            'challenge'=>$challenge,
            'label'=>self::cleanLabel($label),
            'expires_at'=>time()+300,
        ];

        $exclude = [];
        foreach (self::credentials($userId) as $credential) {
            $exclude[] = [
                'type'=>'public-key',
                'id'=>(string)$credential['credential_id_b64'],
                'transports'=>$credential['transports'],
            ];
        }

        return [
            'challenge'=>$challenge,
            'rp'=>['id'=>self::rpId(),'name'=>self::rpName()],
            'user'=>[
                'id'=>self::b64url(hash('sha256', 'serververwaltung-user:' . $userId, true)),
                'name'=>(string)($user['email'] ?? ('user-'.$userId)),
                'displayName'=>Auth::displayName($user) ?: ('Nutzer '.$userId),
            ],
            'pubKeyCredParams'=>[
                ['type'=>'public-key','alg'=>-7],   // ES256
                ['type'=>'public-key','alg'=>-257], // RS256
            ],
            'timeout'=>60000,
            'attestation'=>'none',
            'authenticatorSelection'=>[
                'residentKey'=>'preferred',
                'requireResidentKey'=>false,
                'userVerification'=>'required',
            ],
            'excludeCredentials'=>$exclude,
        ];
    }

    public static function finishRegistration(array $user, array $credential): array
    {
        $pending = $_SESSION['_webauthn_register'] ?? null;
        unset($_SESSION['_webauthn_register']);
        $userId = (int)($user['id'] ?? 0);
        if (!is_array($pending)
            || (int)($pending['user_id'] ?? 0) !== $userId
            || (int)($pending['expires_at'] ?? 0) < time()) {
            throw new \RuntimeException('Passkey-Registrierung ist abgelaufen. Bitte erneut starten.');
        }

        $type = (string)($credential['type'] ?? '');
        if ($type !== 'public-key') throw new \RuntimeException('Ungültiger Passkey-Typ.');
        $response = is_array($credential['response'] ?? null) ? $credential['response'] : [];
        $clientDataRaw = self::decodeB64((string)($response['clientDataJSON'] ?? ''));
        $attestationRaw = self::decodeB64((string)($response['attestationObject'] ?? ''));
        $rawId = self::decodeB64((string)($credential['rawId'] ?? $credential['id'] ?? ''));
        if ($clientDataRaw === '' || $attestationRaw === '' || $rawId === '') {
            throw new \RuntimeException('Unvollständige Passkey-Antwort.');
        }

        self::verifyClientData($clientDataRaw, 'webauthn.create', (string)$pending['challenge']);
        $decoded = WebAuthnCbor::decode($attestationRaw);
        if (!is_array($decoded) || !isset($decoded['authData']) || !is_string($decoded['authData'])) {
            throw new \RuntimeException('Ungültiges WebAuthn-Attestation-Objekt.');
        }
        $auth = self::parseRegistrationAuthData($decoded['authData']);
        if (!hash_equals($rawId, $auth['credential_id'])) {
            throw new \RuntimeException('Passkey-Credential-ID stimmt nicht überein.');
        }

        [$pem,$alg] = self::coseToPem($auth['cose_key']);
        if (!in_array($alg, [-7,-257], true)) throw new \RuntimeException('Nicht unterstützter Passkey-Algorithmus.');
        if (@openssl_pkey_get_public($pem) === false) throw new \RuntimeException('Passkey-Public-Key konnte nicht validiert werden.');

        $transports = [];
        foreach ((array)($response['transports'] ?? []) as $transport) {
            if (is_string($transport) && preg_match('/^[a-z-]{2,32}$/', $transport)) $transports[] = $transport;
        }
        $transports = array_values(array_unique($transports));
        $credentialIdB64 = self::b64url($auth['credential_id']);
        $label = (string)($pending['label'] ?? 'Passkey');
        $now = date('c');

        Database::withBusyRetry(static function () use ($userId,$credentialIdB64,$pem,$alg,$auth,$label,$transports,$now): void {
            $stmt = Database::connection()->prepare(
                'INSERT INTO webauthn_credentials(user_id,credential_id_b64,public_key_pem,algorithm,sign_count,label,transports_json,aaguid,created_at,last_used_at) '
                . 'VALUES(?,?,?,?,?,?,?,?,?,NULL)'
            );
            $stmt->execute([
                $userId,$credentialIdB64,$pem,$alg,(int)$auth['sign_count'],$label,
                json_encode($transports, JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE),
                (string)$auth['aaguid'],$now,
            ]);
        }, 4);

        return [
            'id'=>(int)Database::connection()->lastInsertId(),
            'credential_id_b64'=>$credentialIdB64,
            'label'=>$label,
            'algorithm'=>$alg,
            'sign_count'=>(int)$auth['sign_count'],
        ];
    }

    public static function authenticationOptions(int $userId): array
    {
        if (!self::supported()) throw new \RuntimeException('Passkeys benötigen HTTPS und eine gültige app_url.');
        $credentials = self::credentials($userId);
        if (!$credentials) throw new \RuntimeException('Für dieses Konto ist kein Passkey registriert.');
        $challenge = self::randomB64(32);
        $_SESSION['_webauthn_authenticate'] = [
            'user_id'=>$userId,
            'challenge'=>$challenge,
            'expires_at'=>time()+180,
        ];
        $allow = [];
        foreach ($credentials as $credential) {
            $allow[] = [
                'type'=>'public-key',
                'id'=>(string)$credential['credential_id_b64'],
                'transports'=>$credential['transports'],
            ];
        }
        return [
            'challenge'=>$challenge,
            'rpId'=>self::rpId(),
            'allowCredentials'=>$allow,
            'timeout'=>60000,
            'userVerification'=>'required',
        ];
    }

    public static function finishAuthentication(int $userId, array $credential): array
    {
        $pending = $_SESSION['_webauthn_authenticate'] ?? null;
        unset($_SESSION['_webauthn_authenticate']);
        if (!is_array($pending)
            || (int)($pending['user_id'] ?? 0) !== $userId
            || (int)($pending['expires_at'] ?? 0) < time()) {
            throw new \RuntimeException('Passkey-Anfrage ist abgelaufen. Bitte erneut versuchen.');
        }

        $credentialId = self::b64url(self::decodeB64((string)($credential['rawId'] ?? $credential['id'] ?? '')));
        if ($credentialId === '') throw new \RuntimeException('Passkey-Credential-ID fehlt.');
        $stmt = Database::connection()->prepare(
            'SELECT * FROM webauthn_credentials WHERE user_id=? AND credential_id_b64=? LIMIT 1'
        );
        $stmt->execute([$userId,$credentialId]);
        $stored = $stmt->fetch();
        if (!$stored) throw new \RuntimeException('Dieser Passkey gehört nicht zu diesem Konto.');

        $response = is_array($credential['response'] ?? null) ? $credential['response'] : [];
        $clientDataRaw = self::decodeB64((string)($response['clientDataJSON'] ?? ''));
        $authData = self::decodeB64((string)($response['authenticatorData'] ?? ''));
        $signature = self::decodeB64((string)($response['signature'] ?? ''));
        if ($clientDataRaw === '' || strlen($authData) < 37 || $signature === '') {
            throw new \RuntimeException('Unvollständige Passkey-Anmeldeantwort.');
        }

        self::verifyClientData($clientDataRaw, 'webauthn.get', (string)$pending['challenge']);
        $parsed = self::parseAssertionAuthData($authData);
        $signed = $authData . hash('sha256', $clientDataRaw, true);
        $alg = (int)$stored['algorithm'];
        $opensslAlg = match ($alg) {
            -7, -257 => OPENSSL_ALGO_SHA256,
            default => throw new \RuntimeException('Nicht unterstützter Passkey-Algorithmus.'),
        };
        $verify = openssl_verify($signed, $signature, (string)$stored['public_key_pem'], $opensslAlg);
        if ($verify !== 1) throw new \RuntimeException('Passkey-Signatur ist ungültig.');

        $oldCount = max(0,(int)($stored['sign_count'] ?? 0));
        $newCount = max(0,(int)$parsed['sign_count']);
        // Multi-Device-Passkeys verwenden häufig 0. Nur wenn beide Seiten einen
        // echten Counter liefern, muss er streng wachsen.
        if ($oldCount > 0 && $newCount > 0 && $newCount <= $oldCount) {
            Audit::log('auth.passkey.counter_anomaly','user',(string)$userId,[
                'credential_id'=>(int)$stored['id'],'old'=>$oldCount,'new'=>$newCount,
            ]);
            throw new \RuntimeException('Passkey-Signaturzähler ist nicht plausibel. Bitte einen anderen Passkey verwenden.');
        }

        Database::withBusyRetry(static function () use ($newCount,$stored): void {
            Database::connection()->prepare('UPDATE webauthn_credentials SET sign_count=?,last_used_at=? WHERE id=?')
                ->execute([$newCount,date('c'),(int)$stored['id']]);
        }, 4);
        return ['credential_id'=>(int)$stored['id'],'label'=>(string)$stored['label']];
    }

    public static function deleteCredential(int $userId, int $credentialId): bool
    {
        $stmt = Database::connection()->prepare('DELETE FROM webauthn_credentials WHERE id=? AND user_id=?');
        $stmt->execute([$credentialId,$userId]);
        return $stmt->rowCount() === 1;
    }

    private static function verifyClientData(string $raw, string $expectedType, string $expectedChallenge): array
    {
        $data = json_decode($raw, true);
        if (!is_array($data)) throw new \RuntimeException('Ungültige WebAuthn clientDataJSON.');
        if (!hash_equals($expectedType, (string)($data['type'] ?? ''))) throw new \RuntimeException('Ungültiger WebAuthn-Vorgangstyp.');
        $challenge = rtrim(strtr((string)($data['challenge'] ?? ''), '+/', '-_'), '=');
        if (!hash_equals(rtrim($expectedChallenge,'='), $challenge)) throw new \RuntimeException('WebAuthn-Challenge stimmt nicht überein.');
        if (!hash_equals(self::origin(), strtolower(rtrim((string)($data['origin'] ?? ''), '/')))) {
            throw new \RuntimeException('WebAuthn-Origin stimmt nicht mit der Serverkonfiguration überein.');
        }
        if (!empty($data['crossOrigin'])) throw new \RuntimeException('Cross-Origin-WebAuthn ist nicht erlaubt.');
        return $data;
    }

    private static function parseRegistrationAuthData(string $authData): array
    {
        if (strlen($authData) < 55) throw new \RuntimeException('WebAuthn authenticatorData ist zu kurz.');
        self::verifyRpAndFlags($authData, true);
        $signCount = unpack('N', substr($authData,33,4))[1];
        $offset = 37;
        $aaguidRaw = substr($authData,$offset,16); $offset += 16;
        $credLen = unpack('n', substr($authData,$offset,2))[1]; $offset += 2;
        if ($credLen <= 0 || strlen($authData) < $offset + $credLen + 1) throw new \RuntimeException('Ungültige Passkey-Credential-Länge.');
        $credentialId = substr($authData,$offset,$credLen); $offset += $credLen;
        $cose = WebAuthnCbor::decodeAt($authData,$offset);
        if (!is_array($cose)) throw new \RuntimeException('Passkey-COSE-Schlüssel fehlt.');
        return [
            'sign_count'=>$signCount,
            'credential_id'=>$credentialId,
            'cose_key'=>$cose,
            'aaguid'=>self::uuidFromBytes($aaguidRaw),
        ];
    }

    private static function parseAssertionAuthData(string $authData): array
    {
        self::verifyRpAndFlags($authData, false);
        return ['sign_count'=>unpack('N', substr($authData,33,4))[1]];
    }

    private static function verifyRpAndFlags(string $authData, bool $requireAttestedData): void
    {
        if (strlen($authData) < 37) throw new \RuntimeException('WebAuthn authenticatorData ist zu kurz.');
        $expected = hash('sha256', self::rpId(), true);
        if (!hash_equals($expected, substr($authData,0,32))) throw new \RuntimeException('WebAuthn-RP-ID stimmt nicht überein.');
        $flags = ord($authData[32]);
        if (($flags & 0x01) === 0) throw new \RuntimeException('Passkey hat keine Benutzeranwesenheit bestätigt.');
        if (($flags & 0x04) === 0) throw new \RuntimeException('Passkey-Benutzerverifizierung (PIN/Biometrie) fehlt.');
        if ($requireAttestedData && ($flags & 0x40) === 0) throw new \RuntimeException('Passkey enthält keine Credential-Daten.');
    }

    private static function coseToPem(array $cose): array
    {
        $kty = (int)($cose[1] ?? 0);
        $alg = (int)($cose[3] ?? 0);
        if ($kty === 2 && $alg === -7) {
            $crv = (int)($cose[-1] ?? 0);
            $x = $cose[-2] ?? null;
            $y = $cose[-3] ?? null;
            if ($crv !== 1 || !is_string($x) || !is_string($y) || strlen($x)!==32 || strlen($y)!==32) {
                throw new \RuntimeException('Ungültiger ES256-Passkey-Schlüssel.');
            }
            $algId = hex2bin('301306072A8648CE3D020106082A8648CE3D030107');
            $point = "\x04".$x.$y;
            $spki = self::derSequence($algId . self::derBitString($point));
            return [self::pem('PUBLIC KEY',$spki),$alg];
        }
        if ($kty === 3 && $alg === -257) {
            $n = $cose[-1] ?? null;
            $e = $cose[-2] ?? null;
            if (!is_string($n) || !is_string($e) || $n === '' || $e === '') throw new \RuntimeException('Ungültiger RS256-Passkey-Schlüssel.');
            $rsa = self::derSequence(self::derInteger($n) . self::derInteger($e));
            $algId = hex2bin('300d06092a864886f70d0101010500');
            $spki = self::derSequence($algId . self::derBitString($rsa));
            return [self::pem('PUBLIC KEY',$spki),$alg];
        }
        throw new \RuntimeException('Dieser Passkey verwendet einen nicht unterstützten Schlüsseltyp. Unterstützt: ES256 und RS256.');
    }

    private static function derSequence(string $data): string { return "\x30".self::derLength(strlen($data)).$data; }
    private static function derBitString(string $data): string { $v="\x00".$data; return "\x03".self::derLength(strlen($v)).$v; }
    private static function derInteger(string $data): string
    {
        $data = ltrim($data, "\x00");
        if ($data === '') $data = "\x00";
        if ((ord($data[0]) & 0x80) !== 0) $data = "\x00".$data;
        return "\x02".self::derLength(strlen($data)).$data;
    }
    private static function derLength(int $length): string
    {
        if ($length < 128) return chr($length);
        $bytes=''; $v=$length;
        while ($v>0) { $bytes=chr($v&0xff).$bytes; $v >>= 8; }
        return chr(0x80|strlen($bytes)).$bytes;
    }
    private static function pem(string $type,string $der): string
    {
        return "-----BEGIN {$type}-----\n".chunk_split(base64_encode($der),64,"\n")."-----END {$type}-----\n";
    }

    private static function cleanLabel(string $label): string
    {
        $label = trim(preg_replace('/\s+/u',' ',strip_tags($label)) ?? '');
        if ($label === '') $label = 'Passkey ' . date('d.m.Y H:i');
        return mb_substr($label,0,80,'UTF-8');
    }
    private static function randomB64(int $bytes): string { return self::b64url(random_bytes($bytes)); }
    public static function b64url(string $raw): string { return rtrim(strtr(base64_encode($raw),'+/','-_'),'='); }
    public static function decodeB64(string $value): string
    {
        $value = trim($value);
        if ($value === '') return '';
        $normalized = strtr($value,'-_','+/');
        $pad = strlen($normalized)%4;
        if ($pad) $normalized .= str_repeat('=',4-$pad);
        $decoded = base64_decode($normalized,true);
        return $decoded === false ? '' : $decoded;
    }
    private static function uuidFromBytes(string $bytes): string
    {
        if (strlen($bytes)!==16) return '';
        $h=bin2hex($bytes);
        return substr($h,0,8).'-'.substr($h,8,4).'-'.substr($h,12,4).'-'.substr($h,16,4).'-'.substr($h,20,12);
    }
}

/** Minimaler CBOR-Decoder für WebAuthn attestationObject/COSE_Key. */
final class WebAuthnCbor
{
    public static function decode(string $data): mixed
    {
        $offset=0;
        $value=self::item($data,$offset);
        return $value;
    }
    public static function decodeAt(string $data,int &$offset): mixed { return self::item($data,$offset); }

    private static function item(string $data,int &$offset): mixed
    {
        if ($offset>=strlen($data)) throw new \RuntimeException('CBOR unerwartet beendet.');
        $initial=ord($data[$offset++]);
        $major=$initial>>5; $ai=$initial&0x1f;
        if ($ai===31) throw new \RuntimeException('Indefinite CBOR-Längen werden nicht unterstützt.');
        $length=self::length($data,$offset,$ai);
        return match($major){
            0=>$length,
            1=>-1-$length,
            2=>self::bytes($data,$offset,$length),
            3=>self::text($data,$offset,$length),
            4=>self::array($data,$offset,$length),
            5=>self::map($data,$offset,$length),
            6=>self::item($data,$offset),
            7=>self::simple($ai,$length),
            default=>throw new \RuntimeException('Unbekannter CBOR-Typ.'),
        };
    }
    private static function length(string $data,int &$offset,int $ai): int
    {
        if($ai<24)return $ai;
        $size=match($ai){24=>1,25=>2,26=>4,27=>8,default=>throw new \RuntimeException('Ungültige CBOR-Länge.')};
        if(strlen($data)<$offset+$size)throw new \RuntimeException('CBOR-Länge abgeschnitten.');
        $chunk=substr($data,$offset,$size);$offset+=$size;
        if($size===1)return ord($chunk);
        if($size===2)return unpack('n',$chunk)[1];
        if($size===4)return unpack('N',$chunk)[1];
        $parts=unpack('Nhi/Nlo',$chunk);
        $value=((int)$parts['hi']<<32)|(int)$parts['lo'];
        if($value<0)throw new \RuntimeException('CBOR-Wert zu groß.');
        return $value;
    }
    private static function bytes(string $data,int &$offset,int $len): string
    {
        if($len<0||strlen($data)<$offset+$len)throw new \RuntimeException('CBOR-Bytefolge abgeschnitten.');
        $v=substr($data,$offset,$len);$offset+=$len;return $v;
    }
    private static function text(string $data,int &$offset,int $len): string { return self::bytes($data,$offset,$len); }
    private static function array(string $data,int &$offset,int $len): array
    {
        $out=[];for($i=0;$i<$len;$i++)$out[]=self::item($data,$offset);return $out;
    }
    private static function map(string $data,int &$offset,int $len): array
    {
        $out=[];for($i=0;$i<$len;$i++){$k=self::item($data,$offset);$v=self::item($data,$offset);if(!is_int($k)&&!is_string($k))throw new \RuntimeException('Ungültiger CBOR-Map-Key.');$out[$k]=$v;}return $out;
    }
    private static function simple(int $ai,int $length): mixed
    {
        return match($ai){20=>false,21=>true,22,23=>null,default=>$length};
    }
}
