<?php
declare(strict_types=1);
namespace Webspine;
final class Router {
    private array $routes = [];
    private array $patterns = [];
    public function get(string $path, callable $handler): void {
        if (isset($this->routes[$path])) throw new \LogicException('Duplicate route: ' . $path);
        $this->routes[$path] = $handler;
    }
    public function dispatch(string $method, string $path): ?Response {
        if (!in_array($method, ['GET', 'HEAD'], true)) return new Response('Method not allowed', 405, ['Allow' => 'GET, HEAD']);
        if (isset($this->routes[$path])) return ($this->routes[$path])();
        foreach ($this->patterns as [$pattern, $handler]) {
            if (preg_match($pattern, $path, $matches)) return $handler($matches);
        }
        return null;
    }
    /** Trusted site/plugin regex; handlers may return null to use the site's 404. */
    public function getPattern(string $pattern, callable $handler): void {
        if (@preg_match($pattern, '') === false) throw new \InvalidArgumentException('Invalid route pattern.');
        $this->patterns[] = [$pattern, $handler];
    }
}
