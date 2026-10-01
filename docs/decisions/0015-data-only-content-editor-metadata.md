# 0015: Data-only content editor metadata

Accepted, 1 October 2026, issue #26.

Typed content and Razor presentation were separated in decision 0014, but management still needed a type-specific defaults/form switch. A reusable content extension must carry its authoring contract too.

An optional `ContentEditorMetadata` belongs to `IBlockDefinition`. Version 1 describes bounded text, image references, destinations, approved choices, integers and repeated scalar/structured items. Registration verifies exact serialized property coverage and type compatibility. The generic board derives labels, defaults, forms and summaries from this data. The authenticated v2 schema endpoint exposes metadata, current choices/defaults and capabilities filtered by caller authority. Both writing paths retain mandatory metadata bounds, typed validators, composition rules, conditional revisions and adapter capabilities.

Definitions without metadata remain compatible with typed clients. A `SpecializedEditor` ID explicitly declares that a type needs another editor; the basic board offers viewing and an unsupported explanation, without a destructive fallback. Executable editor loading is intentionally unsupported in this slice. Trusted compiled code stays under operator-controlled registration.

Defaults initialize new objects only. Existing values are never removed or coerced by a metadata update. Breaking payload or validation changes need a new content type version and explicit validated migration. Metadata changes are captured in package provenance and candidate identity; retained previews keep their bytes. A general schema designer, frontend replacement, plugin loader and automated content-version migration remain outside this decision.

The independent `Webspine.Content.Faq` module demonstrates the full authoring contract with a typed semantic validator. Studio supplies its Razor renderer. Management contains no FAQ type/property switch.
