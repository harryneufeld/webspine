<?php
declare(strict_types=1);
namespace Webspine;
use Webspine\Contracts\{Storage, Settings, Pages, Mail};
final class App {
    public Registry $services;
    public Router $router;
    public Hooks $hooks;
    public Plugins $plugins;
    public Theme $theme;
    public Site $site;
    public readonly string $coreRoot;
    public array $config;
    public array $version;
    public function __construct(public string $root, ?array $config = null) {
        $this->coreRoot = dirname(__DIR__);
        $this->version = require dirname(__DIR__) . '/version.php';
        $this->config = $config ?? require $root . '/config/' . (is_file($root . '/config/local.php') ? 'local.php' : 'example.php');
        $this->services = new Registry(); $this->router = new Router(); $this->hooks = new Hooks();
        $this->plugins = new Plugins($this); $this->theme = new Theme($this);
        $this->site = new Site($this);
        $providers = $this->config['providers'] ?? [];
        if (empty($providers['storage'])) throw new \RuntimeException('Select a storage provider before booting.');
        $this->plugins->load($providers['storage'], 'storage');
        $storage = $this->services->get(Storage::class);
        $this->services->set(Settings::class, $storage->settings());
        $this->services->set(Pages::class, $storage->pages());
        if (!empty($providers['mail'])) $this->plugins->load($providers['mail'], 'mail');
        foreach ($this->config['plugins'] ?? [] as $id) $this->plugins->load($id);
        $this->router->get('/health', function () { $ok = $this->health()['ok']; return Response::json(['status' => $ok ? 'ok' : 'unavailable'], $ok ? 200 : 503); });
        $this->site->register();
        $this->hooks->fire('app.ready', $this);
    }
    public function install(): void {
        $this->services->get(Storage::class)->install();
        $this->site->install();
    }
    public function health(): array {
        try {
            $status = $this->services->get(Storage::class)->health();
            if (!$status['ok']) return $status;
            $this->theme->active();
            // Check the pages defined by this site, without assuming specific templates.
            $this->site->checkRendering();
            return ['ok' => true, 'version' => $this->version['version'], 'api' => $this->version['api'], 'storage' => $status];
        } catch (\Throwable $e) { return ['ok' => false, 'error' => $e->getMessage()]; }
    }
    public function handle(string $method, string $path): Response {
        if (!in_array($method, ['GET', 'HEAD'], true)) return new Response('Method not allowed', 405, ['Allow' => 'GET, HEAD']);
        if (str_starts_with($path, '/assets/')) return $this->theme->asset($path);
        $response = $this->router->dispatch($method, $path);
        if ($response) return $response;
        return $this->site->render('not-found');
    }
}
