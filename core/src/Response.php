<?php
declare(strict_types=1);
namespace Webspine;
final class Response {
    public function __construct(public string $body = '', public int $status = 200, public array $headers = []) {}
    public static function json(array $value, int $status = 200): self {
        return new self(json_encode($value, JSON_THROW_ON_ERROR), $status, ['Content-Type' => 'application/json; charset=utf-8']);
    }
    /** Fixed trusted destinations; validation is not an open-redirect allowlist. */
    public static function redirect(string $destination, int $status = 302): self {
        if (!in_array($status, [301,302,303,307,308], true)) throw new \InvalidArgumentException('Invalid redirect status.');
        if ($destination === '' || preg_match('/[\x00-\x20\x7f]/', $destination) || !preg_match('//u', $destination)
            || preg_match('/%(?![a-f0-9]{2})/', $destination)) throw new \InvalidArgumentException('Invalid redirect destination.');
        $parts = parse_url($destination);
        if ($parts === false) throw new \InvalidArgumentException('Invalid redirect destination.');
        $decodedPath = rawurldecode($parts['path'] ?? '');
        if (preg_match('/[\x00-\x1f\x7f]|\\\\/', $decodedPath)) throw new \InvalidArgumentException('Invalid redirect path.');
        if (str_starts_with($destination, '/')) {
            $path = $decodedPath;
            if (str_starts_with($path, '//') || isset($parts['host']) || isset($parts['scheme'])) throw new \InvalidArgumentException('Redirect requires a local absolute path or HTTP(S) URL.');
        } elseif (!in_array(strtolower($parts['scheme'] ?? ''), ['http','https'], true) || !isset($parts['host'])
            || isset($parts['user']) || isset($parts['pass']) || !filter_var($destination, FILTER_VALIDATE_URL)) {
            throw new \InvalidArgumentException('Redirect requires a local absolute path or HTTP(S) URL without credentials.');
        }
        return new self('', $status, ['Location'=>$destination]);
    }
    public const DEFAULT_HEADERS = [
        'Content-Type' => 'text/html; charset=utf-8',
        'X-Content-Type-Options' => 'nosniff', 'Referrer-Policy' => 'strict-origin-when-cross-origin',
        'Content-Security-Policy' => "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'",
    ];
    /** Response headers plus each default whose name (case-insensitive) the response did not set. */
    public function emittedHeaders(): array {
        $headers = $this->headers;
        $set = array_change_key_case($headers);
        foreach (self::DEFAULT_HEADERS as $name => $value) if (!isset($set[strtolower($name)])) $headers[$name] = $value;
        return $headers;
    }
    public function send(bool $head = false): void {
        http_response_code($this->status);
        foreach ($this->emittedHeaders() as $name => $value) header($name . ': ' . $value);
        if (!$head) echo $this->body;
    }
}
