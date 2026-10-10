# Site language and visitor errors

Set `'language' => 'de-DE'` in `site/meta.php` to declare the language of a
site's pages. Missing language defaults to `en`. The Starter shared layout emits
`<html lang="<?= e($site->meta['language']) ?>">`; custom themes should use the
same escaped value. Core updates preserve existing site themes and metadata,
so adopt this layout change explicitly on an existing website.

Supported tags use the common `language[-Script][-REGION][-variant]` subset of
[BCP 47](https://www.rfc-editor.org/rfc/rfc5646.html): a 2–8 letter language,
optional 4-letter script, optional 2-letter or 3-digit region, then optional
5–8 alphanumeric variants (or 4 characters starting with a digit). Examples:
`en`, `de-DE`, `zh-Hans-CN`, `es-419`, `de-CH-1901`. Maximum length is 63 bytes.
Validation checks syntax, not registration or whether the site text matches.
Extensions, private-use tags, extlangs and grandfathered tags are not supported.
Invalid metadata fails boot/CLI health with an operator diagnostic.

This declares language; it does not translate content, create alternate routes,
select languages per visitor, or emit `hreflang` links.

## Optional visitor copy

Create `site/errors.json` to customize framework error presentation:

```json
{
  "language": "de-DE",
  "http": {
    "400": {
      "title": "Ungültige Anfrage",
      "message": "Bitte prüfen Sie die eingegebene Adresse."
    },
    "405": {
      "title": "Methode nicht erlaubt",
      "message": "Bitte öffnen Sie die Seite erneut."
    }
  },
  "unavailable": {
    "title": "Vorübergehend nicht erreichbar",
    "message": "Bitte versuchen Sie es später erneut.",
    "operator": ""
  }
}
```

The catalogue is **data-only JSON**, outside `public/`, and is preserved by core
updates. Its language is explicit and independent of metadata because emergency
pages must work even when `site/meta.php`, configuration, providers, templates,
or bootstrap cannot execute. Set both language values to the intended language.
Missing catalogue/language retains English defaults; omitted unavailable fields
retain their English defaults, so supply all three for a fully localized page.
An empty `operator` hides the default visitor-visible setup/health instructions;
operator diagnostics still go to the private PHP error log.

`http` entries require both `title` and `message`, keyed by status `400`–`599`.
They apply to framework `HttpError` responses, router/asset method errors and
generic missing assets. They preserve status, `Allow`, and other original
headers, emit HTML with `Content-Language`, and default to `Cache-Control:
no-store`. Unconfigured statuses retain existing behavior. Normal responses,
application-returned errors, `/health` JSON and the site-defined 404 page keep
their own presentation. Plugins can deliberately reuse the catalogue with
`$app->visitorErrors->present($response)` when appropriate for a visitor page.

`unavailable` handles unexpected public-entry failures, lock acquisition failures
and interrupted updates with status 503, `no-store`, safe security headers and
no body for HEAD. Only catalogue copy is shown; exception/configuration details
remain in private logs. It uses standalone HTML without a theme, database or
provider. Configure PHP to log errors privately and disable `display_errors`
on hosted sites as described in [deployment](deployment.md).

All copy is UTF-8 plain text and HTML-escaped. Titles allow at most 200 bytes;
messages/operator copy allow 4,000 bytes each. Control characters other than
tab/newline/carriage return are rejected. The regular, non-symlink JSON file is
limited to 64 KiB, nesting depth 8 and 100 HTTP entries. Unknown keys, invalid
types/tags/statuses or malformed JSON fail boot and CLI health. Public emergency
presentation falls back to English if the catalogue is unreadable or invalid,
and keeps the reason in private logs. If the presentation helper itself cannot
load, a minimal English emergency page remains available.
