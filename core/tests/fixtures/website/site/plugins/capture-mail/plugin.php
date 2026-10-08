<?php
declare(strict_types=1);
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $app->services->set(\Webspine\Contracts\Mail::class, new class($app->root) implements \Webspine\Contracts\MailWithReplyTo {
            public function __construct(private string $root) {}
            public function send(string $to,string $subject,string $body): void {
                $this->capture($to,$subject,$body,null);
            }
            public function sendWithReplyTo(string $to,string $subject,string $body,\Webspine\Contracts\ReplyTo $replyTo): void {
                $this->capture($to,$subject,$body,$replyTo);
            }
            private function capture(string $to,string $subject,string $body,?\Webspine\Contracts\ReplyTo $replyTo): void {
                // Disposable fixture only: no SMTP or production services.
                if(is_file($this->root.'/storage/mail-unavailable'))throw new \RuntimeException('Simulated fixture mail outage.');
                file_put_contents($this->root.'/storage/captured-mail.jsonl',json_encode(['to'=>$to,'subject'=>$subject,'body'=>$body,'reply_to'=>$replyTo],JSON_THROW_ON_ERROR)."\n",FILE_APPEND|LOCK_EX);
            }
        });
    }
};
