<?php
declare(strict_types=1);
namespace Webspine;
/** Deliberate client errors; messages must not contain private data. */
final class HttpError extends \RuntimeException {
    public function __construct(public readonly int $status, string $message) { parent::__construct($message); }
}
