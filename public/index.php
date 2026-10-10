<?php
declare(strict_types=1);
// The entire request holds a shared lock; activation takes the exclusive lock.
$root = dirname(__DIR__);
$lock = null;
$visitorErrors = null;
try {
    // Data-only presentation must also work when bootstrap or executable site files fail.
    require_once $root . '/core/src/VisitorErrors.php';
    try { $visitorErrors = \Webspine\VisitorErrors::load($root); }
    catch (Throwable $catalogueError) {
        error_log('webspine visitor catalogue failed: ' . $catalogueError->getMessage());
        $visitorErrors = new \Webspine\VisitorErrors();
    }
    $lock = @fopen($root . '/storage/update.lock', 'c+');
    if (!$lock || !flock($lock, LOCK_SH)) throw new RuntimeException('Cannot acquire the framework update lock.');
    if (is_file($root . '/storage/update-pending.json')) throw new RuntimeException('Update recovery required.');
    require $root . '/core/bootstrap.php';
    $request = \Webspine\Request::fromGlobals();
    $app = new \Webspine\App($root);
    $app->handle($request)->send($request->method === 'HEAD');
} catch (\Webspine\HttpError $e) {
    $response = new \Webspine\Response($e->getMessage(), $e->status, ['Content-Type'=>'text/plain; charset=utf-8','Cache-Control'=>'no-store']);
    ($visitorErrors ? $visitorErrors->present($response) : $response)->send(($_SERVER['REQUEST_METHOD'] ?? '') === 'HEAD');
} catch (Throwable $e) {
    // Keep diagnostics private and independent of App/bootstrap availability.
    $cause = $e->getPrevious() ?? $e;
    $file = str_replace('\\', '/', $cause->getFile());
    $prefix = rtrim(str_replace('\\', '/', $root), '/') . '/';
    if (str_starts_with($file, $prefix)) $file = substr($file, strlen($prefix));
    error_log('webspine request failed: ' . $e->getMessage() . ' [' . $file . ', line ' . $cause->getLine() . ']');
    http_response_code(503);
    header('Content-Type: text/html; charset=utf-8'); header('X-Content-Type-Options: nosniff'); header('Cache-Control: no-store');
    header('Referrer-Policy: strict-origin-when-cross-origin');
    header("Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'");
    if ($visitorErrors) header('Content-Language: ' . $visitorErrors->languageTag());
    if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'HEAD') echo $visitorErrors ? $visitorErrors->unavailableHtml() : '<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>webspine · unavailable</title><h1>web<strong>spine</strong></h1><p>The site is temporarily unavailable.</p><p>Site operator: run <code>php core/bin/console.php health</code> from the project directory and inspect the PHP error log. For initial setup only, run <code>php core/bin/console.php install</code> after checking configuration.</p></html>';
} finally { if (is_resource($lock)) { flock($lock, LOCK_UN); fclose($lock); } }
