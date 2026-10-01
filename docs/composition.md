# Composition contract v2

Implemented library slice for #14, 1 October 2026. `Webspine.Core.Composition` provides an opt-in v2 model, registry, validator and captured-input build path. The [spinecms store](spinecms-storage.md) now supports v2 capture/conditional persistence and explicit migration/recovery. The [composition board and `/api/v2`](composition-editing.md) implement typed editing, media and shared-write permissions. Existing v1 sites continue using their flat editor until explicit migration. Patterns/Records remain #17.

## Objects and placement

`CompositionSnapshot` carries explicit `contractVersion: 2`, source identity, opaque revision and `CompositionWebsite`. A website selects one captured design layout. Each page supplies every declared Region exactly once, including empty Header/Footer Regions. Region ordering comes from the design; placement ordering comes from the CMS collections.

`Block` contains a site-unique ID, explicit owner, registered type ID/version, typed JSON fields and ordered children. Page ownership uses `{ "kind": "page", "id": "home" }`. Shared ownership uses `{ "kind": "shared", "id": "branding" }`, where branding is a `SharedBlock` definition with a root Block ID. Each non-root Block has exactly one owning placement; each shared root has zero owning placements and is reached through explicit references. Shared definitions may remain unused but must still validate.

`Placement` has a separate site-unique ID and either `kind: block` targeting an owned Block or `kind: shared` targeting a shared definition. Local targets must match the surrounding owner; shared references can cross owner boundaries. Moves preserve object and placement IDs. Sharing, detachment, deletion and conditional persistence are specified by decision 0009 but their editing operations are not implemented here.

## Validation and bounds

Core validation always precedes registered rendering. It checks unique IDs, ownership, reachability of owned objects, references, cycles (including unused shared definitions), Region constraints and registered type versions. Containers accept only types in the captured Group rules; Region rules constrain their immediate placements. Non-container Blocks cannot carry children. Registered validators add payload rules and cannot waive graph checks.

At most 8 container/Group levels and 1,000 expanded Block occurrences per page, counted across all Regions and repeated shared references. Each shared subtree is also bounded independently. Additional input limits: 100 pages, 500 image assets, 10,000 stored Blocks, 1,000 shared definitions, 10 Regions, 1,000 placements per container/Region, 65,536 characters of fields per Block, and field JSON depth 16. The reader bounds input at 16,777,216 characters and JSON depth 32. Design CSS is bounded at 1,048,576 characters. Existing bounded metadata, canonical routes, HTTPS/known-page links and asset path rules remain mandatory.

Use `CompositionJson.Read` for content. Property names are exact camelCase; unknown members, duplicate JSON properties and numeric enum values are rejected. Registered payload decoding also rejects unknown fields and honors `JsonRequired`. Deserialization alone is not validation: call the contract validator/build path afterward. Collection counts, revisions, references and required objects are checked there.

## Register a type

Each `BlockRegistration<T>` binds a typed .NET payload schema to its validator and renderer. `JsonRequired` declares required serialized members; validator code declares field bounds and semantic rules. The descriptor supplies type/schema/renderer versions, module identity/version, compatible contract version and whether children are permitted. Schema IDs identify these registered payload contracts; no general JSON Schema editor/generator is implemented.

```csharp
public sealed record QuoteFields([property: JsonRequired] string Quote);

var quote = new BlockRegistration<QuoteFields>(
    new("quote", 1, 2, "my-quotes", "1", "quote-fields-v1", "1", false),
    (value, context) => CompositionRules.Text(value.Quote, 1000),
    (value, context, html) =>
    {
        html.Markup("<blockquote>"); // Installed, trusted markup.
        html.Text(value.Quote);      // Escaped content.
        html.Markup("</blockquote>");
    });

var registry = new BlockRegistry(StandardBlocks.Registrations.Add(quote));
```

Imports: `Webspine.Core.Composition`, `System.Text.Json.Serialization`; use immutable collections for registrations/inputs. Add quote to the design's permitted types where it may appear. Registry construction sorts type IDs ordinally and rejects duplicate type IDs, incompatible contract versions and conflicting module versions. Only one version of each type is installed per registry. Breaking payload changes require a new version and explicit content migration, not opportunistic coercion.

Standard registrations cover text, image, CTA, cards and Group. A Group declares approved mode/alignment/spacing tokens and columns; design CSS interprets their emitted classes. CSS belongs to captured design, never Block fields. Image/link validation helpers verify assets and destinations. `HtmlOutput.Text` escapes text and attributes; `Markup` is for trusted installed code. Compiled registrations are not a sandbox. Keep executable implementations out of CMS data and record code dependencies through future module packaging.

## Sources and capture

`ICompositionSource` is a storage-independent read/capture boundary. `CompositionCapabilities` declares exact type versions, operations, atomic conditional writes and consistent capture. `Require` rejects unsupported operations/types; every non-read operation requires atomic conditional writes. `RequireCapture` requires read and consistent capture. These guards do not implement authentication or prove an adapter's promises: adapter conformance and server permissions remain mandatory. `CompositionEditor` implements typed authorized mutation operations without exposing unrestricted graph replacement; see [editing](composition-editing.md).

`CompositionBuildPipeline.BuildAsync(source, design)` checks capabilities before capture, checks source/design identity, validates captured content and requires supported captured types. It never falls back to spinecms. `Build(CapturedComposition)` supports inputs already captured by a trusted caller; it still validates them. Neither path queries mutable data while rendering.

Capture includes content, selected design/CSS and immutable bytes for exactly the declared image assets. Artifact paths and collisions are validated by the common builder. The output contains all page files, assets, `assets/composition.css` and `composition-manifest.json`. Those last two paths are reserved; conflicting assets fail. The manifest captures content digest, design/layout rules, registry/schema/renderer/module versions and registration implementation digests. Implementation digests cover payload and delegate assemblies; transitive module dependency packaging remains future module-loader work. Registrations must be backed by file-based compiled assemblies.

All files, including the manifest/CSS, enter the final artifact digest. Standard rendering escapes content; retained file arrays are immutable. Management retains and privately hosts these artifacts. Standard/design renderers use `BlockRenderContext.Link` and `AssetPath` for optional path-prefix routing; custom renderers must use these helpers too. Durable jobs and publication remain future work.

## Examples and compatibility

- [Content JSON](../examples/composition-v2.json): shared branding in Header/Footer and nested page-owned content.
- [Captured design JSON](../examples/composition-design-v2.json): Region/type rules, approved Group choices and shared responsive CSS.
- [Decision 0009](decisions/0009-composition-records-and-substitutable-cms.md): ownership and staged feature semantics.

The examples are loaded and built by the check suite. A separate test provider proves the source boundary and a Quote registration proves type extension without engine switch changes.

v1 `ContentContract.Version`, JSON records, retained previews and HTTP field-edit semantics are unchanged. v2 content identifies version 2 explicitly and uses its separate serializer/model. Typed composition HTTP operations use `/api/v2`; `/api/v1` refuses v2 content instead of reinterpreting it. Database schema version is separate from content version: metadata schema 2 retains v1 rows, with explicit content migration, history readers, mappings and fresh-revision recovery. No retained artifact bytes are rewritten or v1 payloads silently reinterpreted.

Storage backends remain implementation details of spinecms/adapters. Multiple external business-data providers and typed Records are separate from choosing SQLite/PostgreSQL/MySQL persistence; neither additional database backends nor Record providers are introduced here.
