# Registered content editing

Implemented for #26; [decision 0015](decisions/0015-data-only-content-editor-metadata.md) records the boundary. The existing composition board now derives creation, focused forms, summaries and type labels from registered definitions. Website markup remains in the selected design package.

## Register a type

Supply a `ContentEditorMetadata` as the third argument of `BlockDefinition<T>`, alongside the descriptor and typed validator. Register the definition in the selected package, map it to a compatible Razor component, and allow it in the appropriate Regions/Groups. See `src/Webspine.Content.Faq/FaqContent.cs` and Studio's `FrequentlyAsked.razor`: the FAQ definition depends only on Core, while Studio owns presentation. Management needs no FAQ-specific changes.

Metadata uses exact serialized property names, readable labels/descriptions, required flags, initial values and bounds. Basic registration must describe every serialized property exactly once, including nested list-item properties. The supported kinds are text (single/multiline), captured image references, validated destinations, approved choices, bounded integers and repeated scalar/structured items. Reference/choice sources are explicit: images, pages or the selected design's Group options. They cannot query arbitrary external resources or carry markup, scripts, styles or executable code.

Version 1 bounds registration to 32 fields per object, 64 total fields and three nested object levels, 100 list items, 100 static choices, 120-character labels and 500-character descriptions. Strings declare a limit of at most 5,000 characters. Lists require a present `ImmutableArray<T>` (use a zero minimum to allow empty lists); nullable/omitted lists are intentionally unsupported by the basic editor. Scalar payload properties use strings or integers; only text supports multiline editing, and integer choices must be numeric. Each type may impose tighter rules. FAQ allows 1–20 questions, 160-character headings, 200-character questions and 3,000-character answers; its typed validator also rejects duplicate questions after trimming and case folding.

The board presents native labelled controls, list-item fieldsets, explicit add/remove controls and a single **Save changes** action. Adding a structured item saves its declared initial values, then the editor can fill it in. Nothing requires browser JavaScript. Approved references and choices are resolved against the current site/design. Required references without an available choice make basic creation unavailable.

## API discovery and writes

`GET /api/v2/schema` requires bearer `content:read`; cookies are refused. The response includes `contractVersion: 2`, `metadataVersion: 1`, the current `revision`, package descriptor, layout/Group rules, operations and shared-write authority. Each registered type has its descriptor, `editor`, `defaults`, `resolvedChoices`, `boardEditable`, `boardCreatable`, `canCreate`, `canUpdate` and an optional `unavailable` explanation. Nested choice paths use names such as `items[].imageAssetId`. Enum values follow the existing composition JSON contract.

Operation flags combine caller permissions and selected adapter capabilities. They describe potential actions: actual writes still check ownership, placement, shared impact, revision and the complete graph. No implementation binaries, runtime inventories or credentials are returned. `defaults` is a proposed new payload, not an update to existing content; typed validation may impose additional semantic requirements.

Use the ordinary `POST /api/v2/changes` create/update commands with the registered `typeId`, exact `typeVersion` and structured fields. Updates supply the complete payload. Metadata validation and typed server validation apply through the same application service as the board. Unknown fields/types, invalid references/choices, stale writes, missing shared authority/acknowledgement and unsupported source operations fail before persistence.

## Specialized editors and evolution

Definitions may omit `Editor` for compatibility with existing typed extensions. To declare a specialized requirement, provide metadata with an explicit `SpecializedEditor` identifier and an empty basic field list. Discovery reports this requirement and the basic board displays a read-only explanation. A typed API client can still use the registered payload if its operation is permitted. Loading a specialized editor executable or registering a management UI integration is intentionally unsupported; that needs a separately designed trusted integration contract. The board never substitutes a generic form that drops unrepresented values.

Defaults apply only when creating a Block or explicitly adding a list item. Board saves preserve omitted existing fields; unknown submitted fields are rejected. Label/help/default changes do not rewrite stored data. Removing/renaming a field, changing its type, tightening a bound beyond existing values or changing validator semantics incompatibly requires a new type version and explicit migration: preserve/export the source, transform deliberately, validate the complete result, rehearse a build and commit conditionally. There is no automatic block-version conversion endpoint in this slice. An incompatible package cannot open an existing draft by silently repairing it.

Metadata is retained with definition identities in candidate inputs/provenance. Changes affect new candidate identity; viewing retained previews does not regenerate forms or website bytes. Existing v1 sites and stored historical previews remain retained until an operator explicitly migrates content.

## Review fixture

`dotnet run --project tests/Webspine.ManagementChecks -- --metadata-preview` runs the FAQ checks and keeps their isolated temporary host alive for keyboard/mobile review. It prints the loopback URL; sign in with the disposable fixture owner `owner` / `Disposable-Test!123`. Stop it with Ctrl+C after review. This uses synthetic content/accounts only. Normal checks stop their hosts automatically. Hosted management, arbitrary schema editing, frontend replacement and public publication remain separate work.
