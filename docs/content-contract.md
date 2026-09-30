# Initial content contract

Implemented discovery slice, 30 September 2026. Contract version: 1.

`IContentSource` returns an immutable `ContentSnapshot` containing source identity, an opaque revision and website content. It declares read, conditional draft write and editorial workflow capabilities. Unsupported writes fail explicitly. The demo adapter is read-only and hashes its fixture bytes for its revision; this is not the persistent CMS.

The website has stable IDs, canonical page routes, titles, descriptions, language, sections and referenced assets. Four typed section kinds are currently registered in code: text, image, CTA and cards. Content has no executable templates or CSS fields. Unknown JSON fields fail in the demo loader. Additional component kinds will require a versioned schema/validation/renderer registration mechanism; a general component registry is not implemented yet.

Mandatory validation checks identifiers, unique pages/routes, a home page, bounded strings and collections, canonical internal links, HTTPS external links and existing image references. Artifact paths reject traversal and case-insensitive collisions. Text and attributes are escaped during rendering. These checks do not yet constitute an upload sanitization pipeline; the sample uses a trusted repository SVG.

`DraftChange` carries an expected source revision and a specific page, section and field. Future write adapters must compare and save atomically, after management authorization. The shape alone does not establish permissions, persistence or concurrency safety.

`BuildPipeline` validates before and after custom validators, renders captured content/design/assets, runs export contributors, seals immutable output and checks required pages and assets. The artifact includes source identity/revision, contract version, design revision, file digests and an overall digest. The demo design revision hashes its renderer assembly, CSS and preview base path; production design capture and module-version provenance remain release work.

The optional five-page sample persists as repository fixture files for repeatable discovery and tests. Current preview output lives only in memory. An optional future setup action can copy demo content into an empty CMS explicitly; no automatic seed, reset or customer-data replacement occurs today.
