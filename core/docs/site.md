# Separate the website from its engine

The ownership boundary is `core/` for the system and `site/` for customization.
Keep the familiar name `site/themes/`: a theme describes presentation. The website
itself lives in `site/`. Changing wording should not require editing a theme or
framework; changing a theme should not silently discard website content.

| Where | What to edit |
| --- | --- |
| `site/meta.php` | Site name, brand parts, SEO description, indexing, initial theme |
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

Providers install schema only. The CLI then runs site/install.php to initialize
site data through Settings and Pages. Existing values and content are preserved
by this site's installer. Core updates never run the site installer. The
starter enables no feature plugins by default. Optional field-notes and catalog
examples demonstrate hook-based routes and entity declarations.

## Template data

Page templates and layouts run in separate local scopes, each receiving the
original `$data` array and ordinary aliases such as `$title` and `$body`.
Reserved variables are `$app`, `$site`, `$theme`, `$directory`, `$file` (the
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

The renderer returns HTML without a page layout. Templates receive an explicit
`$props` array and escape plain-text values with `e()`; they do not inherit page
variables. Names use lowercase letters, digits, and hyphens, starting with a
letter. Missing components fail explicitly. Component CSS/JS remains in the
theme's assets with scoped classes and unique IDs for repeated interactions.
Reuse useful patterns without requiring a component for every element. These
are server-rendered PHP templates, with no frontend runtime or build step.
Component templates are site-owned and preserved by core updates.

## Safe framework updates

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
