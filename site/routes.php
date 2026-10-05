<?php
declare(strict_types=1);
use Webspine\{App, Response};
use Webspine\Contracts\Pages;
return static function (App $app): void {
    foreach (['/' => 'home', '/about' => 'about', '/contact' => 'contact'] as $path => $id) {
        $app->router->get($path, fn() => $app->site->render($id));
    }
    // Optional database pages use the same shared page template.
    $app->router->getPattern('~^/pages/([a-z0-9-]+)$~D', static function (array $matches) use ($app): ?Response {
        $page = $app->services->get(Pages::class)->find($matches[1]);
        return $page ? $app->theme->render('page', ['title' => $page['title'], 'body' => $page['body'], 'page' => '']) : null;
    });
};
