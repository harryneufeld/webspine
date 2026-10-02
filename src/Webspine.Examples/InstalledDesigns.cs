using System.Collections.Immutable;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Designs.Studio;
using Webspine.Designs.Fieldwork;
using Webspine.Rendering.Razor;

namespace Webspine.Examples;

// Installation wiring and optional example content, outside the engine and management forms.
public sealed record WebsiteStarter(CompositionWebsite Composition,
    ImmutableDictionary<string, ImmutableArray<byte>> Assets);
public sealed record InstalledDesign(RazorDesignPackage Package,
    string ExampleLabel, string ExampleDescription, Func<string, bool, WebsiteStarter> Start);

public static class InstalledDesigns
{
    public static InstalledDesign Select(string id, string? version)
    {
        InstalledDesign[] installed =
        [
            new(StudioPackage.Create(), "Use demo", "Home, Services, Products, About and Contact", StudioExample.Start),
            new(FieldworkPackage.Create(), "Use service business example", "Home, Services, Our approach and Contact", FieldworkExample.Start)
        ];
        var selected = new DesignPackageCatalog(installed.Select(i => i.Package)).Select(id, version);
        return installed.Single(i => ReferenceEquals(i.Package, selected));
    }
}
