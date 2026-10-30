# Deployment

Technical reference for hosting webspine. For the agent's step-by-step process
with a non-technical user, see [publishing.md](publishing.md); for framework
updates, see [update.md](update.md).

Requires PHP 8.3+ (CLI and web worker). Point the document root to `public/`,
never the project root. Only the **domain root** is supported, not a URL
subdirectory. Use HTTPS and a local-disk SQLite database; network filesystems
are unsupported.

## PHP setup

| Extension | Needed for |
|---|---|
| `pdo_sqlite` (with PDO) | Every website: settings, pages, entities, queue, analytics |
| `zip` | Core updates, packaging and release verification |
| `openssl` | SMTP mail over TLS |

Check with the same PHP executable the site uses:

```sh
php -v       # 8.3 or newer
php --ini    # loaded php.ini
php -m       # loaded extensions
php core/bin/console.php health
```

`Loaded Configuration File: (none)` means no extensions are configured. Copy
`php.ini-development` (local) or `php.ini-production` (hosted) to `php.ini` in
the shown configuration path, or next to `php.exe` on Windows, then enable:

```ini
extension_dir = "ext"
extension=pdo_sqlite
extension=zip
extension=openssl
```

Recheck with `php --ini` and `php -m`, and restart the serving PHP process; CLI
and web worker may load different ini files. Hosted sites keep `display_errors`
off and log errors privately.

- **Windows (winget `PHP.PHP.8.4`, verified 8.4.25):** ships no active
  `php.ini`, so only PDO loads. Create it next to `php.exe`
  (`(Get-Command php).Source`); DLLs are in its `ext\` folder.
- **Linux/macOS:** packages such as `php8.3-sqlite3` and `php8.3-zip` enable
  extensions through extra ini files.

See PHP's [configuration file](https://www.php.net/manual/en/configuration.file.php)
and [Windows extensions](https://www.php.net/install.pecl.windows.php) guides.

## Server configurations

Adjust paths, domain and PHP-FPM endpoint in these examples. TLS and
HTTP-to-HTTPS redirects are hosting settings.

- **[Apache](hosting/apache.conf):** PHP (mod_php or PHP-FPM), `mod_rewrite`,
  and `AllowOverride FileInfo Indexes Options` for `public/`. Keep the shipped
  `.htaccess`; do not alias private project directories.
- **[nginx](hosting/nginx.conf):** route to `public/index.php`, configure
  PHP-FPM, keep `REQUEST_URI`/`QUERY_STRING` via `fastcgi_params`.
- **CloudPanel:** PHP 8.3+, site root `public/` (check `{{root}}` in both server
  blocks). Replace the [frontend](hosting/cloudpanel-frontend.conf) and
  [backend](hosting/cloudpanel-backend.conf) locations, keeping generated TLS,
  proxy, PHP settings and ports. Remove conflicting `try_files`, PHP locations
  or file-exists shortcuts; validate in the Vhost Editor before saving.

`/assets/` URLs are served through PHP from `site/themes/<theme>/assets/`, so
they must reach the application; CloudPanel's static regex must start with
`^(?!/assets/)`. Keep `{{varnish_proxy_pass}}` and purge Varnish after deploys.
CORS and HTTP/3 are separate hosting settings.

## Permissions and installation

The PHP worker reads code and config and writes **only `storage/`** (database,
sidecars, update lock). Typical modes: code folders `755`, files `644`, private
config `750`/`640` with the worker's group, storage folders `2770`, files `660`.
Never use `777`. The maintenance user needs write access to code for updates.

Run `install` and `health` with the site's PHP version and storage group before
serving, then check `/health`, pages and theme assets. Public health is
generic; detailed CLI output stays private.

## Diagnosing an unavailable site

Run `php core/bin/console.php health` and read the PHP error log. Health only
diagnoses; it does not repair, install schema or migrate data. Run `install`
only for initial setup, not as an outage remedy.

Configuration loads from `config/local.php`, else `config/example.php`; each
must return an array. CLI errors exit nonzero and name the file and line,
relative to the project root, without printing configuration values.

Startup failures return a generic HTTP 503 with `Cache-Control: no-store`,
without messages, paths or configuration. Keep `display_errors=Off` and
`log_errors=On` in production, or PHP may leak details first.

## Search indexing

Starter sites ship with `'indexable' => false` in `site/meta.php`: every page
emits `<meta name="robots" content="noindex">` and shows a launch reminder.
Setting `true` removes both; missing or non-boolean values keep indexing off.
Custom themes implement their own policy. Hosting headers can still block
indexing, and search engines decide what to include.

`robots.txt`, sitemaps and canonical URLs are not generated; define them in
`site/routes.php` ([example](site.md#domain-root-robots-and-sitemap-routes)).
Protect private staging sites with authentication. For public pages that must
stay out of search, send `X-Robots-Tag: noindex` and allow crawling;
`robots.txt` is not access control. See Google's
[robots](https://developers.google.com/crawling/docs/robots-txt/robots-txt-spec),
[sitemap](https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview)
and [noindex](https://developers.google.com/search/docs/crawling-indexing/block-indexing) guides.

## Backups, workers and caches

Before updates or risky changes, drain or stop PHP workers, cron queue workers
and other SQLite writers, and create a verified private full-site backup (see
[update.md](update.md#backup-first)). Copying an active SQLite file is
unsafe; use SQLite's backup facilities.

After updates, restart PHP-FPM or Apache PHP workers **before restoring
traffic** to clear OPcache; CLI `opcache_reset()` does not reach web workers.
Purge proxy caches and recheck pages and assets. Never expose configuration,
SMTP credentials or backups through the web root.

The [queued contact example](examples/contact-form/README.md) needs its queue
installation and a cron worker sharing the storage group with PHP. Review
[queue guarantees](queue.md) and test captured delivery before using real mail.

## Real-server checks

With Docker Engine and Compose v2:

```sh
sh core/tests/hosting/run.sh
```

Runs disposable Apache + PHP, nginx + PHP-FPM and CloudPanel-style nginx rules
without host ports or private config. It checks pages, query strings, redirects,
asset MIME types, HEAD/404, traversal and symlink rejection, private files,
storage permissions, and update/rollback with warmed OPcache. A negative control
reproduces CloudPanel's unpatched asset failure. It does not verify a real
host's TLS, HTTP/3, CORS, Varnish, ACLs or generated panel settings.

References: [Apache overrides](https://httpd.apache.org/docs/2.4/mod/overrides.html),
[nginx FastCGI](https://nginx.org/en/docs/http/ngx_http_fastcgi_module.html),
[CloudPanel PHP/root](https://www.cloudpanel.io/docs/v2/php/applications/other/),
[PHP OPcache](https://www.php.net/manual/en/opcache.configuration.php).
