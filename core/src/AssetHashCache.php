<?php
declare(strict_types=1);
namespace Webspine;

/** Disposable private cache; metadata is an optimization, not content identity. */
final class AssetHashCache {
    private array $entries = [];
    private bool $loaded = false;
    private array $stats = ['hashes'=>0, 'bytes'=>0, 'hits'=>0];
    private readonly string $path;
    private readonly string $scope;
    public function __construct(string $root, string $version, private bool $enabled) {
        $this->path = $root . '/storage/theme-asset-hashes.json';
        $this->scope = hash('sha256', (realpath($root) ?: $root) . "\0" . $version);
    }
    public function stats(): array { return $this->stats; }
    public static function stamp(string $file): ?array {
        clearstatcache(true, $file);
        $stat = @stat($file);
        return $stat === false ? null : array_intersect_key($stat, array_flip(['dev','ino','mode','size','mtime','ctime']));
    }
    private function load(): void {
        if ($this->loaded) return;
        $this->loaded = true;
        if (!$this->enabled || is_link($this->path) || !is_file($this->path) || @filesize($this->path) > 262144) return;
        $raw = @file_get_contents($this->path);
        if ($raw === false) return;
        try { $cache = json_decode($raw, true, 8, JSON_THROW_ON_ERROR); }
        catch (\JsonException) { return; }
        if (is_array($cache) && ($cache['scope'] ?? null) === $this->scope
            && is_array($cache['entries'] ?? null) && count($cache['entries']) <= 256) $this->entries = $cache['entries'];
    }
    public function hash(string $file): string {
        $this->load();
        $stamp = self::stamp($file);
        $key = hash('sha256', $file);
        $entry = $this->entries[$key] ?? null;
        if ($this->enabled && is_array($entry) && $stamp !== null && ($entry['stamp'] ?? null) === $stamp
            && is_int($entry['time'] ?? null) && $entry['time'] <= time() && $entry['time'] > time() - 600
            && is_string($entry['hash'] ?? null) && preg_match('/^[a-f0-9]{64}$/D', $entry['hash'])) {
            $this->stats['hits']++;
            return $entry['hash'];
        }
        $hash = @hash_file('sha256', $file);
        if ($hash === false) throw new \RuntimeException('Cannot read theme asset.');
        $this->stats['hashes']++;
        $this->stats['bytes'] += $stamp['size'] ?? 0;
        if ($this->enabled && $stamp !== null && $stamp === self::stamp($file)) {
            unset($this->entries[$key]);
            $this->entries[$key] = ['stamp'=>$stamp, 'time'=>time(), 'hash'=>$hash];
            while (count($this->entries) > 256) array_shift($this->entries);
            $this->save();
        }
        return $hash;
    }
    private function save(): void {
        if (is_link($this->path) || !is_dir(dirname($this->path))) return;
        $json = json_encode(['scope'=>$this->scope, 'entries'=>$this->entries], JSON_THROW_ON_ERROR);
        $temporary = $this->path . '.' . bin2hex(random_bytes(8)) . '.tmp';
        $handle = @fopen($temporary, 'x');
        if ($handle === false) return; // Read-only deployments still hash correctly.
        @chmod($temporary, 0600);
        $written = @fwrite($handle, $json); fclose($handle);
        if ($written !== strlen($json) || !@rename($temporary, $this->path)) @unlink($temporary);
    }
    public function clear(): void {
        $this->entries = []; $this->loaded = true;
        if (!$this->enabled) return;
        if (is_link($this->path)) throw new \RuntimeException('Refusing linked theme hash cache; remove it manually.');
        if (is_file($this->path) && !@unlink($this->path)) throw new \RuntimeException('Cannot clear theme hash cache; remove storage/theme-asset-hashes.json before rendering.');
    }
}
