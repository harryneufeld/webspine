<?php
declare(strict_types=1);
namespace Webspine;
/** Immutable HTTP input; route-specific validation and authorization remain explicit. */
final class Request {
    public const MAX_BODY = 65536;
    public readonly string $method;
    public readonly string $path;
    private readonly array $query;
    private readonly array $headers;
    public function __construct(
        string $method, public readonly string $uri, array $headers = [],
        public readonly string $body = '', public readonly ?string $remoteAddress = null,
    ) {
        $this->method = strtoupper($method);
        if (!preg_match('/^[A-Z]+$/D', $this->method) || !str_starts_with($uri, '/') || str_starts_with($uri, '//') || str_contains($uri, '#') || preg_match('/[\x00-\x20\x7f]/', $uri)) throw new HttpError(400, 'Invalid request URI or method.');
        $parts = parse_url($uri);
        if ($parts === false) throw new HttpError(400, 'Invalid request URI.');
        $rawPath = $parts['path'] ?? '/';
        if (preg_match('/%(?![a-f0-9]{2})/i', $rawPath)) throw new HttpError(400, 'Invalid path encoding.');
        $this->path = rawurldecode($rawPath);
        if (preg_match('/[\x00-\x1f\x7f]/', $this->path)) throw new HttpError(400, 'Invalid request path.');
        $query = $parts['query'] ?? '';
        if (strlen($query) > 8192) throw new HttpError(414, 'Query string too long.');
        $this->query = self::parameters($query);
        $normalized = [];
        foreach ($headers as $name => $value) {
            if (!is_string($name) || !is_string($value) || !preg_match("/^[!#$%&'*+.^_`|~0-9A-Za-z-]+$/D", $name) || preg_match('/[\x00-\x1f\x7f]/', $value)) throw new HttpError(400, 'Invalid request header.');
            $normalized[strtolower($name)] = trim($value);
        }
        $this->headers = $normalized;
        if (strlen($body) > self::MAX_BODY) throw new HttpError(413, 'Request body too large.');
    }
    public static function fromGlobals(): self {
        $headers = [];
        foreach ($_SERVER as $key => $value) {
            if (str_starts_with($key, 'HTTP_')) $headers[str_replace('_', '-', substr($key, 5))] = (string) $value;
        }
        foreach (['CONTENT_TYPE','CONTENT_LENGTH'] as $key) if (isset($_SERVER[$key])) $headers[str_replace('_', '-', $key)] = (string) $_SERVER[$key];
        $length = $_SERVER['CONTENT_LENGTH'] ?? null;
        if ($length !== null && (!preg_match('/^[0-9]+$/D', (string) $length) || (float) $length > self::MAX_BODY)) throw new HttpError(413, 'Request body too large or invalid length.');
        $body = file_get_contents('php://input', false, null, 0, self::MAX_BODY + 1);
        if ($body === false) throw new HttpError(400, 'Cannot read request body.');
        return new self($_SERVER['REQUEST_METHOD'] ?? 'GET', $_SERVER['REQUEST_URI'] ?? '/', $headers, $body, $_SERVER['REMOTE_ADDR'] ?? null);
    }
    public function query(?string $name = null, mixed $default = null): mixed { return $name === null ? $this->query : ($this->query[$name] ?? $default); }
    public function header(string $name, ?string $default = null): ?string { return $this->headers[strtolower($name)] ?? $default; }
    public function headers(): array { return $this->headers; }
    public function form(): array {
        if ($this->mediaType() !== 'application/x-www-form-urlencoded') throw new HttpError(415, 'Use application/x-www-form-urlencoded. Multipart/uploads are not supported.');
        return self::parameters($this->body);
    }
    public function json(): array {
        if ($this->mediaType() !== 'application/json') throw new HttpError(415, 'Use application/json.');
        try { $value = json_decode($this->body, true, 16, JSON_THROW_ON_ERROR); }
        catch (\JsonException) { throw new HttpError(400, 'Invalid JSON body.'); }
        if (!is_array($value) || !str_starts_with(ltrim($this->body), '{')) throw new HttpError(400, 'JSON body must be an object.');
        return $value;
    }
    private function mediaType(): string { return strtolower(trim(explode(';', $this->header('Content-Type', ''))[0])); }
    private static function parameters(string $input): array {
        if (preg_match('/%(?![a-f0-9]{2})/i', $input)) throw new HttpError(400, 'Invalid parameter encoding.');
        // Reject before PHP can silently truncate at max_input_vars.
        $limit = min(100, max(0, (int) ini_get('max_input_vars')));
        if ($input !== '' && substr_count($input, '&') + 1 > $limit) throw new HttpError(400, 'Too many parameters.');
        $warning = false;
        set_error_handler(static function () use (&$warning): bool { $warning = true; return true; });
        try { parse_str($input, $values); } finally { restore_error_handler(); }
        if ($warning) throw new HttpError(400, 'Invalid or overly nested parameters.');
        return $values;
    }
}
