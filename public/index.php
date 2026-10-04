<?php
declare(strict_types=1);
// The entire request holds a shared lock; activation takes the exclusive lock.
$root = dirname(__DIR__);
$lock = fopen($root . '/storage/update.lock', 'c+');
if (!$lock || !flock($lock, LOCK_SH)) { http_response_code(503); exit('Temporarily unavailable.'); }
try {
    if (is_file($root . '/storage/update-pending.json')) { http_response_code(503); exit('Update recovery required.'); }
    require $root . '/core/bootstrap.php';
    $app = new \Webspine\App($root);
    $path = parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH);
    $path = is_string($path) ? rawurldecode($path) : '/';
    $app->handle($_SERVER['REQUEST_METHOD'] ?? 'GET', $path)->send(($_SERVER['REQUEST_METHOD'] ?? '') === 'HEAD');
} catch (Throwable $e) {
    error_log('webspine request failed: ' . $e->getMessage());
    http_response_code(503);
    header('Content-Type: text/html; charset=utf-8'); header('X-Content-Type-Options: nosniff');
    echo '<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>webspine · setup required</title><h1>web<strong>spine</strong></h1><p>The site is unavailable. If you are setting it up, run <code>php core/bin/console.php install</code> from the project directory.</p></html>';
} finally { flock($lock, LOCK_UN); fclose($lock); }
