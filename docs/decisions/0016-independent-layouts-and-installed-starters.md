# 0016: Independent layouts and installed website starters

Accepted, 1 October 2026, issue #27.

The second design proof exposed two remaining assumptions: Core required Regions named `header`, `main` and `footer`, and management loaded the demo fixture directly. Both prevented a package from owning its complete website structure.

Region identifiers now belong to the captured layout. Core still requires 1–10 unique declared Regions, valid registered types and bounds, and each page must fill every declared Region exactly once. Ownership, references, cycles, expansion limits and typed validation are unchanged. Studio keeps its existing layout. This is an additive relaxation of design registration, with no snapshot/storage version change or rewriting of existing content. Legacy renderers retain their original layout-specific behavior; new layouts use their own Razor document.

`Webspine.Examples` supplies the default host's explicit installation catalog, starter data and optional legacy converter. Management consumes an `InstalledDesign` rather than a fixture source or a type-specific initializer. Starters provide exactly one validated v1 or v2 website and immutable assets, committed by the same spinecms source. Studio's blank/demo setup remains v1 until explicit migration; Fieldwork starts directly on v2. No existing site is converted on configuration change. Starters are trusted operator-installed code, not another live CMS or an editor's arbitrary schema.

Fieldwork is an independent Razor package with Brand/Content/Contact Regions, a navigation rail, service lists, a shared contact panel and its own CSS/browser assets. It has no Studio or Demo dependency. Both packages reuse Core standard definitions and the Core-only FAQ module. Management no longer references `Webspine.Demo`; the optional read-only `/demo` route uses the installed Studio example plus the explicit legacy compatibility renderer.

Fieldwork intentionally supplies no automatic Studio/v1 migration mapping. Incompatible v1 preview builds/migration and v2 authoring fail clearly; historical preview delivery does not load or rebuild the selected package. Selecting the original package and explicit fresh-revision recovery preserve the source/history. Switching between incompatible layouts requires a deliberately implemented mapping and validated migration, not a CSS-only configuration change.

The proof uses the existing renderer, source, authorization, conditional writes, artifact finalizer and captured executable environment. It does not add production hosting/publication, dynamic rendering, a schema designer, runtime plugin installation or historical replay. Three-platform CI and keyboard/responsive review are the acceptance evidence for this slice.
