# Verification · 4 October 2026

Verified on Windows with official portable PHP 8.3.35, PDO SQLite, ZipArchive,
and OpenSSL. The local development runtime is ignored and is not in releases.

- 57 dependency-free integration checks pass (php core/tests/run.php).
- PHP syntax checks pass across framework, providers, theme, and CLI files.
- Live HTTP checks return 200 for home, docs, example plugin, SQLite page,
  health, CSS, and bootstrap download; private paths and PHP assets return 404;
  POST returns 405 with an Allow header.
- Browser checks at desktop, 768px, 390px, and 320px verify no horizontal page
  overflow. Desktop and mobile screenshots were inspected.
- Architecture tabs respond to click and arrow-key navigation, move focus,
  select the corresponding panel, and report their orientation at each layout.
- Documentation search filters sections, announces result counts, and handles
  no matches. The mobile navigation opens and exposes its expanded state.
- Copy buttons produce terminal commands without decorative shell prompts.
  No browser warning/error logs were observed during these interaction checks.
- Bootstrap and core ZIPs are built, with exact inventories and checksum
  sidecars. The download endpoint returns an actual application/zip payload.

Integration coverage includes idempotent installation, schema recording,
persistence and escaping, parameterized queries, explicit service contracts,
in-memory storage substitution, manifest compatibility, private asset rejection,
bundled dependency checksums, corrupt/missing/unlisted/linked/case-colliding/
traversing archives, CLI activation, site-file and database preservation,
stale-file removal, rollback, post-activation health failure, and journal recovery.

The content/theme separation is also verified: provider schema installation
contains no website copy; a site can use entirely different template names;
plain-text content remains escaped; core archives exclude site-owned paths and
reject a site-content overwrite; customized content, browser titles, routes,
identity, root AGENTS.md, and database persist through activation and recovery.
Browser checks confirm the existing design, architecture snippets, command copy,
and documentation search still render and work after extracting content files.

The consolidated layout is verified: bundled providers, CLI tools, tests, and
guides live under core/; customization lives under site/. Candidate probes load
staged system providers, conflicting custom provider IDs are rejected, and
generated .dist/ files are excluded from both archive types and ignored by Git.

Windows can hold the running PHP CLI file against rename. Activation normally
uses atomic rename; Windows falls back to a verified overwrite for locked
existing files under the exclusive lock and recovery journal. Abrupt failures
can still require restoring maintenance tooling from a verified backup. See
architecture.md and deployment.md for operational limitations.

Remaining gaps: a complete WCAG 2.2 AA audit with screen readers, real SMTP
delivery against configured credentials, load/concurrency testing, and testing
on Linux/production Apache or nginx/PHP-FPM. No claim of accessibility
certification, security audit, or production deployment is made. Signed online
updates, authentication, editing, public forms/uploads, and additional database
adapters remain planned.
