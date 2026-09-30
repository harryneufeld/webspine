# 0006: Built-in content engine and first management workflow

Accepted implementation decision, 30 September 2026.

Use a narrow C# content module with SQLite through Microsoft's maintained `Microsoft.Data.Sqlite` library. It implements the common source contract and stores versioned snapshots, copied sample media and retained preview artifacts. SQLite provides transactions and conditional updates without a separate database service. This is the built-in authoring engine; external CMS adapters remain independent.

| Option reviewed | Fit | Trade-off |
| --- | --- | --- |
| Narrow C# module + SQLite | Existing typed page model, one authoritative source, single-installation .NET runtime | We own editor, revision history and field-mapping behavior; not a general CMS |
| Orchard Core | Maintained ASP.NET Core CMS with modular content, roles and workflows; supports SQLite | Adopting its content/workflow/admin conventions would substantially expand this small authoring slice |
| Payload | Mature content/admin tooling, database adapters | Requires Node/Next.js alongside our runtime, and an additional integration boundary |

This is a documentation/capability comparison, not a claim that all three engines were benchmarked or implemented. The narrow option will be verified through setup, edits, concurrency, export and restart tests. Reconsider it if required editorial workflows or schema needs exceed the bounded website model.

References: [Orchard Core documentation](https://docs.orchardcore.net/en/main/), [Payload installation](https://payloadcms.com/docs/getting-started/installation), [Microsoft.Data.Sqlite package](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12). The selected package is MIT; SQLite is public domain; its SQLitePCLRaw dependency uses Apache-2.0 licensing. Review native runtime assets through the existing three-OS CI matrix.

Persist state transactionally with a schema version. One initial site can be created either from a minimal Home page or an explicit copy of the five-page demo; setup cannot overwrite an existing site. Every successful edit appends a revision and atomically changes the head only when its expected revision matches. All content passes mandatory validation before storage. Media is copied into authoritative storage during demo initialization; no uploads in this slice.

Generate a retained preview from captured content, media and design bytes. Preview IDs identify immutable artifacts; private previews use no shared server cache and no browser/proxy caching. Preview creation is explicit and never publishes. A restart must preserve content, revision history and existing previews.

For eventual deployed accounts select ASP.NET Core Identity with cookie sessions and server authorization; revocable integration credentials remain a separate capability. They are not implemented in this slice. Until then, management is explicitly opt-in, Development-only, and restricted to loopback IPs and localhost Host values. State-changing forms use the maintained ASP.NET Core antiforgery service; credentials and previews are never publicly exposed. This is a local development authoring workflow, not deployable customer account management.

Do not infer production readiness, external connector support, durable build jobs or complete backup/restore from this milestone. They retain their existing acceptance requirements.
