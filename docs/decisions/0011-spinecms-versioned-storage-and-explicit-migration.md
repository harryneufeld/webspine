# spinecms versioned storage and explicit migration

Legacy authoring/migration policy superseded by [decision 0017](0017-v2-only-management-authoring.md), 2 October 2026. This record describes the decision at its original implementation; native v2 is now the sole authoring workflow.


Selected 1 October 2026 for #15.

## Decision

Extend the existing SQLite revision store with explicit per-row content versions and transactional transition metadata. Use one active head across v1/v2; retain old snapshots/media/artifacts. Upgrade schema metadata automatically but migrate actual content only through an explicit expected-revision operation.

The board remains v1 until #16. An explicit offline .NET workflow inspects, migrates or restores a copied installation without hosting HTTP or initializing accounts. A v2 head blocks normal v1 management startup clearly. Automatic content migration would remove the working editor before its replacement exists, so it is rejected.

Use `ICompositionDraftPersistence` as a trusted lower-level commit boundary, distinct from authorized typed application/API operations. Validate the complete proposed draft and media before atomic revision/head change. Do not expose this as unrestricted HTTP graph replacement. Keep registrations/design in the design package; SQLite handles captured content values and opaque revisions. Constructor registry injection supports custom registered content without storage switches.

Migration preserves identity/metadata/assets and compares mapped section payloads to prevent silent loss. IDs use a documented deterministic mapping; shared shell/page-title types are registered by the demo design package. Validate and rehearse a complete captured build before committing. Keep immutable media paths so historical captures stay reproducible. Reject referenced shared deletion; detachment creates independent identities.

Recovery copies a retained v1 snapshot into a new revision rather than pointing back to an old token. This avoids old edit forms becoming valid again. Recovery changes draft content and never claims to roll back a public release or merge newer content.

## Alternatives and consequences

Parallel editable v1/v2 heads or hidden fallback storage would create competing authorities; reject both. Rewriting history or preview output would invalidate prior review evidence. A new revision store/project rename is unnecessary for spinecms naming. Persistence remains replaceable by contract, with SQLite the only implementation for now; additional databases and business-data providers are not introduced.

The explicit migration is temporarily a library/operator capability rather than a user-facing upgrade. #16 must integrate composition operations/permissions and preview routing before normal v2 editing is available. Full authenticated audit, hosted upgrades, backup tooling and retention remain future work.

## Evidence

Tests reconstruct schema v1, demonstrate repeatable metadata upgrade, reject invalid/lossy/stale migrations without partial state, preserve mapped values and raw old records, persist/read/capture nested/shared content across reopening, race conditional commits, copy/delete safely, reject media mutation and recover v1 with fresh revisions. Actual command subprocesses exercise inspect/migrate/restore and the v1-host guard. See VALIDATION.md and PR CI for OS evidence.
