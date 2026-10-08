# Deployment

Requires PHP 8.3+ (CLI and web worker), PDO/PDO SQLite, ZipArchive for releases,
and OpenSSL for SMTP. Point the document root to `public/`, never the project
root. Deployment currently assumes the **domain root**, not a URL subdirectory.
Use HTTPS and a local-disk SQLite database; network filesystems are unsupported.

## Server configurations

Adjust paths, domain, and PHP-FPM endpoint in these versioned, copyable examples;
TLS certificates and HTTP-to-HTTPS redirects are hosting settings.

- **[Apache](hosting/apache.conf):** enable PHP (mod_php or a PHP-FPM handler),
  `mod_rewrite`, and `AllowOverride FileInfo Indexes Options` for `public/`.
  Keep the shipped `.htaccess`; do not alias private project directories.
- **[nginx](hosting/nginx.conf):** route requests to `public/index.php`, configure
  PHP-FPM, and retain `REQUEST_URI`/`QUERY_STRING` through `fastcgi_params`.
- **CloudPanel:** select PHP 8.3+, set the site's root directory to `public/`,
  and verify `{{root}}` resolves there in both server blocks. Replace the
  relevant [frontend locations](hosting/cloudpanel-frontend.conf) and
  [backend locations](hosting/cloudpanel-backend.conf), keeping generated TLS,
  proxy, PHP settings, and ports. Remove conflicting `try_files`, PHP locations,
  or file-exists shortcuts; validate in the Vhost Editor before saving.

CloudPanel's generic static regex must start with `^(?!/assets/)`, as in the
example. `/assets/` URLs are served through PHP from
`site/themes/<theme>/assets/`, outside `public/`; they must reach the application
handler. CORS and HTTP/3 headers are separate hosting settings, not this routing
fix. The frontend example retains `{{varnish_proxy_pass}}`; verify your generated
proxy chain and purge any enabled Varnish cache after deployment.

## Permissions and installation

Let the PHP worker read code/config and write **only `storage/`**, including the
SQLite database, sidecars, and update lock. Common starting permissions: code
folders `755`, code files `644`, private config `750`/`640` with the worker's group,
shared storage folders `2770` and files `660`. Adjust ownership for your host;
never use `777`. The maintenance operator needs code write access for updates.

Run `php core/bin/console.php install` and `health` with the same PHP version and
storage group before serving. Check `/health`, pages, and theme assets; public
health is generic, while detailed CLI output must remain private.

## Diagnosing an unavailable site

Run `php core/bin/console.php health` from the project directory and inspect the
PHP error log. Health is a diagnostic command: it does not repair configuration,
install schema, or migrate data. Only initial setup needs `install`, after valid
configuration is in place. Do not run it as a general outage remedy.

Configuration is loaded from `config/local.php`, or `config/example.php` when the
local file is absent. Each must return an array. Explicit configuration supplied
to `App` bypasses these files. Load failures retain their original exception;
CLI errors exit nonzero and CLI/log diagnostics include the cause's file and
line, with paths inside the project shown relative to its root. Invalid return
types identify the configuration file and type without printing its value.

The emergency response is generic HTTP 503 with `Cache-Control: no-store` and
operator guidance. It does not expose exception messages, paths, configuration
values, or source excerpts, and needs no database or theme to render. Keep logs
and CLI output private. Ensure production PHP has `display_errors=Off` and
`log_errors=On`; PHP's own displayed diagnostics can otherwise disclose details
before the framework can handle a failure.

## Search indexing

Starter pages render full HTML with titles, descriptions, and crawlable links,
but start with `'indexable' => false` in `site/meta.php`. The Starter theme emits
`<meta name="robots" content="noindex">` and displays a launch reminder on every
page. Set `'indexable' => true` when ready: both disappear. Missing or non-boolean
values keep indexing disabled. This is a site-owned theme convention; custom
themes/plugins must implement their own policy. Core updates preserve existing
site settings/templates and do not change existing sites' indexing behavior.
Hosting headers can still block indexing. Search engines decide whether to index
eligible pages; webspine does not guarantee inclusion.

No `robots.txt`, sitemap, or canonical URLs are generated automatically. These
are site-owned choices; define `/robots.txt` and `/sitemap.xml` in
`site/routes.php` with `text/plain` and `application/xml` content types. A missing
robots file returning 404 does not block Google crawling; sitemaps help discovery.
See [Google's robots rules](https://developers.google.com/crawling/docs/robots-txt/robots-txt-spec)
and [sitemap guidance](https://developers.google.com/search/docs/crawling-indexing/sitemaps/overview).

Protect staging/private sites with authentication. For publicly accessible pages
that should stay out of search, set `X-Robots-Tag: noindex` on their responses
(or a robots meta tag in the theme), and allow crawling so engines can see it.
`robots.txt` is not access control or a reliable way to prevent indexing.
See [noindex guidance](https://developers.google.com/search/docs/crawling-indexing/block-indexing).
Check deployed hosting headers and crawler access; customization can change
these defaults.

## Updates and backups

Test on a local clone first. Before updating, drain/stop PHP workers and other
SQLite writers; create and verify a private full-site backup including code,
`public/`, `site/`, config, data, and root files. Exclude the archive destination.
For a live SQLite snapshot use SQLite backup facilities; copying an active
database file is unsafe. The updater's core backup does not replace this archive.

Apply only a trusted core ZIP using `php core/bin/console.php update <zip>`, then
run `health`. Restart PHP-FPM or Apache PHP workers **before restoring traffic**
to clear OPcache, even with timestamp checks enabled. CLI `opcache_reset()` does
not clear the web workers' cache. Purge proxy caches and recheck pages/assets.
Use `recover` after interrupted activation; retain verified backups until done.
Never expose configuration, SMTP credentials, or backups through the web root.

## Real-server checks

With Docker Engine and Compose v2 on a local machine or CI, run:

```sh
sh core/tests/hosting/run.sh
```

This uses disposable fixtures/volumes, no host ports or private configuration.
It runs actual Apache + PHP and nginx + PHP-FPM, plus CloudPanel-style frontend/
backend nginx rules from the examples above. Checks cover pages, query strings,
redirects, permitted asset MIME types, HEAD/404s, traversal/symlink rejection,
private files, writable/read-only storage, and update/rollback after restarting
workers with warmed OPcache and timestamp validation disabled. A negative control
reproduces CloudPanel's unpatched asset failure. Docker is test tooling only.

The suite does **not** install the CloudPanel control panel or verify a host's
TLS, HTTP/3, CORS, Varnish, ACLs, or generated settings. Validate those on a local
CloudPanel staging instance; PHP development-server tests are not server checks.

References: [Apache overrides](https://httpd.apache.org/docs/2.4/mod/overrides.html),
[nginx FastCGI](https://nginx.org/en/docs/http/ngx_http_fastcgi_module.html),
[CloudPanel PHP/root](https://www.cloudpanel.io/docs/v2/php/applications/other/),
[PHP OPcache](https://www.php.net/manual/en/opcache.configuration.php).
