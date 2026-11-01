<?php
declare(strict_types=1);
namespace Webspine\Forms;
use Webspine\{Files,Request,Response};
/** Session, CSRF token, honeypot, whitelist, fill time and rate limit for one public form. */
final class FormGuard {
    public const TOKEN_FIELD = 'csrf';
    private const SESSION_SECONDS = 1800;
    private const RECEIPT_SECONDS = 600;
    private const RECEIPT_LIMIT = 3;
    private const OPTIONS = ['path','fields','honeypot','min_seconds','token_ttl','secure_cookie','max_attempts','window'];
    private string $path;
    /** @var list<string> */
    private array $fields;
    private string $honeypot;
    private int $minSeconds;
    private int $tokenTtl;
    private bool $secureCookie;
    private int $maxAttempts;
    private int $window;
    public function __construct(private string $root, private string $name, array $options) {
        if (!preg_match('/^[a-z][a-z0-9_]{0,39}$/D', $name)) throw new \InvalidArgumentException('Invalid form name.');
        if (array_diff(array_keys($options), self::OPTIONS)) throw new \InvalidArgumentException('Unknown form guard option.');
        $options += ['honeypot'=>'website', 'min_seconds'=>0, 'token_ttl'=>1200, 'secure_cookie'=>true, 'max_attempts'=>5, 'window'=>600];
        $path = $options['path'] ?? null;
        if (!is_string($path) || !preg_match('~^/[A-Za-z0-9/_-]*$~D', $path) || str_contains($path, '//')) throw new \InvalidArgumentException('Configure the form path as a local path.');
        $fields = $options['fields'] ?? null;
        if (!is_array($fields) || !$fields || !array_is_list($fields)) throw new \InvalidArgumentException('Configure the allowed form fields.');
        foreach ([$options['honeypot'], ...$fields] as $key) {
            if (!is_string($key) || !preg_match('/^[a-z][a-z0-9_]{0,39}$/D', $key) || $key === self::TOKEN_FIELD) throw new \InvalidArgumentException('Invalid form field name.');
        }
        if (in_array($options['honeypot'], $fields, true) || count(array_unique($fields)) !== count($fields)) throw new \InvalidArgumentException('Form field names must be distinct.');
        if (!is_int($options['min_seconds']) || $options['min_seconds'] < 0 || $options['min_seconds'] > 3600) throw new \InvalidArgumentException('Configure min_seconds as an integer 0–3600.');
        if (!is_int($options['token_ttl']) || $options['token_ttl'] < 60 || $options['token_ttl'] >= self::SESSION_SECONDS) throw new \InvalidArgumentException('Configure token_ttl from 60 seconds to below the 1800-second session lifetime.');
        if (!is_bool($options['secure_cookie'])) throw new \InvalidArgumentException('Configure secure_cookie as a boolean.');
        if (!is_int($options['max_attempts']) || $options['max_attempts'] < 1 || !is_int($options['window']) || $options['window'] < 1) throw new \InvalidArgumentException('Configure max_attempts and window as positive integers.');
        $this->path = $path;
        $this->fields = $fields;
        $this->honeypot = $options['honeypot'];
        $this->minSeconds = $options['min_seconds'];
        $this->tokenTtl = $options['token_ttl'];
        $this->secureCookie = $options['secure_cookie'];
        $this->maxAttempts = $options['max_attempts'];
        $this->window = $options['window'];
    }
    /** Limiter shared by this form, for per-address or global buckets inside a handler. */
    public function limiter(): RateLimiter { return new RateLimiter($this->root, 'storage/forms/' . $this->name . '/rate.json'); }
    /** GET: $render receives a FormResult with the token and a one-time flash, and returns the Response. */
    public function show(callable $render): Response {
        $this->open();
        try {
            $flash = $_SESSION['form']['flash'] ?? null;
            unset($_SESSION['form']['flash']);
            return $this->finish($render(new FormResult(null, [], $this->issue(), is_string($flash) ? $flash : null)));
        } finally { session_write_close(); }
    }
    /**
     * POST: $handle receives a FormResult for every outcome and returns the Response. The session
     * stays open, and so serializes concurrent submissions, until $handle returns.
     */
    public function submit(Request $request, callable $handle): Response {
        $form = $request->form();
        $client = RateLimiter::client($request->remoteAddress);
        if ($client === null || !$this->limiter()->allow([['kind'=>'ip', 'subject'=>$client, 'max'=>$this->maxAttempts, 'window'=>$this->window]])) {
            $response = $this->finish($handle(new FormResult('limited')));
            $response->headers['Retry-After'] = (string) $this->window;
            return $response;
        }
        $this->open();
        try { return $this->finish($handle($this->verify($request, $form))); }
        finally { session_write_close(); }
    }
    private function verify(Request $request, array $form): FormResult {
        $fields = [];
        $clean = !array_diff(array_keys($form), [self::TOKEN_FIELD, $this->honeypot, ...$this->fields]) && ($form[$this->honeypot] ?? '') === '';
        foreach ($this->fields as $key) {
            $value = $form[$key] ?? '';
            if (!is_string($value)) { $clean = false; $value = ''; }
            // Only whitespace: stripping NUL or other controls here would hide them from field validation.
            $fields[$key] = trim($value, " \t\r\n");
        }
        if ($request->header('Sec-Fetch-Site') === 'cross-site') return new FormResult('invalid', [], $this->issue());
        $receipts = array_filter($_SESSION['form']['receipts'] ?? [], static fn(array $receipt): bool => $receipt['expires'] > time());
        $_SESSION['form']['receipts'] = $receipts;
        $token = $form[self::TOKEN_FIELD] ?? null;
        $fingerprint = hash('sha256', serialize($fields));
        $receipt = is_string($token) ? ($receipts[hash('sha256', $token)] ?? null) : null;
        if ($clean && $receipt && hash_equals($receipt['fingerprint'], $fingerprint)) {
            // A receipt proves this exact submission already succeeded in this session.
            $_SESSION['form']['flash'] = $receipt['flash'];
            return new FormResult('replayed', [], $this->issue());
        }
        $known = $_SESSION['form']['token'] ?? null;
        $matches = is_string($token) && is_string($known) && hash_equals($known, $token);
        if (!$matches || ($_SESSION['form']['expires'] ?? 0) <= time()) {
            unset($_SESSION['form']['token'], $_SESSION['form']['expires']);
            // Input is kept only for this visitor's own expired token, never for a foreign POST.
            return $matches ? new FormResult('expired', $fields, $this->issue()) : new FormResult('invalid', [], $this->issue());
        }
        if (!$clean) return new FormResult('rejected', [], $token);
        if (time() - ($_SESSION['form']['issued'] ?? 0) < $this->minSeconds) return new FormResult('toofast', $fields, $token);
        return new FormResult(null, $fields, $token, null, function (string $flash) use ($token, $fingerprint): void {
            $receipts = $_SESSION['form']['receipts'] ?? [];
            $receipts[hash('sha256', $token)] = ['expires'=>time() + self::RECEIPT_SECONDS, 'fingerprint'=>$fingerprint, 'flash'=>$flash];
            $_SESSION['form']['receipts'] = array_slice($receipts, -self::RECEIPT_LIMIT, null, true);
            $_SESSION['form']['flash'] = $flash;
            unset($_SESSION['form']['token'], $_SESSION['form']['expires'], $_SESSION['form']['issued']);
        });
    }
    /** Current token, renewed when missing or expired. The fill-time clock survives a renewal. */
    private function issue(): string {
        if (!isset($_SESSION['form']['token']) || ($_SESSION['form']['expires'] ?? 0) <= time()) {
            $_SESSION['form']['token'] = bin2hex(random_bytes(32));
            $_SESSION['form']['expires'] = time() + $this->tokenTtl;
            $_SESSION['form']['issued'] ??= time();
        }
        return $_SESSION['form']['token'];
    }
    private function finish(mixed $response): Response {
        if (!$response instanceof Response) throw new \RuntimeException('Form handler must return a Response.');
        // A shared page cache must never store a response carrying a visitor's token.
        $response->headers['Cache-Control'] = 'no-store';
        return $response;
    }
    private function open(): void {
        if (session_status() !== PHP_SESSION_NONE) throw new \RuntimeException('Form guard requires its own session; close the active session first.');
        if (headers_sent($file, $line)) throw new \RuntimeException('Form session cannot start after output at ' . $file . ':' . $line . '; use an isolated HTTP/subprocess test.');
        $directory = Files::target($this->root, 'storage/forms/' . $this->name . '/sessions');
        if (!is_dir($directory) && !mkdir($directory, 0700, true) && !is_dir($directory)) throw new \RuntimeException('Cannot create private form sessions.');
        session_name('webspine_' . $this->name);
        if (!session_start(['save_path'=>$directory, 'use_strict_mode'=>1, 'use_only_cookies'=>1, 'cookie_httponly'=>1, 'cookie_samesite'=>'Lax', 'cookie_secure'=>$this->secureCookie,
            'cookie_path'=>$this->path, 'gc_maxlifetime'=>self::SESSION_SECONDS, 'gc_probability'=>1, 'gc_divisor'=>100])) throw new \RuntimeException('Cannot start form session.');
    }
}
