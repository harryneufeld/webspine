<?php
declare(strict_types=1);
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $app->hooks->on('app.ready', static function (\Webspine\App $ready): void {
            $ready->router->get('/field-notes', fn() => $ready->site->render('field-notes'));
        });
    }
};
