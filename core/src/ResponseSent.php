<?php
declare(strict_types=1);
namespace Webspine;

/** Emission metadata, never a guarantee of browser receipt or a mutable response. */
final readonly class ResponseSent {
    public function __construct(
        public Request $request,
        public int $status,
        public array $headers,
        public bool $head,
        public bool $fastcgiFinished,
    ) {}
}
