<?php
declare(strict_types=1);
require_once __DIR__ . '/ContactForm.php';
require_once __DIR__ . '/MailDelivery.php';
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $form = new \Webspine\Examples\ContactForm($app);
        $app->services->get(\Webspine\Jobs\Handlers::class)->register('contact.deliver', new \Webspine\Examples\MailDelivery($app));
        // Public inquiry submission is intentional; this grants no editing privileges.
        $app->router->get($form->path(), [$form, 'show']);
        $app->router->post($form->path(), [$form, 'submit']);
    }
};
