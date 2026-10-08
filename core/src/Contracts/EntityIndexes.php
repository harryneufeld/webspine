<?php
declare(strict_types=1);
namespace Webspine\Contracts;

/** Optional index declarations; providers own physical indexes and installation. */
interface EntityIndexes extends Entities {
    public function defineIndex(string $name, array $fields): void;
}
