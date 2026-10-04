<?php
declare(strict_types=1);
namespace Webspine\Providers;
use Webspine\Contracts\Entities;
use Webspine\EntityDefinition;

final class SQLiteEntities implements Entities {
    private array $definitions = [];
    public function __construct(private \Closure $connection) {}
    public function define(string $name, array $fields): void {
        if (isset($this->definitions[$name])) throw new \LogicException('Entity already defined: ' . $name);
        $this->definitions[$name] = new EntityDefinition($name, $fields);
    }
    private function db(): \PDO { return ($this->connection)(); }
    private function transaction(callable $action): mixed {
        $db = $this->db(); $db->exec('BEGIN IMMEDIATE');
        try { $result = $action($db); $db->exec('COMMIT'); return $result; }
        catch (\Throwable $e) { $db->exec('ROLLBACK'); throw $e; }
    }
    public function install(): void {
        $this->transaction(function (\PDO $db): void {
            $db->exec('CREATE TABLE IF NOT EXISTS entity_schema_versions (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)');
            $versions = $db->query('SELECT version FROM entity_schema_versions ORDER BY version')->fetchAll(\PDO::FETCH_COLUMN);
            if ($versions && $versions !== [1]) throw new \RuntimeException('Unsupported entity storage schema.');
            if (!$versions) {
                $db->exec('CREATE TABLE entity_definitions (name TEXT PRIMARY KEY, definition TEXT NOT NULL)');
                $db->exec('CREATE TABLE entity_records (id INTEGER PRIMARY KEY AUTOINCREMENT, entity TEXT NOT NULL REFERENCES entity_definitions(name), data TEXT NOT NULL, revision INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL)');
                $db->exec('CREATE INDEX entity_records_by_entity ON entity_records(entity, id)');
                $db->prepare('INSERT INTO entity_schema_versions VALUES (1, ?)')->execute([gmdate('c')]);
            }
            foreach ($this->definitions as $name => $definition) {
                $q = $db->prepare('SELECT definition FROM entity_definitions WHERE name = ?'); $q->execute([$name]);
                $existing = $q->fetchColumn();
                if ($existing !== false && $existing !== $definition->json()) {
                    $records = $db->prepare('SELECT id FROM entity_records WHERE entity = ? LIMIT 1'); $records->execute([$name]);
                    if ($records->fetchColumn() !== false) throw new \RuntimeException('Entity definition changed; explicit data migration required: ' . $name);
                    $db->prepare('UPDATE entity_definitions SET definition = ? WHERE name = ?')->execute([$definition->json(), $name]);
                }
                if ($existing === false) $db->prepare('INSERT INTO entity_definitions VALUES (?, ?)')->execute([$name, $definition->json()]);
            }
        });
    }
    private function definition(string $name): EntityDefinition {
        $definition = $this->definitions[$name] ?? throw new \RuntimeException('Entity is not declared: ' . $name);
        $db = $this->db();
        if (!$db->query("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'entity_schema_versions'")->fetchColumn()) throw new \RuntimeException('Run php core/bin/console.php entities:install before using entities.');
        if ($db->query('SELECT version FROM entity_schema_versions ORDER BY version')->fetchAll(\PDO::FETCH_COLUMN) !== [1]) throw new \RuntimeException('Unsupported entity storage schema.');
        $q = $db->prepare('SELECT definition FROM entity_definitions WHERE name = ?'); $q->execute([$name]);
        $stored = $q->fetchColumn();
        if ($stored === false) throw new \RuntimeException('Entity is not installed; run entities:install: ' . $name);
        if ($stored !== $definition->json()) throw new \RuntimeException('Entity definition changed; run entities:install (populated entities need explicit data migration): ' . $name);
        return $definition;
    }
    public function health(): array {
        try {
            foreach (array_keys($this->definitions) as $name) $this->definition($name);
            if ($this->definitions) $this->db()->query('SELECT id FROM entity_records LIMIT 1');
            return ['ok'=>true, 'entities'=>array_keys($this->definitions)];
        } catch (\Throwable $e) { return ['ok'=>false, 'error'=>$e->getMessage()]; }
    }
    private function identity(int $id): void { if ($id < 1) throw new \InvalidArgumentException('Invalid entity record ID.'); }
    private function record(array $row, EntityDefinition $definition): array {
        $values = json_decode($row['data'], true, 32, JSON_THROW_ON_ERROR);
        return ['id'=>(int)$row['id'], 'entity'=>$row['entity'], 'values'=>$definition->validate($values), 'revision'=>(int)$row['revision'], 'created_at'=>$row['created_at'], 'updated_at'=>$row['updated_at']];
    }
    public function create(string $name, array $values): array {
        $definition = $this->definition($name); $values = $definition->validate($values);
        return $this->transaction(function (\PDO $db) use ($name, $values): array {
            $now = gmdate('c');
            $db->prepare('INSERT INTO entity_records (entity, data, revision, created_at, updated_at) VALUES (?, ?, 1, ?, ?)')->execute([$name, json_encode($values, JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR), $now, $now]);
            return $this->read($name, (int)$db->lastInsertId());
        });
    }
    public function read(string $name, int $id): ?array {
        $definition = $this->definition($name); $this->identity($id);
        $q = $this->db()->prepare('SELECT * FROM entity_records WHERE entity = ? AND id = ?'); $q->execute([$name, $id]);
        $row = $q->fetch(\PDO::FETCH_ASSOC); return $row ? $this->record($row, $definition) : null;
    }
    public function list(string $name, int $limit = 50, int $offset = 0): array {
        $definition = $this->definition($name);
        if ($limit < 1 || $limit > 100 || $offset < 0) throw new \InvalidArgumentException('Invalid entity pagination.');
        $q = $this->db()->prepare('SELECT * FROM entity_records WHERE entity = ? ORDER BY id ASC LIMIT ? OFFSET ?');
        $q->bindValue(1, $name); $q->bindValue(2, $limit, \PDO::PARAM_INT); $q->bindValue(3, $offset, \PDO::PARAM_INT); $q->execute();
        return array_map(fn(array $row) => $this->record($row, $definition), $q->fetchAll(\PDO::FETCH_ASSOC));
    }
    private function revision(array $record, ?int $expected): void {
        if ($expected !== null && $record['revision'] !== $expected) throw new \RuntimeException('Entity revision conflict.');
    }
    public function update(string $name, int $id, array $changes, ?int $expectedRevision = null): ?array {
        $definition = $this->definition($name); $this->identity($id);
        return $this->transaction(function (\PDO $db) use ($name, $id, $changes, $expectedRevision, $definition): ?array {
            $record = $this->read($name, $id); if (!$record) return null;
            $this->revision($record, $expectedRevision);
            $values = $definition->validate(array_replace($record['values'], $changes));
            $db->prepare('UPDATE entity_records SET data = ?, revision = revision + 1, updated_at = ? WHERE entity = ? AND id = ?')->execute([json_encode($values, JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR), gmdate('c'), $name, $id]);
            return $this->read($name, $id);
        });
    }
    public function delete(string $name, int $id, ?int $expectedRevision = null): bool {
        $this->definition($name); $this->identity($id);
        return $this->transaction(function (\PDO $db) use ($name, $id, $expectedRevision): bool {
            $record = $this->read($name, $id); if (!$record) return false;
            $this->revision($record, $expectedRevision);
            $q = $db->prepare('DELETE FROM entity_records WHERE entity = ? AND id = ?'); $q->execute([$name, $id]); return $q->rowCount() === 1;
        });
    }
}
