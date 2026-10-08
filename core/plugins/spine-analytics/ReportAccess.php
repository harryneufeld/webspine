<?php
declare(strict_types=1);
namespace Webspine\Providers\SpineAnalytics;
final class ReportAccess {
    public function __construct(private mixed $passwordHash = null) {}
    public static function hashPassword(#[\SensitiveParameter] string $password): string {
        if (strlen($password) < 16 || strlen($password) > 72 || str_contains($password, "\0")) throw new \InvalidArgumentException('Use a password of 16 to 72 bytes without NUL characters.');
        return password_hash($password, PASSWORD_DEFAULT);
    }
    public function status(#[\SensitiveParameter] string $authorization, bool $secure): int {
        if (!$secure || !is_string($this->passwordHash) || strlen($this->passwordHash) > 256 || password_get_info($this->passwordHash)['algoName'] === 'unknown') return 503;
        if (strlen($authorization) > 2048 || !preg_match('/^Basic ([A-Za-z0-9+\/=]+)$/iD', $authorization, $match)) return 401;
        $decoded = base64_decode($match[1], true);
        if ($decoded === false || !str_contains($decoded, ':')) return 401;
        [$username, $password] = explode(':', $decoded, 2);
        if (strlen($password) > 72 || str_contains($password, "\0")) return 401;
        $valid = password_verify($password, $this->passwordHash);
        return $valid && hash_equals('analytics', $username) ? 200 : 401;
    }
}
