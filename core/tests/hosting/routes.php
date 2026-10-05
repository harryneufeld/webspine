<?php
declare(strict_types=1);
use Webspine\{App, Request, Response};
use Webspine\Contracts\Settings;
return static function (App $app): void {
    (require __DIR__ . '/base-routes.php')($app);
    // Synthetic routes are copied only into disposable containers, never the starter.
    $app->router->get('/hosting-probe', static fn(Request $request) => Response::json([
        'version' => $app->version['version'],
        'query' => $request->query(),
        'uri' => $request->uri,
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
    $app->router->get('/hosting-redirect', static fn(Request $request) => new Response('', 302, [
        'Location' => '/docs?' . http_build_query($request->query(), '', '&', PHP_QUERY_RFC3986),
    ]));
    $app->router->post('/hosting-body', static fn(Request $request) => Response::json([
        'values'=>str_starts_with($request->header('Content-Type',''),'application/json') ? $request->json() : $request->form(),
        'header'=>$request->header('X-Example'), 'query'=>$request->query(),
    ]));
    $app->router->get('/hosting-legacy', static fn(Request $request) => $request->query('page_id') === '41' ? new Response('',302,['Location'=>'/docs']) : new Response('Not found',404));
    $app->router->get('/hosting-mail', static function () use ($app): Response {
        $file = $app->root . '/storage/captured-mail.jsonl';
        $messages = is_file($file) ? array_map(static fn($line)=>json_decode($line,true),file($file,FILE_IGNORE_NEW_LINES|FILE_SKIP_EMPTY_LINES)) : [];
        return Response::json(['messages'=>$messages]);
    });
};
