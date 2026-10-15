# After-response lifecycle

Plugins can register short, best-effort work with the existing Hooks mechanism:

```php
use Webspine\ResponseSent;

$app->hooks->on('response.sent', static function (ResponseSent $event): void {
    // Local, bounded housekeeping. Do not emit response content here.
    // $event->request->method, $event->request->path, $event->status
});
```

The public entry point uses `$app->send($response, $request)`. Custom entry points
can use the same method after `$app->handle($request)`. `handle()` only returns a
response: rendering, health probes and tests that do not send it fire no event.
Direct `Response::send()` retains its existing behavior and does not dispatch
App hooks. There is no shutdown fallback or automatic background worker.

## Guarantee and context

`response.sent` means `Response::send()` returned after asking PHP to emit the
status, headers and (except for HEAD) body. PHP emission, web-server/proxy flush,
FastCGI completion, and receipt by a browser are different events. The hook does
not guarantee client delivery, asynchronous execution or durable completion.

Each managed `App::send()` invocation dispatches once, in listener registration
order, subject to the soft budget below. Sending the same Response object again
is another emission with its own dispatch; it is not an idempotency mechanism.
The listener list is snapshotted when dispatch starts; registrations during a
callback apply to future emissions. Calling `App::send()` recursively from a
listener fails before emitting another response and is logged as a hook failure.

Each listener receives one read-only `Webspine\ResponseSent` value:

| Property | Meaning |
| --- | --- |
| `request` | Original Request, including the original GET/HEAD/POST method |
| `status` | Response status captured before emission |
| `headers` | Response-declared headers merged with framework defaults, captured before emission |
| `head` | True for HEAD; no body was emitted by the sender |
| `fastcgiFinished` | True only if the opt-in FastCGI completion function returned true |

The context contains no mutable Response or response body. The header snapshot
excludes headers generated separately by PHP sessions, servers and proxies.
Request data can be sensitive; plugins decide what they may record or retain.

The public entry point emits the event for normal responses, redirects, assets,
404/405 and handled HTTP errors. An unexpected handler exception produces the
existing generic 503, then dispatches once for that 503 if App and Request were
successfully constructed. Startup/bootstrap/configuration failures before that
point have no App hook context. A send that throws before returning has no event.
Handler `exit`/`die`, process termination and fatal failures can bypass dispatch;
the lifecycle does not synthesize an event from a shutdown function.

## Completion, sessions and locks

The default portable path closes an active PHP session after emission, then
calls `flush()` before hooks. User output buffers, the server, and proxies may
still retain the response; work can delay request completion on Apache,
the development server and other non-FPM runtimes. See PHP's
[flush documentation](https://www.php.net/manual/en/function.flush.php).

For PHP-FPM, opt in through private configuration:

```php
'response' => [
    'finish_fastcgi' => true, // Default false; ignored when unavailable.
    'hook_budget_ms' => 100,  // Default 100; integer from 1 through 1000.
],
```

With this option, the sender attempts
[`fastcgi_finish_request()`](https://www.php.net/manual/en/function.fastcgi-finish-request.php)
after session close and before hooks. A false return or exception is logged and
uses the portable flush path; unsupported runtimes also use the portable path.
`fastcgiFinished` reports the function result, not browser receipt. FPM still
uses the current PHP worker for callbacks; keep worker capacity and hosting
timeouts in mind. There is no process fork, queue or retry.

[`session_write_close()`](https://www.php.net/manual/en/function.session-write-close.php)
runs before completion/hooks so normal subsequent requests do not wait on a
session lock. Later changes to `$_SESSION` are not automatically persisted.
Failures to close the session are logged; the event still runs, so a failing
custom session handler can retain its own locks. Plugins must release their
application/database locks and complete transactions before returning a response.
They must not reopen sessions or hold long transactions in a callback.

The public entry point deliberately holds its shared framework update lock
until callbacks return. Releasing it while plugin/framework PHP still executes
would let activation replace files in use. An update waits for that work, just
as it waits for request handlers and queue workers. Custom entry points own the
same locking obligations; `App::send()` does not acquire an update lock itself.

## Failure handling and execution bounds

Each callback has an output-discarding buffer. Its exceptions and reported PHP
warnings/notices are logged privately with listener number, message and source
file/line; they do not enter the entry point's 503 fallback, append diagnostic
text, retry work, or stop subsequent callbacks within the budget. Headers/status
are restored if PHP has not committed them; committed HTTP output cannot be
changed. Emitting a new response from a listener is rejected.

The budget is a monotonic, **soft total dispatch deadline**. It is checked before
and after each listener. A callback that returns late is logged and remaining
listeners are skipped. The framework cannot interrupt arbitrary PHP, blocking
I/O, `exit`, or fatal errors; one callback can exceed the deadline. The sender
does not extend PHP time limits or change disconnect handling. Operators must
bound blocking I/O and configure PHP/server/worker timeouts, including their
post-FastCGI behavior. No wall-clock completion guarantee is made.

Callbacks are trusted plugin PHP, not sandboxed code. They must not close the
sender/caller's output buffers, replace global error handlers, send headers,
call `exit`, or schedule output in shutdown callbacks. Ordinary output is
discarded even if a listener flushes its own nested buffer. Error handlers and
renderer-owned buffers are restored when a callback returns or throws normally.

Use the existing [durable job queue](queue.md), an appropriate cron worker,
idempotent handlers and retry policy for important mail or business side effects.
Enqueue required work before responding. After-response callbacks and PHP
shutdown functions can be skipped or interrupted and are not durable jobs.
