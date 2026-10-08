# Asset caching and conditional delivery

Theme assets remain under `site/themes/<id>/assets/` and are served through PHP.
The existing allowlist, realpath containment, readable-file checks, MIME types
and response security headers apply before conditional responses or cache reuse.
Private files and executable suffixes remain inaccessible. The hash cache is
disposable framework-owned state under `storage/`, excluded from release ZIPs.

## URL hashes across requests

By default, `assetUrl()` reuses full SHA-256 hashes in
`storage/theme-asset-hashes.json`. The URL format remains the same 12-character
hash prefix. Cache scope includes the canonical site root and framework version;
entries use the canonical asset path and filesystem device/inode, permissions,
size, modification time and change time. Each lookup still resolves/validates the
file before reuse; successful URLs also retain their existing lifecycle cache.

The cache holds at most 256 entries for up to ten minutes from computation and
accepts at most 256 KiB of JSON. Metadata changes, deletion, root/version changes,
expiry and malformed data cause fresh hashing. Writes use a private temporary
file and rename; competing writes can lose cache entries, causing harmless
rehashing. No locks, database migration or required extension are introduced.
Unavailable/read-only storage falls back to hashing. Invalid linked cache files
are never followed; explicit clearing requires removing such a link manually.
These are performance caches, not authenticity checks or storage for secrets.

Disable persistent reuse in private configuration if every new lifecycle must
rehash content, or the filesystem cannot provide useful metadata:

```php
'theme' => ['asset_hash_cache' => false, 'debug' => false],
```

Normal `App::handle()` requests and `App::health()` begin a lifecycle with
`refresh(false)`, retaining cross-request hash entries. Explicit
`$app->theme->refresh()` retains its fresh-file semantics and now also clears the
private hash cache. Use that after direct edits, particularly same-size edits
with preserved timestamps. If persistent cache clearing fails, explicit refresh
throws an actionable error instead of promising invalidation.

Metadata is not complete content identity. Same-size edits within timestamp
resolution, copies preserving timestamps, network filesystems and inode reuse
can evade automatic detection. Drain concurrent rendering workers during such
replacements, then refresh every retained App or remove the cache file before
the next request. A worker with an older in-memory snapshot can otherwise reuse
or restore old entries until expiry. Never assume touching a file proves complete
invalidation. Core updates preserve disposable storage; version scoping prevents
reuse of another core version's entries, including rollback. Unchanged site
assets still belong to the site; updates never rewrite them.

## Conditional HTTP requests

Successful asset responses expose `ETag: "<full-SHA-256>"` and retain
`Cache-Control: public, max-age=3600`. GET and HEAD accept `If-None-Match` with a
strong tag, weak `W/` tag, tag list, or standalone `*`. A matching response is
304 with no body and the same ETag/cache policy; security headers are added by
the response sender. Missing/unsafe/unreadable resources remain generic 404s,
even with `*`. Other methods remain 405. Malformed/nonmatching conditions cause
ordinary 200 delivery; conditions above 8 KiB are ignored. These comparison
rules follow [RFC 9110, section 13.1.2](https://www.rfc-editor.org/rfc/rfc9110.html#section-13.1.2).

ETags are computed from the bytes read for that response, independently of the
metadata cache. A metadata-preserving asset replacement therefore cannot yield
a false 304 from a stale URL hash. HEAD returns current headers without a body.
Both HEAD and conditional GET still read/hash the complete asset: transfer size
improves, but PHP file reads and hashing remain. Last-Modified/date conditions,
range responses and streaming are not implemented by this change.

The existing one-hour freshness policy remains: browsers can reuse fresh cached
responses without contacting PHP. A version query does not freeze an old asset;
the URL serves the current file. Compression belongs to the web server/proxy;
configure it to preserve correct validators for any representation transformation.

Static publishing/offloading was assessed separately. The current updater owns
all of `public/`, so generating site assets there would risk deletion on update.
This release keeps PHP delivery; static publishing needs an explicit ownership
and deployment design before it can safely ship. No generated-public directory
or server routing change is required here.

## Manual rendering diagnostics

Enable `'theme' => ['debug' => true]` for local manual rendering or exports.
Normal requests/health already refresh automatically. For manual pages:

```php
$app->theme->refresh();
$first = $app->site->render('home');
$second = $app->site->render('about'); // Shared lifecycle, checked before rendering.
// After Settings/file changes, start a fresh lifecycle before the next page:
$app->theme->refresh();
```

Debug checks run before outer page/component renders and standalone asset URL
lookups. Nested rendering shares the page lifecycle and adds no repeated debug
scan. Checks compare active Settings selection, loaded plugin manifests/state,
the selected theme/dependency manifest files, layout and previously observed
templates/components/assets. Changes produce a `Stale theme rendering lifecycle`
exception naming the refresh remedy, without warning spam or switching theme
identity during a page. CLI/export callers receive that exception and can report
it normally; renderer buffers remain restored. Apps/sites do not share debug state.

Debug observes at most 256 files per lifecycle. Manifest content is hashed at
boundaries, with a 1 MiB per-manifest limit. Other observed files use metadata;
same-size/metadata-preserving edits and unobserved files can go undetected.
Refresh explicitly after those edits. Changes during an ongoing page are not
detected mid-page. Plugin code changes require a new App and, where relevant,
worker/opcache restart; refresh cannot unload already executed PHP. Debug disabled
retains existing manual caching behavior and adds no Settings reads/file scans at
render boundaries. The three-method `$ui` helper is unchanged.

## Reproducible checks and measurements

`core/tests/asset-cache.php` uses a disposable fixture containing a 64 KiB font,
4 KiB stylesheet and 192 KiB image (260 KiB total). Fifty fresh lifecycles
previously hashed about 13 MiB; the cache hashes 260 KiB once and records 147 hits.
Fresh Apps verify on-disk reuse. Timing is reported but never used as a pass
threshold: filesystem/load differences can outweigh the saved hash work.

The local before/after fixture's 50 conditional image requests reduced response
body bytes from 9,830,400 to zero. Response-byte hashing adds CPU work; this is a
transfer-size measurement, not a delivery-latency claim. Framework checks cover
cache bounds/corruption, disabling/unavailable storage, site/version isolation,
replacement/deletion, exact validators and debug lifecycle changes. Hosting CI
checks GET/HEAD/304, MIME/security/cache headers through Apache, nginx/PHP-FPM
and CloudPanel routing, including after activation and rollback.
