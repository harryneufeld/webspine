# Separate the website from its engine

`core/` is the system; `site/` is the website. Changing wording should not
require editing a theme or the framework, and changing a theme should not
discard content.

| Where | What to edit |
| --- | --- |
| `site/meta.php` | Site name, brand parts, SEO description, indexing, language, initial theme |
| `site/errors.json` | Optional data-only visitor error language and copy |
| `site/pages.php` | Page titles, paths, templates, 404 and plain-text pages |
| `site/content/home.php` | Homepage headline, intro, items, labels, links |
| `site/content/page.php` | Shared page navigation labels |
| `site/content/layout.php` | Navigation/footer labels and link destinations |
| `site/routes.php` | Custom URLs and database-page routes |
| `site/install.php` | Explicit, idempotent starter content initialization |
| `site/themes/<id>/` | Markup, shared layout, components, styles, scripts, assets |
| `site/plugins/<id>/` | Custom capabilities and service providers |
| `core/plugins/<id>/` | Bundled system providers; updated with core |
| `core/bin/`, `core/tests/`, `core/docs/` | System tools, tests, and guides |
| `.dist/` | Generated releases; ignored by Git and archives |
| SQLite through Pages | Persistent plain-text pages and titles |
| Entities service | Declared custom records with shared CRUD; see [entities](entities.md) |

Plugin IDs are unique across `core/plugins/` and `site/plugins/`; duplicates are
rejected. A custom provider uses its own ID, selected in `config/local.php`.

Content is plain PHP arrays: trusted code outside `public/`, editable with a
text search, and never a place for secrets. Page language comes from
`site/meta.php` (HTML `lang`); `site/errors.json` supplies visitor error copy
even during startup failures ([language](language.md)). Starter sites stay
`noindex` until `'indexable' => true` ([search indexing](deployment.md#search-indexing)).

Another theme must implement the templates chosen in `site/pages.php` and
consume compatible content keys. Core needs only a shared layout and site
pages; `home.php` and `docs.php` are starter conventions.

## Routes and installation

`site/routes.php` returns a callable receiving `App` and registers GET/POST
routes or trusted regex patterns (`Router::getPattern`/`postPattern`); see
[requests](requests.md). Handlers return `Response`, or `null` for the site's
`not-found` page. `/health` is reserved by core.

Pages may declare `path` and `slash` for automatic GET/HEAD routes. Duplicate
paths, slash aliases and matching patterns fail clearly. See
[routing and redirects](routing.md).

Providers install schema only; the CLI then runs `site/install.php`, which
preserves existing values. Core updates never run the site installer. No
feature plugins are enabled by default; `field-notes` and `catalog` are examples.

## Template data

Template IDs start with a lowercase letter, then lowercase letters, digits or
hyphens (`section2`, `landing-v2`). Legacy leading-hyphen letter-only IDs still
work. A valid but missing template reports `Missing theme template`.

Page templates and layouts run in separate scopes, each receiving `$data` and
aliases such as `$title` and `$body`. Reserved: `$app`, `$site`, `$theme`, `$ui`,
`$directory`, `$file`, `$template`, `$status`, `$data`, `$content`, and names
starting with `__`; read colliding fields via `$data[...]`. `$content` holds the
rendered page HTML in the layout only. Page-local variables do not reach the
layout; pass shared values in render data. Scopes are not a sandbox.

## Reusable components

Components live only in `site/themes/<id>/components/<name>.php` (lowercase
letters, digits, hyphens, starting with a letter). Text stays in content files
and arrives as explicit props:

```php
echo $app->theme->component('wordmark', [
    'href' => '/',
    'prefix' => $site->meta['brand_prefix'],
    'bold' => $site->meta['brand_bold'],
]);
```

A component returns HTML without a layout. It receives `$props` and the helper
`$ui` (`Webspine\ThemeContext`), never page variables, `$app`, `$site` or
services. `$ui` exposes only `component()`, `id()` (active theme) and
`assetUrl()`, and is also available in pages and layouts. Props are not
extracted, so `$props['ui']` cannot replace it. Missing components fail
explicitly. Escape plain text and URLs with `e()`. Keep CSS/JS in theme assets
with scoped classes and unique IDs for repeated interactive instances.
Components are site-owned and preserved by core updates.

Components nest with `$ui->component()`, up to 64 levels; failures restore
renderer-owned output buffers. The Starter's `action-link` renders `action-icon`.

### Caller-supplied child content

Both `component()` methods accept an optional third argument
`?callable $children` (or named `children:`). The wrapper receives the result
as the string `$children`:

```php
<!-- components/card.php -->
<article class="card">
    <h2><?= e($props['title']) ?></h2>
    <?= $children ?>
</article>

<!-- caller -->
<?= $ui->component('card', ['title' => $title], children: function () use ($ui, $caption) { ?>
    <p><?= e($caption) ?></p>
    <?= $ui->component('action-icon') ?>
<?php }) ?>
```

The callback takes no arguments and may emit output, return an HTML string, or
both (output first). `null` adds nothing; other return types fail. It runs
exactly once per call, before the wrapper, even if unused; `$children` is
reused when echoed again. Omitted callbacks give `''`. Props named `children`
or `ui` stay in `$props`. Child HTML is trusted and not sanitized: escape inside
callbacks. Avoid side effects; named slots are not provided. Exceptions and
invalid returns propagate after restoring buffers; callbacks must not close
buffers they do not own.

### Asset URLs

`$ui->assetUrl('style.css')` returns
`/assets/theme/<active-id>/style.css?v=<12-char SHA-256 prefix>`, relative to the
theme's `assets/`. Pass no leading slash, query, fragment or encoding; escape
the result in HTML. Helper and delivery share one allowlist with realpath
containment.

Segments use ASCII letters, digits, `_` and `-`, optionally joined by single
dots (`fonts/a.b.woff2`). Hidden files, traversal, backslashes, trailing or
double dots and executable chains like `file.php.css` are rejected; links must
resolve to allowed files inside `assets/`. Lowercase extensions only:

| Extensions | Content-Type |
| --- | --- |
| css, js | text/css, text/javascript |
| svg, png, jpg/jpeg, webp | image/svg+xml, image/png, image/jpeg, image/webp |
| ico, avif, gif | image/vnd.microsoft.icon, image/avif, image/gif |
| woff2 | font/woff2 |
| pdf | application/pdf |
| txt | text/plain; charset=utf-8 |
| webmanifest | application/manifest+json |

Assets are trusted public theme files, not uploads; contents (SVG, JS, PDF) are
not sanitized. Local errors distinguish `Invalid theme asset path`,
`Unsupported theme asset extension`, `Missing theme asset` and `Cannot read
theme asset`; public failures are plain 404s.

Delivery sends a one-hour cache policy, full SHA-256 ETags and 304s for matching
`If-None-Match`, with `nosniff`. Hashes are cached per lifecycle and in a bounded
private cache; see [asset caching](assets.md).

### Domain-root robots and sitemap routes

Register these in `site/routes.php` with your public absolute URL:

```php
$app->router->get('/robots.txt', static fn() => new \Webspine\Response(
    "User-agent: *\nDisallow:\nSitemap: https://example.com/sitemap.xml\n", 200,
    ['Content-Type'=>'text/plain; charset=utf-8']));
$app->router->get('/sitemap.xml', static fn() => new \Webspine\Response(
    '<?xml version="1.0" encoding="UTF-8"?>'
    . '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">'
    . '<url><loc>https://example.com/</loc></url></urlset>', 200,
    ['Content-Type'=>'application/xml; charset=utf-8']));
```

List only intended canonical URLs and escape dynamic XML. The Starter `noindex`
policy still applies until changed.

### Rendering lifecycle

Theme identity, manifest validation and asset hashes are cached per `App`.
`App::handle()` and `App::health()` start fresh lifecycles. When rendering
manually, call `$app->theme->refresh()` after direct Settings writes, plugin
changes or theme/asset edits; it also clears the private hash cache.
`validate($id)` always validates freshly. Long-lived workers use the normal
entry point or refresh per request.

For manual exports and tests, `'theme' => ['debug' => true]` checks for stale
selection, plugins, manifests and up to 256 observed files at render
boundaries and throws `Stale theme rendering lifecycle` instead of switching
mid-page. Metadata-preserving edits and loaded PHP can still be missed; see
[manual rendering diagnostics](assets.md#manual-rendering-diagnostics).

## Safe framework updates

### Rendering health probes

CLI health, public `/health`, theme selection and updater probes render the
pages in `site/pages.php`. During the probe, PHP warnings and notices become
exceptions and fail health (`@` and `error_reporting()` exclusions still
apply). Local health shows the diagnostic; public health returns
`{"status":"unavailable"}` with HTTP 503. Normal page requests are unaffected.

Health does not crawl asset URLs, plugin routes, mail or workflows; cover those
in site tests. A failed probe blocks updates, rolls back activation, or restores
the previous theme selection.

### Applying updates

Create a site once from a bootstrap ZIP, version your own `site/` and
instructions, then apply core release ZIPs with
`php core/bin/console.php update` ([update](update.md)). Core archives
exclude `site/`, `config/`, `storage/`, `.dist/` and `AGENTS.md`; the updater
rejects site paths and bootstrap ZIPs. Git pulls and archive overlays do not
follow this contract. `core/docs/` is update-owned; site guides belong in `site/`.

## Site tests

Keep website checks in `site/tests/run.php`. They use explicit local config, no
mail and a disposable SQLite database, never private config or real data.
Framework tests use `core/tests/fixtures/website/` and are independent of the
site.
