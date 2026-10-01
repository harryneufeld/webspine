# spinecms composition storage and migration

Implemented for #15, 1 October 2026. `Webspine.Content.Sqlite` persists versioned v1 or v2 content behind common contracts. The product name is spinecms; project/namespace and source identity (`builtin`, `sqlite`) remain compatible. There is one active head, not parallel editable CMS copies.

## Storage versus content versions

Database schema version 2 adds `revisions.contract_version` and `content_transitions` to the existing content database. Metadata upgrades run transactionally and idempotently at initialization, tagging old rows as v1 without rewriting their JSON. Existing v1 sites remain v1 and continue using the current board/API. No startup seeds or converts content.

An explicit content migration creates a new v2 revision and switches the same head. Historical rows, media and preview JSON are retained. A transition records original/new revisions and section identity mappings. This is recovery metadata, not a complete authenticated audit trail. Newer unsupported database versions refuse initialization.

`ICompositionSource` reads and captures the v2 head. `ICompositionDraftPersistence` defines trusted create/conditional-commit operations on proposed validated drafts; it is not an HTTP graph-replacement endpoint or permission boundary. #16 must authorize typed application operations before using this persistence boundary. Capability reporting exposes read/capture and atomic-write guarantees; it does not yet advertise the future typed editing operations. UI/API remain v1 for now.

Commits take an expected opaque revision, validate the whole graph and required media, insert one immutable snapshot and move the head in one immediate SQLite transaction. Conflicts and validation failures leave no partial head/revision/media changes. Site identity cannot be replaced. Capture reads head/content/media in one read transaction with an immutable captured design supplied by the caller. Snapshots must fit the v2 reader bounds before insertion.

Media paths are immutable: replacement bytes require a new path. Removed references do not delete media required by history. Upload sanitization, retention/garbage collection and quota policy remain #16/release work. Existing trusted media and proposed byte maps are not an upload endpoint.

## Migration mapping and design

`LegacyCompositionMapping` maps each `(page ID, section ID)` to `block-` and `placement-` followed by the first 32 hexadecimal characters of SHA-256 over UTF-8 `pageId + "/" + sectionId`. The complete mapping is retained, and validation rejects duplicate IDs. This handles repeated section IDs across pages and avoids overflowing bounded identifiers. Preserve page IDs, routes, titles/descriptions, website title/language, field values and asset IDs/paths.

The demo design package supplies registered `site-header`, `site-footer` and `page-title` types in addition to the five standard types. Header/Footer become shared definitions; each page receives independent reference placements and page-owned title/content Blocks. Navigation stores stable page IDs, resolves captured names/routes and retains current-page behavior. Website title is reused from captured metadata. Layout wrappers share the same `site-container` class. The module supplies captured CSS and rendering; SQLite does not import its templates or own design definitions.

Migration checks the identity map and every original section's type/values, validates composition, captures stored media and rehearses a complete build before inserting the revision/head transition. Renderer failure, missing media, reserved/colliding paths, incompatible design or v2 bounds reject migration with the v1 head intact. Previously generated output is not rebuilt or rewritten; newly generated v2 markup need not be byte-identical to v1. Repeating migration with the current v2 revision validates it and changes nothing. Stale expected revisions fail.

`CompositionCopies` supplies pure helpers for detaching a shared reference and deleting an unreferenced definition. Detach expands its subtree into independent Blocks/placements with new IDs and preserved fields/media references; the selected outer placement keeps its ID. Remaining references stay shared. Persistence also refuses deleting a shared definition referenced by the current draft, even when a proposed commit removes the references simultaneously: detach/remove first, then delete in a later commit. Browser/API commands and shared-write permissions arrive in #16.

## Operator workflow on a copy

The board/API cannot edit v2 yet. Use this migration slice on a copied installation for verification; keep the working v1 site until #16 provides editing. A normally started management host refuses a v2 head clearly instead of misreading it. Retained preview bytes remain stored/readable through the library, but the current v1 HTTP preview host is unavailable while that guard is active.

Stop the host and copy its private data directory, including the database and any SQLite journal/WAL sidecars if present. Keep the original intact. Use the same absolute copy directory for every command. These commands run through the .NET host but exit without binding HTTP ports, initializing accounts or changing account credentials. Development and explicit management configuration are still required. The commands initialize content-schema metadata, including `inspect`; inspection does not migrate content.

```text
dotnet run --project src/Webspine.Management -- --environment Development --Management:Enabled true --Management:DataDirectory "<absolute-copy-directory>" --Content:Action inspect
```

The output lists head revision/version and revision history. Use the exact inspected revision:

```text
dotnet run --project src/Webspine.Management -- --environment Development --Management:Enabled true --Management:DataDirectory "<absolute-copy-directory>" --Content:Action migrate --Content:ExpectedRevision "<inspected-v1-revision>"
```

The result reports the original revision, new revision and identity map. Preserve the report with private migration records. No raw content fields or credentials are printed. Missing/invalid/stale parameters or failed validation return a nonzero exit code. No missing database/site is automatically created by this workflow.

To recover v1 content after verification, inspect the current head and select a retained v1 revision:

```text
dotnet run --project src/Webspine.Management -- --environment Development --Management:Enabled true --Management:DataDirectory "<absolute-copy-directory>" --Content:Action restore --Content:ExpectedRevision "<current-v2-revision>" --Content:LegacyRevision "<retained-v1-revision>"
```

Restore validates the retained v1 snapshot/media and creates a **fresh** v1 revision before switching the head atomically. Old forms therefore remain stale; v2 history and previews are retained. This restores CMS content, not a published release, and does not merge v2 edits into v1. Inspect/reconcile those edits before recovery. Database metadata stays schema 2; this is not a downgrade for an older application binary. Original-file backup recovery must be done with all users/processes stopped and the matching application version.

The current code can then start the v1 editor on the recovered copy. Full installation backup/restore, hosted migrations and publication are separate MVP work.

## Verification

The management checks reconstruct the previously shipped schema, reopen/upgrade it, compare original JSON/preview records, test failed/lossy/stale migration, convert all demo section types, persist mappings and shared shell, commit nested/shared objects, race two writers, detach nested shared Groups, reject referenced deletion/media replacement, preserve old artifacts, restore with a fresh revision, and exercise the real offline commands/startup guard. Tests use isolated temporary directories and clean up their hosts.

SQLite remains the only persistence implementation. Another backend must provide these transactional/revision/capture guarantees; external business-data providers and Records remain separate from backend selection.
