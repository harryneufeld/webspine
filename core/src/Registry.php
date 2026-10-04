<?php
declare(strict_types=1);
namespace Webspine;
final class Registry {
    private array $services = [];
    public function set(string $contract, object $service): void {
        if (!interface_exists($contract) || !($service instanceof $contract)) {
            throw new \InvalidArgumentException('Service must implement its declared interface.');
        }
        if (isset($this->services[$contract])) throw new \LogicException('Service already registered: ' . $contract);
        $this->services[$contract] = $service;
    }
    public function get(string $contract): object {
        return $this->services[$contract] ?? throw new \RuntimeException('Service unavailable: ' . $contract);
    }
    public function has(string $contract): bool { return isset($this->services[$contract]); }
}
