<?php
declare(strict_types=1);
namespace Webspine\Contracts;

/** Optional capability; the existing three-argument Mail contract is unchanged. */
interface MailWithReplyTo extends Mail {
    public function sendWithReplyTo(string $to, string $subject, string $body, ReplyTo $replyTo): void;
}
