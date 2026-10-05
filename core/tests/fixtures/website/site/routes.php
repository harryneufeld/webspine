<?php
declare(strict_types=1);
use Webspine\{App, Response};
use Webspine\Contracts\Pages;

// The website chooses URLs; core provides the router.
return static function (App $app): void {
    $app->router->get('/', fn() => $app->site->render('home'));
    $app->router->get('/docs', fn() => $app->site->render('docs'));
    $app->router->getPattern('~^/pages/([a-z0-9-]+)$~D', static function (array $matches) use ($app): ?Response {
        $page = $app->services->get(Pages::class)->find($matches[1]);
        return $page ? $app->theme->render('page', [
            'title' => $page['title'], 'body' => $page['body'], 'page' => '',
        ]) : null;
    });
};
