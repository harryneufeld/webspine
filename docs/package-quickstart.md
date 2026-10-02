# Create a website design package

This quickstart follows the implemented Studio and Fieldwork examples. Packages are trusted compiled code installed by an operator. Content editors change structured values; they cannot upload executable packages. Use a .NET SDK compatible with `global.json`.

## Try the two installed designs

Start Fieldwork in its own disposable or new data directory:

```text
dotnet run --project src/Webspine.Management -- --urls http://127.0.0.1:9087 --environment Development --Management:Enabled true --Management:DataDirectory .local/fieldwork --Design:Package fieldwork --Design:Version 1
```

Open `/manage`, create the owner, enter a business name such as **Oak & Hearth**, then choose **Use service business example**. It creates four native v2 pages with shared navigation/contact content, nested Groups, an image, service lists and FAQ. **Start blank** instead creates one Home page with the design's required shared shell. Open a page, edit a question, save and **Build preview**. The Show/Hide all answers button is a captured progressive enhancement; individual native disclosures work without JavaScript. The example has no connected enquiry form or booking backend.

Use a separate directory and port for Studio:

```text
dotnet run --project src/Webspine.Management -- --urls http://127.0.0.1:9088 --environment Development --Management:Enabled true --Management:DataDirectory .local/studio --Design:Package studio --Design:Version 1
```

Choose **Use demo** to create five native v2 pages, or **Start blank** for one editable Home page. No enable/migration step is needed. Its Header/Main/Footer document and card grid differ from Fieldwork's Brand/Content/Contact document, navigation rail and numbered service lists. Both use the same source, editor/API, renderer and artifact pipeline. One data directory owns one website; these commands are separate installations, not multi-tenant hosting.

Do not select Fieldwork against an existing Studio database as an automatic redesign. Incompatible authoring/new builds fail and retained previews remain readable. Select the original package to resume editing its v2 data. Legacy v1 workspaces require a fresh v2 installation in a separate directory; legacy migration is not supported.

## Add a package project

Create a class-library project under `src/Webspine.Designs.MySite` and use `Microsoft.NET.Sdk.Razor`. Reference `Microsoft.AspNetCore.App`, `Webspine.Rendering.Razor` and your independent content module(s). Follow `src/Webspine.Designs.Fieldwork/Webspine.Designs.Fieldwork.csproj` for embedded CSS/script resources and `_Imports.razor` for namespaces. Add the project to `Webspine.slnx` and to the explicit host installation wiring project.

Use `FieldworkPackage.Create()` as the complete factory reference. Supply:

- A versioned `DesignPackageDescriptor` with contract `2`, renderer `webspine-razor` version `1`.
- A `BlockRegistry` of pure typed definitions, with complete basic editor metadata when board authoring is supported.
- A `CompositionDesign` with your own layout ID, 1–10 named Regions, allowed type IDs and placement bounds, plus approved Group options.
- One `RazorDocumentComponent` and a compatible `RazorContentComponent<T>` mapping for every definition.
- Explicit immutable assets under safe `assets/` paths, including at least one stylesheet.

The document owns semantic structure. Iterate `Context.Region("your-region")` and render each placement with `<RazorBlock Context="Context" Placement="placement" />`. Use `Context.Page.Title`/`Description` and `Context.Website.Language` for metadata. Use `Context.Link`, `Context.Asset` and `Context.DesignAsset` for links and assets inside protected preview prefixes. Group components recurse over `Block.Children` through the same dispatcher. Centralize gutters, typography, responsive rules and reusable styles inside the package; reserve scrollbar space. Native controls and progressively enhanced static HTML must remain useful without a Blazor browser runtime.

## Add an independent content type

Keep the definition in a class library referencing only Core. For example:

```csharp
using System.Text.Json.Serialization;
using Webspine.Core.Composition;

public sealed record NoticeFields(
    [property: JsonRequired] string Heading,
    [property: JsonRequired] string Message);

public static class NoticeContent
{
    public static IBlockDefinition Definition => new BlockDefinition<NoticeFields>(
        new("notice", 1, 2, "my-site-content", "1", "notice-fields-v1", "none", false),
        (fields, _) =>
        {
            CompositionRules.Text(fields.Heading, 160);
            CompositionRules.Text(fields.Message, 2000);
        },
        new("Notice", "A short message for visitors",
            [EditorField.Text("heading", "Heading", "Good to know", 160),
             EditorField.Text("message", "Message", "Your message", 2000, true)],
            "heading"));
}
```

In the design project, add a component with the content module's namespace imported:

```razor
@inherits RazorContentComponent<NoticeFields>
<aside class="notice"><h2>@Fields.Heading</h2><p>@Fields.Message</p></aside>
```

Register `NoticeContent.Definition` alongside the package's other definitions, map `new RazorComponentBinding("notice", 1, typeof(Notice))`, and add `notice` to each intended Region/Group allowed-type list. Style `.notice` in captured package CSS. No Core, renderer dispatch or management form switch changes are needed. The FAQ module shows structured repeated fields plus a semantic validator; [field metadata](field-metadata.md) describes supported kinds, limits and specialized-editor requirements.

Ordinary Razor expressions escape content. Do not use `MarkupString` for CMS text, execute customer-supplied templates/styles, query the live source from a component or assume C# click handlers survive static HTML export. Any browser enhancement must be an explicit captured script with native behavior as its baseline.

## Install and seed it explicitly

Add the package project reference to the operator-controlled installation catalog (`Webspine.Examples` in the default host). Add one `InstalledDesign` entry with your package, readable setup labels and a native v2 starter function. `InstalledDesigns.Select` validates exact ID/version selection through the package catalog. Management consumes this registration through dependency injection.

The starter returns `WebsiteStarter`: a native v2 `CompositionWebsite`, plus immutable media bytes keyed by declared file path. For a new layout, use native v2: supply each declared Region once per page, registered type versions, unique Block/placement IDs, explicit page/shared ownership and valid references. `FieldworkExample.Start` is a complete four-page and blank-site reference. All starter data is validated before an atomic first commit. Once committed, spinecms is the authoritative editable source; the starter is never a synchronized shadow source or a render-time lookup.

Select your installed ID using `--Design:Package my-site --Design:Version 1` against a new data directory. Unknown IDs/versions and missing/incompatible component mappings fail. Payload changes incompatible with stored values need new type versions and explicit migration; defaults initialize new Blocks/list items only.

## Verify authoring and retained output

Create/edit through the board and through bearer `GET /api/v2/schema` / `POST /api/v2/changes`. Use the current opaque revision for every write; shared edits additionally need shared-write scope and exact affected-page acknowledgement. The schema describes fields, current choices/defaults and permitted operations; typed validators remain authoritative. See [composition editing](composition-editing.md) for request examples.

Build a preview and inspect the actual HTML/CSS/images/scripts. Retained private candidate inputs bind content, definitions/metadata, layout, mappings, explicit assets and executable dependencies; public provenance identifies the capture. New content/design changes create new candidate identities. Viewing an old preview serves its stored bytes without rebuilding it.

Run the required checks:

```text
dotnet build Webspine.slnx --configuration Release
dotnet run --project tests/Webspine.Checks --configuration Release --no-build
dotnet run --project tests/Webspine.ManagementChecks --configuration Release --no-build
dotnet run --project tests/Webspine.RazorChecks --configuration Release --no-build
```

`dotnet run --project tests/Webspine.ManagementChecks -- --independent-preview` runs the independent-design checks and retains their disposable Fieldwork host for browser review. It prints the loopback URL; the synthetic owner is `owner` / `Disposable-Test!123`. Stop with Ctrl+C after review. Normal checks clean up automatically. Review keyboard operation, narrow screens, actual script MIME/CSP and prefixed navigation; CI runs Windows/Linux/macOS.

Current limits: trusted explicit installation, static prerendering, explicit CSS/browser assets and cooperative component cancellation. Hosted management/publication, durable jobs, OS/container pinning, historical replay, automatic package/content migration, hot-loading, a schema designer and specialized editor loading remain separate work.

## Patterns and content Records

[Patterns and Records](patterns-records.md) document the Studio Product/card workflow, registered schema/input versions, source capabilities, board/API conditional operations, affected-page authority, frozen values/media and explicit breaking-version migrations. This is implemented for native v2; external business providers remain planned.
