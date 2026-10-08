<?php
declare(strict_types=1);
namespace Webspine\Contracts;
use Webspine\{EntityFilter,EntityQuery};

/** Optional capability on the existing Entities service; no fallback row scanning. */
interface EntityQueries extends Entities {
    public function search(string $name, ?EntityQuery $query = null, int $limit = 50, int $offset = 0): array;
    public function count(string $name, ?EntityFilter $filter = null): int;
}
