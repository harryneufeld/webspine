# Composition editing

Implemented for #16. Existing installations remain on v1 until an operator selects **Enable composition editing** in the overview, or runs the explicit migration described in [spinecms storage](spinecms-storage.md). This creates a v2 revision; existing revisions, images and preview bytes remain retained. `/manage` then opens the composition board. Legacy writes fail clearly; they never modify a v2 graph through v1 fields.

New Fieldwork installations can start directly on composition v2 from the installed blank/service-business starter. Existing Studio setup remains v1 until explicit migration. The board consumes each package's declared Regions and definitions; it does not require Header/Main/Footer names. See the [package quickstart](package-quickstart.md). Selecting an incompatible package does not transform a stored site; old previews remain retrievable.

## Board

Expand a page to inspect Header, Main and Footer, including nested Groups. Each element has its own Edit/View link, Move up/down buttons and a More disclosure with applicable actions. Shared references are marked. Successful actions reopen the current page. Page details change the page title and description; the page-title element links to these details. Focused editors use the registered type's [field metadata](field-metadata.md): readable labels, text areas for longer copy, approved choices, image references and bounded lists. Custom types with complete basic metadata are creatable/editable without adding management templates. Missing metadata or specialized editor requirements are shown explicitly as unsupported by the basic board.

Each area offers **Add element**, **Reuse shared content** and **Group selected** when supported. Add opens a choice of types allowed in that area, then that type's fields before saving. **Add after this element** preserves the insertion point without entering a numeric position. Grouping uses checkboxes within one area and a focused Group setup screen. **Move to another area** offers destinations within the same owner; up/down adjusts position. More also offers Make shared, Detach and Remove where applicable. Removal requires a confirmation. Moves preserve ownership; cross-page reuse goes through sharing/reference/detach instead of implicitly reparenting content. Shared Groups remain structurally editable in their library, including when unused.

Shared arrangements show a confirmation page with the exact affected pages before saving. Shared field editors show this summary alongside the fields and require acknowledgement. The server recomputes the impact against the submitted revision and refuses missing/wrong acknowledgements or stale drafts. Shared Blocks remain editable from the Shared Blocks library even when no pages reference them. Referenced definitions cannot be deleted: remove/detach references first, then delete in a separate revision. Detach copies the complete resolved subtree with new Block/child-placement IDs and retains the selected outer placement ID.

All controls use native forms, labels, selects, checkboxes and disclosure controls. They work with keyboard navigation without drag-and-drop or JavaScript. Layout and focus styles use the existing management design tokens and narrow-screen rules. Saving a conflict never overwrites the newer revision; reopen the board before retrying. The first composition editor does not retain unsaved field text after a conflict.

## Permissions and adapters

Ordinary page composition requires `content:write`. Creating shared content and changing shared-owned Blocks/placements additionally require **`content:shared:write`**. Merely referencing shared content on a page or detaching an independent page copy does not modify the shared definition and needs only content editing authority. Changing website title/language requires `settings:write`; preview building requires `preview:build`.

Owner/Operator profiles have shared-write authority; Editor/Reviewer profiles do not. Startup adds this permission only to existing named Owner/Operator profiles and updates their security stamp: old sessions and app credentials for those upgraded accounts must be renewed. Stored app scopes never expand. Newly issued credentials must explicitly include shared-write scope when needed. The upgrade is transactional and idempotent; it does not alter the content database.

`CompositionEditor` lives in Core and accepts typed commands, caller authority, the expected revision and acknowledged page IDs. The board and API call it through the same application service. It validates metadata bounds, typed validators and the entire proposed graph before the selected `ICompositionDraftPersistence` commits atomically. Providers explicitly advertise type versions and supported operations; unsupported operations return a capability error and never fall back to spinecms. The host consumes the configured design package and spinecms; configuring an external provider and its conformance/migration remains #18. Preview storage still uses SQLite and will become a separate release contract.

## HTTP API v2

Bearer authentication is required; cookies cannot authenticate API requests. Management remains opt-in, Development-only and loopback/localhost-only. Issue app credentials under **Connected apps**. Use `content:read` alongside any write scopes for `/changes`.

| Method/path | Purpose |
| --- | --- |
| `GET /api/v2/site` | Read the validated composition snapshot |
| `GET /api/v2/capabilities` | Supported type versions, operations and capture guarantees |
| `GET /api/v2/schema` | Data-only field metadata, resolved choices/defaults and caller-permitted operations (`content:read`) |
| `GET /api/v2/shared/{id}/impact` | Current revision and affected page IDs |
| `POST /api/v2/changes` | Apply one typed conditional operation |
| `POST /api/v2/media` | Upload raw `image/png` bytes; `If-Match: "revision"` is mandatory |
| `POST /api/v2/previews` | Capture/build `{ "expectedRevision": "revision" }` |

An ordinary edit:

```json
{
  "expectedRevision": "revision-from-GET-site",
  "change": {
    "operation": "update",
    "blockId": "existing-block-id",
    "fields": { "heading": "A new heading", "text": "Updated words." }
  },
  "acknowledgedPages": []
}
```

For a shared edit, fetch its impact and include the exact sorted `affectedPages` in `acknowledgedPages`. The revision and acknowledgement belong together; a changed draft requires a fresh review. No raw graph-replacement endpoint exists. JSON uses exact camelCase names; unknown/duplicate fields, missing required command members and unregistered fields/types are refused.

Commands use these members inside `change` (the discriminator `operation` must appear first):

| operation | Members |
| --- | --- |
| `create` | `location`, `index`, `typeId`, `typeVersion`, `fields` |
| `update` | `blockId`, `fields` (complete registered payload) |
| `move` | `placementId`, `location`, `index` (position after removal) |
| `group` | `location`, `placementIds`, `fields` (registered Group payload) |
| `reference` | `sharedId`, `location`, `index` |
| `promote`, `detach`, `delete` | `placementId` |
| `deleteShared` | `sharedId` |
| `page` | `pageId`, `title`, `description` |
| `settings` | `title`, `language` |

API positions start at **0**. A location is either `{ "pageId": "home", "regionId": "main", "parentBlockId": null }` or `{ "pageId": null, "regionId": null, "parentBlockId": "existing-group-id" }`. Preserve Block IDs separately from placement IDs. Ordinary field updates cannot change owners, children or type versions.

Errors use problem responses: 401 for invalid credentials, 403 for missing authority, 409 for revision/contract state conflicts, 422 for invalid content or acknowledgement, 415 for an unsupported upload content type and 501 for an unsupported adapter capability. Request bodies are bounded. API preview URLs reuse the authenticated `/api/v1/previews/{id}/` delivery route; send the credential for every page/asset request. Human previews use `/manage/preview/{id}/`. Both keep links/images/CSS inside their private prefix and retain exact bytes after edits.

## Media policy

Upload accepts **PNG only**, up to 2 MiB, 4096 pixels per side and 4 million pixels. The first subset is non-interlaced 8-bit RGB/RGBA. The validator checks signatures, chunk bounds/order/CRC, mandatory chunks, compression and exact filtered pixel length; it rejects animation, unsupported critical chunks and invalid filters. Ancillary metadata is stripped. The final file path includes the full SHA-256 of the stored bytes; the asset ID uses its first 58 hex characters with a prefix to fit the content contract. An identifier collision is refused. Declared browser content type or filename alone is never trusted, and upload filenames never become storage paths.

Existing approved fixture media, including its trusted SVG, remains selectable and captured; new SVG/HTML/JPEG/WebP uploads are refused. There is no filesystem path input or external URL fetch. Images are copied into the snapshot capture and artifact. Referenced historical bytes cannot be replaced or garbage-collected by these editing operations. More upload formats, quotas and retention cleanup require separate work.

## Next boundaries

Patterns/Records and page blueprints follow in #17. The v2 demo keeps its existing five pages; adding pages under a design that requires a header/title/footer blueprint is not exposed yet. Page deletion, route changes, design editing, plugin installation, a visual canvas and public publication are also outside this slice. Content/image export is available from the composition board.
