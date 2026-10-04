# Separate the website from its engine

The ownership boundary is `core/` for the system and `site/` for customization.
Keep the familiar name `site/themes/`: a theme describes presentation. The website
itself lives in `site/`. Changing wording should not require editing a theme or
framework; changing a theme should not silently discard website content.

| Where | What to edit |
| --- | --- |
| `site/meta.php` | Site name, brand parts, SEO description, initial theme |
| `site/pages.php` | Page titles, template choices, 404 and plain-text pages |
| `site/content/home.php` | Homepage copy, headline, subline, labels, links |
| `site/content/docs.php` | The website's documentation text and examples |
| `site/content/layout.php` | Navigation/footer labels and link destinations |
| `site/routes.php` | URLs and mapping routes to site pages |
| `site/install.php` | Explicit, idempotent starter content initialization |
| `site/themes/<id>/` | Presentation markup, shared layout, styles, scripts, assets |
| `site/plugins/<id>/` | Custom capabilities and service providers |
| `core/plugins/<id>/` | Bundled system providers; updated with core |
| `core/bin/`, `core/tests/`, `core/docs/` | System tools, tests, and guides |
| `.dist/` | Generated releases; ignored by Git and excluded from archives |
| SQLite through Pages | Persistent plain-text pages and their titles |

Plugin IDs must be unique across core/plugins/ and site/plugins/. A custom
provider uses its own ID and is selected explicitly in config/local.php;
duplicate IDs are rejected rather than silently overriding system code.

For example, the homepage's subline is `hero_description` and `hero_ownership`
in `site/content/home.php`. Its three headline lines are `headline_line_1`,
`headline_line_2`, and `headline_line_3`. Keep keys stable; change their values.
The browser title is in `site/pages.php`. The theme places and styles those
values; it escapes plain text with `e()`.

Content is deliberately ordinary PHP arrays. No extraction, compilation,
templating dependency, or administration interface is needed. AI can locate a
sentence with a text search and make a small, reviewable edit. These PHP files
execute trusted code and remain outside public/. Do not put secrets in content.

The current content schema reflects this starter website. Another theme must
implement the templates chosen in site/pages.php and consume compatible content
keys, or provide an explicit mapping. A completely different website can replace
these definitions and use different templates. Core requires only a shared
layout and site-defined pages; it does not require home.php or docs.php.

Site routes return a callable receiving App. They can register exact routes or
trusted regex patterns through Router::getPattern. Handlers return Response, or
null for the site-defined not-found page. The core /health route is reserved for
generic status. Site.pages uses a conventional `not-found` entry for 404 output.

Providers install schema only. The CLI then runs site/install.php to initialize
site data through Settings and Pages. Existing values and content are preserved
by this site's installer. Core updates never run the site installer. The
release-download capability is optional and selected through private config;
its fallback copy is site-owned. Remove its navigation links if you disable it.

## Reusable components

Components live only in `site/themes/<id>/components/<name>.php`. Text and data
stay in the existing site content files and are passed as explicit props.
For example, the Studio header and footer share the `wordmark` component:

```php
$copy = $site->content('layout');
echo $app->theme->component('wordmark', [
    'href' => $copy['href_'],
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
