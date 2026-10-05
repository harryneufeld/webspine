<?php
declare(strict_types=1);
use Webspine\{App, Response};
use Webspine\Contracts\Settings;
return static function (App $app): void {
    (require __DIR__ . '/base-routes.php')($app);
    // Synthetic routes are copied only into disposable containers, never the starter.
    $app->router->get('/hosting-probe', static fn() => Response::json([
        'version' => $app->version['version'],
        'query' => $_GET,
        'uri' => $_SERVER['REQUEST_URI'],
        'php' => PHP_VERSION_ID,
        'extensions' => array_map('extension_loaded', ['pdo_sqlite', 'zip', 'openssl']),
        'opcache' => (bool) (opcache_get_status(false)['opcache_enabled'] ?? false),
        'cached' => opcache_is_script_cached($app->coreRoot . '/version.php'),
        'timestamp_checks' => ini_get('opcache.validate_timestamps'),
        'storage_writable' => is_writable($app->root . '/storage'),
        'core_writable' => is_writable($app->coreRoot . '/version.php'),
        'saved' => $app->services->get(Settings::class)->get('hosting-check'),
    ]));
    $app->router->get('/hosting-write', static function () use ($app): Response {
        $app->services->get(Settings::class)->set('hosting-check', 'preserved');
        return Response::json(['written' => true]);
    });
    $app->router->get('/hosting-redirect', static fn() => new Response('', 302, [
        'Location' => '/docs?' . http_build_query($_GET, '', '&', PHP_QUERY_RFC3986),
    ]));
};
