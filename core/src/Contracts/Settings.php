<?php
declare(strict_types=1);
namespace Webspine\Contracts;
interface Settings {
    public function get(string $key, ?string $default = null): ?string;
    public function set(string $key, string $value): void;
}
