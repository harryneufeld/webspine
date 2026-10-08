<?php
declare(strict_types=1);
namespace Webspine;
final class Router {
    private array $routes = [];
    private array $patterns = [];
    private array $pagePaths = [];
    public function __construct(private ?VisitorErrors $visitorErrors = null) {}
    private function methodError(array $headers):Response {
        $response=new Response('Method not allowed',405,$headers);
        return $this->visitorErrors?->present($response)??$response;
    }
    public function get(string $path, callable $handler): void {
        $this->add('GET', $path, $handler);
    }
    public function post(string $path, callable $handler): void { $this->add('POST', $path, $handler); }
    /** Opt-in page routes; slash aliases redirect GET/HEAD only. */
    public function page(string $path, callable $handler, string $slash = 'preserve'): void {
        if (!preg_match('#^/[A-Za-z0-9._~/-]*$#D', $path) || str_contains($path, '//')
            || preg_match('#/(?:\.|\.\.)(?:/|$)#', $path) || $path === '/assets' || str_starts_with($path, '/assets/')) throw new \InvalidArgumentException('Invalid page route path.');
        if (!in_array($slash, ['preserve','strip','append'], true)) throw new \InvalidArgumentException('Invalid page slash policy.');
        if ($path !== '/' && ($slash === 'strip' && str_ends_with($path, '/') || $slash === 'append' && !str_ends_with($path, '/'))) throw new \InvalidArgumentException('Page path must match its canonical slash policy.');
        $alias = $path === '/' || $slash === 'preserve' ? null : ($slash === 'strip' ? $path . '/' : substr($path, 0, -1));
        $paths = $alias === null ? [$path] : [$path, $alias];
        foreach ($paths as $candidate) {
            if (isset($this->routes[$candidate]['GET'])) throw new \LogicException('Duplicate page route: ' . $candidate);
            foreach ($this->patterns as [$method,$pattern]) if ($method === 'GET' && preg_match($pattern, $candidate)) throw new \LogicException('Page route conflicts with GET pattern: ' . $candidate);
        }
        $this->get($path, $handler);
        if ($alias !== null) $this->get($alias, static function(Request $request) use ($path):Response {
            $query = strpos($request->uri, '?');
            return Response::redirect($path . ($query === false ? '' : substr($request->uri, $query)), 308);
        });
        foreach ($paths as $candidate) $this->pagePaths[$candidate] = true;
    }
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
            return $this->methodError(['Allow'=>implode(', ', $allowed)]);
        }
        if (!in_array($method, ['GET','HEAD','POST'], true)) return $this->methodError(['Allow'=>'GET, HEAD']);
        return null;
    }
    /** Trusted site/plugin regex; handlers may return null to use the site's 404. */
    public function getPattern(string $pattern, callable $handler): void {
        $this->pattern('GET', $pattern, $handler);
    }
    public function postPattern(string $pattern, callable $handler): void { $this->pattern('POST', $pattern, $handler); }
    private function pattern(string $method, string $pattern, callable $handler): void {
        if (@preg_match($pattern, '') === false) throw new \InvalidArgumentException('Invalid route pattern.');
        if ($method === 'GET') foreach (array_keys($this->pagePaths) as $path) {
            if (preg_match($pattern, $path)) throw new \LogicException('GET pattern conflicts with page route: ' . $path);
        }
        $this->patterns[] = [$method, $pattern, $handler];
    }
}
