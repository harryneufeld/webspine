<?php
declare(strict_types=1);
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $app->services->set(\Webspine\Contracts\Mail::class, new class($app->root) implements \Webspine\Contracts\Mail {
            public function __construct(private string $root) {}
            public function send(string $to,string $subject,string $body): void {
                // Disposable fixture only: no SMTP or production services.
                file_put_contents($this->root.'/storage/captured-mail.jsonl',json_encode(['to'=>$to,'subject'=>$subject,'body'=>$body],JSON_THROW_ON_ERROR)."\n",FILE_APPEND|LOCK_EX);
            }
        });
    }
};
