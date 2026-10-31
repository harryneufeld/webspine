<?php
declare(strict_types=1);
namespace Webspine\Contracts;

/** In-memory file for a MailMessage; a content ID embeds it for `cid:` use in HTML. */
final readonly class Attachment {
    public const MAX_BYTES = 10485760;
    public function __construct(
        public string $filename, public string $content,
        public string $type = 'application/octet-stream', public ?string $contentId = null,
    ) {
        if ($filename === '' || strlen($filename) > 255 || !preg_match('//u', $filename)
            || preg_match('/[\x00-\x1f\x7f"\/\\\\]/', $filename) || in_array($filename, ['.', '..'], true)) {
            throw new \InvalidArgumentException('Invalid attachment filename.');
        }
        if (!preg_match('~^[a-z0-9][a-z0-9!#$&^_.+-]{0,63}/[a-z0-9][a-z0-9!#$&^_.+-]{0,63}$~iD', $type)) {
            throw new \InvalidArgumentException('Invalid attachment media type.');
        }
        if ($contentId !== null && !preg_match('/^[A-Za-z0-9._-]{1,64}$/D', $contentId)) {
            throw new \InvalidArgumentException('Invalid attachment content ID.');
        }
        if (strlen($content) > self::MAX_BYTES) throw new \InvalidArgumentException('Attachment exceeds 10 MiB.');
    }
    /** Reads a trusted local file; never pass a path chosen by visitors. */
    public static function fromFile(string $path, ?string $filename = null, string $type = 'application/octet-stream', ?string $contentId = null): self {
        $size = is_file($path) ? filesize($path) : false;
        if ($size === false) throw new \InvalidArgumentException('Attachment file is not readable.');
        if ($size > self::MAX_BYTES) throw new \InvalidArgumentException('Attachment exceeds 10 MiB.');
        $content = file_get_contents($path, false, null, 0, self::MAX_BYTES + 1);
        if ($content === false) throw new \InvalidArgumentException('Attachment file is not readable.');
        return new self($filename ?? basename($path), $content, $type, $contentId);
    }
}
