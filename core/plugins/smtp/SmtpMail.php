<?php
declare(strict_types=1);
namespace Webspine\Smtp;
use Webspine\Contracts\{MailMessage,MailWithMessage,MailWithReplyTo,ReplyTo};
use PHPMailer\PHPMailer\PHPMailer;

class SmtpMail implements MailWithReplyTo, MailWithMessage {
    public function __construct(private array $config) {}
    public function send(string $to, string $subject, string $body): void {
        $this->sendMessage(new MailMessage($to, $subject, $body));
    }
    public function sendWithReplyTo(string $to, string $subject, string $body, ReplyTo $replyTo): void {
        $this->sendMessage(new MailMessage($to, $subject, $body, replyTo: $replyTo));
    }
    public function sendMessage(MailMessage $message): void {
        $c = $this->config;
        if (!in_array($c['encryption'], ['tls', 'smtps'], true)) throw new \RuntimeException('SMTP requires TLS.');
        $mail = $this->mailer();
        $mail->isSMTP(); $mail->Host = $c['host']; $mail->Port = (int)$c['port'];
        $mail->SMTPAuth = $c['username'] !== ''; $mail->Username = $c['username']; $mail->Password = $c['password'];
        $mail->SMTPSecure = $c['encryption']; $mail->CharSet = 'UTF-8'; $mail->Timeout = 10;
        $mail->setFrom($c['from'], $c['from_name']); $mail->addAddress($message->to);
        if ($message->replyTo !== null) $mail->addReplyTo($message->replyTo->address, $message->replyTo->name);
        $mail->Subject = $message->subject;
        if ($message->html === null) {
            $mail->isHTML(false); $mail->Body = $message->text;
        } else {
            // Trusted HTML from the caller with the required plain-text alternative.
            $mail->isHTML(true); $mail->Body = $message->html; $mail->AltBody = $message->text;
        }
        foreach ($message->attachments as $file) {
            if ($file->contentId === null) $mail->addStringAttachment($file->content, $file->filename, PHPMailer::ENCODING_BASE64, $file->type);
            else $mail->addStringEmbeddedImage($file->content, $file->contentId, $file->filename, PHPMailer::ENCODING_BASE64, $file->type);
        }
        $mail->send();
    }
    protected function mailer(): PHPMailer { return new PHPMailer(true); }
}
