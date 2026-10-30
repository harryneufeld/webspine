# Working on webspine

webspine is an open-source website foundation for AI to build and maintain.
Inspect existing work before editing, make scoped changes, and protect secrets.

## Where things go

| Path | Purpose |
| --- | --- |
| `site/meta.php` | Site identity, SEO description, language, indexing, theme |
| `site/pages.php` | Page titles, paths, templates, plain-text pages, 404 copy |
| `site/content/` | Editable copy, e.g. `headline`, `intro`, `items` in `home.php` |
| `site/routes.php` | Custom URLs and database-page routing |
| `site/install.php` | Idempotent starter-data initialization |
| `site/themes/<id>/` | Layout, templates, `components/`, scoped CSS/JS, assets |
| `site/plugins/<id>/` | Custom capabilities, providers, entities |
| `site/tests/` | Site-specific checks |
| `config/local.php` | Private configuration; never expose or commit it |
| `storage/` | Persistent data, journals, backups |
| `public/` | Only web document root; no site logic here |
| `core/` | Framework; do not edit it for website work |

Keep content keys stable when changing wording. Text belongs in content
files; presentation belongs in the theme.

## Rules

- PHP 8.3+, HTML, CSS, vanilla JS. No required Composer, npm, frontend
  framework, or build step.
- Escape plain-text values and URLs with `e()`; no trusted HTML in content.
- Reuse components before duplicating markup; pass data as explicit props.
- Custom entities go in `site/plugins/<feature>/` via the Entities service.
- New mutation endpoints need validation, authorization and CSRF protection.
- Preserve the theme identity, responsive behavior, accessibility, and
  reduced-motion support. Follow [design guidelines](core/docs/design.md).
- Starter pages are `noindex` until `'indexable' => true` in `site/meta.php`;
  set it only when the user is ready to be found by search engines.
- Develop and test locally with disposable data, never production services.
- Third-party libraries need recorded version, license, source and checksum.
- Do not claim planned features exist.

## Verify

```sh
php core/bin/console.php install
php core/bin/console.php health
php core/tests/run.php
php site/tests/run.php
php -S localhost:8080 -t public public/router.php
```

Lint changed PHP files. Check changed pages in the browser, including mobile
widths. Extend `site/tests/` as the site grows. Hosting or routing configuration
changes also need `sh core/tests/hosting/run.sh` (Docker). Report checks run and gaps.
If `php` is missing on this development checkout, use `.tools/php/php.exe`.

## Deployment and updates

The agent may deploy and update production when the user authorizes it, and
handles all technical steps itself. Follow [publishing](core/docs/publishing.md)
for deployment and [update](core/docs/update.md) for framework updates and
migration notes.

## Guides

[site](core/docs/site.md) · [components and assets](core/docs/site.md#reusable-components) ·
[routing](core/docs/routing.md) · [requests](core/docs/requests.md) ·
[entities](core/docs/entities.md) · [design](core/docs/design.md) ·
[publishing](core/docs/publishing.md) · [deployment](core/docs/deployment.md) ·
[update](core/docs/update.md) · [architecture](core/docs/architecture.md)
