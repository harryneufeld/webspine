using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Designs.Studio;

public sealed class StudioLegacyConverter : ILegacyCompositionConverter
{
    public static string PlacementId(string region, string pageId) => "placement-" + BuildPipeline.Hash(Encoding.UTF8.GetBytes(region + "/" + pageId))[..32];
    public static string TitleBlockId(string pageId) => "title-" + BuildPipeline.Hash(Encoding.UTF8.GetBytes(pageId))[..32];

    public CompositionWebsite Convert(ContentSnapshot legacy, ImmutableArray<Block> sections,
        Func<string, string, (string BlockId, string PlacementId)> sectionIdentity)
    {
        JsonElement Fields<T>(T fields) => JsonSerializer.SerializeToElement(fields, CompositionJson.Options);
        var blocks = sections.Add(new("site-header-root", new(OwnerKind.Shared, "site-header"), "site-header", 1,
            Fields(new HeaderFields("Independent studio", legacy.Website.Pages.Select(p => p.Id).ToImmutableArray())), []))
            .Add(new("site-footer-root", new(OwnerKind.Shared, "site-footer"), "site-footer", 1, Fields(new FooterFields("Draft preview · Not published")), []));
        foreach (var page in legacy.Website.Pages)
            blocks = blocks.Add(new(TitleBlockId(page.Id), new(OwnerKind.Page, page.Id), "page-title", 1, Fields(new PageTitleFields()), []));
        var pages = legacy.Website.Pages.Select(page => new CompositionPage(page.Id, page.Path, page.Title, page.Description,
        [
            new("header", [new(PlacementId("header", page.Id), TargetKind.Shared, "site-header")]),
            new("main", new[] { new Placement(PlacementId("title", page.Id), TargetKind.Block, TitleBlockId(page.Id)) }
                .Concat(page.Sections.Select(s => { var mapping = sectionIdentity(page.Id, s.Id); return new Placement(mapping.PlacementId, TargetKind.Block, mapping.BlockId); })).ToImmutableArray()),
            new("footer", [new(PlacementId("footer", page.Id), TargetKind.Shared, "site-footer")])
        ])).ToImmutableArray();
        return new(legacy.Website.Id, legacy.Website.Title, legacy.Website.Language, "standard", pages, blocks,
            [new("site-header", "site-header-root"), new("site-footer", "site-footer-root")], legacy.Website.Assets);
    }
}
