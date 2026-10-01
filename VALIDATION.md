# MVP discovery validation

Date: 30 September 2026. Environment: Windows, .NET SDK 10.0.401.

- Solution build succeeded with zero warnings and errors.
- All 12 console checks passed: five-page fixture/assets; unsupported writes; opaque revision changes; unknown fields; missing references; unsafe links/paths; HTML escaping; deterministic output; contributor order/digests; mandatory validation and sealed output; failed/incomplete builds; cancellation.
- Management smoke checks passed for enabled Development, disabled-by-default Development, and rejection of demo-enabled Production with nonzero exit.
- HTTP checks covered /demo and /demo/, all five pages, CSS, SVG, export index, missing-page 404 and privacy headers.
- Browser navigation passed all five pages. Inspected desktop and mobile Home; Home and Products had no horizontal overflow or broken images at 390px width.
- Fixed a trailing-slash redirect loop and made invalid demo configuration exit cleanly rather than throw an unhandled exception.
- Temporary verification servers were stopped. Screenshots/logs are ignored under .local/verification/.
- Local Git has no configured remote or push; the sibling PoC remains separate.

Commands: `dotnet build Webspine.slnx`, `dotnet run --no-build --project tests/Webspine.Checks`, and `dotnet run --no-build --project tests/Webspine.ManagementChecks`.

Portability follow-up, 30 September 2026: removed the PowerShell-only management script and replaced it with a C# console check using portable process and HTTP APIs. It uses argument lists, runtime/dependency manifests from its own build output, bounded startup waits and cleanup of its own child hosts. The new runner passed all three management scenarios on Windows. The solution still builds with zero warnings/errors.

A GitHub workflow now builds and executes both check projects on Ubuntu 24.04, Windows 2025 and macOS 15. It has not run remotely because no GitHub repository is configured. Linux and macOS execution are pending; do not claim those platforms have already been validated. No verification hosts remain running.

These checks establish the discovery contracts and optional demo. Persistent CMS editing, accounts/permissions, durable workers, protected customer previews, deployment/recovery and AI access are not implemented. CMS selection, component/module registration and production design provenance remain open. Compiled extensions are trusted code, not sandboxed.

## Delivery follow-up, 30 September 2026

Added separate delivery and memory-cache implementation projects. Build passed with zero warnings/errors; all 12 content/build checks still passed. The expanded management runner passed five delivery groups and four actual-host configuration groups:

- Cache miss/hit preserves artifact bytes and runs ordered delivery header hooks on both.
- Changing/restoring the supplied artifact selects the correct cache namespace. This tests the source seam, not actual publication/rollback.
- Credential, authenticated, cookie and query requests bypass shared caching.
- HEAD has no body; matching ETags return 304; stale ETags return content; missing/unsafe routes and unsupported methods are rejected.
- No-cache substitution works and extensions cannot override protected security headers.
- Actual HTTP demo requests pass with cache enabled and disabled; cache configuration does not change bytes.
- Demo-disabled Development and demo-enabled Production restrictions remain intact.

All test child hosts were stopped by their runners. Native Linux/macOS verification is still pending GitHub execution. Dynamic rendering, production delivery policies and independently running public delivery remain planned; see `docs/delivery.md`.

## Shared layout follow-up, 30 September 2026

Reproduced a 15px difference caused by the scrollbar: at a 1000 × 1100 viewport, Home's main was 937px wide while Contact's was 952px. Added a stable scrollbar gutter (with an overflow fallback), an explicit shared container and central layout tokens. Browser measurements now show all five routes at the same 937px shell width and 24px left gutter in that viewport. At 390 × 844, all five use the same 327px shell width and 24px gutter, without horizontal overflow. Title/prose widths remain intentional shared variants. Saved a corrected screenshot under ignored `.local/verification/shared-layout.png`. Build passed with zero warnings/errors; the temporary preview was stopped.

## Naming follow-up, 30 September 2026

Adopted lowercase `webspine` with the web**spine** wordmark and modular web engine description. Renamed the solution/projects/namespaces, CI commands, diagnostic headers and current documentation. Repository contents, Git metadata and ignored local files moved into the `webspine` subdirectory of the existing Codex workspace. Windows retains a handle to the old `vibe-press` directory, which was left empty; no repository data remains there. Historical decisions and original copyright notices retain their earlier names.

`dotnet build Webspine.slnx` passed with zero warnings/errors. All 12 content/build checks and all nine delivery/management check groups passed using the renamed projects. Identified and stopped an older VibePress demo process that held the management directory open. New verification hosts were cleaned up by their runners. No remote or push was added. Linux/macOS verification remains pending.

## Repository setup, 30 September 2026

The user authorized pushing this project to `harryneufeld/webspine`. The existing local repository on `main` had no commits or remote. Created the GitHub repository and changed it to private at the user's request before uploading source. Reviewed the initial source/documentation/configuration file set and checked it for common token/private-key patterns. Ignored local data, credentials, generated outputs and the sibling PoC are excluded. This section supersedes earlier statements that repository ownership/visibility are undecided; native-platform CI results belong to the repository's Actions runs.

Initial commit `2b59cd2` was pushed to `origin/main` and its full SHA verified against GitHub. The first CI run started; its result was pending at setup verification. Added `.gitattributes` for consistent LF source files across operating systems. The current repository is private, with `main` tracking `origin/main`; earlier no-remote/no-upload statements describe the preceding discovery stage. The initial token lacked workflow-upload permission, so the successful push used the existing Git credential manager login without extending token scopes.

## Local authoring follow-up, 1 October 2026

The first repository CI run (36780418180) completed successfully on Ubuntu, Windows and macOS. The local authoring change adds a separate SQLite content adapter, explicit blank/demo setup, approved field editing, page creation, content/media ZIP export and retained prerendered previews. Decision 0006 records the bounded content-engine and future identity choices.

Windows verification: solution build passed with zero warnings/errors; all 12 core checks and all 15 management check groups passed. Additional coverage exercises no automatic seeding, restart persistence including media, atomic stale/forbidden/invalid writes, two competing adapter instances, duplicate routes, immutable retained preview bytes, stale build rejection, actual HTTP setup/edit/preview/export, antiforgery rejection, Host restrictions, conflict form preservation, blank setup and management-enabled Production rejection. Test hosts use their own temporary data and are stopped by the runner.

Browser verification: created an explicit five-page demo in isolated ignored `.local/browser-authoring`, changed and saved a Home heading, then built a preview showing that heading. Inspected the overview at desktop and 390px mobile widths; no horizontal overflow, and panels stack on mobile. Screenshot: `.local/verification/management-overview.png`. Reset the temporary viewport override after verification. This data is not the default editor database and is not committed.

This slice does not implement accounts, scoped API/AI credentials, uploads, a general component registry, durable build workers, production previews, public promotion, rollback, import or backup/restore. Private preview here means loopback-only Development access, not account authorization. CI for the new authoring branch is recorded in the pull request checks; the preceding CI run does not validate this new change.

## Owner accounts, external apps and shared metadata, 1 October 2026

Added owner Identity bootstrap/session authentication, per-operation server permissions, separately scoped expiring/revocable app credentials and a conditional HTTP authoring API. Website title and language are editable through both settings forms and that API; Core owns the common page field mapping and authoring contract. Identity/runtime data remains ignored and separate from authoritative content.

Windows Release build passed with zero warnings/errors. Core checks passed; expanded management coverage includes owner bootstrap protection, login/logout, anonymous UI/API denial, cookie/Bearer separation, stale settings preserving submitted values, shared title/language rendering, API page creation/editing and preview output, unknown/invalid input rejection, independent settings scopes, individual revocation, digest-only token storage, expiry, security-stamp invalidation and persistence after restart. Old preview bytes remain unchanged after metadata edits and restart. The standard matrix verifies the same checks on Linux, Windows and macOS in the implementation pull request.

Browser verification used an isolated synthetic owner/site under ignored `.local/browser-accounts`: sign-in, saved title change, updated management overview and reopened settings; inspected settings and Connected apps at desktop/mobile widths without horizontal overflow at 390px. Saved `.local/verification/website-settings.png`, reset the viewport and stopped the owned verification host. The initial Debug build encountered files in use, so verification used Release output. The final Debug build also passed with zero warnings/errors; a final process inventory found no management hosts running.

This is still Development-only and loopback-restricted. Additional human users/roles, recovery, hosted configuration, durable identity upgrade migrations, per-page scopes, durable audit, full section/media authoring, MCP/OpenAPI and publication remain unimplemented. API structured logs identify account/credential IDs but are not a durable audit trail.

## Human account administration and recovery, 1 October 2026

Added fixed operator/editor/reviewer permissions, original-owner protection, conditional account changes, disabling/re-enabling, authenticated password changes and a .NET CLI operator-recovery path. Existing single-owner claims upgrade transactionally and idempotently without a database-schema change or content reset. App credentials remain limited to the explicit app scope allowlist.

Windows Release build passed with zero warnings/errors. All 12 core checks and 20 management groups passed. New actual-host checks cover editor/reviewer denials, operator administration, stale account changes, owner protection, disabled login, role changes invalidating sessions and app credentials, reviewer self-service password changes, legacy owner upgrade, actual recovery through stdin without password output, unknown-account failure, old-session/token rejection after recovery and preserved website/account/preview state. The initial failure came from a test helper submitting an unchecked Disable checkbox; corrected the simulated form submission.

Browser inspected an isolated owner/editor/reviewer fixture, account controls, protected owner row and role descriptions; 390px mobile had no horizontal overflow. Saved `.local/verification/people-and-access.png`, reset the viewport and stopped the owned verification host. Synthetic fixture/runtime files remain ignored. Linux/macOS results are recorded by the pull request CI checks.

Limits remain: Development/loopback only; no public/email recovery, MFA, invitations, arbitrary permission profiles, per-page grants, durable audit or hosted schema upgrade/deployment workflow. Reviewer is not a publisher. Composition proposal #9 and button-target issue #11 remain separate work.

## Composition contract v2, 1 October 2026

Added an opt-in composition library with stable object/placement IDs, explicit ownership/shared references, typed registration, graph validation, adapter capabilities and captured-input builds. The existing v1 management/storage/API remain unchanged; this is #14, not the storage/editor migration.

Windows Release build passed with zero warnings/errors. All 26 core check groups and 20 management groups passed. Fourteen new composition groups cover published JSON examples, shared/nested expansion, strict fields, missing references/ownership/orphans, disconnected/shared cycles, eight/nine-level nesting, exact 1,000/1,001 occurrence bounds, Region/type/layout rules, custom Quote extension, registration conflicts/order, design/CSS/content/media digests, immutable prior output, escaping, unsafe paths/links, independent source capture, capability denial, source/design substitution and cancellation. The first test compilation needed explicit Placement constructor types and a fixture constructor warning fix; final checks run the newly built assemblies.

No browser workflow changes: v2 is not wired into the board/API. CI verifies the library and existing suite on Windows/Linux/macOS in this implementation PR. The composition guide distinguishes the library from planned migration, editing, Records/Patterns, hosted builds and publication.

## spinecms composition persistence and migration, 1 October 2026

Added database schema 2 metadata without automatic content conversion, v2 capture and trusted conditional commits, immutable media paths, historical readers, deterministic section identity maps, explicit migration with a rehearsed build, shared/nested detach/delete helpers and fresh-revision v1 recovery. The demo design registers shared header/footer/page-title rendering independently of SQLite. An offline .NET operator workflow runs without HTTP/account initialization; normal v1 management refuses a v2 head until #16.

Windows Release build passed with zero warnings/errors. All 26 core groups and 30 management groups passed. Ten new storage/operator groups reconstruct the shipped v1 schema; verify repeatable upgrade, failed/lossy/stale migration rollback, exact old JSON/preview retention, all section-value/page/media mappings, shared shell, multi-object persistence/reopening, concurrent writers, stale preview rejection, nested detach with new identities and preserved image references, referenced deletion, media immutability, fresh-revision recovery preserving v2 history, atomic fresh creation and actual inspect/migrate/restore subprocesses/startup guard. Compilation initially caught a test expression needing parentheses/simplification; corrected before running the new suite.

No customer database was migrated. Checks use isolated temporary directories and stop their own processes. Existing account/UI/API regression groups still pass. The implementation PR runs the same checks on Linux/macOS/Windows. Limits: current board/API remain v1, operator migration is for a copy, v2 HTTP preview hosting is not yet integrated, uploads/permissions are #16, and full backup/hosted migration/publication remain planned.
# Composition editing — 1 October 2026

Issue #16: typed composition commands now share authorization/validation between the board and bearer API. Local Release build has zero warnings/errors; 26 Core groups and 38 management groups pass (64 total). New checks cover create/update/group/move, cycles/ownership, sharing/impact acknowledgement, detaching, referenced deletion, unsupported adapters without fallback, bounded PNG validation/upload/selection, UI/API equivalence, CSRF/stale edits, board list editing, shared confirmation before persistence, v2 export and private five-page previews that preserve exact bytes after edits. Human-account checks also cover conservative shared-permission upgrade and invalidation of previous administrative sessions/credentials. Existing v1/account/recovery/delivery checks continue passing.

Browser review uses an isolated disposable local workspace. At 390 × 844, the board and shared editor have no horizontal overflow; shared editor inputs all have labels, and page disclosure toggles with the keyboard. The same management tokens/CSS and demo stylesheet classes apply across v2 content. Three-OS CI result is recorded with the pull request; local results above are Windows evidence only. Patterns/page blueprints, external CMS configuration and publication remain future issues.

## PR #22 management-board review, 1 October 2026

Replaced the global arrangement form in response to the PR feedback. Page areas now show nested structure, contextual Edit/View, Move up/down and More actions. Addition selects an allowed type, then shows only its fields; grouping and movement use focused forms, and removal confirms before writing. Group layout choices come from the design. Shared Groups can be managed from their library even when unused. Successful edits reopen the current page.

Windows Release build passed with zero warnings/errors; 26 Core groups and 40 management groups pass (66 total). The added HTTP regressions cover add-after placement order, current-page redirects, stable placement IDs through reordering/grouping/movement, stale reorder rejection, forbidden area types, approved Group selectors, focused forms and pre-write removal confirmation. Existing shared-authority, exact-impact acknowledgement, CSRF and immutable-preview checks pass.

Browser review used an isolated synthetic owner/demo under ignored `.local/review22`. Inspected desktop element rows and the 390 × 844 board/add-text flow: no horizontal overflow, and every visible text field had a label. Only Heading and Text appeared for text creation. Reset the viewport, closed the review tab, stopped the temporary host and removed its disposable database. Cross-platform results belong to the new PR checks; no customer data was changed. A visual preview designer remains planned.

## Razor design-package prototype — 1 October 2026

Issue #24 is implemented locally on `feature/razor-design-prototype`, based on main after merged PR #22. A dedicated in-memory fixture host renders v2 shared branding, nested Groups, typed text/image/CTA/FAQ components and a package-owned document shell through static Razor components. CSS, JavaScript and image bytes are frozen before rendering; the retained manifest identifies content/design, selected component, relevant executable assemblies/runtime and assets. Decision 0013 selects this direction for the next production package refactor. Existing production management, content and stored previews are not migrated.

Windows Release build passed with zero warnings/errors. All 26 Core groups, 40 management groups and 7 Razor groups passed (73 total). New regression evidence covers repeatable bytes, escaping and preview-prefix routing, actual emitted CSS/script/image references, captured implementation/runtime identity, script changes affecting new candidates without changing prior output, missing assets/unsupported mappings/invalid graphs, awaited async component lifecycles, pre- and mid-lifecycle cancellation, rendering failure without successful partial artifacts, subsequent-build recovery, and preservation of the established v2 finalization digest format. Existing migration, account, authorization, conflict and retained-preview checks still pass.

Browser review inspected the actual loopback page and captured script under CSP. Expand/Collapse all answers and individual native disclosures work with keyboard Enter; expanded-state labels update, images load and browser error/warning logs are empty. At 390 × 844 the hero and nested grid use one column with no horizontal overflow; desktop has no overflow. Saved desktop/mobile full-page evidence under ignored `.local/verification/razor-prototype/` and reset the viewport. The fixture preview can remain open for review without touching customer data.

The three-OS CI workflow includes the new suite; Linux/macOS execution remains unverified locally. This is a bounded renderer proof, not completion of #23 or production package/editing integration. The prototype still uses local component dispatch and the existing combined validator/renderer registry. Production JavaScript MIME/CSP support, complete executable dependency capture and configured package contracts belong to #25. Generic board/API field metadata is #26, and two-site reuse is #27. Cooperative cancellation does not provide process isolation or forced termination of arbitrary trusted component code.

## Configured design packages — issue #25

Implemented locally on `feature/configured-design-packages`, following the local #24 checkpoint. Core now owns pure typed definitions/validation and consumed package/renderer contracts; website markup is outside Core. `Webspine.Rendering.Razor` renders the configured `Webspine.Designs.Studio` package, while `Webspine.Rendering.Legacy` preserves compatibility. Management composition authoring, migration rehearsal and previews consume selected definitions/layout/rendering instead of `DemoComposition` calls. Unknown package/version fails before authoring storage initialization. Decision 0014 and `docs/design-packages.md` describe supported scope and limits.

Windows Release build passes with zero warnings/errors. Verification includes the existing 26 Core groups, 41 management groups and 14 Razor groups (7 prototype plus 7 production package groups). Production checks cover incompatible/duplicate/missing mappings, missing stylesheet/script/media, unsafe prefixes and collisions, repeatable five-page rendering/escaping, immutable changed-package candidates, exact executable ZIP/inventory integrity including native runtime dependencies, awaited lifecycle/cancellation/failure, explicit script MIME/CSP and candidate-bound script authorization, and an independent typed Quote definition/component without renderer dispatch changes. Management HTTP checks cover selected-package provenance, private candidate/executable input retention, prohibited executable delivery, exact preview bytes after editing/reopening, plus existing account/authorization, CSRF, concurrency and migration/recovery behavior.

Browser review of the production Studio package fixture confirmed shared shell, prefixed navigation from Home to Services, correct loaded CSS/SVG/script, keyboard activation of the package back-to-top enhancement with focus moved to main, and no horizontal overflow at 390px on Home/Services. Screenshots are ignored under `.local/verification/studio-package/{desktop,mobile}.png`. The temporary viewport was reset and the owned fixture host stopped.

The executable bundle retains actual installed application/dependency/shared-framework/native bytes, runtime/RID and OS description privately; website output contains provenance, never runtime ZIPs/binaries. This does not archive an OS image or implement historical replay; supported installation is file-based and framework-dependent. Pinning the hosted OS/container, durable jobs/retention cleanup and replay remain future work. Explicit CSS is supported; generated bundling/isolation is not implicitly supported. Trusted component cancellation remains cooperative. PR #28 was subsequently merged with successful Windows/Linux/macOS CI. Parent #23 remains open through the two-website proof in #27.

## Registered field metadata — issue #26, 1 October 2026

Implemented on `feature/content-field-metadata` from merged main. Core definitions optionally declare bounded data-only editor metadata; registration verifies complete property coverage. The board uses metadata for creation/forms/labels/summaries, while `/api/v2/schema` exposes metadata, resolved choices/defaults and caller-permitted operations. A separate Core-only FAQ module supplies typed validation, with Studio-owned Razor markup. Specialized editor requirements are explicitly unsupported by the basic board, with no value-dropping fallback. Decision 0015 documents defaults and explicit schema migration.

Windows Release build passed with zero warnings/errors. All 26 Core, 46 management and 14 Razor groups passed (86 total). Five new management groups cover invalid/incomplete/duplicate/versioned metadata registration; authenticated data-only discovery and permission filtering; FAQ board/API create/edit/list addition/removal and typed duplicate-question validation; unknown fields/count bounds, stale writes and forbidden edits; shared ownership/exact affected-page acknowledgement; escaped native FAQ previews, unchanged earlier bytes after editing and retained output after reopening SQLite. Existing adapter capability, CSRF, concurrency, authorization, migration and package capture checks continue passing.

Browser review used the disposable FAQ fixture. A labelled focused editor saved a readable heading/question/answer through keyboard activation. At 390 × 844 it has no horizontal overflow; the preview's native FAQ disclosure opens using Enter and exposes the saved answer. Screenshots are ignored under `.local/verification/field-metadata/`. Three-OS CI remains required for this new branch; these #26 results are local Windows evidence. Two-site reuse, hosted deployment, specialized editor loading, a general schema designer and publication remain separate work.
