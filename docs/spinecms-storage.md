# spinecms composition storage

Updated 2 October 2026. `Webspine.Content.Sqlite` is the first `ICompositionDraftPersistence` implementation. All new websites start on composition v2. The retired v1 editor and its migration/recovery tooling are removed; legacy authoring-data conversion is outside scope. See [decision 0017](decisions/0017-v2-only-management-authoring.md).

## Versioned persistence

Database schema version 2 tags immutable revisions with their content contract version. Initialization transactionally adds metadata to old schema-1 databases without rewriting snapshot or artifact JSON and refuses newer unsupported database versions. A v1 content head remains v1 and cannot be edited. Existing obsolete transition tables are left untouched; new databases do not create them. Database schema version and content contract version are distinct.

`ICompositionSource` defines consistent read/capture. `ICompositionDraftPersistence` supplies trusted create/conditional commits; it is not an authorized HTTP graph-replacement operation. `CompositionEditor` checks typed commands, permissions, registered type capabilities and the whole proposed graph before persistence.

Commits compare an opaque expected revision in an immediate transaction, validate graph/media, insert an immutable snapshot and replace the head atomically. Invalid or stale proposals leave no partial head, history or media. Source/site identity cannot be replaced. Capture reads head, content and media in one transaction with a frozen design. Serialized snapshots must fit v2 reader bounds.

Media paths are immutable: replacement bytes require a new path. Removed references do not delete historical media. PNG upload is implemented through authorized [composition operations](composition-editing.md); byte maps at the trusted persistence boundary are not an upload permission boundary. Retention/quota/garbage collection remain release work.

## Shared content and output

Detaching a shared reference copies its entire subtree into page-owned Blocks and child placements with new IDs, retaining the selected outer placement ID and captured field/image values. Remaining references remain shared. Referenced definitions cannot be deleted; detach/remove references first and delete in a later revision. Shared changes require separate authority and exact affected-page acknowledgement.

Previews retain source identity/revision, content contract, design provenance and exact file bytes. Reading a stored artifact does not read the current draft or invoke its renderer. Existing human and API preview URLs remain valid with preview permission, including v1 artifacts and after changing the selected package. Approval/publication/rollback are separate planned release work.

## Fresh setup for a retired workspace

Preserve the original private data directory. Stop the host and configure `Management:DataDirectory` to a separate empty folder. Restart, create an owner account and create a blank or example v2 website. No automatic conversion, deletion, head replacement or legacy restore occurs. Original preview URLs remain accessible when running the original workspace. Full installation backups/restores must use matching application versions and stopped processes; a rehearsed production procedure remains planned.

The old `Content:Action` inspect/migrate/restore commands now fail before initialization. Offline password recovery is unaffected. External CMS source migration (#18) and future incompatible v2 schema evolution are separate concerns, not a replacement v1 conversion workflow.

## Verification

Management checks seed the actual old database schema as historical data and verify idempotent metadata initialization, unchanged snapshots/preview JSON/head, rejected legacy operations and exact authenticated preview retrieval under another selected design. Native v2 checks cover fresh creation with atomic media validation, graph/shared/nested commits, reopen, competing writers, stale preview saves, detach/delete and immutable history/assets. Retired offline commands are checked for nonzero exit without opening/modifying content. CI runs the checks on Windows, Linux and macOS.
