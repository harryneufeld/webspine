<?php
declare(strict_types=1);
namespace Webspine\Contracts;

/** Optional capability for HTML alternatives and attachments; the Mail contract is unchanged. */
interface MailWithMessage extends Mail {
    public function sendMessage(MailMessage $message): void;
}
