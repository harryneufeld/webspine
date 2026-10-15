<?php
declare(strict_types=1);
use Webspine\{App, HttpError, Request, Response, ResponseSent};

// Synthetic routes and private observations for disposable lifecycle fixtures only.
return static function (App $app): void {
    $events = $app->root . '/storage/lifecycle-events.jsonl';
    $record = static function (ResponseSent $event, string $stage) use ($app, $events): void {
        $probe = fopen($app->root . '/storage/update.lock', 'c+');
        $locked = !flock($probe, LOCK_EX | LOCK_NB);
        if (!$locked) flock($probe, LOCK_UN);
        fclose($probe);
        file_put_contents($events, json_encode([
            'id'=>$event->request->query('id'), 'stage'=>$stage, 'method'=>$event->request->method,
            'status'=>$event->status, 'head'=>$event->head, 'fastcgi'=>$event->fastcgiFinished,
            'session_active'=>session_status() === PHP_SESSION_ACTIVE, 'update_locked'=>$locked,
            'ignore_abort'=>(bool) ignore_user_abort(),
            'headers'=>$event->headers,
        ], JSON_THROW_ON_ERROR) . "\n", FILE_APPEND | LOCK_EX);
    };
    $observed = static fn(ResponseSent $event): bool => str_starts_with($event->request->path, '/lifecycle-') && $event->request->path !== '/lifecycle-events';
    $app->hooks->on('response.sent', static function (ResponseSent $event) use ($app, $record, $observed): void {
        if (!$observed($event)) return;
        $record($event, 'first');
        switch ($event->request->path) {
            case '/lifecycle-output': echo 'PRIVATE_HOOK_OUTPUT'; ob_start(); echo 'PRIVATE_NESTED_OUTPUT'; ob_end_flush(); flush(); break;
            case '/lifecycle-throw': echo 'PRIVATE_HOOK_OUTPUT'; ob_start(); throw new RuntimeException('PRIVATE_HOOK_EXCEPTION');
            case '/lifecycle-warning': trigger_error('PRIVATE_HOOK_WARNING', E_USER_WARNING); break;
            case '/lifecycle-header': http_response_code(599); header('X-Hook-Leak: private'); break;
            case '/lifecycle-reentrant': $app->send(new Response('PRIVATE_REENTRANT_OUTPUT'), $event->request); break;
            case '/lifecycle-readonly': $event->status = 599; break;
            case '/lifecycle-budget': usleep(150000); break;
        }
    });
    $app->hooks->on('response.sent', static function (ResponseSent $event) use ($record, $observed): void {
        if ($observed($event)) $record($event, 'second');
    });
    foreach (['normal','output','throw','warning','header','reentrant','readonly','budget','session'] as $name) {
        $app->router->get('/lifecycle-' . $name, static function (Request $request) use ($app): Response {
            if ($request->path === '/lifecycle-session') {
                session_start(['save_path'=>$app->root . '/storage', 'use_cookies'=>0, 'cache_limiter'=>'']);
                $_SESSION['lifecycle'] = 'saved';
            }
            return new Response('Lifecycle body', 201, ['X-Lifecycle'=>'original']);
        });
    }
    $app->router->get('/lifecycle-client-error', static function (): Response { throw new HttpError(400, 'Client error'); });
    $app->router->get('/lifecycle-failure', static function (): Response { throw new RuntimeException('PRIVATE_HANDLER_ERROR'); });
    $app->router->get('/lifecycle-exit', static function (): never { exit('Early exit'); });
    $app->router->get('/lifecycle-direct', static function (): never { (new Response('Direct body'))->send(); exit; });
    $app->router->get('/lifecycle-events', static function (Request $request) use ($events): Response {
        $rows = is_file($events) ? array_map(static fn($line) => json_decode($line, true, flags: JSON_THROW_ON_ERROR), file($events, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES)) : [];
        return Response::json(array_values(array_filter($rows, static fn($row) => $row['id'] === $request->query('id'))));
    });
};
