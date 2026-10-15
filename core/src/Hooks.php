<?php
declare(strict_types=1);
namespace Webspine;
final class Hooks {
    private array $actions = [];
    public function on(string $event, callable $handler): void { $this->actions[$event][] = $handler; }
    /** Snapshot listeners; registrations during dispatch apply to the next dispatch. */
    public function listeners(string $event): array { return $this->actions[$event] ?? []; }
    public function fire(string $event, mixed ...$args): void {
        foreach ($this->actions[$event] ?? [] as $handler) $handler(...$args);
    }
}
