# Configured design packages

Composition authoring and previews select an installed trusted package. The built-in `studio` version `1` is implemented by `Webspine.Designs.Studio` using `Webspine.Rendering.Razor`. For local management:

```text
dotnet run --project src/Webspine.Management -- --urls http://127.0.0.1:9087 --environment Development --Management:Enabled true --Design:Package studio --Design:Version 1
```

Selection defaults to `studio`; an omitted version selects its registered version. Unknown IDs/versions fail startup. Packages are explicitly compiled/registered, not uploaded through management. Existing content must validate against the selected layout/definitions. There is no automatic content migration when configuration changes.

Core definitions supply payload types and validators. A Razor package supplies its document component, compatible typed content components, allowed Regions/Group choices, and immutable `assets/` bytes. Components derive from `RazorDocumentComponent` or `RazorContentComponent<T>`. Use `RazorPageContext.Link`, `Asset` and `DesignAsset` so preview navigation stays within its prefix; recursive dispatch uses `RazorBlock`. Ordinary Razor expressions escape CMS text. Do not turn content into `MarkupString`, query mutable sources, or assume a Blazor browser runtime.

The selected package and captured content are validated before rendering. Asset collisions, undeclared component assets and missing media fail. Static rendering awaits asynchronous lifecycle work; cancellation is cooperative. Core finalization returns immutable files and a digest binding all output. A package's changed script, stylesheet, definition or executable environment creates a different candidate identity.

Previews retain private `build-inputs/<candidate-id>.zip` and content-addressed `<digest>.runtime.zip` alongside the existing SQLite artifact storage. The first contains content/configuration and captured asset bytes. The second contains installed application/dependency binaries, native dependencies, shared runtimes and host bytes with an integrity inventory. Keep these private and include them in backup planning. They are never website assets. OS/architecture prerequisites and future container pinning/replay limits are described in [decision 0014](decisions/0014-configured-static-design-packages.md).

Only explicit package CSS is supported; generated isolation/bundling output must be added explicitly before such features can be used. Package scripts are retained and listed in `design-package-manifest.json`. Management preview delivery validates that declaration, serves those files as JavaScript and permits same-origin scripts through CSP. Default delivery remains script-free. The manifest is build provenance, not permission for content credentials to install code.

Existing v1 sites use `Webspine.Rendering.Legacy` until explicit composition migration. Old stored previews keep their exact bytes; viewing them does not load a package or rebuild them. Migration rehearses the selected renderer before committing a new head.

To inspect the isolated production-package verification fixture after its checks:

```text
dotnet run --project tests/Webspine.RazorChecks -- --preview
```

Open `http://127.0.0.1:9095/preview/fixed/`. This loopback fixture has no customer data or management accounts; stop it after review. Generic authoring metadata/UI remains #26, and two distinct website designs remain #27.
