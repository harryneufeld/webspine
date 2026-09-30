# Working on webspine

Read `PROJECT.md`, `docs/architecture.md` and the relevant issue before implementation. This is a fresh MVP; treat the sibling PoC as reference only.

- Use C# and the SDK declared in `global.json`. Keep nullable checks and warnings-as-errors enabled.
- Support Windows, Linux and macOS; Linux is the primary hosting target. Required build, verification and application operations must not depend on PowerShell, Windows APIs, IIS or platform-specific paths. Select cross-platform dependencies and verify all three operating systems in CI.
- Use one authoritative content source per site. The built-in CMS and external connectors implement the same contract. Keep vendor-specific details inside adapters.
- Keep services behind explicit extension contracts; see `docs/extensions.md`. Hooks cannot disable core authorization, validation or artifact guarantees. Compiled plugins are trusted code, not sandboxed AI operations.
- Reused UI elements must share their components and design tokens within a design package. Keep page shells, gutters, typography and responsive behavior consistent across routes; reserve scrollbar space to avoid navigation shifts. Document intentional layout variants rather than introducing per-page overrides.
- Enforce permissions and conditional writes on the server. Do not add arbitrary executable content or CSS fields.
- Freeze release inputs. Bind approval to exact output, publish without rebuilding, and preserve the live release when a build fails.
- Keep rollback separate from CMS content changes. Keep public delivery independent of management.
- Never commit credentials, databases, private snapshots, runtime files or build output. Do not import the PoC's `.local/` data.
- Select maintained identity/storage components explicitly; record significant decisions under `docs/decisions/`.
- Add meaningful regression coverage for authorization, concurrency, adapters and release behavior as they are implemented. Do not copy assertions solely to create passing tests.
- Build the solution after code changes and run the relevant checks. Update documentation to distinguish implemented behavior from planned work.
- Keep GitHub issue status authoritative once issues exist. Issue IDs in the draft plan are planning IDs, not GitHub issue numbers.
- Preserve user changes. Do not push, publish or change repository visibility without session authorization.
