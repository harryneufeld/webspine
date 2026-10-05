<?php
declare(strict_types=1);
namespace Webspine;
final class Router {
    private array $routes = [];
    private array $patterns = [];
    public function get(string $path, callable $handler): void {
        $this->add('GET', $path, $handler);
    }
    public function post(string $path, callable $handler): void { $this->add('POST', $path, $handler); }
    private function add(string $method, string $path, callable $handler): void {
        if (isset($this->routes[$path][$method])) throw new \LogicException('Duplicate route: ' . $method . ' ' . $path);
        $this->routes[$path][$method] = $handler;
    }
    public function dispatch(string $method, string $path, ?Request $request = null): ?Response {
        $method = strtoupper($method);
        $request ??= new Request($method, $path);
        if ($request->method !== $method || $request->path !== $path) throw new \InvalidArgumentException('Request does not match dispatch method/path.');
        $effective = $method === 'HEAD' ? 'GET' : $method;
        $allowed = [];
        if (isset($this->routes[$path])) {
            $allowed = array_keys($this->routes[$path]);
            if (isset($this->routes[$path][$effective])) return ($this->routes[$path][$effective])($request);
        }
        foreach ($this->patterns as [$registeredMethod, $pattern, $handler]) {
            if (preg_match($pattern, $path, $matches)) {
                $allowed[] = $registeredMethod;
                if ($registeredMethod === $effective) return $handler($matches, $request);
            }
        }
        if ($allowed) {
            if (in_array('GET', $allowed, true)) $allowed[] = 'HEAD';
            $allowed = array_values(array_intersect(['GET','HEAD','POST'], array_unique($allowed)));
            return new Response('Method not allowed', 405, ['Allow'=>implode(', ', $allowed)]);
        }
        if (!in_array($method, ['GET','HEAD','POST'], true)) return new Response('Method not allowed', 405, ['Allow'=>'GET, HEAD']);
        return null;
    }
    /** Trusted site/plugin regex; handlers may return null to use the site's 404. */
    public function getPattern(string $pattern, callable $handler): void {
        $this->pattern('GET', $pattern, $handler);
    }
    public function postPattern(string $pattern, callable $handler): void { $this->pattern('POST', $pattern, $handler); }
    private function pattern(string $method, string $pattern, callable $handler): void {
        if (@preg_match($pattern, '') === false) throw new \InvalidArgumentException('Invalid route pattern.');
        $this->patterns[] = [$method, $pattern, $handler];
    }
}
