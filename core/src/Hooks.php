<?php
declare(strict_types=1);
namespace Webspine;
final class Hooks {
    private array $actions = [];
    public function on(string $event, callable $handler): void { $this->actions[$event][] = $handler; }
    public function fire(string $event, mixed ...$args): void {
        foreach ($this->actions[$event] ?? [] as $handler) $handler(...$args);
    }
}
