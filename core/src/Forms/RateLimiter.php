<?php
declare(strict_types=1);
namespace Webspine\Forms;
use Webspine\Files;
/** Locked fixed-window counters in one private JSON file; subjects are stored only as salted hashes. */
final class RateLimiter {
    private const MAX_BYTES = 1048576;
    private string $file;
    public function __construct(string $root, string $relative, private int $maxKeys = 4096) {
        if ($maxKeys < 1) throw new \InvalidArgumentException('Configure at least one limiter key.');
        $this->file = Files::target($root, $relative);
    }
    /**
     * True when every bucket is below its limit, then counts the call in all of them.
     * A refused call counts nowhere, so one exhausted bucket does not burn the others.
     * @param list<array{kind:string,subject:string,max:int,window:int}> $buckets
     */
    public function allow(array $buckets): bool {
        if (!$buckets) throw new \InvalidArgumentException('Configure at least one limiter bucket.');
        foreach ($buckets as $bucket) {
            if (!is_string($bucket['kind'] ?? null) || !preg_match('/^[a-z][a-z0-9_-]{0,39}$/D', $bucket['kind']) || !is_string($bucket['subject'] ?? null)
                || !is_int($bucket['max'] ?? null) || $bucket['max'] < 1 || !is_int($bucket['window'] ?? null) || $bucket['window'] < 1) throw new \InvalidArgumentException('Invalid limiter bucket.');
        }
        $directory = dirname($this->file);
        if (!is_dir($directory) && !mkdir($directory, 0700, true) && !is_dir($directory)) throw new \RuntimeException('Cannot create private limiter.');
        $mask = umask(0077);
        try { $file = fopen($this->file, 'c+'); } finally { umask($mask); }
        if (!$file) throw new \RuntimeException('Cannot open limiter.');
        if (!flock($file, LOCK_EX)) { fclose($file); throw new \RuntimeException('Cannot lock limiter.'); }
        try {
            $raw = stream_get_contents($file, self::MAX_BYTES + 1);
            if ($raw === false || strlen($raw) > self::MAX_BYTES) throw new \RuntimeException('Invalid limiter state.');
            $dirty = $raw === '';
            $state = $raw === '' ? ['salt' => bin2hex(random_bytes(32)), 'keys' => []] : json_decode($raw, true, 16, JSON_THROW_ON_ERROR);
            if (!is_array($state) || !is_string($state['salt'] ?? null) || !preg_match('/^[a-f0-9]{64}$/D', $state['salt']) || !is_array($state['keys'] ?? null)) throw new \RuntimeException('Invalid limiter state.');
            $now = time();
            foreach ($state['keys'] as $key => $record) {
                if (!is_array($record) || !is_int($record['until'] ?? null) || !is_int($record['count'] ?? null) || $record['count'] < 0) throw new \RuntimeException('Invalid limiter record.');
                if ($record['until'] <= $now) { unset($state['keys'][$key]); $dirty = true; }
            }
            $allowed = true; $added = 0; $records = [];
            foreach ($buckets as $bucket) {
                $key = hash_hmac('sha256', $bucket['kind'] . ':' . $bucket['subject'], $state['salt']);
                if (!isset($state['keys'][$key])) $added++;
                $record = $state['keys'][$key] ?? ['until' => $now + $bucket['window'], 'count' => 0];
                if ($record['count'] >= $bucket['max']) $allowed = false;
                $record['count']++;
                $records[$key] = $record;
            }
            // Fail closed when full: known subjects continue, new ones are refused.
            if ($added > 0 && count($state['keys']) + $added > $this->maxKeys) $allowed = false;
            if ($allowed) { $state['keys'] = $records + $state['keys']; $dirty = true; }
            if ($dirty) {
                $body = json_encode($state, JSON_THROW_ON_ERROR);
                rewind($file);
                if (!ftruncate($file, 0) || fwrite($file, $body) !== strlen($body) || !fflush($file)) throw new \RuntimeException('Cannot persist limiter.');
            }
            return $allowed;
        } finally { flock($file, LOCK_UN); fclose($file); }
    }
    /** Limiter subject for a peer address: IPv4 as-is, IPv4-mapped as IPv4, IPv6 by /64; null when invalid. */
    public static function client(?string $address): ?string {
        if ($address === null || !filter_var($address, FILTER_VALIDATE_IP)) return null;
        $packed = inet_pton($address);
        if ($packed === false) return null;
        if (strlen($packed) === 4) return $address;
        if (str_starts_with($packed, str_repeat("\0", 10) . "\xff\xff")) return (string) inet_ntop(substr($packed, 12));
        return bin2hex(substr($packed, 0, 8)) . '::/64';
    }
}
