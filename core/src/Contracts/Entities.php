<?php
declare(strict_types=1);
namespace Webspine\Contracts;

/** Optional provider capability. Definitions are trusted plugin code, never request input. */
interface Entities {
    public function define(string $name, array $fields): void;
    /** Explicitly install infrastructure and declared entities; never called by a request. */
    public function install(): void;
    public function health(): array;
    /** Records contain id, entity, values, revision, created_at, updated_at. */
    public function create(string $name, array $values): array;
    public function read(string $name, int $id): ?array;
    public function list(string $name, int $limit = 50, int $offset = 0): array;
    /** Partial update. Optional expected revision rejects stale writes. */
    public function update(string $name, int $id, array $changes, ?int $expectedRevision = null): ?array;
    public function delete(string $name, int $id, ?int $expectedRevision = null): bool;
}
