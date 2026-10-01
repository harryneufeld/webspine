# Composition, Records and a substitutable CMS

Accepted direction, 1 October 2026. Resolves the design assessment in [#9](https://github.com/harryneufeld/webspine/issues/9). This is a decision about planned behavior, not an implemented contract upgrade.

## Problem and decision

Flat sections and renderer-owned headers/footers cannot express nested composition or editable shared content. Establish a common composition/data contract owned by webspine, implemented first by **spinecms**, the default replaceable CMS. Keep exactly one authoritative CMS per website. External CMS compatibility remains an MVP objective; universal compatibility is not promised.

The management board is the default editor, not part of CMS storage. Its composition operations and external AI operations must use the same application service/API rules. An alternative UI must not require privileged access to spinecms internals. This does not require internal services to call their own HTTP endpoints; equivalent authenticated operations must be available through the API.

## Vocabulary and ownership

| Concept | Meaning | Owner |
| --- | --- | --- |
| Block | Typed content/presentation instance, such as Text, Image or CTA | Active CMS owns instance values; registered module defines schema/validation and design package supplies rendering |
| Group | Block with ordered child placements and approved stack/grid/alignment/spacing choices | Active CMS owns composition; design package controls permitted options |
| Region | Named layout slot with child constraints, such as Header, Main or Footer | Definition in design package; filled placements in CMS |
| Spine / layout | Defines Regions and their arrangement | Versioned design package; page selects an available layout |
| Pattern | Reusable structure with typed inputs and explicitly permitted overrides | Versioned design package; independent instance inputs in CMS |
| Shared Block | One site-owned Block or Group referenced by multiple placements | Active CMS |
| Record | Structured, presentation-independent content data with typed fields | Active CMS for content Records; external application for its business data |

Use `SharedBlock` rather than the ambiguous proposal name Core. Use descriptive layout names in contracts; Spine is the product term. A logo starts as an Image Block with a logo role. Keep the existing CTA and cards types during migration; a separate Button type and target behavior remain #11.

## First-stage rules

- One site-wide default layout defines Header, Main and Footer. Page-specific layout variants are deferred. CMS instances may compose registered Blocks within Region rules; editors do not modify executable templates, CSS or layout definitions.
- Ordinary Blocks and their descendants have a single page owner. Shared Blocks and descendants have a site owner. Cross-site reuse is deferred. Ownership is independent of placement.
- Blocks have stable IDs unique within a site. Placements have separate stable IDs, an explicit target (owned Block or Shared Block reference), and order represented by an ordered collection. Moving preserves IDs. One ordinary Block has one placement; a Shared Block may have many reference placements. No implicit cross-page ownership transfer: use an explicit copy or promote-to-shared operation.
- Group children can be registered Blocks, Groups or Shared Block references if allowed by the destination constraints. Reject dangling references, duplicate placement IDs, multiple ownership and cycles across Groups/shared references. Initial limits: at most 8 Group levels and 1,000 expanded Block occurrences per rendered page, including layout/shared content. Repeated references count on each expansion. Preserve current bounded page, field and asset limits; implementation must enforce expansion limits before rendering.
- Shared references have no placement-specific content/style overrides initially. Detach resolves a reference into an independent subtree with new IDs owned by the destination page or shared container; other references remain intact. Copies preserve values and asset references. Deletion is rejected while referenced; removing a placement does not delete a shared definition.
- Continue one opaque site revision and atomic conditional writes. Multi-object operations validate and commit together. External adapters unable to guarantee an operation must reject it rather than partially apply it.
- Content editing does not grant design editing. Shared content writes require an explicit additional permission, planned as `content:shared:write`, because they affect multiple pages. Affected pages and placements must be shown before a shared change is submitted. Ordinary content editors are not implicitly granted this permission. Follow-up implementation defines the role defaults and migrates existing credentials conservatively.
- The renderer resolves the validated graph from frozen content and design inputs. Shared edits affect newly generated previews; retained previews/releases stay unchanged. Failures do not replace public output.

## Patterns and Records: specified now, implemented later

A Pattern is a blueprint, not a shared content instance. Each instance supplies its own typed inputs, including optional Record references. No arbitrary descendant editing is implied: only declared inputs and override slots are editable. Pattern and schema IDs/versions belong to captured design/module provenance. Resolve against the selected versioned design package; do not fetch a mutable latest definition during a build. Compatible design updates apply when that package is deliberately selected for a new build. Breaking input changes require a migration; an instance cannot silently lose fields. General per-instance version selection is deferred.

Record schemas are registered and versioned by developers/modules, with field types, bounds, required fields and approved bindings. Content Records live in the selected CMS and use its conditional writes. SQLite persistence is a spinecms implementation detail. Application services enforce permissions and rules before an adapter writes; storage does not define business behavior. A schema-derived editor is future work, not a general user-defined schema designer.

An external business-data provider may supply typed Records from an ERP or similar application. It is not a second CMS and cannot secretly own page composition. References include provider identity, record type and stable record ID. Reads do not grant writes; business mutations need separately declared operations and domain rules. Never mirror unsupported external-CMS composition into editable spinecms storage.

Bindings map declared Record fields to compatible Block/Pattern inputs, without executable expressions or arbitrary queries. Missing required data fails the build; optional data needs an explicit schema default or omission rule. Required referenced data cannot be deleted without resolving references. For external deletions, reject a new candidate rather than rendering misleading fallback data. Capture record values, referenced media, provider/schema versions and revision/consistency evidence before rendering. A provider without an acceptable consistent capture strategy cannot supply an approved candidate. Build workers and public delivery do not query mutable business sources. Live data on Dynamic pages requires a later runtime/approval policy.

## Examples (illustrative, not an API payload)

```text
Site layout: standard
  Header Region -> placement h1 -> Shared Block navigation
    Group: horizontal
      Image: main-logo
      Group: navigation-links
        Link Blocks (future registered kind)
  Main Region (Home page)
    Group: vertical
      Text: introduction
      Group: grid
        CTA: services
        CTA: products
  Footer Region -> placement f1 -> Shared Block branding
```

Other pages fill their own Main Region and have distinct Header/Footer placements referencing the same shared instances. Navigation can retain the existing renderer behavior until a registered navigation/link representation is introduced; this example does not make that type available today.

```text
Content Records: Product A, Product B
  fields: name, description, image
Pattern: product-card@1
  input: product (Product reference)
  structure: Image + Text + approved link presentation
Main / product-grid
  instance card-a -> product: Product A
  instance card-b -> product: Product B
```

Changing Product A affects its bound views in the next candidate. Changing the Pattern's design affects both cards in a candidate built with that updated design package. Changing card-a's reference does not change card-b. Commerce and business workflows are not introduced by this example.

## Fit, compatibility and implementation stages

Current `WebsiteContent`/`PageContent`/`ContentSection` and `ContentContract.Version = 1` remain unchanged by this document. `IContentSource` currently reports only read, conditional draft write and workflow capabilities; `IWebsiteAuthoringSource` covers page/settings edits. `ContentFields`, validation and `DemoRenderer` contain closed type switches. SQLite stores immutable JSON revisions and a head pointer, while management uses shared authoring operations. These are useful foundations, but none yet implement a Block tree, registry, Regions, shared editing or Record bindings.

1. Define the versioned composition contract and explicit operation/type capabilities, plus deterministic schema/validator/renderer registration. Keep extension failures subject to mandatory validation.
2. Implement spinecms persistence and a repeatable migration. Map old page-scoped section identities to site-unique Block identities while preserving page IDs, routes and values. Document the identity mapping. Keep historical v1 snapshots readable and retained artifact bytes unchanged; never rewrite stored previews. Validate a full migration before atomically changing the head and retain an operator recovery path. Do not rename existing projects/namespaces merely to introduce the spinecms product name.
3. Add shared application/API operations and an editor for create/edit/move/group/reference/detach/delete, including approved media handling. Default and alternative clients receive the same validation/conflict guarantees. Demonstrate five-page shared shell and nested content.
4. Add versioned Patterns and a small content Record example after the first composition workflow works. Business providers, live queries, cross-site reuse and arbitrary schemas remain deferred.
5. Prove an external CMS adapter and migration against these capabilities. The board remains the editor for supported operations; spinecms accepts no content writes while another CMS is active. Source switching does not publish automatically.

Storage and API contract versions must be explicit; new operations must not silently change `/api/v1` field-edit semantics. Choose the concrete compatibility endpoint/version during stage 1. Adapter capability reporting covers supported types, create/update/move/share/media operations, conditional-write guarantees and consistent capture. Unsupported operations are explicit in the API and UI. No hidden fallback database or two-way CMS synchronization.

## Alternatives and consequences

Replacing only the CMS UI would simplify integration but abandon the agreed CMS substitution goal. Combining external data with an editable spinecms composition store would obscure authority. Both are rejected for the CMS model. Separate business-data providers remain legitimate because their ownership is explicit.

Implementing Patterns, arbitrary Records and a visual builder at once would make the first migration and extension contract harder to review. Stage those features instead. The accepted model adds bounded structured composition to MVP scope while retaining developer-controlled design and one active CMS. It does not change the initial audience to enterprise or promise arbitrary vendor support.

## Implementation issues

- [#14](https://github.com/harryneufeld/webspine/issues/14): versioned contract, registration and capabilities.
- [#15](https://github.com/harryneufeld/webspine/issues/15): spinecms storage and v1 migration, after #14.
- [#16](https://github.com/harryneufeld/webspine/issues/16): API/editor, shared authorization and media, after #14–15.
- [#17](https://github.com/harryneufeld/webspine/issues/17): Patterns and content Records, after #14–16.
- [#18](https://github.com/harryneufeld/webspine/issues/18): external CMS and migration proof, after #14–17.

Release jobs/publication work remains in the wider MVP plan. GitHub owns implementation status; accepting this decision does not complete these tasks.

## Verification required

Follow-up issues must cover Windows/Linux/macOS, migration/restart/recovery, stale concurrent graph edits, shared-write authorization, missing targets, cycles, expansion limits, schema/renderer compatibility, and unchanged retained previews. Show that a new registered Block and another content source can be implemented without modifying engine type switches. Frozen inputs must include every referenced shared object, Pattern, Record and asset before a candidate is eligible for publication.
