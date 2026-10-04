<?php
declare(strict_types=1);
require_once __DIR__ . '/SQLiteStorage.php';
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $path = $app->config['sqlite']['path'] ?? 'storage/site.sqlite';
        if (!str_starts_with($path, '/') && !preg_match('/^[A-Za-z]:[\\\\\/]/', $path)) $path = $app->root . '/' . $path;
        $app->services->set(\Webspine\Contracts\Storage::class, new \Webspine\Providers\SQLiteStorage($path));
    }
};
