<?php
declare(strict_types=1);
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        foreach (['Exception', 'PHPMailer', 'SMTP'] as $class) require_once $app->coreRoot . '/vendor/phpmailer/src/' . $class . '.php';
        $app->services->set(\Webspine\Contracts\Mail::class, new class($app->config['smtp']) implements \Webspine\Contracts\Mail {
            public function __construct(private array $config) {}
            public function send(string $to, string $subject, string $body): void {
                if (!filter_var($to, FILTER_VALIDATE_EMAIL) || preg_match('/[\r\n]/', $subject)) throw new \InvalidArgumentException('Invalid email.');
                $c = $this->config;
                if (!in_array($c['encryption'], ['tls', 'smtps'], true)) throw new \RuntimeException('SMTP requires TLS.');
                $mail = new \PHPMailer\PHPMailer\PHPMailer(true);
                $mail->isSMTP(); $mail->Host = $c['host']; $mail->Port = (int)$c['port'];
                $mail->SMTPAuth = $c['username'] !== ''; $mail->Username = $c['username']; $mail->Password = $c['password'];
                $mail->SMTPSecure = $c['encryption']; $mail->CharSet = 'UTF-8'; $mail->Timeout = 10;
                $mail->setFrom($c['from'], $c['from_name']); $mail->addAddress($to);
                $mail->Subject = $subject; $mail->Body = $body; $mail->isHTML(false); $mail->send();
            }
        });
    }
};
