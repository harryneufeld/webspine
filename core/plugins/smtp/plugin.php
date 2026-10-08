<?php
declare(strict_types=1);
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        foreach (['Exception', 'PHPMailer', 'SMTP'] as $class) require_once $app->coreRoot . '/vendor/phpmailer/src/' . $class . '.php';
        require_once __DIR__ . '/SmtpMail.php';
        $app->services->set(\Webspine\Contracts\Mail::class, new \Webspine\Smtp\SmtpMail($app->config['smtp']));
    }
};
