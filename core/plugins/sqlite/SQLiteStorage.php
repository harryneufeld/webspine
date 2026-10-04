<?php
declare(strict_types=1);
namespace Webspine\Providers;
use Webspine\Contracts\{Storage, Settings, Pages};
final class SQLiteStorage implements Storage, Settings, Pages {
    private ?\PDO $db = null;
    private ?SQLiteEntities $entities = null;
    public function __construct(private string $path) {}
    private function db(bool $install = false): \PDO {
        if ($this->db) return $this->db;
        if (!$install && !is_file($this->path)) throw new \RuntimeException('Site is not installed. Run php core/bin/console.php install.');
        if ($install && !is_dir(dirname($this->path)) && !mkdir(dirname($this->path), 0700, true)) throw new \RuntimeException('Cannot create storage.');
        $db = new \PDO('sqlite:' . $this->path, null, null, [\PDO::ATTR_ERRMODE => \PDO::ERRMODE_EXCEPTION]);
        $db->exec('PRAGMA busy_timeout = 5000');
        $db->exec('PRAGMA foreign_keys = ON');
        $this->db = $db;
        return $db;
    }
    public function install(): void {
        $db = $this->db(true);
        $db->exec('BEGIN IMMEDIATE');
        try {
            $db->exec('CREATE TABLE IF NOT EXISTS schema_versions (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)');
            $versions = $db->query('SELECT version FROM schema_versions ORDER BY version')->fetchAll(\PDO::FETCH_COLUMN);
            if ($versions && $versions !== [1]) throw new \RuntimeException('Unsupported schema; use provider-specific migration tooling.');
            if (!$versions) {
                $db->exec('CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL)');
                $db->exec('CREATE TABLE pages (slug TEXT PRIMARY KEY, title TEXT NOT NULL, body TEXT NOT NULL)');
                $db->prepare('INSERT INTO schema_versions VALUES (1, ?)')->execute([gmdate('c')]);
            }
            $db->exec('COMMIT');
        } catch (\Throwable $e) { $db->exec('ROLLBACK'); throw $e; }
    }
    public function health(): array {
        try {
            $db = $this->db();
            $versions = $db->query('SELECT version FROM schema_versions ORDER BY version')->fetchAll(\PDO::FETCH_COLUMN);
            return ['ok' => $versions === [1] && $db->query('PRAGMA quick_check')->fetchColumn() === 'ok', 'provider' => 'sqlite', 'schema' => $versions];
        } catch (\Throwable $e) { return ['ok' => false, 'error' => $e->getMessage()]; }
    }
    public function settings(): Settings { return $this; }
    public function pages(): Pages { return $this; }
    public function entities(): \Webspine\Contracts\Entities {
        require_once __DIR__ . '/SQLiteEntities.php';
        return $this->entities ??= new SQLiteEntities(fn() => $this->db());
    }
    public function get(string $key, ?string $default = null): ?string {
        $q = $this->db()->prepare('SELECT value FROM settings WHERE key = ?'); $q->execute([$key]);
        $v = $q->fetchColumn(); return $v === false ? $default : $v;
    }
    public function set(string $key, string $value): void {
        $q = $this->db()->prepare('INSERT INTO settings (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value'); $q->execute([$key, $value]);
    }
    public function find(string $slug): ?array {
        $q = $this->db()->prepare('SELECT slug, title, body FROM pages WHERE slug = ?'); $q->execute([$slug]); return $q->fetch(\PDO::FETCH_ASSOC) ?: null;
    }
    public function put(string $slug, string $title, string $body): void {
        if (!preg_match('/^[a-z0-9]+(?:-[a-z0-9]+)*$/D', $slug) || trim($title) === '' || strlen($title) > 300 || strlen($body) > 1000000) throw new \InvalidArgumentException('Invalid page content.');
        $q = $this->db()->prepare('INSERT INTO pages VALUES (?, ?, ?) ON CONFLICT(slug) DO UPDATE SET title = excluded.title, body = excluded.body'); $q->execute([$slug, $title, $body]);
    }
}
