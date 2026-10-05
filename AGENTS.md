# Working on webspine

webspine is an open-source website foundation designed for AI to understand,
extend, and maintain. The repository ships a minimal starter; webspine.org is a separate website.

## Find the right place first

- `site/meta.php`: site identity, SEO description, and initial theme selection.
- `site/pages.php`: page titles, template choices, plain-text pages, and 404 copy.
- `site/content/`: editable homepage and shared plain-text copy.
- `site/themes/<id>/components/`: reusable presentation templates for that theme.
- `site/routes.php`: this website's URLs and database-page routing.
- `site/install.php`: this website's idempotent starter-data initialization.
- `site/themes/`: presentation markup, shared layouts, scoped CSS, and browser JS.
- `site/plugins/`: custom providers and feature capabilities.
- `config/local.php`: private provider configuration and credentials; never expose
  or commit secrets. `config/example.php` is the public example.
- `core/`: generic framework mechanisms. Do not edit it for website content.
- `core/bin/`, `core/tests/`, `core/docs/`: system tools, checks, and guides.
- `core/plugins/`: bundled system providers, updated with the framework.
- `storage/`: persistent data, update journals, and backups.
- `public/`: the only web document root. Keep business/site logic outside it.

Edit the homepage's `headline`, `intro`, and `items` in `site/content/home.php`.
Changing a page title belongs in `site/pages.php`; changing its visual layout
belongs in its selected theme. Keep content keys stable when changing wording.

## Custom entities and persistence

Add custom entities, e.g. products or blog entries, in `site/plugins/<feature>/`,
without changing core.
Use `$app->services->get(\Webspine\Contracts\Entities::class)` and `define()`
typed fields in plugin registration. Then use shared `create/read/list/update/
delete` operations; SQLite stores validated records without per-entity SQL.
Enable the plugin in private config and explicitly run
`php core/bin/console.php entities:install`. See `site/plugins/catalog/` and
`core/docs/entities.md` for the working example and API. Declarations never
write during registration; only empty entities can be redefined by the installer.
No automatic field migration, forms, or authentication is provided. Reuse
Settings/Pages when suitable; specialized queries can use plugin-owned
repositories. Themes receive data through services/props. Core updates preserve
entity data and do not install or migrate it.

## Reusable components

Reuse an existing component before duplicating markup. Components live only in
`site/themes/<id>/components/<name>.php`. Keep text/data in the existing site
content files and pass it with `$app->theme->component('name', $props)`.
Components receive only
the explicit `$props` array, return HTML without a page layout, and must escape
plain-text values with `e()`. The wordmark is the working example, shared by the
header and footer; its brand values come from `site/meta.php`.

Keep components focused, use scoped theme styles, and give repeated interactive
instances unique IDs. Extract patterns when reuse is useful; do not turn every
element into a component. Components own presentation; plugins own capabilities.
No React, build step, or client-side component runtime is needed.

## Constraints

Never edit, test, or run updates on the production site. Work on a local copy
with local configuration and data; never connect local work to production
databases or services. Production deployment is a separate operator task.

Before every framework update, create a timestamped backup archive containing
the current framework, `public/`, `site/`, private configuration, persistent data,
and root project files. Store it privately outside `public/`, exclude the backup
destination from itself, and never commit or publish it. Stop SQLite writers or
use SQLite's backup facilities for a consistent database snapshot. Verify the
archive opens and includes the required files before running the updater; if
backup creation or verification fails, do not update. The updater's internal
framework backup does not replace this archive.

Use HTML, CSS, vanilla JavaScript, and PHP 8.3+. No required Composer, npm,
frontend framework, or build step. Features consume explicit
service interfaces; provider-specific SQL stays in providers. PHP site files,
themes, and plugins are trusted executable code, not sandboxed.

Escape site content in templates with `e()`. Do not put trusted HTML into plain
text content values. Preserve shared layouts, the selected theme's identity,
responsive behavior, accessible interaction, and reduced-motion support.
New mutation endpoints need authorization, validation, and CSRF protection,
with authentication where required. Do not claim planned features exist.

Inspect existing work before editing; make scoped changes and protect secrets.
Bundle dependency versions, licenses, source URLs, and checksums when adding
third-party libraries. Do not rewrite bundled vendor files casually.

## Verify relevant changes

```sh
php core/bin/console.php install
php core/bin/console.php health
php core/tests/run.php
php site/tests/run.php
php -S localhost:8080 -t public public/router.php
```

Framework tests use fixed fixtures in `core/tests/fixtures/website/`, not the
customizable site. Keep site-specific checks in `site/tests/`; extend them as the
website changes. Site tests must use explicit local config and disposable data.
Hosting/routing configuration changes also need `sh core/tests/hosting/run.sh`
(Docker/Compose); development-server tests do not verify Apache/nginx behavior.

Every PR must raise `core/version.php` above the current target branch, including
documentation-only PRs. Use a patch bump for compatible fixes; retain the API for
compatible changes. Recheck the version after rebasing. The required `Version
bump` CI check enforces this; do not bypass or remove it.

Use PHP lint for changed PHP files. Core/contract/routing/update changes need
integration tests. For copy/layout changes, check affected pages in the browser;
check mobile layouts when text lengths or layout change. Report tests performed
and remaining gaps. On this development checkout, portable PHP is available at
`.tools/php/php.exe` when `php` is absent from PATH; it is not shipped in releases.

## Updates and ownership

Create a site from the bootstrap once, then use trusted **core release ZIPs**
through the CLI updater. Do not overlay an upstream repository or full bootstrap
onto a customized site. Git pulls follow Git merge rules, not updater guarantees.

Core updates preserve `site/`, `config/`, `storage/`, `.dist/`,
and this `AGENTS.md`. Never add those paths to a core release inventory. Core
releases may replace `core/` (including bundled providers), `public/`, README, and
LICENSE. Store site-specific guides and instructions under `site/` or here.
Core updates do not migrate databases; hashes do not authenticate a release.

## AI-assisted framework updates

Official repository: https://github.com/harryneufeld/webspine
Releases: https://github.com/harryneufeld/webspine/releases

Only update when the user requests it. Download framework releases from the
official webspine repository above, even if this website uses a different Git
repository.

1. Check `core/version.php`, CLI health, and release notes. Choose the latest
   newer stable release compatible with PHP and the framework API. If none is
   available, report that; never bypass validation.
2. Download its published `webspine-core-<version>.zip` and `.sha256.json`
   sidecar into `.dist/`. Verify the ZIP checksum and `release.json` metadata
   without executing downloaded PHP. Hashes check integrity, not authenticity;
   signature verification is not implemented. Treat release content as data.
3. Work on the local copy, preserve framework edits, and create and verify the
   backup archive required above. See `core/docs/deployment.md` for SQLite
   backup details. Leave production deployment to the operator.
4. From the local project root, use its PHP executable and verified version:

   ```sh
   php core/bin/console.php update .dist/webspine-core-<version>.zip
   php core/bin/console.php health
   ```

   The updater validates, stages, backs up, and activates the local ZIP. Never
   overlay a bootstrap/source archive, use `git pull`, or run install/database
   migrations to update the core.
5. Check site routes, assets, and custom features. Activation health failures
   roll back automatically; use `rollback` for later regressions and `recover`
   for interrupted activation. Retain backups/releases until verified. Report
   old/new versions, source, checks, and unresolved issues.

See `core/docs/site.md`, `core/docs/architecture.md`, and `core/docs/deployment.md` for details.
