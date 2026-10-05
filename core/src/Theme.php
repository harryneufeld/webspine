<?php
declare(strict_types=1);
namespace Webspine;
use Webspine\Contracts\Settings;
final class Theme {
    public function __construct(private App $app) {}
    public function validate(string $id): array {
        if (!preg_match('/^[a-z][a-z0-9-]*$/D', $id)) throw new \RuntimeException('Invalid theme identity.');
        $m = json_decode(file_get_contents($this->app->root . '/site/themes/' . $id . '/theme.json'), true, 32, JSON_THROW_ON_ERROR);
        if (($m['id'] ?? null) !== $id || ($m['api'] ?? null) !== $this->app->version['api'] || !preg_match('/^\d+\.\d+\.\d+$/D', $m['version'] ?? '') || !is_array($m['dependencies'] ?? null)) throw new \RuntimeException('Incompatible theme.');
        if (!is_file($this->app->root . '/site/themes/' . $id . '/layout.php')) throw new \RuntimeException('Missing shared theme layout.');
        foreach ($m['dependencies'] as $plugin => $minimum) {
            if (!isset($this->app->plugins->loaded[$plugin]) || !is_string($minimum) || !preg_match('/^\d+\.\d+\.\d+$/D', $minimum) || version_compare($this->app->plugins->loaded[$plugin]['version'], $minimum, '<')) throw new \RuntimeException('Unsatisfied theme dependency.');
        }
        return $m;
    }
    public function active(): string {
        $id = $this->app->services->get(Settings::class)->get('theme');
        if ($id === null) throw new \RuntimeException('No active theme. Run the site installer.');
        $this->validate($id);
        return $id;
    }
    public function component(string $name, array $props = []): string {
        if (!preg_match('/^[a-z][a-z0-9-]*$/D', $name)) throw new \InvalidArgumentException('Invalid component identity.');
        $file = $this->app->root . '/site/themes/' . $this->active() . '/components/' . $name . '.php';
        if (!is_file($file)) throw new \RuntimeException('Missing theme component: ' . $name);
        // Explicit props only; components do not inherit page template variables.
        return (static function (string $file, array $props): string {
            ob_start();
            try { require $file; return ob_get_clean(); }
            catch (\Throwable $e) { ob_end_clean(); throw $e; }
        })($file, $props);
    }
    public function render(string $template, array $data = [], int $status = 200): Response {
        $theme = $this->active();
        if (!preg_match('/^[a-z-]+$/D', $template)) throw new \RuntimeException('Invalid template.');
        $directory = $this->app->root . '/site/themes/' . $theme;
        $file = $directory . '/' . $template . '.php';
        if (!is_file($file)) throw new \RuntimeException('Missing theme template.');
        $context = ['app'=>$this->app, 'site'=>$this->app->site, 'theme'=>$theme,
            'directory'=>$directory, 'template'=>$template, 'status'=>$status, 'content'=>''];
        $content = $this->renderFile($file, $data, $context);
        $context['content'] = $content;
        $html = $this->renderFile($directory . '/layout.php', $data, $context);
        return new Response($html, $status);
    }
    private function renderFile(string $file, array $data, array $context): string {
        // Fresh local scope for each file; preserve original data and renderer context.
        return (static function (string $__file, array $data, array $__context): string {
            $__level = ob_get_level();
            extract($__context, EXTR_SKIP);
            $file = $__file;
            extract(array_filter($data, static fn($key) => is_string($key) && !str_starts_with($key, '__'), ARRAY_FILTER_USE_KEY), EXTR_SKIP);
            ob_start();
            try {
                require $__file;
                while (ob_get_level() > $__level + 1) ob_end_flush();
                return ob_get_clean();
            } catch (\Throwable $e) {
                while (ob_get_level() > $__level) ob_end_clean();
                throw $e;
            }
        })($file, $data, $context);
    }
    public function asset(string $path): Response {
        $id = $this->active();
        $prefix = '/assets/theme/' . $id . '/';
        if (!str_starts_with($path, $prefix)) return new Response('Not found', 404);
        $relative = substr($path, strlen($prefix));
        if (!preg_match('/^[a-zA-Z0-9_\/-]+\.(css|js|svg|woff2|png|jpg|webp)$/D', $relative) || str_contains($relative, '..')) return new Response('Not found', 404);
        $base = realpath($this->app->root . '/site/themes/' . $id . '/assets');
        $file = realpath(($base ?: '') . '/' . $relative);
        if (!$base || !$file || !str_starts_with($file, $base . DIRECTORY_SEPARATOR) || !is_file($file)) return new Response('Not found', 404);
        $types = ['css' => 'text/css', 'js' => 'text/javascript', 'svg' => 'image/svg+xml', 'woff2' => 'font/woff2', 'png' => 'image/png', 'jpg' => 'image/jpeg', 'webp' => 'image/webp'];
        return new Response(file_get_contents($file), 200, ['Content-Type' => $types[pathinfo($file, PATHINFO_EXTENSION)], 'Cache-Control' => 'public, max-age=3600']);
    }
}
