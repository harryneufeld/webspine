# Separate the website from its engine

The ownership boundary is `core/` for the system and `site/` for customization.
Keep the familiar name `site/themes/`: a theme describes presentation. The website
itself lives in `site/`. Changing wording should not require editing a theme or
framework; changing a theme should not silently discard website content.

| Where | What to edit |
| --- | --- |
| `site/meta.php` | Site name, brand parts, SEO description, indexing, language, initial theme |
| `site/errors.json` | Optional data-only visitor error language and copy |
| `site/pages.php` | Page titles, template choices, 404 and plain-text pages |
| `site/content/home.php` | Homepage copy, headline, subline, labels, links |
| `site/content/page.php` | Shared page navigation labels |
| `site/content/layout.php` | Navigation/footer labels and link destinations |
| `site/routes.php` | URLs and mapping routes to site pages |
| `site/install.php` | Explicit, idempotent starter content initialization |
| `site/themes/<id>/` | Presentation markup, shared layout, styles, scripts, assets |
| `site/plugins/<id>/` | Custom capabilities and service providers |
| `core/plugins/<id>/` | Bundled system providers; updated with core |
| `core/bin/`, `core/tests/`, `core/docs/` | System tools, tests, and guides |
| `.dist/` | Generated releases; ignored by Git and excluded from archives |
| SQLite through Pages | Persistent plain-text pages and their titles |
| Entities service | Declared custom records with shared CRUD; see entities.md |

Plugin IDs must be unique across core/plugins/ and site/plugins/. A custom
provider uses its own ID and is selected explicitly in config/local.php;
duplicate IDs are rejected rather than silently overriding system code.

Edit `headline`, `intro`, and `items` in `site/content/home.php`. The browser title
is in `site/pages.php`; identity is in `site/meta.php`. The Starter theme places
and escapes these values. The repository's example site is intentionally small;
webspine.org's marketing content and design are maintained separately.

Declare page language in `site/meta.php`; the Starter layout uses its escaped
value for HTML `lang`. Optional `site/errors.json` supplies visitor error copy
even during startup failures. See [site language and visitor errors](language.md).

Before launching a Starter site, set `'indexable' => true` in `site/meta.php`.
Until then its shared layout emits `noindex` and shows a reminder. Custom themes
own this behavior; see [search indexing](deployment.md#search-indexing).

Content is deliberately ordinary PHP arrays. No extraction, compilation,
templating dependency, or administration interface is needed. AI can locate a
sentence with a text search and make a small, reviewable edit. These PHP files
execute trusted code and remain outside public/. Do not put secrets in content.

The current content schema reflects this starter website. Another theme must
implement the templates chosen in site/pages.php and consume compatible content
keys, or provide an explicit mapping. A completely different website can replace
these definitions and use different templates. Core requires only a shared
layout and site-defined pages; it does not require home.php or docs.php.

Site routes return a callable receiving App. Register GET/POST routes or trusted
regex patterns through Router::getPattern/postPattern. See [request context and
protected POST examples](requests.md). Handlers return Response, or
null for the site-defined not-found page. The core /health route is reserved for
generic status. Site.pages uses a conventional `not-found` entry for 404 output.

Pages may declare an optional `path` and `slash` policy for automatic GET/HEAD
registration. The starter declares its three page paths in `site/pages.php`;
database/custom routes remain in `site/routes.php`. Existing sites without page
paths keep their manual routes. Duplicate paths, slash aliases and matching GET
patterns fail clearly rather than silently replacing a handler. See
[routing and redirects](routing.md) for adoption and migration examples.

Providers install schema only. The CLI then runs site/install.php to initialize
site data through Settings and Pages. Existing values and content are preserved
by this site's installer. Core updates never run the site installer. The
starter enables no feature plugins by default. Optional field-notes and catalog
examples demonstrate hook-based routes and entity declarations.

## Template data

Template identifiers start with a lowercase letter followed by lowercase letters,
digits or hyphens, such as `section2` or `landing-v2`. Existing leading-hyphen
identifiers containing only lowercase letters/hyphens remain accepted for
compatibility; prefer the letter-first grammar for new files. Dots, separators,
underscores, uppercase letters and leading digits remain invalid. A valid but
missing template retains the `Missing theme template` diagnostic.

Page templates and layouts run in separate local scopes, each receiving the
original `$data` array and ordinary aliases such as `$title` and `$body`.
Reserved variables are `$app`, `$site`, `$theme`, `$ui`, `$directory`, `$file` (the
current file), `$template`, `$status`, `$data`, `$content`, and names starting
with `__`. Access colliding fields explicitly, e.g. `e($data['content'])`.
`$content` is empty in the page and contains rendered page HTML in the layout.

Page-local assignments do not reach the layout. Pass shared values in the render
data instead; themes that relied on leaked page variables must adapt. Escape
plain-text data with `e()`; only rendered page HTML belongs in `$content`.
Scope isolation is not a sandbox: PHP themes remain trusted executable code.

## Reusable components

Components live only in `site/themes/<id>/components/<name>.php`. Text and data
stay in the existing site content files and are passed as explicit props.
For example, the Starter header and footer share the `wordmark` component:

```php
$copy = $site->content('layout');
echo $app->theme->component('wordmark', [
    'href' => '/',
    'prefix' => $site->meta['brand_prefix'],
    'bold' => $site->meta['brand_bold'],
]);
```

The renderer returns HTML without a page layout. Components receive an explicit
`$props` array and a `Webspine\ThemeContext` helper named `$ui`; they do not inherit
page variables, `$app`, `$site`, or services. `$ui` is also available in page
templates and layouts. It exposes only `component()`, `id()` and `assetUrl()`.
Escape plain-text values and asset URLs with `e()`. Props are not extracted, so
`$props['ui']` cannot replace the helper. Names use lowercase letters, digits, and
hyphens, starting with a letter. Missing components fail explicitly. Component CSS/JS remains in the
theme's assets with scoped classes and unique IDs for repeated interactions.
Reuse useful patterns without requiring a component for every element. These
are server-rendered PHP templates, with no frontend runtime or build step.
Component templates are site-owned and preserved by core updates.

A component can now render its own children, with separate props for each call:

```php
<!-- components/button.php -->
<button type="button">
    <?= $ui->component('icon', ['label' => $props['icon_label']]) ?>
    <?= e($props['label']) ?>
</button>

<!-- components/icon.php -->
<img src="<?= e($ui->assetUrl('images/icon.svg')) ?>" alt="<?= e($props['label']) ?>">
```

`$ui->id()` returns the active theme identity. The Starter's `action-link` and
`action-icon` demonstrate nesting without changing their markup or behavior.
Nesting is limited to 64 components to catch runaway recursion; missing children
and thrown exceptions fail explicitly and restore renderer-owned output buffers.
Existing components using only `$props` continue to work, including a manually
passed renderer prop. PHP themes remain trusted code, not a sandbox.

### Asset URLs

`$ui->assetUrl('style.css')` (or `$app->theme->assetUrl('style.css')`) returns
`/assets/theme/<active-id>/style.css?v=<12-character-SHA-256-prefix>`.
Paths are relative to the active theme's `assets/` directory. Invalid, missing,
unreadable, executable or escaping paths fail explicitly; the helper and asset
delivery use the same allowlist and realpath containment checks. Do not supply a
leading slash, query string, fragment, or URL-encoded path to the helper. Escape
the returned URL when placing it in HTML.

Directory and filename segments use ASCII letters, digits, underscores and
hyphens, optionally separated by single dots. For example, `fonts/a.b.woff2` and
`images.v2/logo.dark.avif` are valid. Hidden files, empty/dot/traversal segments,
backslashes, trailing/consecutive dots and executable suffix chains such as
`file.php.css` are rejected. Extensions are lowercase and case-sensitive:

| Extensions | Content-Type |
| --- | --- |
| css, js | text/css, text/javascript |
| svg, png, jpg/jpeg, webp | image/svg+xml, image/png, image/jpeg, image/webp |
| ico, avif, gif | image/vnd.microsoft.icon, image/avif, image/gif |
| woff2 | font/woff2 |
| pdf | application/pdf |
| txt | text/plain; charset=utf-8 |
| webmanifest | application/manifest+json |

MIME types follow the [IANA registry](https://www.iana.org/assignments/media-types/).
Asset files are trusted, public theme resources, not an upload area; the framework
does not validate their contents or sanitize SVG, JavaScript, PDFs or manifests.
Keep private documents/configuration outside assets, even with allowed extensions.
Contained links must resolve to allowed asset files; links outside the assets
directory and links disguising executable/unsupported targets are rejected.

Local helper exceptions distinguish `Invalid theme asset path`, `Unsupported
theme asset extension`, `Missing theme asset`, and `Cannot read theme asset`.
Public asset failures remain generic 404s without those diagnostics. HTTP paths
are decoded once by Request; encoded traversal and executable suffixes remain
invalid. Successful responses keep `nosniff` through the normal response sender.

Each asset hash/URL is reused within a rendering lifecycle. Across lifecycles,
a bounded private metadata cache avoids rehashing unchanged files. See
[asset caching and conditional delivery](assets.md) for its invalidation rules,
read-only fallback, measurement and optional disabling.

PHP delivery retains its one-hour cache policy and adds full SHA-256 ETags.
Matching `If-None-Match` GET/HEAD requests return 304 without a body; tags always
describe the actual response bytes independently of the URL hash cache. Asset
delivery still reads and hashes bytes, including for HEAD/304. This reduces
transfer size; it does not implement streaming, static publishing or offloading.

### Domain-root robots and sitemap routes

These URLs belong to `site/routes.php`, not to a theme's assets directory. For a
site served at the domain root, register explicit GET routes and use your site's
public absolute URL in the sitemap. HEAD is handled by the normal entry point:

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

Keep URL lists and indexing decisions site-owned. Escape dynamic XML values and
list only intended public canonical URLs. A robots file is not access control;
Starter's default noindex policy still applies until explicitly changed. No
robots file, sitemap generator or subdirectory-hosting support is added by these
examples. See [deployment indexing guidance](deployment.md#search-indexing).

### Rendering lifecycle

The Theme instance caches active identity and successful manifest validation.
Nested components therefore do not repeatedly query Settings or parse theme.json.
`App::handle()` begins a fresh lifecycle for each request; `App::health()` refreshes
before probing so CLI theme selection and failed-selection restoration inspect
current settings/files. Each App owns its cache; it is never shared globally.

When manually rendering several pages/components on one App, they share the cache.
After direct Settings changes, plugin/dependency changes or theme/asset file edits,
call `$app->theme->refresh()` before the next render. This also clears the private
hash cache, covering replacements that preserve filesystem metadata.
`validate($id)` always performs
a fresh explicit validation. Long-lived workers must use the normal request entry
point or refresh before each manually managed request. Theme/content changes
within an ongoing render should be applied between pages, followed by refresh.

For manual exports/tests, opt in with `'theme' => ['debug' => true]` in local
configuration. At outer `render()`, `component()` and standalone `assetUrl()`
boundaries, debug checks the cached selection, loaded plugin state, manifests and
observed files. A supported change throws `Stale theme rendering lifecycle` with
the remedy; it never silently switches identity midway through a page. Call
`refresh()` before the next page, or create a new App after plugin changes.
Debug is disabled by default. It checks at most 256 observed files; ordinary
asset/template checks use timestamps/size/identity and can miss metadata-preserving
edits. Manifest checks also hash content (up to 1 MiB each). Unobserved files,
already loaded PHP/opcache and changes during one render need explicit handling.
See [lifecycle and cache boundaries](assets.md#manual-rendering-diagnostics).

Compatibility: `$ui` is now reserved in page/layout scope; a colliding data value
remains available in `$data['ui']`. Components never extract props. A component
that supplied its own local `$ui` may rename that variable to use the helper.

## Safe framework updates

### Rendering health probes

CLI health, public `/health`, theme selection and updater health probes render the
pages configured in `site/pages.php`. During that probe, reported PHP warnings
and notices (`E_WARNING`, `E_NOTICE`, `E_USER_WARNING`, `E_USER_NOTICE`) become
exceptions and fail health. For example, a raw template `file_get_contents()` or
`hash_file()` of a missing asset can no longer leave a successful rendering probe.
Local health includes the diagnostic and source location; public health returns
only `{"status":"unavailable"}` with HTTP 503. CLI health exits unsuccessfully.

The temporary handler is restored on success or failure, and renderer-owned output
buffers are cleaned up. `@` suppression and `error_reporting()` exclusions remain
effective; unrelated severities such as deprecations retain the caller's handler
behavior. Ordinary page requests do not install this probe handler. Treat that
boundary deliberately: suppressing a warning in a template also hides it from
the rendering probe.

Health does not crawl browser asset URLs or automatically test every plugin route,
mail service or business workflow. Use explicit `assetUrl()` calls for validated
asset references and site tests for other routes/behaviors. A failed candidate
probe blocks updates; a failed post-activation probe restores the previous core;
a failed theme-selection probe restores the previous selection.

### Applying updates

1. Start a new website from a full bootstrap ZIP.
2. Customize and version your own site/ and instructions.
3. Apply trusted newer core-release ZIPs through `php core/bin/console.php update`.

Core release archives exclude site/, config/, storage/, .dist/,
and the site's root AGENTS.md. The updater rejects attempts to include site paths,
checks candidate rendering against the installed site's existing definitions,
and preserves its content through activation and rollback. Full bootstrap ZIPs
include starter site/theme/provider files for creating a new website and are
rejected by the core updater.

An upstream Git pull or archive overlay does not follow this preservation
contract. Use a separate checkout to develop the framework; use core release
archives to update a customized installation. Framework documentation in core/docs/
is update-owned; website-specific guides belong in site/.

## Site tests

Keep website checks in `site/tests/run.php` and run `php site/tests/run.php`.
The starter uses explicit local configuration, no mail, and a disposable SQLite
database; it never loads private config or modifies your site data. Update these
checks as you customize the site. Framework checks (`php core/tests/run.php`)
use dedicated fixtures in `core/tests/fixtures/website/` and remain independent
of your theme, content, plugins, and private configuration.
