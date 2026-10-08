<?php
declare(strict_types=1);
namespace Webspine\Jobs;

/** Generic JSON jobs. No HTTP, theme, SMTP, or domain-specific fields. */
interface Queue {
    public function install(): void;
    public function enqueue(string $type, int $version, array $payload, ?string $dedupe = null, int $delay = 0, int $maxAttempts = 5): string;
    public function claim(array $types, int $leaseSeconds = 300, ?int $now = null): ?Job;
    public function complete(Job $job, ?int $now = null): bool;
    public function fail(Job $job, string $code, bool $permanent = false, ?int $now = null): bool;
    public function renew(Job $job, int $seconds = 300, ?int $now = null): bool;
    public function retry(string $id, ?int $now = null): bool;
    public function status(): array;
    /** Explicitly remove at most 1,000 completed/failed jobs older than this many days. */
    public function prune(int $days = 30): int;
}

final readonly class Job {
    public function __construct(
        public string $id, public string $type, public int $version, public array $payload,
        public int $attempt, public int $maxAttempts, public string $claimToken,
    ) {}
}

interface Handler {
    /** False pauses this job type without consuming attempts. */
    public function ready(): bool;
    public function handle(Job $job): void;
}

interface Handlers {
    public function register(string $type, Handler $handler): void;
    public function available(): array;
    public function get(string $type): Handler;
}

final class HandlerRegistry implements Handlers {
    private array $handlers = [];
    public function register(string $type, Handler $handler): void {
        if (!preg_match('/^[a-z][a-z0-9.-]{0,79}$/D', $type)) throw new \InvalidArgumentException('Invalid job type.');
        if (isset($this->handlers[$type])) throw new \LogicException('Job handler already registered.');
        $this->handlers[$type] = $handler;
    }
    public function available(): array {
        return array_keys(array_filter($this->handlers, static fn(Handler $h): bool => $h->ready()));
    }
    public function get(string $type): Handler { return $this->handlers[$type] ?? throw new \RuntimeException('Unknown job handler.'); }
}

/** Controlled failure category only; never store exception text or payloads in logs. */
final class PermanentFailure extends \RuntimeException {
    public function __construct(public readonly string $reason = 'invalid_payload') { parent::__construct('Job permanently rejected.'); }
}
