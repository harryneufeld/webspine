using System.Collections.Immutable;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Designs.Studio;
using Webspine.Designs.Fieldwork;
using Webspine.Rendering.Razor;

namespace Webspine.Examples;

// Installation wiring and optional example content, outside the engine and management forms.
public sealed record WebsiteStarter(WebsiteContent? Legacy, CompositionWebsite? Composition,
    ImmutableDictionary<string, ImmutableArray<byte>> Assets);
public sealed record InstalledDesign(RazorDesignPackage Package, ILegacyCompositionConverter LegacyConverter, bool SupportsLegacy,
    string ExampleLabel, string ExampleDescription, Func<string, bool, WebsiteStarter> Start);

public static class InstalledDesigns
{
    public static InstalledDesign Select(string id, string? version)
    {
        InstalledDesign[] installed =
        [
            new(StudioPackage.Create(), new StudioLegacyConverter(), true, "Use demo", "Home, Services, Products, About and Contact", StudioExample.Start),
            new(FieldworkPackage.Create(), new UnsupportedLegacyConverter(), false, "Use service business example", "Home, Services, Our approach and Contact", FieldworkExample.Start)
        ];
        var selected = new DesignPackageCatalog(installed.Select(i => i.Package)).Select(id, version);
        return installed.Single(i => ReferenceEquals(i.Package, selected));
    }
}

public sealed class UnsupportedLegacyConverter : ILegacyCompositionConverter
{
    public CompositionWebsite Convert(ContentSnapshot legacy, ImmutableArray<Block> sections,
        Func<string, string, (string BlockId, string PlacementId)> identities) =>
        throw new SourceOperationNotSupportedException("This design has no v1 migration mapping. Select the original design or provide an explicit validated migration; existing content and previews are preserved.");
}
