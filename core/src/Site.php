<?php
declare(strict_types=1);
namespace Webspine;

/** Loads site-owned definitions. PHP site files are trusted executable code. */
final class Site {
    public array $meta;
    public array $pages;
    private array $content = [];

    public function __construct(private App $app) {
        $this->meta = $this->load('meta');
        $this->meta['language'] = VisitorErrors::language(array_key_exists('language',$this->meta) ? $this->meta['language'] : 'en');
        $this->pages = $this->load('pages');
    }

    private function load(string $name): array {
        $path = $this->app->root . '/site/' . $name . '.php';
        if (!is_file($path)) throw new \RuntimeException('Missing site definition: ' . $name);
        $value = require $path;
        if (!is_array($value)) throw new \RuntimeException('Site definition must return an array.');
        return $value;
    }

    public function content(string $name): array {
        if (!preg_match('/^[a-z][a-z0-9-]*$/D', $name)) throw new \InvalidArgumentException('Invalid content identity.');
        return $this->content[$name] ??= $this->load('content/' . $name);
    }

    public function render(string $id): Response {
        $page = $this->pages[$id] ?? throw new \RuntimeException('Unknown site page: ' . $id);
        $data = $page['data'] ?? [];
        $data['title'] = $page['title'];
        $data['page'] = $id;
        return $this->app->theme->render($page['template'], $data, $page['status'] ?? 200);
    }

    public function register(): void {
        $register = require $this->app->root . '/site/routes.php';
        if (!is_callable($register)) throw new \RuntimeException('Site routes must return a callable.');
        $register($this->app);
        foreach ($this->pages as $id => $page) {
            if (!isset($page['path'])) {
                if (array_key_exists('slash', $page)) throw new \InvalidArgumentException('Page slash policy requires a path: ' . $id);
                continue;
            }
            if (!is_string($page['path']) || !is_string($page['slash'] ?? 'preserve')) throw new \InvalidArgumentException('Invalid page route declaration: ' . $id);
            if ($id === 'not-found' || ($page['status'] ?? 200) === 404) throw new \InvalidArgumentException('404 pages cannot declare routes: ' . $id);
            $this->app->router->page($page['path'], fn() => $this->render($id), $page['slash'] ?? 'preserve');
        }
    }

    public function install(): void {
        $install = require $this->app->root . '/site/install.php';
        if (!is_callable($install)) throw new \RuntimeException('Site installer must return a callable.');
        $install($this->app);
    }

    public function checkRendering(): void {
        // Only rendering probes turn reported warnings/notices into health failures.
        // Respect @/error_reporting(), and preserve unrelated handler behavior.
        $diagnostics = E_WARNING | E_NOTICE | E_USER_WARNING | E_USER_NOTICE;
        $previous = null;
        $previous = set_error_handler(static function (int $severity, string $message, string $file, int $line) use (&$previous, $diagnostics) {
            if ($severity & $diagnostics) {
                if (!(error_reporting() & $severity)) return false;
                throw new \ErrorException('Rendering diagnostic: ' . $message . ' in ' . $file . ':' . $line, 0, $severity, $file, $line);
            }
            return $previous !== null ? $previous($severity, $message, $file, $line) : false;
        });
        try {
            foreach (array_keys($this->pages) as $id) $this->render($id);
        } finally { restore_error_handler(); }
    }
}
