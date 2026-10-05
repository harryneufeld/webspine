<?php
declare(strict_types=1);
// Optional example: enable "field-notes" in config/local.php.
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $app->hooks->on('app.ready', static function (\Webspine\App $ready): void {
            $ready->router->get('/field-notes', fn() => $ready->theme->render('page', [
                'title' => 'An example plugin',
                'body' => 'This page is registered through the app.ready action hook. Replace this optional plugin with a capability your site needs.',
            ]));
        });
    }
};
