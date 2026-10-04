<?php
declare(strict_types=1);
use Webspine\{App, Response};
use Webspine\Contracts\Plugin;

// Optional capability for the webspine project website; no core-owned copy.
return new class implements Plugin {
    public function register(App $app): void {
        $app->router->get('/download', static function () use ($app): Response {
            $filename = 'webspine-' . $app->version['version'] . '.zip';
            $file = $app->root . '/.dist/' . $filename;
            return is_file($file)
                ? new Response(file_get_contents($file), 200, [
                    'Content-Type' => 'application/zip',
                    'Content-Disposition' => 'attachment; filename="' . $filename . '"',
                    'Cache-Control' => 'no-store',
                ])
                : $app->site->render('download-unavailable');
        });
    }
};
