# 0014 — Configured static design packages

Status: implemented locally for #25, 1 October 2026. Three-platform CI and review remain pending.

## Decision

Core owns `IBlockDefinition`, typed decoding/validation, the composition graph, `IDesignPackage`, `ICompositionRenderer` and immutable artifact finalization. Core contains no website markup or rendering dependency. `Webspine.Rendering.Razor` implements standalone static Razor rendering; `Webspine.Designs.Studio` owns its typed component mappings, document, Regions, CSS and JavaScript. The old string renderer is isolated in `Webspine.Rendering.Legacy` for existing v1 behavior, compatibility checks and historical examples.

Management explicitly registers installed packages and selects `Design:Package` (default `studio`) and optional exact `Design:Version`. Unknown selections, duplicate registrations, unsupported contract/renderer versions and missing/incompatible typed mappings fail before authoring. The selected definitions/design govern composition validation, edits, migration rehearsal and previews. The optional demo fixture remains a setup choice; management composition operations do not select `DemoComposition`.

The current catalog consumes Razor packages. Multiple rendering implementations, installation discovery, hot-loading and a marketplace are outside this milestone. `ILegacyCompositionConverter` is explicitly supplied for the installed Studio schema. Existing v1 sites keep the compatibility renderer until explicit migration. New v1 previews use selected package CSS and record provenance; old stored previews are never rebuilt or rewritten.

## Frozen inputs and executable retention

Each package freezes descriptor, layout/configuration, content definitions and their implementation identities, document/component mappings, and immutable explicit asset bytes. Production builds receive `CapturedComposition`; they do not call the CMS during rendering. Mandatory graph/payload validation runs before rendering, and the unchanged v2 finalizer includes every emitted file in the candidate identity.

The first package capture archives the installed file-based application binaries and dependency/runtime manifests, application `runtimes/` native dependencies, the actual .NET and ASP.NET shared-framework directories, matching hostfxr and dotnet host. Sorted ZIP entries have fixed timestamps; inventory records every file's exact bytes, digest and length. Runtime, runtime identifier and OS description enter package provenance/identity. Application capture deliberately excludes settings, databases, credentials and arbitrary working-directory files. Packages use embedded resources or explicit captured assets; undeclared local files/live network reads are unsupported.

Management retains the executable ZIP once by digest in private `build-inputs/`, plus one candidate ZIP with content, design/configuration and all required media/browser assets. Integrity is checked on reuse; candidate storage precedes preview recording. Only website output enters delivery. A failed render/capture/input write does not record a successful preview or alter an existing preview. Orphaned private inputs from a later revision conflict may remain; retention/garbage collection belongs to durable build storage.

This is byte retention of a framework-dependent, file-based executable closure, not an implemented historical replay worker or an archived operating system. The matching OS/architecture and external OS libraries remain prerequisites. Hosted reproducibility requires a pinned OS/container image and a worker that restores the recorded runtime layout; those are deployment/job work. Single-file, trimmed, self-contained applications, arbitrary plugin probing paths and runtime installation changes during a process are unsupported. Components must remain deterministic for frozen inputs and cooperate with cancellation; trusted code is not sandboxed or forcibly terminated.

## Assets and delivery

Supported package styles are explicit captured CSS. CSS isolation, generated bundles and framework browser assets are not implicitly discovered or supported. A package must provide compatible typed components for every installed definition. Shared recursive dispatch uses `DynamicComponent`; adding a type/presentation needs no renderer switch. Core still owns bounds, ownership and reference validation.

Studio progressively enhances its usable static HTML with a captured back-to-top script. `DesignScriptPolicy.FromArtifact` checks the retained trusted manifest and exact declared script bytes; preview delivery opts in for that candidate. Only those `.js` assets receive JavaScript MIME, with `script-src 'self'`; inline execution stays blocked. Default delivery remains script-free. Content credentials cannot install scripts or packages.

## Evidence and follow-up

Windows Release build has zero warnings/errors. Existing v1/v2, authorization, migration/recovery, conditional writes and retained-output checks remain intact. Production package checks cover selection/mappings, repeatable five-page output, escaping/prefixes, complete executable inventory/bytes, changed assets, missing inputs/collisions, lifecycle cancellation/failure, explicit script policy and an independent typed Quote definition/component. Management checks verify private retained inputs and reopened preview bytes. The three-OS workflow runs all suites; Linux/macOS results require CI.

The management frontend still has fixed field templates. #26 supplies shared editing metadata and must address readable navigation, focused editing, shared impact and mobile/keyboard use. If replacement is necessary, redesign from user tasks rather than reproducing the current management page. #27 proves two independent website designs; #23 stays open until that broader framework proof is complete.
