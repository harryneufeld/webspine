# spineanalytics

Optional Webspine API 1 plugin, ID `spine-analytics`, version 0.1.0.
Requires PHP 8.3+ and PDO SQLite. No framework changes, browser scripts,
tracking pixels, external services, theme dependency or MariaDB requirement.

## Installation

This optional plugin is bundled in core releases and bootstraps under
`core/plugins/spine-analytics`. Do not also install a site plugin with the same
ID: Webspine rejects ambiguous identities. Collection and reporting stay off
by default. Merge settings into private `config/local.php`, preserving providers
and existing plugins:

```php
'plugins' => ['spine-analytics'], // Append to your existing list.
'spine_analytics' => [
    'enabled' => true,
    'preview' => false,
    'report_enabled' => true,
    'password_hash' => null, // Replace with the generated hash below.
    'device_metrics' => false,
    'pages' => ['/'],
],
```

Explicitly initialize private aggregate storage:

```sh
php core/plugins/spine-analytics/cli.php install
```

Registration and ordinary framework installation do not create analytics tables.
Framework updates do not install or migrate analytics data. Run the analytics
installer explicitly when initializing its schema.

Initialise storage using the operating-system user running PHP, or grant PHP
access. Append `spine-analytics` to the existing configuration's plugins list
and merge the section in `config.example.php`. Preserve existing providers,
SMTP settings and other plugins. Collection stays off unless
`spine_analytics.enabled` is explicitly true.

`pages` explicitly allowlists fixed public paths, e.g. ['/', '/docs']; default
is only '/'. Never include personal data, secrets or customer IDs. Unknown
paths become [other]. Queries/fragments are discarded. Prefixes and regular
expressions are not supported.

## Private reporting

```sh
php core/plugins/spine-analytics/cli.php report 30
php core/plugins/spine-analytics/cli.php html 30 > /private/path/report.html
```

JSON and self-contained HTML go to stdout. Store exports outside public/.
The HTML works offline, without scripts, external fonts or network requests.
Reports support 1–90 days: daily totals, successful page GETs, response errors,
pages, traffic categories and recognised agent names. Uses system colour mode.

Set `spine_analytics.preview=true` in **local configuration only** to expose
/_insights with 7/30/90-day controls. The route registers only for a loopback
peer (127.0.0.1 or ::1). This is not authentication: keep preview=false in
production, including behind loopback proxies. Production browser reports require the separate password-protection option below.
Optional local demo-7.json/demo-30.json/demo-90.json files in the storage directory
can be read with ?demo=1; the report prominently labels these as sample data.
Never package demo files or databases.

## Data and retention

Private database: storage/spine-analytics/counts.sqlite. Directory 0700 and
database 0600 on Unix; protect equivalent Windows permissions. Do not expose
storage/ through the web server. SQLite uses WAL: use SQLite's backup facility
or stop writers and checkpoint first; copying only a live database can lose
uncheckpointed transactions.

Rows contain UTC day, allowlisted page (or [other]), coarse category, fixed
agent name, method GET/HEAD/POST/OTHER, response status and an incremented count.
No individual events, visitor IDs, IPs, raw user agents, query strings,
referrers or request bodies are stored. URI, user agent and method are read
in memory to classify requests. No cookies are read/set and no fingerprinting.
Aggregates expire after 90 UTC calendar days on the next counted request.
Export/backup retention is separate.

Other hosting logs and plugins may process personal data independently. This
design avoids browser tracking but is not a blanket legal compliance or consent
exemption. Operators must assess their deployment and document processing,
purpose, legal basis and retention in their privacy policy.

## Limits

Only requests reaching PHP after registration count. Assets, /health,
/favicon.ico and /_insights are excluded. Static/cache/proxy responses and
early boot errors do not count. All methods count; successful page GETs are
reported separately. These are requests, not unique visitors.

Shutdown writes are best effort with a 250 ms SQLite contention timeout.
WAL with `synchronous=NORMAL` and immediate transactions serialise increments.
Recent committed counts can be lost after power failure; this telemetry favors
low write overhead over durable event logging. Failures log a generic
notice and do not replace the website response. Suitable for modest sites,
not lossless logging or high-volume analytics; validate under expected load.

User-agent names can be spoofed; browser-like requests may be bots. AI training,
search and user-triggered fetches are separate. No source-IP verification.
Classifier references:
- https://developers.openai.com/api/docs/bots
- https://privacy.claude.com/en/articles/8896518-does-anthropic-crawl-data-from-the-web-and-how-can-site-owners-block-the-crawler

## Framework integration and verification

The plugin's SQLite provider owns its schema and SQL; it uses a separate private
database independently of the site's selected storage provider. Classes live in
`Webspine\Providers\SpineAnalytics`. No third-party code is bundled.

`php core/tests/run.php` runs aggregate, concurrency, header-category, access,
route and real HTTP tests with disposable data and explicit local configuration.
It also verifies core release packaging includes the plugin. Tests never load
this checkout's private configuration or contact production services.

The CLI accepts `WEBSPINE_SITE_ROOT` for an explicit local fixture root. HTML and
JSON reports use that root's configuration. Password hashing works before
configuration or storage initialization and writes only its result to stdout.

## Optional browser / OS / device totals

Set spine_analytics.device_metrics=true to derive coarse browser family,
OS family and device category from the already-transmitted User-Agent header.
Run cli.php install on upgrade to create the new table without changing existing
counts. No JavaScript, additional client hints or device APIs are requested.
This option defaults to false. Only browser-like counted requests contribute;
recognised bots and unknown clients are excluded. All counted HTTP methods and
statuses contribute. Classification is approximate, especially for tablets
presenting desktop headers, Chromium variants and disguised automation.

header_totals stores only day, dimension, fixed value and hit count. Browser,
OS and device are independent rows. There are no page/status/bot columns,
joint browser-OS-device rows or identifiers linking requests between tables.
Daily aggregate similarities can still reveal coarse correlations on small
sites; the report does not claim absolute anonymity. No raw headers, versions,
model names, client hints, IPs or geography are stored. Same 90-day retention
applies. Historical records are not backfilled; disabling this option stops
new device totals, while already stored totals expire normally.

Document the additional purpose and categories before public activation and
assess the legal basis. This feature alone does not establish consent exemption.

## Password-protected production dashboard

Set spine_analytics.report_enabled=true to register /_insights with HTTP Basic
authentication. This is independent of collection and defaults to false. Keep
preview=false on production. When report_enabled is true, password checks also
apply locally, even if preview=true; preview cannot bypass authentication.
Username is analytics. No cookies or authentication sessions are created.

You can temporarily enter a plaintext password in private `config/local.php`:

```php
'password' => 'YOUR_UNIQUE_PASSWORD_AT_LEAST_16_BYTES',
'password_hash' => null,
```

Then run:

```sh
php core/plugins/spine-analytics/cli.php set-password
```

The command replaces `password` with `null` and sets `password_hash` to the
generated hash in the same file. It preserves unrelated code, comments and
settings, creates no plaintext backup, and never prints the password. Run it
before enabling the dashboard. Repeat this process to rotate the password.
Use single-quoted literal keys and a single-quoted password; PHP requires `\\`
for a literal backslash and `\'` for a literal apostrophe. Dynamic expressions,
duplicate keys and spread entries in the analytics array are rejected without
changing the file. Repeating the command after conversion leaves it unchanged.
The command only edits existing private `config/local.php`, never the public
example. The dashboard authenticates using `password_hash` only.

Alternatively, generate a hash using stdin with
`php core/plugins/spine-analytics/cli.php hash-password`. The helper accepts
16–72 bytes, rejects NUL characters, and uses PHP's `password_hash()`; access
checks use `password_verify()`. There is no password file or public setup route.

For Bash, use a hidden prompt:

```bash
read -r -s -p 'New report password: ' spineReportPassword
printf '\n'
printf '%s' "$spineReportPassword" | php core/plugins/spine-analytics/cli.php hash-password
unset spineReportPassword
```

For PowerShell:

```powershell
$spineReportPassword = Read-Host 'New report password' -AsSecureString
$spineReportCredential = [PSCredential]::new('analytics', $spineReportPassword)
$spineReportCredential.GetNetworkCredential().Password | php core/plugins/spine-analytics/cli.php hash-password
Remove-Variable spineReportCredential, spineReportPassword
```

Paste the generated hash into a **single-quoted** PHP string (hashes contain `$`):

```php
'password_hash' => 'PASTE_GENERATED_HASH_HERE',
```

The placeholder does not authorize access. Never commit the private config or
pass a password in command arguments. To rotate, generate a new hash and replace
the configured value; subsequent requests require the new password. The existing
core updater preserves `config/local.php` and all aggregate storage.

Browsers may remember Basic credentials for their session; there is no in-page
logout or multi-user account system. CLI report commands remain available to
the server operator and do not require the browser password.

Production requests must provide HTTPS=on or HTTPS=1 through the trusted server
configuration. The plugin does not trust arbitrary X-Forwarded-Proto headers.
Configure a TLS-terminating proxy to pass the real HTTPS status to PHP. Plain
HTTP is allowed only for loopback requests on PHP's built-in development server.
Missing/invalid password hashes or insecure requests return 503 without report
data. Incorrect or missing credentials return 401 with the browser login prompt.
Password-protected responses are no-store; raw credentials are never logged by
this plugin. Configure the web server's normal rate limiting for /_insights.

Ensure the Authorization header reaches PHP (e.g. HTTP_AUTHORIZATION in a
FastCGI configuration); PHP_AUTH_USER/PHP_AUTH_PW are also supported. Keep
storage inaccessible, and use a strong unique password. Server access remains
necessary to edit private configuration: there is no public setup or password-reset endpoint.
