<?php
declare(strict_types=1);
namespace Webspine;
use Webspine\Contracts\Plugin;
final class Plugins {
    public array $loaded = [];
    private array $visiting = [];
    public function __construct(private App $app) {}
    private function directory(string $id): string {
        if (!preg_match('/^[a-z][a-z0-9-]*$/D', $id)) throw new \RuntimeException('Invalid plugin identity.');
        $system = $this->app->coreRoot . '/plugins/' . $id;
        $custom = $this->app->root . '/site/plugins/' . $id;
        if (is_dir($system) && is_dir($custom)) throw new \RuntimeException('Ambiguous plugin identity; choose a unique custom ID: ' . $id);
        if (is_dir($system)) return $system;
        if (is_dir($custom)) return $custom;
        throw new \RuntimeException('Plugin not found: ' . $id);
    }
    public function manifest(string $id): array {
        if (!preg_match('/^[a-z][a-z0-9-]*$/D', $id)) throw new \RuntimeException('Invalid plugin identity.');
        $path = $this->directory($id) . '/plugin.json';
        $m = json_decode(file_get_contents($path), true, 32, JSON_THROW_ON_ERROR);
        if (($m['id'] ?? null) !== $id || !preg_match('/^\d+\.\d+\.\d+$/D', $m['version'] ?? '') || ($m['api'] ?? null) !== $this->app->version['api'] || !is_array($m['dependencies'] ?? null)) {
            throw new \RuntimeException('Incompatible plugin manifest: ' . $id);
        }
        return $m;
    }
    public function load(string $id, ?string $provider = null): void {
        if (isset($this->loaded[$id])) return;
        if (isset($this->visiting[$id])) throw new \RuntimeException('Circular plugin dependency: ' . $id);
        $m = $this->manifest($id);
        if ($provider !== null && ($m['provider'] ?? null) !== $provider) throw new \RuntimeException('Selected plugin does not declare the provider role: ' . $id);
        if (isset($m['provider']) && $m['provider'] !== $provider) throw new \RuntimeException('Provider must be explicitly selected: ' . $id);
        $this->visiting[$id] = true;
        foreach ($m['dependencies'] as $dep => $minimum) {
            if (!is_string($dep) || !is_string($minimum) || !preg_match('/^\d+\.\d+\.\d+$/D', $minimum)) throw new \RuntimeException('Invalid dependency declaration.');
            $dm = $this->manifest($dep);
            if (version_compare($dm['version'], $minimum, '<')) throw new \RuntimeException('Unsatisfied dependency: ' . $dep);
            $this->load($dep);
        }
        $plugin = require $this->directory($id) . '/plugin.php';
        if (!$plugin instanceof Plugin) throw new \RuntimeException('Plugin entry must implement Plugin.');
        $plugin->register($this->app);
        $this->loaded[$id] = $m;
        unset($this->visiting[$id]);
    }
}
