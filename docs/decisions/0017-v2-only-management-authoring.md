# 0017 — Native v2 as the sole management authoring workflow

Accepted 2 October 2026 for #31. Supersedes the legacy editing/migration policy in decisions 0010, 0011, 0014 and 0016; their other composition, storage and design guarantees remain applicable.

## Problem

The independent design proof established a reusable v2 composition workflow, but Studio still created v1 drafts that needed an explicit conversion. Maintaining two editors, write APIs, converters and offline migration/restore tools made the early MVP harder to understand and extend. The owner explicitly chose to drop legacy editing without requiring migration of legacy authoring data.

## Decision

All blank/example starters return native v2 composition and media. Studio stores its example as native structured composition data; Fieldwork constructs native composition. The default `/manage` screen is the composition board. Setup creates exactly one validated v2 revision atomically; it never overwrites existing content.

Remove legacy management page/section/settings/preview authoring, type-specific approved field mappings, SQLite legacy writes, the old authoring extension interface, converters, identity mapping and offline content inspect/migrate/restore tooling. Authenticated retired UI/API routes return 410 with v2 guidance instead of executing or adapting old commands. Obsolete offline options exit before storage initialization. Account recovery remains a separate supported workflow.

A v1 content head is unsupported for authoring. Show clear guidance to preserve its private directory and create a new v2 website in a separate empty directory. Do not convert, erase, reinterpret or replace its data. There is no v1 authoring migration deliverable. Metadata initialization can tag old database rows without rewriting their content/artifact JSON; existing obsolete transition tables are left untouched rather than deleted.

Retained preview retrieval stays independent of the editable source and selected design. Preserve authenticated old preview URLs and exact stored bytes. New API previews use `/api/v2/previews/{id}/`; the `/api/v1/previews/{id}/` GET/HEAD route remains retrieval only. Read-only legacy rendering fixtures remain for independent build/delivery regression evidence, not a second management editing model.

Preserve page creation through a bounded typed v2 operation. Required Regions come from the selected layout. Follow the first page's required-area shape, using its existing shared references or registered basic defaults, assign fresh page-owned identities and validate the full graph/routes before commit. Require create/type/share capabilities as applicable and content-edit authority. Do not change shared navigation or fields automatically; editors update navigation through its separately authorized shared editor. Designs lacking safe defaults fail explicitly and atomically.

## Consequences

New installations have one editing path and no enable/conversion step. Old authoring clients must use v2 commands and explicit registered schemas; v1 data is intentionally unsupported for editing. Native starters remain installation-owned examples, never synchronized shadow sources. Permission, CSRF, concurrency, shared-impact and capture guarantees are retained.

This does not remove future v2 schema evolution or the independent external CMS source-migration proof in #18. Patterns/Records, rich page blueprints, production workers/hosting/publication and full installation recovery remain separate work.

## Verification

Exercise blank/example setup and page creation for both designs, typed board/API edits and denied/stale/invalid writes. Seed the previously shipped database schema as historical test data; verify unchanged JSON/head/history and exact preview bytes under another selected design. Check retired UI/API operations and offline commands cannot write or convert. Run Core, management and Razor checks on Windows/Linux/macOS and review the board with keyboard and narrow mobile viewport.
