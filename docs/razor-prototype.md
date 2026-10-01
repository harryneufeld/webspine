# Razor design-package prototype

Issue [#24](https://github.com/harryneufeld/webspine/issues/24) supplies a bounded, runnable proof before the production refactor. [Decision 0013](decisions/0013-static-razor-design-package-proof.md) records the selected direction, ownership and remaining limits. The production board and renderer still use their existing implementation.

## Run and inspect

From the repository root, with the SDK selected by `global.json`:

```text
dotnet build Webspine.slnx --configuration Release
dotnet run --no-build --configuration Release --project experiments/Webspine.RazorPrototype
```

Open `http://127.0.0.1:9094/`. Stop with Ctrl+C. The dedicated host builds the fictional Alder Studio fixture once, then serves immutable artifact bytes. It binds only to `127.0.0.1:9094` independently of URL configuration, accepts GET/HEAD, and returns 404 for unknown files. There is no customer source, management database, account bootstrap or migration. Only the owned fixture CSS/script/image are served under a restrictive CSP; the page uses no external fonts, scripts or services.

The page demonstrates a document shell in `StudioDocument.razor`, typed text/image/CTA/branding/FAQ components, recursive `RenderFragment` Group children, and one site-owned branding block referenced by both header and footer. Native FAQ disclosures work with the keyboard without JavaScript. The captured script adds Expand/Collapse all answers and synchronizes its label/expanded state. No C# event handler or Blazor browser runtime is assumed to execute in exported HTML.

`/razor-prototype-manifest.json` describes the source revision/content digest, design and layout rules, selected document component, relevant assembly digests/runtime, captured asset digests and design revision. All five files enter the final artifact digest. The experiment keeps this artifact in memory; durable retention and publication are not implemented here.

## Checks

```text
dotnet run --no-build --configuration Release --project tests/Webspine.RazorChecks
dotnet run --no-build --configuration Release --project tests/Webspine.Checks
dotnet run --no-build --configuration Release --project tests/Webspine.ManagementChecks
```

The new seven-group suite covers repeatable bytes, nested/shared expansion, typed FAQ content, metadata, text/attribute escaping, preview prefixes, CSS/script/image completeness, implementation/runtime evidence, changed-script identity and retained bytes, invalid graph/missing asset/missing mapping rejection, awaited asynchronous component lifecycle work, cancellation/failure without successful partial artifacts, unchanged established v2 digest format and immutable/incomplete finalization. The existing suites cover source/permission/conflict/migration/recovery/preview behavior. CI runs all three suites on Windows, Linux and macOS.

Browser review additionally verifies real script execution under CSP, native disclosures and enhancement controls with the keyboard, loaded images, no browser errors and no horizontal overflow at desktop/390px mobile. Runtime screenshots belong under ignored `.local/verification/razor-prototype/`, not in Git.

## Developer changes and handoff

The prototype contains deliberately local component dispatch in `Components/BlockView.razor`; it is not the new registration API. Validation temporarily reuses the existing block registry, whose raw-HTML delegates are never used for this website. The design CSS/script/image are embedded resources so their bytes are frozen before rendering. Modify a component or asset and rebuild/restart to obtain a new candidate. Mutating later inputs cannot change an already returned `BuiltArtifact`.

#25 implements configured packages and content/rendering separation, including declared dependency/asset capture, pre-build mapping validation and production preview/delivery script support. #26 makes registered types editable in board/API without central switches. #27 demonstrates two independent websites and writes the production package quickstart. Full executable environment capture, CSS isolation, dynamic pages, browser Blazor, worker isolation and production hosting are not proven by this prototype.

If the management frontend must be replaced during that work, start with user tasks and human-readable navigation/forms rather than reproducing the current management page. That requirement is recorded in decision 0013.
