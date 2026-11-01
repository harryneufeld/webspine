# Public forms

`Webspine\Forms\FormGuard` protects a form that anyone may POST to without logging
in: its own session, CSRF token, honeypot, field whitelist, per-IP rate limit and
optional minimum fill time. It works without JavaScript and under the default CSP.
Authentication and authorization for privileged actions stay separate.

```php
use Webspine\Forms\{FormGuard, FormResult};
use Webspine\Response;

$guard = new FormGuard($app->root, 'inquiry', [
    'path' => '/inquiry',
    'fields' => ['name', 'email', 'message'],
    'min_seconds' => 3,
]);
$app->router->get('/inquiry', fn() => $guard->show(fn(FormResult $form) => render($form->token, $form->flash)));
$app->router->post('/inquiry', fn(\Webspine\Request $request) => $guard->submit($request, function (FormResult $form) {
    if ($form->reason === 'limited') return new Response('Too many attempts.', 429);
    if ($form->reason === 'replayed') return new Response('', 303, ['Location' => '/inquiry']);
    if ($form->reason === 'rejected') return new Response('Rejected.', 422, ['Content-Type' => 'text/plain; charset=utf-8']);
    if (!$form->ok) return render($form->token, null, $form->fields, $form->reason, $form->status);
    // Validate $form->fields, then do the work. Errors re-render with the same token.
    $form->complete('sent');
    return new Response('', 303, ['Location' => '/inquiry']);
}));
```

The template needs the token and the honeypot. The `hidden` wrapper keeps it from
people and needs no CSS:

```php
<input type="hidden" name="csrf" value="<?= e($token) ?>">
<div hidden><label>Leave empty <input name="website" tabindex="-1" autocomplete="off"></label></div>
```

## Options

| Option | Default | Meaning |
|---|---|---|
| `path` | required | Form URL; also the session cookie path |
| `fields` | required | Allowed POST keys, without token and honeypot |
| `honeypot` | `website` | Honeypot field name |
| `min_seconds` | `0` | Minimum fill time; try 3 for short forms, 10 for long ones |
| `token_ttl` | `1200` | Token lifetime in seconds, 60 to below 1800 |
| `secure_cookie` | `true` | Set `false` only for local HTTP fixtures |
| `max_attempts`, `window` | `5`, `600` | POSTs per client per window |

The name selects the session `webspine_<name>` and private files under
`storage/forms/<name>/`. Each response gets `Cache-Control: no-store`.

## Order of checks

Malformed requests fail first through `Request::form()` (400, 413, 415). The handler
then receives one result:

| `reason` | `status` | When | `fields` | Token |
|---|---|---|---|---|
| `limited` | 429 | Per-IP budget used; no session is opened, `Retry-After` is added | empty | none |
| `invalid` | 403 | Missing or foreign token, or a cross-site `Sec-Fetch-Site` | empty | new |
| `replayed` | 303 | This session already completed these exact values with this token | empty | current |
| `expired` | 403 | The visitor's own token expired | kept | new |
| `rejected` | 422 | Honeypot filled, unknown key, or non-string value | empty | current |
| `toofast` | 422 | Faster than `min_seconds`; one more click succeeds | kept | current |
| `null` | none | All passed (`ok` is true) | trimmed | current |

Input is never redisplayed from a foreign POST, so another site cannot prefill the
form. The fill-time clock survives a token renewal and restarts after success.
`status` is a suggestion: the handler builds the `Response`, and exceptions such as
`HttpError` pass through.

`complete()` retires the token, sets the flash shown once by the next `show()` and
keeps a ten-minute receipt of hashes, so a double-click ends in `replayed`. Skip it
when the work fails and the visitor can retry with the same token. The session stays
open while the handler runs, which makes concurrent submissions of a session wait.
Keep the handler short, and do slow work such as sending mail in the
[queue](queue.md).

Field rules stay in the plugin. `FormText::safe($value, $multiline)` accepts valid
UTF-8 without control characters; `FormText::units()` counts like HTML `maxlength`.
Neither needs `mbstring`. Fields are trimmed of whitespace only, so check them with
`safe()`. A checkbox is true only for the exact value you render, such as `'1'`.

## More rate limits

Check per-address and global limits in the handler, after validation, so a typo costs
nothing. A refused call counts in no bucket:

```php
if (!$guard->limiter()->allow([
    ['kind' => 'mail', 'subject' => strtolower($email), 'max' => 1, 'window' => 3600],
    ['kind' => 'global', 'subject' => 'day', 'max' => 200, 'window' => 86400],
])) return new Response('Please try again later.', 429);
```

`RateLimiter` also works alone: `new RateLimiter($app->root, 'storage/my-feature/rate.json')`.
`RateLimiter::client()` normalizes a peer address and counts IPv6 per /64.

## Deployment

- Exclude the form URL from page caches such as Varnish. `no-store` should be
  enough, but a cache that ignores it shares one visitor's token with everyone.
- The guard uses `REMOTE_ADDR` and never trusts forwarded headers. Behind a proxy,
  configure the server's real-IP handling, or all visitors share one budget.
- `storage/` must stay writable and outside `public/`.

## Limits

This stops simple bots and casual abuse, not a targeted attacker who loads the form,
waits and posts. There is no CAPTCHA, proof of work or address confirmation; use a
confirmation link when a form mails the address it receives, plus a global bucket.

- Fixed windows allow a burst of up to twice the limit around a boundary.
- When the limiter is full (4,096 keys), new clients are refused until records expire.
- Salted hashes of IP and mail addresses are pseudonymous personal data, not
  anonymous. They are removed once their window has passed and another call arrives.
- Each GET without a cookie creates a session file until garbage collection.
- Only one PHP session can be active. The guard refuses to start inside another, and
  session checks after CLI output must run in a subprocess or HTTP fixture.
