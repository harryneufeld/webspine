<?php
declare(strict_types=1);
namespace Webspine\Forms;
/** What a form handler receives: the verdict, submitted strings, and the token to render. */
final class FormResult {
    private const STATUS = ['limited'=>429, 'invalid'=>403, 'expired'=>403, 'rejected'=>422, 'toofast'=>422, 'replayed'=>303];
    public readonly bool $ok;
    public readonly ?int $status;
    /**
     * @param ?string $reason limited, invalid, expired, rejected, toofast, replayed; null when the checks passed.
     * @param array<string,string> $fields Trimmed whitelisted values; empty unless they may be redisplayed.
     * @param ?string $token Current token; null only for limited, which opens no session.
     * @param ?string $flash Success notice set by a completed submission, shown once on the next GET.
     */
    public function __construct(
        public readonly ?string $reason, public readonly array $fields = [], public readonly ?string $token = null,
        public readonly ?string $flash = null, private readonly ?\Closure $completer = null,
    ) {
        $this->ok = $reason === null;
        $this->status = self::STATUS[$reason] ?? null;
    }
    /** Call once the work succeeded: retires the token, keeps a receipt and sets the success flash. */
    public function complete(string $flash = 'sent'): void {
        if ($this->completer === null) throw new \LogicException('Only an accepted submission can be completed.');
        ($this->completer)($flash);
    }
}
