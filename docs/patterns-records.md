# Patterns and reusable content Records

Implemented for local Development authoring, 2 October 2026; [#17](https://github.com/harryneufeld/webspine/issues/17). Hosting, publication, external CMS adapters and business providers remain separate work.

## Records, Patterns and shared elements

**Records** store structured information about a subject, independently of its presentation. **Patterns** are package-owned presentation blueprints that bind to compatible Records. **Shared elements** reuse one composed element instance across pages. These are separate kinds of reuse, not synonyms.

Default Studio offers **Records → Add record → Content entry**, with a required title (160 characters), optional description (3,000 characters, default empty text) and optional managed image. Use it for any subject, then choose **Add element → Record card** on a page. The card selects a Content entry and has its own optional caption (160 characters). Reusing the same Record avoids copying information; changing a card's selection changes only that instance. Select images through the Images library.

The engine has no built-in Product concept. Types and forms come from the installed `RecordRegistry` and editor metadata, and presentation comes from the design package. Developers can register schemas for people, services, events or other content with their own fields and compatible Patterns. Content entry is one neutral starter schema, not a universal schema or an in-browser schema designer. Fieldwork remains independent and does not register these types.

## Optional Product proof and existing content

Select `--Design:Package studio-products --Design:Version 1` to enable the optional Product@1 / product-card@1 demonstration from #17. A Product has a required name, optional description and managed image, with the same bounds as Content entry. No price, inventory, ordering or commerce behavior is introduced.

Default Studio retains the Product schema/component registrations to validate, edit and render existing drafts, but sets their editor metadata `AllowCreate` to false. They do not appear in creation selectors, and typed create operations reject them; discovery reports `canCreate: false`, while authorized updates remain available. No existing data or retained artifact is renamed, rewritten or deleted. The opt-in example enables creation through the same registry and operations.

In the optional example, use **Records → Add record → Product**, then **Add element → Product card**. Select a Product by name and optionally enter a card caption. Two cards can select different Products; several cards/pages can select the same Product without copying its values. The Pattern's structure and styling belong to the package's Razor component; editors cannot edit arbitrary descendants, templates or CSS.

Editing a card's selection affects that instance. Editing a used Product affects every card that selects it in newly built previews. The form shows the affected pages and requires their acknowledgement. Used Records require both `content:write` and `content:shared:write`, including when all their uses are currently on one page or in an unplaced Shared Block in the library. Creating unused Records requires `content:write`. Readers see disabled fields. Used Records cannot be deleted; replace/remove their references first, then confirm removal of the unused Record. Existing previews retain their exact bytes.

## Contract and API

`CompositionWebsite.Records` is an additive v2 collection with an empty default for older native v2 snapshots. No existing JSON revision or artifact is rewritten. Each `ContentRecord` has a site-local stable ID, `schemaId`, explicit `schemaVersion`, opaque per-Record `revision`, and strict typed `fields`. The snapshot's `SourceIdentity` identifies its authoritative CMS. Site and Record revisions are separate: an update requires both the current site revision and the current Record revision.

`RecordRegistry` registers developer-owned `IRecordDefinition`/`RecordDefinition<T>` schemas, semantic validation and bounded data-only editing metadata. Records have no renderer registration. `BlockRegistry.Records` supplies schemas to mandatory whole-snapshot validation. Collections are limited to 1,000 Records, field objects to 64 KiB characters, and registered schemas to 100. Duplicate IDs/properties, unknown fields, invalid values/versions and missing referenced images are rejected. Omitted/null optional fields resolve through their declared defaults; unknown fields are never silently removed.

Patterns are registered typed leaf Blocks with a `PatternDescriptor`. Their ID/version must match the Block definition; all editor fields must be declared either as typed `RecordInputs` or `OverrideSlots`. A Record input names its schema/version and a required choice field. Registration verifies binding metadata and any human-readable label field against the Record schema. The selected design package owns the fixed component structure and styling. Record references must be declared Pattern inputs so impact and shared authority cannot be bypassed through unrelated fields. Record-to-Record references, general visual Pattern construction and arbitrary local overrides are outside this slice.

`CompositionCapabilities.RecordSchemas` declares the source's supported schema/module versions. `RecordCreate`, `RecordUpdate` and `RecordDelete` are separately supported operations; writes require atomic conditional guarantees. Missing capability support fails explicitly without a fallback store. Reads/candidates also check declared Record schemas.

Authenticated discovery uses `GET /api/v2/schema`: `recordSchemas` contains schema descriptors, field metadata and effective write flags; each Pattern type contains its versioned descriptor, inputs and override slots. Read Records through `GET /api/v2/site`. `GET /api/v2/records/{id}/impact` returns the site revision, Record revision, whether any instance references it (including the shared library), and sorted affected page IDs.

Use the existing `POST /api/v2/changes` envelope with these typed operations:

| Operation discriminator | Change fields |
| --- | --- |
| `createRecord` | `schemaId`, `schemaVersion`, `fields` |
| `updateRecord` | `recordId`, `expectedRecordRevision`, `fields` |
| `deleteRecord` | `recordId`, `expectedRecordRevision` |

For example, create a neutral Content entry in default Studio:

```json
{
  "expectedRevision": "current-site-revision",
  "change": {
    "operation": "createRecord",
    "schemaId": "content-entry",
    "schemaVersion": 1,
    "fields": { "title": "Repair workshop", "description": "Everyone welcome." }
  },
  "acknowledgedPages": []
}
```

Create a card with the ordinary `create` operation (`typeId: "record-card"`, `typeVersion: 1`, `fields: { "recordId": "returned-record-id", "caption": "Everyone welcome" }`). Rebind it with `update`. Update used Records with the exact `acknowledgedPages` returned at the current revision. Permission failures are 403, site/Record conflicts 409, invalid data/references/acknowledgement or package-disabled creation 422, unsupported source schemas/operations 501. Human forms use the same application operations and CSRF protection.

## Capture, compatibility and explicit upgrades

The SQLite source captures a transactionally consistent snapshot containing Record values, identities, schema versions and Record/site revisions, plus immutable media bytes. All Records are validated, including unused ones. The selected package freezes schema descriptors, editing defaults, implementation digests, Pattern descriptors, component versions and executable closure. Private `build-inputs/{candidate}.zip` retains the original content, schema/design provenance and media; executable ZIPs stay private. Website manifests contain schema/Pattern provenance and Record identity/revision evidence, with a digest binding the complete content capture.

`RazorPageContext.Record<T>` resolves only frozen CMS data and verifies the requested CLR/schema version. Required absent/deleted/incompatible Records and missing media fail capture/build. Optional Product data follows the frozen schema defaults. Neither the build worker nor public delivery queries mutable CMS/business data. Compatible deliberately selected package revisions apply only to new candidates; stored previews are served independently of the current package.

Breaking schema/input changes require a new version and an explicit developer-registered migration. Merely selecting version 2 cannot reinterpret version 1 data. `CompositionUpgrade.ApplyAsync` accepts the old and target installed designs/registries and an explicitly selected `CompositionUpgradePlan`. Its trusted `VersionedFieldMigration` callbacks produce complete new Record/Pattern field objects. The caller must hold content, shared-content and settings permissions and supply the expected site revision. Each changed schema/input version needs exactly one mapping; missing mappings, dropped types, invalid values, capability failures or stale revisions leave the head unchanged. The complete result validates before one conditional commit, preserves site/page/Block/Record/placement identities, creates new Record revisions and retains old JSON revisions.

This is a trusted host application boundary, demonstrated with a Product/Card v1→v2 migration in the checks. No generic migration HTTP endpoint, operator command or user-supplied mapping language is installed. A deployment adding breaking versions must install and explicitly invoke its own reviewed migration plan with both package versions available. Test it against a backed-up workspace before selecting the new authoring package. This does not restore retired v1 website conversion tooling.

## Future external business providers

The implemented Product Records belong to the active CMS. A future external business provider is a separate typed data boundary, not a second editable CMS. References must include provider identity, stable Record ID and schema/version; reads must declare their consistency/revision evidence and field mappings. Capture all bound values and media with that evidence before building. Provider data changing or disappearing after a retained capture cannot alter its output; missing required data at a new capture must reject the candidate.

Read access must not grant business writes. Any supported business mutation needs separate explicit operations, credentials, user permissions, conditional/domain rules and audit in the originating application. Orders, inventory or ERP rules remain there. A provider lacking an acceptable consistent capture strategy cannot contribute an approved candidate. No ERP connector, mutation, live query or synchronized business-data mirror is implemented here; Dynamic runtime queries need a later approval policy. External CMS substitution remains #18 through the same composition/Record source capability boundary.

## Verification

`RecordPatternChecks` exercises the explicitly selected Product example: authenticated board/API operations, two Products/three cards across pages, typed/unknown-field rejection, permissions, exact impact, site/Record conflicts, guarded deletion and media, escaping, frozen inputs, retained bytes, compatible new design capture, and atomic explicit breaking migration/reopening. `DefaultRecordChecks` verifies neutral Record/card authoring, type selection, server-enforced retirement of example creation and preservation of existing Product editing, rendering and history. Both run on Windows, Linux and macOS. `--default-records-preview` provides the neutral browser fixture; `--records-preview` provides the optional Product fixture. The respective `--default-records-only` and `--records-only` flags run focused checks.
