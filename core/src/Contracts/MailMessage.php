<?php
declare(strict_types=1);
namespace Webspine\Contracts;

/** A validated message: plain text, optional trusted HTML alternative, Reply-To and attachments. */
final readonly class MailMessage {
    public const MAX_BODY_BYTES = 1048576;
    public const MAX_ATTACHMENTS = 10;
    public const MAX_ATTACHMENT_BYTES = 20971520;
    /** @param list<Attachment> $attachments */
    public function __construct(
        public string $to, public string $subject, public string $text,
        public ?string $html = null, public ?ReplyTo $replyTo = null, public array $attachments = [],
    ) {
        if (strlen($to) > 254 || !filter_var($to, FILTER_VALIDATE_EMAIL) || preg_match('/[\x00-\x1f\x7f]/', $to)
            || strlen($subject) > 998 || !preg_match('//u', $subject) || preg_match('/[\r\n\x00]/', $subject)) {
            throw new \InvalidArgumentException('Invalid email.');
        }
        foreach ([$text, $html ?? ''] as $body) {
            if (strlen($body) > self::MAX_BODY_BYTES || !preg_match('//u', $body) || str_contains($body, "\0")) {
                throw new \InvalidArgumentException('Mail bodies must be UTF-8 text of at most 1 MiB.');
            }
        }
        if (!array_is_list($attachments) || count($attachments) > self::MAX_ATTACHMENTS) {
            throw new \InvalidArgumentException('Use a list of at most 10 attachments.');
        }
        $bytes = 0; $ids = [];
        foreach ($attachments as $attachment) {
            if (!$attachment instanceof Attachment) throw new \InvalidArgumentException('Attachments must be Attachment instances.');
            $bytes += strlen($attachment->content);
            if ($attachment->contentId !== null) {
                if ($html === null || isset($ids[$attachment->contentId])) throw new \InvalidArgumentException('Embedded content IDs need HTML and must be unique.');
                $ids[$attachment->contentId] = true;
            }
        }
        if ($bytes > self::MAX_ATTACHMENT_BYTES) throw new \InvalidArgumentException('Attachments exceed 20 MiB in total.');
    }
}
