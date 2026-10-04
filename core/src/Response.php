<?php
declare(strict_types=1);
namespace Webspine;
final class Response {
    public function __construct(public string $body = '', public int $status = 200, public array $headers = []) {}
    public static function json(array $value, int $status = 200): self {
        return new self(json_encode($value, JSON_THROW_ON_ERROR), $status, ['Content-Type' => 'application/json; charset=utf-8']);
    }
    public function send(bool $head = false): void {
        http_response_code($this->status);
        $headers = $this->headers + [
            'Content-Type' => 'text/html; charset=utf-8',
            'X-Content-Type-Options' => 'nosniff', 'Referrer-Policy' => 'strict-origin-when-cross-origin',
            'Content-Security-Policy' => "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'",
        ];
        foreach ($headers as $name => $value) header($name . ': ' . $value);
        if (!$head) echo $this->body;
    }
}
