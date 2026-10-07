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
        $this->theme->refresh();
        try {
            $status = $this->services->get(Storage::class)->health();
            if (!$status['ok']) return $status;
            if ($this->services->has(\Webspine\Contracts\Entities::class)) {
                $entities = $this->services->get(\Webspine\Contracts\Entities::class)->health();
                if (!$entities['ok']) return ['ok'=>false, 'entities'=>$entities];
            }
            $this->theme->active();
            // Check the pages defined by this site, without assuming specific templates.
            $this->site->checkRendering();
            return ['ok' => true, 'version' => $this->version['version'], 'api' => $this->version['api'], 'storage' => $status];
        } catch (\Throwable $e) { return ['ok' => false, 'error' => $e->getMessage()]; }
    }
    public function handle(string|Request $method, ?string $path = null): Response {
        $this->theme->refresh();
        try {
            $request = $method instanceof Request ? $method : new Request($method, $path ?? '/');
            if (str_starts_with($request->path, '/assets/')) {
                if (!in_array($request->method, ['GET','HEAD'], true)) return new Response('Method not allowed', 405, ['Allow'=>'GET, HEAD']);
                return $this->theme->asset($request->path);
            }
            $response = $this->router->dispatch($request->method, $request->path, $request);
            return $response ?? $this->site->render('not-found');
        } catch (HttpError $e) {
            return new Response($e->getMessage(), $e->status, ['Content-Type'=>'text/plain; charset=utf-8','Cache-Control'=>'no-store']);
        }
    }
}
