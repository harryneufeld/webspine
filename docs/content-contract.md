# Content contract

The default replaceable CMS is named spinecms. See the accepted next-contract direction below; the implementation remains version 1.

Implemented discovery slice, 30 September 2026. Contract version: 1.

`IContentSource` returns an immutable `ContentSnapshot` containing source identity, an opaque revision and website content. It declares read, conditional draft write and editorial workflow capabilities. Unsupported writes fail explicitly. The demo adapter is read-only and hashes its fixture bytes for its revision; this is not the persistent CMS.

The website has stable IDs, canonical page routes, shared title/language, page titles/descriptions, sections and referenced assets. Four typed section kinds are currently registered in code: text, image, CTA and cards. Content has no executable templates or CSS fields. Unknown JSON fields fail in the demo loader and authoring API. Language tags use bounded structural validation. Additional component kinds will require a versioned schema/validation/renderer registration mechanism; a general component registry is not implemented yet.

Mandatory validation checks identifiers, unique pages/routes, a home page, bounded strings and collections, canonical internal links, HTTPS external links and existing image references. Artifact paths reject traversal and case-insensitive collisions. Text and attributes are escaped during rendering. These checks do not yet constitute an upload sanitization pipeline; the sample uses a trusted repository SVG.

`DraftChange` carries an expected source revision and a specific page, section and field. The SQLite adapter implements validated conditional writes with a transaction, new opaque revision and atomic head update. It also exposes page-level editing, page creation and consistent content/media capture. Approved field mappings are shared with the local editor. External write adapters must provide the same atomic comparison guarantee. The editor uses owner Identity permissions and the HTTP API uses app scopes; both remain Development-only and loopback-restricted. `IWebsiteAuthoringSource` adds conditional website-settings, page-creation and page-edit operations; Core owns the shared approved field mapping.

`BuildPipeline` validates before and after custom validators, renders captured content/design/assets, runs export contributors, seals immutable output and checks required pages and assets. The artifact includes source identity/revision, contract version, design revision, file digests and an overall digest. The demo design revision hashes its renderer assembly, CSS and preview base path; production design capture and module-version provenance remain release work.

The optional five-page sample persists as repository fixture files for repeatable discovery and tests. Explicit Use demo setup copies content and media into SQLite; Start blank creates a minimal Home page. Neither startup nor visiting the editor seeds or replaces data. Saved drafts and immutable preview artifacts survive restart. The separate read-only `/demo/` fixture still lives in memory. See `management.md` for the local workflow and limits.

## Accepted next contract (not implemented)

[Decision 0009](decisions/0009-composition-records-and-substitutable-cms.md) owns detailed composition/data rules and staged migration. The contract belongs to webspine, with spinecms as the first implementation and exactly one active CMS. The board and external clients operate through supported adapter capabilities; changing CMS does not inherently remove webspine editing.

The first stage adds registered typed Blocks, ordered Groups, named layout Regions and site-shared references. Separate object/placement IDs preserve identity on moves. Page-owned objects have one placement; shared objects can have multiple references. Reject dangling references, cycles, invalid ownership and disallowed placement. Initial expanded-page limits are 8 Group levels and 1,000 Block occurrences. Shared changes require additional permission and disclose affected pages. Detachment copies a resolved subtree with new IDs; referenced shared definitions cannot be deleted.

Design packages own definitions/rendering and allowed options; CMS data contains instances and approved choices, never executable templates or arbitrary CSS. Later Patterns declare typed inputs and overrides; independent instances can bind separate typed content Records. Record schema registration is versioned. External business Records have explicit providers and read/write boundaries. Required missing data fails a candidate; optional data uses declared defaults. Capture every reference, schema/design version, bound value and media asset before rendering.

Keep v1 historical snapshots readable, retained preview bytes unchanged and migration atomic/recoverable. Map old page-scoped section IDs explicitly; preserve page IDs, routes and content. New API operations must have documented version/capability negotiation without silently changing existing v1 field-edit behavior. Schema/validator/renderer registration must allow custom types without adding engine switch cases. These capabilities, migrations and operations are follow-up work, not available in today's interfaces.
