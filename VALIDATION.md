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
