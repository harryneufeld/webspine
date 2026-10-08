<?php
declare(strict_types=1);
namespace Webspine;
use Webspine\Contracts\Settings;
final class Theme {
    private const ASSET_TYPES = [
        'css'=>'text/css', 'js'=>'text/javascript', 'svg'=>'image/svg+xml', 'woff2'=>'font/woff2',
        'png'=>'image/png', 'jpg'=>'image/jpeg', 'jpeg'=>'image/jpeg', 'webp'=>'image/webp',
        'ico'=>'image/vnd.microsoft.icon', 'avif'=>'image/avif', 'gif'=>'image/gif',
        'pdf'=>'application/pdf', 'txt'=>'text/plain; charset=utf-8', 'webmanifest'=>'application/manifest+json',
    ];
    private ?string $activeId = null;
    private array $assetUrls = [];
    private int $componentDepth = 0;
    private int $renderDepth = 0;
    private readonly AssetHashCache $hashes;
    private readonly ThemeLifecycle $lifecycle;
    private readonly ThemeContext $ui;
    public function __construct(private App $app) {
        $this->ui = new ThemeContext($this);
        $this->hashes = new AssetHashCache($app->root, $app->version['version'], ($app->config['theme']['asset_hash_cache'] ?? true) === true);
        $this->lifecycle = new ThemeLifecycle($app, ($app->config['theme']['debug'] ?? false) === true);
    }
    /** Begin a new rendering lifecycle or refresh after direct settings/file changes. */
    public function refresh(bool $clearAssetHashes = true): void {
        $this->activeId = null; $this->assetUrls = []; $this->lifecycle->reset();
        if ($clearAssetHashes) $this->hashes->clear();
    }
    public function assetHashStats(): array { return $this->hashes->stats(); }
    private function renderBoundary(): void {
        if ($this->componentDepth === 0 && $this->renderDepth === 0) $this->lifecycle->check();
    }
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
        if ($this->activeId !== null) return $this->activeId;
        $id = $this->app->services->get(Settings::class)->get('theme');
        if ($id === null) throw new \RuntimeException('No active theme. Run the site installer.');
        $manifest = $this->validate($id);
        $this->lifecycle->begin($id, $manifest);
        return $this->activeId = $id;
    }
    public function component(string $name, array $props = []): string {
        $this->renderBoundary();
        if (!preg_match('/^[a-z][a-z0-9-]*$/D', $name)) throw new \InvalidArgumentException('Invalid component identity.');
        $file = $this->app->root . '/site/themes/' . $this->active() . '/components/' . $name . '.php';
        if (!is_file($file)) throw new \RuntimeException('Missing theme component: ' . $name);
        $this->lifecycle->remember($file);
        if ($this->componentDepth >= 64) throw new \RuntimeException('Theme component nesting limit exceeded.');
        $this->componentDepth++;
        try {
            // Explicit props plus presentation helpers; no inherited page variables.
            return (static function (string $__file, array $props, ThemeContext $ui): string {
                $__level = ob_get_level();
                $file = $__file;
                ob_start();
                try {
                    require $__file;
                    while (ob_get_level() > $__level + 1) ob_end_flush();
                    return ob_get_clean();
                } catch (\Throwable $e) {
                    while (ob_get_level() > $__level) ob_end_clean();
                    throw $e;
                }
            })($file, $props, $this->ui);
        } finally { $this->componentDepth--; }
    }
    public function render(string $template, array $data = [], int $status = 200): Response {
        $this->renderBoundary();
        $theme = $this->active();
        if (!preg_match('/^(?:[a-z][a-z0-9-]*|-[a-z-]*)$/D', $template)) throw new \RuntimeException('Invalid template.');
        $directory = $this->app->root . '/site/themes/' . $theme;
        $file = $directory . '/' . $template . '.php';
        if (!is_file($file)) throw new \RuntimeException('Missing theme template.');
        $this->lifecycle->remember($file);
        $context = ['app'=>$this->app, 'site'=>$this->app->site, 'theme'=>$theme, 'ui'=>$this->ui,
            'directory'=>$directory, 'template'=>$template, 'status'=>$status, 'content'=>''];
        $this->renderDepth++;
        try {
            $content = $this->renderFile($file, $data, $context);
            $context['content'] = $content;
            $html = $this->renderFile($directory . '/layout.php', $data, $context);
            return new Response($html, $status);
        } finally { $this->renderDepth--; }
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
    private function assetType(string $relative): string {
        // Nonempty dotted segments; no hidden files, traversal, URL syntax or executable suffix chains.
        if (!preg_match('~^(?:[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*/)*[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)+$~D', $relative)
            || preg_match('~\.(?:php[0-9]*|phtml|pht|phar|cgi|pl|py|rb|sh|bat|cmd|ps1|exe|com|dll|asp|aspx|jsp)(?:\.|/|$)~i', $relative)) {
            throw new \RuntimeException('Invalid theme asset path: ' . $relative);
        }
        $type = self::ASSET_TYPES[pathinfo($relative, PATHINFO_EXTENSION)] ?? null;
        if ($type === null) throw new \RuntimeException('Unsupported theme asset extension: ' . $relative);
        return $type;
    }
    private function assetFile(string $id, string $relative): string {
        $this->assetType($relative);
        $base = realpath($this->app->root . '/site/themes/' . $id . '/assets');
        if (!$base) throw new \RuntimeException('Missing theme asset: ' . $relative);
        $file = realpath($base . '/' . $relative);
        if (!$file) throw new \RuntimeException('Missing theme asset: ' . $relative);
        if (!str_starts_with($file, $base . DIRECTORY_SEPARATOR)) throw new \RuntimeException('Invalid theme asset path: ' . $relative);
        // Contained links must not disguise an executable or unsupported target.
        $this->assetType(str_replace(DIRECTORY_SEPARATOR, '/', substr($file, strlen($base) + 1)));
        if (!is_file($file)) throw new \RuntimeException('Missing theme asset: ' . $relative);
        if (!is_readable($file)) throw new \RuntimeException('Cannot read theme asset: ' . $relative);
        return $file;
    }
    /** Theme-relative path, with a content hash reused for this rendering lifecycle. */
    public function assetUrl(string $relative): string {
        $this->renderBoundary();
        $id = $this->active();
        if (isset($this->assetUrls[$relative])) return $this->assetUrls[$relative];
        $file = $this->assetFile($id, $relative);
        $this->lifecycle->remember($file);
        try { $hash = $this->hashes->hash($file); }
        catch (\RuntimeException $e) { throw new \RuntimeException('Cannot read theme asset: ' . $relative, 0, $e); }
        return $this->assetUrls[$relative] = '/assets/theme/' . $id . '/' . $relative . '?v=' . substr($hash, 0, 12);
    }
    public function asset(string $path, ?Request $request = null): Response {
        $id = $this->active();
        $prefix = '/assets/theme/' . $id . '/';
        if (!str_starts_with($path, $prefix)) return $this->app->visitorErrors->present(new Response('Not found', 404));
        $relative = substr($path, strlen($prefix));
        try {
            $file = $this->assetFile($id, $relative);
            $body = @file_get_contents($file);
            if ($body === false) return $this->app->visitorErrors->present(new Response('Not found', 404));
            // Validators describe these exact bytes, independently of the URL cache.
            $etag = '"' . hash('sha256', $body) . '"';
            $headers = ['Content-Type'=>$this->assetType($relative), 'Cache-Control'=>'public, max-age=3600', 'ETag'=>$etag];
            $condition = $request?->header('If-None-Match');
            if ($condition !== null && in_array($request->method, ['GET','HEAD'], true) && self::matches($condition, $etag)) return new Response('', 304, $headers);
            return new Response($request?->method === 'HEAD' ? '' : $body, 200, $headers);
        } catch (\RuntimeException) { return $this->app->visitorErrors->present(new Response('Not found', 404)); }
    }
    private static function matches(string $condition, string $etag): bool {
        if (strlen($condition) > 8192) return false;
        if (trim($condition) === '*') return true;
        // Parse complete syntax first; commas inside quoted opaque tags are legal.
        if (!preg_match('/^\s*(?:W\/)?"[\x21\x23-\x7e\x80-\xff]*"(?:\s*,\s*(?:W\/)?"[\x21\x23-\x7e\x80-\xff]*")*\s*$/D', $condition)) return false;
        preg_match_all('/(?:W\/)?("[\x21\x23-\x7e\x80-\xff]*")/', $condition, $tags);
        return in_array($etag, $tags[1], true);
    }
}
