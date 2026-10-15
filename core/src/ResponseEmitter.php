<?php
declare(strict_types=1);
namespace Webspine;

/** Managed emission plus bounded, best-effort presentation lifecycle callbacks. */
final class ResponseEmitter {
    private bool $sending = false;
    private readonly bool $finishFastcgi;
    private readonly int $budgetMs;
    public function __construct(private readonly App $app) {
        $config = $app->config['response'] ?? [];
        if (!is_array($config)) throw new \InvalidArgumentException('Response configuration must be an array.');
        $finish = $config['finish_fastcgi'] ?? false;
        $budget = $config['hook_budget_ms'] ?? 100;
        if (!is_bool($finish) || !is_int($budget) || $budget < 1 || $budget > 1000) {
            throw new \InvalidArgumentException('Response finish_fastcgi must be boolean; hook_budget_ms must be an integer from 1 to 1000.');
        }
        $this->finishFastcgi = $finish; $this->budgetMs = $budget;
    }
    public function send(Response $response, Request $request): void {
        if ($this->sending) throw new \LogicException('Cannot emit another response from response.sent.');
        $this->sending = true;
        try {
            $status = $response->status; $headers = $response->emittedHeaders();
            $head = $request->method === 'HEAD';
            $response->send($head);
            // Everything after emission stays out of the entry point's error-response path.
            set_error_handler(static function (int $severity, string $message, string $file, int $line): bool {
                if (!(error_reporting() & $severity)) return false;
                throw new \ErrorException($message, 0, $severity, $file, $line);
            });
            try {
                try {
                    if (session_status() === PHP_SESSION_ACTIVE && !session_write_close()) throw new \RuntimeException('Cannot close the response session.');
                } catch (\Throwable $error) { $this->log($error, 'session close'); }
                $finished = false;
                try {
                    if ($this->finishFastcgi && function_exists('fastcgi_finish_request')) {
                        $finished = \fastcgi_finish_request();
                        if (!$finished) throw new \RuntimeException('FastCGI completion returned false; using portable fallback.');
                    }
                } catch (\Throwable $error) { $this->log($error, 'FastCGI completion'); }
                try { if (!$finished) flush(); }
                catch (\Throwable $error) { $this->log($error, 'portable flush'); }
                $this->notify(new ResponseSent($request, $status, $headers, $head, $finished));
            } catch (\Throwable $error) { $this->log($error, 'completion'); }
            finally { restore_error_handler(); }
        } finally { $this->sending = false; }
    }
    private function notify(ResponseSent $event): void {
        $deadline = hrtime(true) + $this->budgetMs * 1000000;
        foreach ($this->app->hooks->listeners('response.sent') as $index => $handler) {
            if (hrtime(true) >= $deadline) {
                error_log('webspine response.sent budget exhausted; remaining listeners skipped.');
                break;
            }
            $level = ob_get_level(); $headers = headers_list(); $status = http_response_code();
            // Discard even explicitly flushed listener output, without retaining a large string.
            ob_start(static fn(string $output): string => '', 1);
            set_error_handler(static function (int $severity, string $message, string $file, int $line): bool {
                if (!(error_reporting() & $severity)) return false;
                throw new \ErrorException($message, 0, $severity, $file, $line);
            });
            try { $handler($event); }
            catch (\Throwable $error) { $this->log($error, 'listener ' . ($index + 1)); }
            finally {
                while (ob_get_level() > $level) ob_end_clean();
                restore_error_handler();
                if (!headers_sent()) {
                    header_remove();
                    foreach ($headers as $header) header($header, false);
                    if ($status !== false) http_response_code($status);
                }
            }
            if (hrtime(true) >= $deadline) {
                error_log('webspine response.sent budget exhausted after listener ' . ($index + 1) . '; remaining listeners skipped.');
                break;
            }
        }
    }
    private function log(\Throwable $error, string $phase): void {
        $file = str_replace('\\', '/', $error->getFile());
        $prefix = rtrim(str_replace('\\', '/', $this->app->root), '/') . '/';
        if (str_starts_with($file, $prefix)) $file = substr($file, strlen($prefix));
        error_log('webspine response.sent ' . $phase . ' failed: ' . $error->getMessage() . ' [' . $file . ', line ' . $error->getLine() . ']');
    }
}
