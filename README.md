# web**spine**

**Your own AI-driven website.**

A lean, open-source foundation for building and maintaining your website with
your choice of AI. Clear boundaries between content, themes, components, and
plugins give your AI guardrails to keep the code organized and maintainable.

Built with plain PHP, HTML, CSS, and JavaScript. No frontend framework, Node.js
runtime, or build step—just straightforward deployment on a PHP web server with
SQLite support.

You own the code and can keep developing it independently of future webspine
releases.

## Start

Enable PHP extensions `pdo_sqlite`, `zip` (release tooling), and `openssl` (SMTP).
From the extracted project directory:

```sh
php core/bin/console.php install
php core/bin/console.php health
php -S localhost:8080 -t public public/router.php
```

Visit http://localhost:8080. The minimal Starter theme includes a homepage,
About and Contact pages, a shared layout, and reusable components. It uses system
fonts and works without JavaScript. Replace the example content with your own.
The marketing website at webspine.org is maintained separately and is not shipped.

Copy `config/example.php` to `config/local.php` to customize providers. Do not
commit credentials. The example configuration works with local SQLite and no mail.
Install is explicit and idempotent. Normal requests never install or migrate.
`health` diagnoses configuration, storage, and rendering problems; it does not
repair them or install data. For an unavailable site, run it and inspect the PHP
error log. Configuration load failures include file/line context in private CLI
diagnostics and logs; public errors remain generic. See [diagnostics](core/docs/deployment.md#diagnosing-an-unavailable-site).
No feature plugins are enabled by default. Optional examples in `site/plugins/`
show a hook-based route (`field-notes`) and entity declarations (`catalog`).
The optional bundled [spineanalytics plugin](core/plugins/spine-analytics/README.md)
provides aggregate request counts and a private dashboard. Enable it explicitly
in `config/local.php`; dashboard authentication uses a configured password hash.

## Boundaries

`core/` is framework-owned; `site/` is content, identity, titles, and routes;
`site/themes/` is presentation; `site/plugins/` is capability.
`config/` is private, `storage/` is persistent, and **only `public/` is web-facing**.
`core/bin/`, `core/tests/`, and `core/docs/` contain system tools, checks, and guides.
`core/plugins/` contains bundled system providers and optional features. Custom extensions belong in
`site/plugins/`. Generated release ZIPs live in `.dist/`, which is ignored by Git
and excluded from release inventories; packaging creates it when needed.

Providers implement PHP service interfaces. Features consume those contracts
through an explicit registry. Manifests declare identity, version, API, and
dependencies. PHP extensions execute trusted code; they are not sandboxed.

SQLite is intended for local disk and modest write concurrency. Future database
adapters own their SQL, migrations, and data-transfer tooling.

Edit the homepage headline and subline in `site/content/home.php`, page titles
in `site/pages.php`, and identity in `site/meta.php`. Theme templates escape and
present those values. Providers install schema; `site/install.php` seeds this
website explicitly and idempotently. See `AGENTS.md` and `core/docs/site.md`.

Reusable presentation components live in `site/themes/<id>/components/`.
Pass text/data from existing site content files and render them through
`$app->theme->component('name', $props)` with explicit inputs. Components receive
`$ui` for nested `component()` calls, active theme `id()`, and content-versioned
`assetUrl()` paths, without inheriting page variables. The same helper is available
to pages and layouts. The Starter action link renders its own icon and its layout
uses shared asset URLs. Safe dotted asset names and explicit image/font/document
MIME types share validation between URL helpers and delivery. See
[asset formats and diagnostics](core/docs/site.md#asset-urls) and
[components and rendering lifecycle](core/docs/site.md#reusable-components).

## Commands

```sh
php core/bin/console.php theme starter
php core/bin/console.php entities:install  # explicitly install enabled entity declarations
php core/bin/console.php package --full    # downloadable bootstrap, includes theme/providers
php core/bin/console.php package           # core-only update archive
php core/bin/console.php update /path/to/newer-core-release.zip
php core/bin/console.php rollback
php core/bin/console.php recover           # interrupted activation recovery
php core/tests/run.php               # framework checks with independent fixtures
php site/tests/run.php               # checks for your customized website
```

Core updates validate exact inventories, hashes, PHP/API compatibility, and
candidate health. They stage changes, back up managed files, hold an activation
lock, check active health, and roll back failures. Preserve site files, themes, custom plugins,
configuration, and data. Do not migrate databases. Hashes are integrity checks,
**not release authentication**; apply only trusted local archives. Signed online
updates are planned. Drain production workers and reset OPcache after activation.

Create a website from a bootstrap once; update the framework using core releases.
Do not overlay the upstream repository or another full bootstrap onto a customized
site. Git pulls use merge rules and do not offer the core updater's preservation
guarantee. The site's root `AGENTS.md` is preserved; framework docs and README are
update-owned.

Bundled dependencies and per-file SHA-256 inventories are recorded in
`core/vendor/dependencies.json`. PHPMailer 7.1.1 is LGPL-2.1.
The starter has no third-party font or frontend dependencies. The project is MIT.

Framework tests use their own small site fixtures and do not depend on your
content, theme, private configuration, or database. Customize `site/tests/run.php`
for your website; its starter checks use a throwaway SQLite database.

## Status

The starter disables search indexing with `noindex` and displays a reminder.
When ready to launch, set `'indexable' => true` in `site/meta.php`; the Starter
theme removes both the directive and reminder. Search engines decide whether
to index eligible pages. No `robots.txt` or sitemap is generated automatically.
See [deployment and indexing](core/docs/deployment.md#search-indexing).

Implemented: routing/responses, plugin loading, contracts/registry/hooks, SQLite
settings/pages, recorded installation schema, theme selection/layouts/assets,
declarative entities with validated SQLite CRUD and explicit installation,
optional SMTP, bounded request context and opt-in POST routes, a copyable
[protected contact form](core/docs/examples/contact-form/README.md), example feature plugin,
CLI tools, reversible local core updates,
and dependency-free integration checks including storage substitution.

Rendering health probes fail on reported PHP warnings/notices as well as thrown
exceptions. Public health exposes generic status only; local CLI diagnostics
include the source location. Suppressed diagnostics and unvisited asset URLs
remain outside the probe. See [rendering health probes](core/docs/site.md#rendering-health-probes).

Planned: MariaDB/PostgreSQL adapters and data transfer; separate extension
updates; signed online updates, potentially operated by AI; content/admin editing,
authentication, a general form builder, and uploads.

The starter targets WCAG 2.2 AA and progressively enhanced interaction. A full
assistive-technology audit is still needed. Future mutation endpoints require
authorization, validation, and CSRF protection, with authentication for privileged
actions and abuse protection for public inquiries.

See `core/docs/architecture.md`, [deployment and server configurations](core/docs/deployment.md), and `core/docs/contributing.md`.
