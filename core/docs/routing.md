# Page paths, canonical slashes and redirects

Website URLs belong to the site's definitions. Core provides explicit routing
tools and does not normalize every incoming URL or redirect arbitrary 404s.
Existing `Router::get/post` and pattern handlers retain their behavior.

## Optional page paths

Declare a page's URL alongside its title/template in `site/pages.php`:

```php
return [
    'home' => ['path'=>'/', 'template'=>'home', 'title'=>'Home'],
    'about' => ['path'=>'/about', 'slash'=>'strip', 'template'=>'page',
        'title'=>'About', 'data'=>['body'=>'About this site.']],
    'guide' => ['path'=>'/guide/', 'slash'=>'append', 'template'=>'section2',
        'title'=>'Guide'],
    'not-found' => ['template'=>'page', 'title'=>'Not found', 'status'=>404,
        'data'=>['body'=>'This address does not exist.']],
];
```

Site registration runs `site/routes.php`, then registers declared paths as
GET/HEAD page routes. Enabled plugins can still add routes; conflicting GET
registrations fail. Omit `path` (or use null without `slash`) for pages reached
only by custom handlers, such as a homepage inspecting legacy query parameters.
The conventional `not-found` page and pages with status 404 cannot declare paths.
An optional slash policy requires a path; declaring it alone fails.

Paths start with `/` and use ASCII letters, digits, hyphens, underscores, dots,
tildes and `/` separators. Root is allowed. Query/fragment text, percent-encoded
declarations, backslashes, duplicate separators and `.`/`..` segments are rejected.
Declare decoded ASCII paths; Request decodes incoming path escapes once. Assets
remain reserved under `/assets`, and the existing `/health` route cannot be
replaced. Paths are case-sensitive. Unicode page slugs require custom routing;
this small declaration API does not introduce a URL encoding or locale system.

The new starter puts its unchanged `/`, `/about` and `/contact` URLs in page
definitions. Existing sites keep `site/pages.php` and `site/routes.php` during core
updates. To adopt a path, remove its equivalent manual GET registration first.
No database routes, site data or pages are migrated automatically.

## Explicit slash policy

`slash` accepts:

- `preserve` (default): register only the declared path. `/about` and `/about/`
  can intentionally serve different pages when both are declared.
- `strip`: require a path without a trailing slash and redirect its single-slash
  alternate to it, e.g. `/about/` to `/about`.
- `append`: require a path ending in `/` and redirect its unsuffixed alternate to
  it, e.g. `/guide` to `/guide/`.

Root stays `/` with any policy and never redirects to an empty path or `//`.
The declared path must match its policy. No policy applies to unrelated exact or
pattern routes, unknown paths, assets, or database pages automatically.

Slash aliases register GET/HEAD and issue **308** redirects. They retain the raw
query suffix, including percent encoding, `+`, repeated parameters and an empty
`?`, without parsing/rebuilding it. They do not reflect host headers. The target
is the declared canonical page, which renders instead of redirecting back.
POST/other method mismatches receive 405; explicit POST handlers at either path
retain their behavior. Canonicalization never silently converts a POST into GET.

Exact GET paths and generated aliases must not collide. A GET pattern matching
a declared path or alias is also rejected, regardless of registration order;
automatic exact routes never silently shadow a catch-all or dynamic handler.
Use manual handlers without a page `path` when intentional overlap is needed.
POST patterns may coexist because method dispatch remains explicit.

For manually wired pages, the same opt-in helper is available:

```php
$app->router->page('/about', fn() => $app->site->render('about'), slash: 'strip');
```

## Validated redirect responses

```php
use Webspine\Response;

return Response::redirect('/new-address', 308);
```

The helper creates an empty-body response with `Location`. The normal response
sender supplies security headers. Allowed statuses are 301, 302 (default), 303,
307 and 308. For method semantics:

- 301/302 mean permanent/temporary relocation; clients may convert POST to GET.
- 303 explicitly directs the client to retrieve the target (typically after a
  successful POST).
- 307/308 mean temporary/permanent relocation preserving the method and body.

Destinations must be local absolute paths beginning with a single `/`, or valid
HTTP(S) URLs without user/password credentials. Scheme-relative authorities,
unsupported schemes, malformed percent escapes, invalid UTF-8 and literal
control/whitespace are rejected. Backslashes and encoded control bytes in the
path are rejected; properly encoded query data remains query data. Encode spaces
in paths with `%20`. The helper preserves the supplied destination; it does not
build a canonical URL, validate a site's approved hosts, or prevent an open
redirect when trusted code supplies an arbitrary external URL from user input.
Keep destinations fixed or enforce your own explicit allowlist.

Permanent redirects may be cached by clients. Use temporary redirects while
testing mappings; choose any extra cache policy explicitly for sensitive flows.
Request-specific redirects can set `Cache-Control: no-store` on the returned
response. No authentication, mutation or CSRF protection is implied by a redirect.

## Fixed WordPress migration maps

Inside a custom homepage GET handler, use known IDs rather than a user-supplied
destination. Omit `path` from the homepage definition when keeping this handler:

```php
$app->router->get('/', static function(\Webspine\Request $request) use ($app) {
    $destinations = ['41'=>'/about', '52'=>'/guide/'];
    $id = $request->query('page_id');
    if (is_string($id) && isset($destinations[$id])) {
        return \Webspine\Response::redirect($destinations[$id], 301);
    }
    return $app->site->render('home');
});
```

For old fixed permalinks, register explicit GET handlers returning redirects to
known pages. Decide deliberately whether old tracking/query data should be kept;
the helper does not copy it automatically. Avoid broad regex replacements based
on arbitrary request values. Business migration maps stay in `site/routes.php`
or a site plugin. Page `path` is a routing declaration, not an automatic SEO
canonical tag, sitemap, multilingual route or absolute-URL generator.

Framework checks cover helper validation, slash/query/root/HEAD behavior, method
boundaries, exact/pattern conflicts, auto registration, unknown pages and digit
template names. Hosting CI verifies redirects and security headers through Apache,
nginx/PHP-FPM and CloudPanel before/after activation and rollback.
