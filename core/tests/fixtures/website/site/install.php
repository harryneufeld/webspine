<?php
declare(strict_types=1);
use Webspine\App;
use Webspine\Contracts\{Settings, Pages};

// Seed this website explicitly and idempotently. Providers own schema, not copy.
return static function (App $app): void {
    $settings = $app->services->get(Settings::class);
    if ($settings->get('theme') === null) {
        $theme = $app->site->meta['initial_theme'];
        $app->theme->validate($theme);
        $settings->set('theme', $theme);
    }
    $pages = $app->services->get(Pages::class);
    if ($pages->find('hello') === null) {
        $pages->put('hello', 'Your next chapter starts here.', 'This page lives in SQLite. Presentation lives in your theme. The core keeps the two connected.');
    }
};
