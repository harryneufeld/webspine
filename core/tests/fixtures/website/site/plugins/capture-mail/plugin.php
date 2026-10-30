<?php
declare(strict_types=1);
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $app->services->set(\Webspine\Contracts\Mail::class, new class($app->root) implements \Webspine\Contracts\MailWithReplyTo, \Webspine\Contracts\MailWithMessage {
            public function __construct(private string $root) {}
            public function send(string $to,string $subject,string $body): void {
                $this->sendMessage(new \Webspine\Contracts\MailMessage($to,$subject,$body));
            }
            public function sendWithReplyTo(string $to,string $subject,string $body,\Webspine\Contracts\ReplyTo $replyTo): void {
                $this->sendMessage(new \Webspine\Contracts\MailMessage($to,$subject,$body,replyTo:$replyTo));
            }
            public function sendMessage(\Webspine\Contracts\MailMessage $message): void {
                // Disposable fixture only: no SMTP or production services.
                if(is_file($this->root.'/storage/mail-unavailable'))throw new \RuntimeException('Simulated fixture mail outage.');
                $attachments=array_map(static fn($file)=>['filename'=>$file->filename,'type'=>$file->type,'bytes'=>strlen($file->content),'content_id'=>$file->contentId],$message->attachments);
                file_put_contents($this->root.'/storage/captured-mail.jsonl',json_encode(['to'=>$message->to,'subject'=>$message->subject,'body'=>$message->text,
                    'html'=>$message->html,'reply_to'=>$message->replyTo,'attachments'=>$attachments],JSON_THROW_ON_ERROR)."\n",FILE_APPEND|LOCK_EX);
            }
        });
    }
};
