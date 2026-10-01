# 0013 — Static Razor components for the design-package proof

Status: accepted for the #24 prototype, 1 October 2026. Production integration belongs to #25–27 under #23.

## Problem and evidence

The current v2 builder owns document markup and website CSS classes. Management selects `DemoComposition` directly, and registered block types are not automatically creatable/editable in the board. These boundaries prevent the demo from demonstrating an independent website framework. Merely moving C# HTML strings into templates would leave the coupling intact.

The bounded experiment at `experiments/Webspine.RazorPrototype` renders actual v2 content through compiled `.razor` components and .NET `HtmlRenderer`, outside a request. It exercises typed fields, recursive Groups, shared branding in two Regions, a design-owned document shell and metadata, captured CSS/image/JavaScript files, and an FAQ with ordinary browser behavior. It uses the existing mandatory composition validator and shared v2 artifact finalizer. The component implementation, selected runtime and input asset digests are recorded in the retained manifest.

Windows checks demonstrate repeatable bytes for identical frozen inputs and implementation/runtime, escaping, prefix-aware links, missing-input rejection, awaited asynchronous component lifecycles, cooperative cancellation and failed-render rejection. Browser review exercises script delivery under the prototype's CSP, keyboard disclosures and responsive layout. Three-OS verification is configured in CI; local evidence is Windows only. See `../razor-prototype.md` and `../../VALIDATION.md`.

## Decision and ownership

Use static Razor components as the initial renderer for the next design-package implementation. Keep that rendering dependency outside Core. Developers author typed components, layouts and CSS using the existing .NET SDK. Public output is retained HTML/assets. A Blazor browser runtime and C# browser event handling are not part of this choice. Progressive enhancement uses package-owned JavaScript, with usable native HTML as the baseline.

- **Core** owns source/content contracts, conditional-edit rules, ownership/reference/placement validation and immutable artifact finalization. It does not decide website tags, document layout or CSS classes.
- **Content definitions** own typed field schemas, defaults, validation and editing metadata. They do not choose visual presentation. The active CMS owns values/composition/media; installed definitions are trusted developer code.
- **Design packages** own component mappings, document shell, layouts/Regions, styles and browser assets. A layout selects allowed types and design choices within core invariants. The same content definition may have different presentations in different packages.
- **Rendering adapter/build orchestration** receives validated frozen content/design/assets and the selected executable package. It performs no live CMS reads and returns staging output. Core finalization includes every output file in the artifact identity. Authorization and validation cannot be disabled by a renderer.
- **Management** selects the configured package through its consumed contract. The demo becomes one package, rather than a special application dependency. The board/API use shared type metadata and application operations (#26).

Use explicit, deterministic trusted package registration during the operator's build/deployment process. Define ID/version, contract compatibility, mappings, layout rules and required assets before accepting a package. Missing/incompatible mappings must fail before authoring/building. Do not invent a runtime loader or marketplace for this milestone.

## Capture, assets and compatibility

Capture content and referenced media consistently, then freeze the selected design/configuration and all required CSS, scripts and other design asset bytes. Record content/schema/module/renderer versions and exact executable identities. A package's required dependencies and assets must be declared, validated and captured; changing them affects new candidates only. The prototype records the relevant direct assembly digests and runtime description. It does not archive a runnable dependency closure: production packaging must pin/capture the full executable environment and package dependencies in #25, without claiming universal reproducibility from a few assembly hashes.

Use the same captured content and prefix/link helpers for page links, image paths and design assets. Asset path collisions and missing files fail the build. CSS isolation/bundling is not exercised here; #25 must either capture its generated output explicitly or keep supported package CSS explicit. Browser scripts require declared package assets, the correct MIME type and an explicit delivery CSP policy. Current production delivery does not support JavaScript MIME/CSP; the dedicated prototype host supports only its own trusted captured script. This decision does not relax production delivery policy implicitly.

Keep v1/v2 stored content, revisions, HTTP semantics, migration/recovery and retained previews intact. Extracted `CompositionArtifactFinalizer` preserves the existing v2 digest format; v1 keeps its original finalization. A different renderer creates a new candidate and never rewrites old output. Any breaking content/design contract change requires explicit versioning and migration rather than opportunistic conversion.

## Boundaries and follow-up

This is one fixture page and a renderer feasibility decision, not completion of the framework. The experiment has local component dispatch and temporarily reuses the old combined validator/renderer registry. #25 separates definitions from presentation and supplies configured packages; #26 removes editor type switches; #27 proves two distinct websites. No management frontend, customer data, accounts or stored previews are changed by the experiment.

Components are trusted executable code. Lifecycle work must cooperate with cancellation; this does not isolate or forcibly terminate arbitrary code. Durable workers, execution limits, executable-environment retention and publication remain subsequent release work. Static rendering supports informational pages; dynamic data and interactive Blazor modes need separate decisions.

Management frontend replacement is not a prerequisite for this rendering choice. If authoring integration requires a replacement, redesign it from the user's tasks: clear page/shared-content navigation, readable labels and hierarchy, focused editing, understandable impact/conflict messages and a visible preview/release state. Review keyboard use and mobile layouts. Reuse shared UI components/design tokens within that new design, without copying the current page structure merely for visual continuity.

Reference: [Microsoft's standalone Razor rendering documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-components-outside-of-aspnetcore?view=aspnetcore-10.0).
