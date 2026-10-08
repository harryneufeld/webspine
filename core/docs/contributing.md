# Contributing with or without AI

Read existing files before editing. Protect site-owned work and secrets. Keep
the core small; capabilities belong in plugins, presentation belongs in themes,
and website content, identity, page titles, and routes belong in site/.
Use service contracts instead of provider-specific SQL outside a provider.
Record dependency versions, licenses, source URLs, and per-file SHA-256 hashes.
Do not add Composer/npm/build steps as requirements.

Run PHP syntax checks and php core/tests/run.php for core changes. Review affected
templates at desktop and mobile sizes. Check keyboard navigation, focus,
contrast, reduced motion, layout overflow, and accessible names. Report gaps
honestly; automated checks are not a WCAG certification or security audit.

Every new mutation endpoint needs authorization, validation, and CSRF defenses
before release, with authentication for privileged actions. Explicitly public
inquiries also need abuse protection. Escape untrusted output and use parameterized
queries. Review third-party PHP extensions as executable trusted code.

Do not replace an existing theme's identity with a generic design. Shared
layouts and scoped styles reduce accidental drift. The brand is lowercase
webspine; only spine is bold when displayed in formatted text.

Keep site content keys stable. Edit text values in site/content/, not framework
classes. Preserve the site's AGENTS.md during core releases. A theme change
must keep compatible content keys/templates or explicitly adapt them. Never
include site-owned paths in a core-release inventory. See core/docs/site.md.

Do not describe planned functionality as implemented. Keep README, in-site
documentation, and release version aligned. Every framework PR, including documentation,
must raise `core/version.php` above the current target branch. The required
`Version bump` CI check rejects unchanged/decreased or malformed versions; strict
branch protection requires the PR to be up to date, preventing two PRs from
merging the same version. Rebase and adjust the version when needed. Use a patch
bump for compatible fixes and keep API 1 for compatible changes. Breaking API changes
need a future compatibility/migration policy; the current updater rejects them.

Framework tests use `core/tests/fixtures/website/`, never the live/customizable
site. Website checks belong in `site/tests/` and should use disposable data and
explicit local configuration. Run both suites when changing the starter. The
repository ships only the minimal Starter theme; webspine.org is independent.

For canonical source, archive verification and exact release-asset publication,
follow [the framework release workflow](releases.md). Never upload an output
directory with a wildcard; packaging/verification failures stop publication.
