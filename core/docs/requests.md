# Requests and POST routes

Exact handlers receive a `Webspine\Request`. Existing zero-argument handlers
continue to work. Pattern handlers receive `($matches, $request)`.

```php
$app->router->get('/search', function (\Webspine\Request $request) {
    $term = $request->query('q', ''); // Validate type before using it.
    return new \Webspine\Response(is_string($term) ? $term : '', 200,
        ['Content-Type' => 'text/plain; charset=utf-8']);
});
$app->router->post('/save', function (\Webspine\Request $request) {
    $fields = $request->form();
    // Authorize, verify CSRF, validate, then perform the operation.
    return new \Webspine\Response('Saved', 200);
});
```

`method`, `uri` (including query), decoded `path`, raw `body`, and
`remoteAddress` are read-only. Use `query()`, case-insensitive `header()`,
`headers()`, `form()` or `json()`. The peer address comes from `REMOTE_ADDR`;
forwarded headers are not trusted automatically.

Query/form parsing follows PHP semantics: bracket notation produces arrays,
repeated scalar keys keep the last value, and dots/spaces in keys become
underscores. Always validate types. Limits are 8 KiB of query, 64 KiB of body,
and at most 100 query/form fields (or PHP's lower `max_input_vars`). Malformed
input returns 400; oversized query/body returns 414/413. `form()` accepts only
URL-encoded bodies; `json()` accepts JSON objects with depth at most 16.
Other media types return 415. Multipart forms and uploads are unsupported.

GET and POST may share a path. HEAD follows GET; method mismatches return 405
with `Allow`. Unknown GET/HEAD/POST paths use the site's 404 page. Assets and
`/health` remain read-only. Registering POST does **not** supply authentication,
authorization, CSRF, validation, or abuse protection; the endpoint owns them.
Public inquiries can be intentionally anonymous; privileged actions need
authentication and authorization.

For legacy URLs, map known query values to fixed local destinations:

```php
// Inside the site's '/' handler, before rendering its normal homepage:
$destinations = ['41' => '/about'];
$id = $request->query('page_id');
if (is_string($id) && isset($destinations[$id])) {
    return \Webspine\Response::redirect($destinations[$id], 301);
}
```

Never redirect to an arbitrary user-supplied destination. See the optional
[protected contact-form example](examples/contact-form/README.md).

See [page paths, slash policies and migration redirects](routing.md) for the
validated redirect helper, fixed permalink maps, and method-preserving semantics.

## Security headers and Content Security Policy

Every `Response` is sent with these defaults unless it sets the same header
itself (names compare case-insensitively, as in HTTP):

| Header | Default |
|---|---|
| `Content-Type` | `text/html; charset=utf-8` |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Content-Security-Policy` | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'` |

The default CSP allows only same-origin scripts, styles, fonts, frames and form
targets, `data:` images, no inline scripts/styles, and no framing by other sites.
`Response::DEFAULT_HEADERS` holds these values and `emittedHeaders()` returns the
exact headers `send()` will emit, for tests.

To allow an external embed or widget, override the policy only on the response
that needs it, and add only the origins and directives that embed requires.
Copy the full default and extend it, because a CSP header replaces the default
entirely. For example, a video page in `site/routes.php` (remove a `path` for the
same URL from `pages.php`, or the route conflicts):

```php
$app->router->get('/video', static function () use ($app): Response {
    $response = $app->theme->render('page', ['title' => 'Video', 'body' => '', 'page' => '']);
    $response->headers['Content-Security-Policy'] = "default-src 'self'; script-src 'self'; "
        . "style-src 'self'; img-src 'self' data:; font-src 'self'; "
        . "frame-src https://www.youtube-nocookie.com; "
        . "base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    return $response;
});
```

Other security headers keep their defaults. Prefer one specific origin per
directive over wildcards, never add `'unsafe-inline'`/`'unsafe-eval'` or remove
the header to make an embed work, and keep `frame-ancestors 'none'` unless the
site itself must be framed. Each allowed origin receives visitor requests: the
site owner decides whether it is acceptable and covers it in the privacy policy
and any consent the site needs. Emergency 503 pages in `public/index.php` always
use the default policy.
