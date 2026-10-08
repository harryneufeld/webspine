<?php
declare(strict_types=1);
namespace Webspine\Smtp;
use Webspine\Contracts\{MailWithReplyTo,ReplyTo};
use PHPMailer\PHPMailer\PHPMailer;

class SmtpMail implements MailWithReplyTo {
    public function __construct(private array $config) {}
    public function send(string $to, string $subject, string $body): void {
        $this->deliver($to, $subject, $body, null);
    }
    public function sendWithReplyTo(string $to, string $subject, string $body, ReplyTo $replyTo): void {
        $this->deliver($to, $subject, $body, $replyTo);
    }
    protected function mailer(): PHPMailer { return new PHPMailer(true); }
    private function deliver(string $to, string $subject, string $body, ?ReplyTo $replyTo): void {
        if (!filter_var($to, FILTER_VALIDATE_EMAIL) || preg_match('/[\x00-\x1f\x7f]/', $to)
            || preg_match('/[\r\n\x00]/', $subject)) throw new \InvalidArgumentException('Invalid email.');
        $c = $this->config;
        if (!in_array($c['encryption'], ['tls', 'smtps'], true)) throw new \RuntimeException('SMTP requires TLS.');
        $mail = $this->mailer();
        $mail->isSMTP(); $mail->Host = $c['host']; $mail->Port = (int)$c['port'];
        $mail->SMTPAuth = $c['username'] !== ''; $mail->Username = $c['username']; $mail->Password = $c['password'];
        $mail->SMTPSecure = $c['encryption']; $mail->CharSet = 'UTF-8'; $mail->Timeout = 10;
        $mail->setFrom($c['from'], $c['from_name']); $mail->addAddress($to);
        if ($replyTo !== null) $mail->addReplyTo($replyTo->address, $replyTo->name);
        $mail->Subject = $subject; $mail->Body = $body; $mail->isHTML(false); $mail->send();
    }
}
