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
