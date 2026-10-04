# Architecture

The app reads private PHP configuration before connecting to storage. It loads
the explicitly selected storage provider, registers Storage, Settings, and Pages
contracts, optionally registers Mail, then loads features. The app.ready action
runs after framework and site routes are registered. Dependencies are plugin IDs mapped
to minimum semantic versions; dependency graphs reject cycles. API is an exact
integer compatibility boundary. Providers cannot be implicitly selected by a
feature's dependency graph.

Storage is lazy: construction and registration do not create a database. Install
owns schema creation in an immediate SQLite transaction. Schema 1 is recorded,
installation is idempotent, and unknown schemas are rejected. No SQL lives in
feature plugins or core. Database adapters may expose the same contracts but
must implement their own queries, migrations, and data movement.

Providers install schema only. App::install then runs the trusted site/install.php
callable to initialize this website through Settings and Pages, preserving existing
values. Normal requests and core updates never run the installer.

Site definitions in site/meta.php and site/pages.php own identity, browser titles,
template choices, and plain-text page copy. site/content/ contains editable text
catalogs; site/routes.php owns URLs. Theme markup reads and escapes those values.
Site.php provides generic loading and rendering without hardcoded homepage,
documentation, download, or 404 copy. /health remains a generic framework route;
release-download is an optional feature plugin. Health renders the site's own
page definitions instead of requiring specific starter templates.

Settings stores active theme identity. Theme validation checks manifest identity
and API before rendering. Every page uses a shared layout. Pages stores plain
text; output is escaped, not interpreted as arbitrary HTML. Assets are read only
inside the active theme's real assets directory and constrained by file type.

The framework owns core/ (including bundled providers, tools, tests, and guides), public/, README.md, and LICENSE.
An exact core-release inventory controls those paths; core updates may remove
stale managed files. Site-owned site/, config/, storage/, and .dist/
are never in a core update. Bootstrap packages include sample site-owned files
but are rejected by the updater. SMTP's bundled library lives in core/vendor/;
plugin updates are separately planned.

The root AGENTS.md is shipped in full bootstrap packages and preserved in core
updates. Upstream Git pulls/overlays do not provide this preservation contract.
See site.md for the bootstrap-once, core-releases-afterwards workflow.

Update validation rejects unlisted, missing, duplicate/case-colliding, traversing,
linked, private, oversized, corrupt, and API-incompatible entries. Staging occurs
under a private storage/updates/<id>/ folder. Candidate health runs in a fresh PHP
process with original private config/data/extensions and staged core. Versions
must agree between manifest and version.php. Same-version and downgrade updates
are rejected; rollback restores an explicit backup instead.

Every web request holds a shared update lock before loading PHP. Activation
holds the exclusive lock, snapshots all managed files, records a recovery
journal before replacing them, removes stale files, then probes the active core.
Activation failures restore a hash-verified backup. The persistent pending
journal blocks requests after an abrupt termination until recover restores the
old core. Rollback also records a pending journal so interrupted restoration is
recoverable. Backups are retained for operator inspection; no automatic retention
policy. CLI environment overrides are used by candidate health only, never HTTP.

This protects cooperating workers, not arbitrary external writes or OPcache.
Drain production traffic and clear OPcache. If recovery tools themselves are
damaged during a power loss, restore core/ from the verified private
backup or known trusted release first. Filesystem backups are not data backups.
Trusted executable candidate code can do anything PHP can do; checksums do not
make malicious releases safe. Release authentication is planned.
