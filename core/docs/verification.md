# Verification · 5 October 2026

Verified locally on Windows with PHP 8.3.35, PDO SQLite, ZipArchive, and OpenSSL.
The portable runtime and test output are ignored and are not shipped.

- 82 framework integration checks pass (`php core/tests/run.php`). These use
  dedicated fixtures in `core/tests/fixtures/website/`, independent of the
  customized site, private configuration, and existing database.
- 10 starter checks pass (`php site/tests/run.php`). They use explicit local
  configuration with mail disabled and a throwaway SQLite database. Coverage
  includes fresh installation, shared layouts, public pages, site-owned 404,
  assets, component escaping, and escaped database-page content.
- PHP syntax checks pass for the starter, optional example plugins, and fixtures.
- The minimal Starter theme was reviewed in the browser at desktop and 320px
  widths. Pages have readable text, wrapping navigation, visible focus styling,
  a skip link, and no horizontal page overflow. About navigation was verified.
  No JavaScript, third-party fonts, or frontend dependencies are required.
- Bootstrap packaging is checked for the Starter theme and default content;
  marketing content, private configuration, persistent data, and development
  output are excluded. Core packages continue to exclude site-owned files.

Framework coverage includes explicit idempotent installation, persistence,
provider substitution, parameterized queries, generic entity CRUD, revision
conflicts, contracts, manifest compatibility, protected assets, dependency
checksums, archive integrity, candidate health, activation, preservation,
rollback, and recovery. The theme-scope, request-context, POST-routing, and asset
performance feedback remains separate work; these mechanisms were not changed.

webspine.org is a separate website. Its deployed content and theme were not
modified during the starter replacement. Existing users keep their own site
files through core updates; the new starter is for new projects.

Remaining gaps: a full WCAG 2.2 AA and screen-reader audit, real SMTP delivery,
load/concurrency testing, and cross-platform/server verification. This document
makes no accessibility-certification or security-audit claim. Signed updates,
authentication, editing, public forms/uploads, and other database adapters remain
planned.
