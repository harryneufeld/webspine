<?php
declare(strict_types=1);
use Webspine\App;
use Webspine\Contracts\Settings;
// Schema belongs to the provider. This site only selects its initial theme.
return static function (App $app): void {
    $settings = $app->services->get(Settings::class);
    if ($settings->get('theme') === null) {
        $theme = $app->site->meta['initial_theme'];
        $app->theme->validate($theme);
        $settings->set('theme', $theme);
    }
};
