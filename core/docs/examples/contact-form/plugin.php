<?php
declare(strict_types=1);
require_once __DIR__ . '/ContactForm.php';
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $form = new \Webspine\Examples\ContactForm($app);
        // Public inquiry submission is intentional; this grants no editing privileges.
        $app->router->get('/contact-example', [$form, 'show']);
        $app->router->post('/contact-example', [$form, 'submit']);
    }
};
