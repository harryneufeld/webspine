# Content contract

Updated 2 October 2026. The default replaceable CMS is spinecms. Composition **v2** is the active management/storage authoring contract; [composition.md](composition.md) specifies its model, bounds and extensions.

`ICompositionSource` returns immutable snapshots with source identity, opaque revision and declared read/consistent-capture capabilities. `ICompositionDraftPersistence` defines trusted validated create/conditional commits. `CompositionEditor` authorizes typed operations before this boundary, rejects stale revisions and validates the whole graph. Unsupported adapter operations fail explicitly without hidden spinecms fallback. There is exactly one authoritative CMS per website.

Composition consists of stable pages, typed Blocks, Groups, named design Regions and site-shared references. Object IDs and placement IDs are separate. Validation rejects unknown types/fields, dangling references, cycles, invalid ownership, disallowed placements, duplicate routes and invalid media/links. Content supplies approved data and references; it cannot provide executable templates, JavaScript or CSS.

Registered definitions own typed validation and optional [editing metadata](field-metadata.md). The selected [design package](design-packages.md) owns rendering, mappings, layouts/styles/scripts and approved options. Both blank/example starters create native v2 snapshots once; persisted CMS content becomes authoritative. Startup and reads never seed or replace content.

Shared changes require separate authority and exact affected-page review. Page-local reference/detach operations preserve shared values. A new page fills declared required areas using existing compatible shared references or registered defaults and receives new page-owned identities. Canonical/duplicate route and full-graph checks apply before the atomic commit. Future Patterns/Records (#17) and external CMS adapters/source migration (#18) remain separate work.

Builds freeze content, referenced media, definitions/metadata, components, layout, styles/scripts and executable dependency provenance, then validate/finalize exact output. Candidate retrieval serves retained bytes independently of the current source or installed design; public publication and historical replay workers remain planned.

## Retired contract v1

The old flat `ContentSnapshot`/section model and read-only rendering fixtures remain for discovery/build regression checks and historical data inspection. Legacy SQLite writes, management forms, approved field mappings, `IWebsiteAuthoringSource`, converters and v1-to-v2 migration/restore tooling are removed. The legacy adapter reports no conditional-write capability and explicitly rejects draft changes. API v1 authoring operations return 410; known retained preview GET/HEAD routes remain available with preview permissions.

Old snapshot/artifact JSON is not rewritten or silently reinterpreted. Existing v1 authoring data does not need migration: preserve its private directory and start a fresh v2 workspace separately. See [management](management.md) and [storage](spinecms-storage.md). Previously accepted migration decisions are historical and superseded by decision 0017.
