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

Every new mutation endpoint needs authentication, authorization, validation,
and CSRF defenses before release. Escape untrusted output and use parameterized
queries. Review third-party PHP extensions as executable trusted code.

Do not replace an existing theme's identity with a generic design. Shared
layouts and scoped styles reduce accidental drift. The brand is lowercase
webspine; only spine is bold when displayed in formatted text.

Keep site content keys stable. Edit text values in site/content/, not framework
classes. Preserve the site's AGENTS.md during core releases. A theme change
must keep compatible content keys/templates or explicitly adapt them. Never
include site-owned paths in a core-release inventory. See core/docs/site.md.

Do not describe planned functionality as implemented. Keep README, in-site
documentation, and release version aligned. For core releases increment the
semantic version and keep API 1 for compatible changes. Breaking API changes
need a future compatibility/migration policy; the current updater rejects them.
