using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Demo;

public sealed record HeaderFields([property: JsonRequired] string Subtitle, [property: JsonRequired] ImmutableArray<string> PageIds);
public sealed record FooterFields([property: JsonRequired] string Message);
public sealed record PageTitleFields;

// Design-package behavior, not SQLite knowledge. The migration adapter supplies converted section Blocks.
public static class DemoComposition
{
    private static BlockTypeDescriptor Descriptor(string id) => new(id, 1, 2, "webspine-demo", "1", id + "-fields-v1", "1", false);
    public static BlockRegistry Registry() => new(StandardBlocks.Registrations.AddRange(new IBlockRegistration[]
    {
        new BlockRegistration<HeaderFields>(Descriptor("site-header"), (fields, context) =>
        {
            CompositionRules.Text(fields.Subtitle, 160);
            if (fields.PageIds.IsDefaultOrEmpty || fields.PageIds.Length > 100 || fields.PageIds.Distinct().Count() != fields.PageIds.Length)
                throw new ContentValidationException("Navigation needs unique page references.");
            foreach (var id in fields.PageIds) context.Page(id);
        }, (fields, context, html) =>
        {
            html.Markup("<a class=\"skip\" href=\"#main\">Skip to content</a><a class=\"brand\" href=\"/\">");
            html.Text(context.WebsiteTitle); html.Markup("<span>"); html.Text(fields.Subtitle); html.Markup("</span></a><nav aria-label=\"Main navigation\">");
            foreach (var id in fields.PageIds)
            {
                var page = context.PageById(id);
                html.Markup("<a href=\""); html.Text(page.Path); html.Markup("\"");
                if (id == context.Page.Id) html.Markup(" aria-current=\"page\"");
                html.Markup(">"); html.Text(page.Title); html.Markup("</a>");
            }
            html.Markup("</nav>");
        }),
        new BlockRegistration<FooterFields>(Descriptor("site-footer"), (fields, _) => CompositionRules.Text(fields.Message, 300),
            (fields, context, html) => { html.Markup("<span>"); html.Text(context.WebsiteTitle); html.Markup("</span><p>"); html.Text(fields.Message); html.Markup("</p>"); }),
        new BlockRegistration<PageTitleFields>(Descriptor("page-title"), (_, _) => { }, (fields, context, html) =>
        {
            html.Markup("<div class=\"page-title\"><p class=\"eyebrow\">"); html.Text(context.WebsiteTitle + " / " + context.Page.Title);
            html.Markup("</p><h1>"); html.Text(context.Page.Description); html.Markup("</h1></div>");
        })
    }));

    public static async Task<CompositionDesign> DesignAsync(CancellationToken ct = default)
    {
        var css = await File.ReadAllTextAsync(Path.Combine(DemoSite.FixtureDirectory, "design", "site.css"), ct);
        css += "\n.group{display:flex}.mode-stack{flex-direction:column}.mode-row{flex-wrap:wrap}.mode-grid{display:grid}.align-start{align-items:start}.align-center{align-items:center}.spacing-small{gap:.75rem}.spacing-medium{gap:1.5rem}.columns-2{grid-template-columns:repeat(2,minmax(0,1fr))}.columns-3{grid-template-columns:repeat(3,minmax(0,1fr))}.columns-4{grid-template-columns:repeat(4,minmax(0,1fr))}@media(max-width:40rem){.mode-grid{grid-template-columns:1fr}}";
        var registry = Registry();
        ImmutableArray<string> main = ["text", "image", "cta", "cards", "group", "page-title"];
        var revision = BuildPipeline.Hash(Encoding.UTF8.GetBytes(css + "\n" + registry.Resolve("site-header", 1).ImplementationDigest));
        return new("demo-design", revision, new("standard", [new("header", ["site-header", "group"], 1, 10),
            new("main", main, 1, 1000), new("footer", ["site-footer", "group"], 1, 10)]),
            new(registry.Descriptors.Select(d => d.Id).ToImmutableArray(), ["stack", "row", "grid"], ["start", "center"], ["small", "medium"], 4), css);
    }

    public static string PlacementId(string region, string pageId) => "placement-" + BuildPipeline.Hash(Encoding.UTF8.GetBytes(region + "/" + pageId))[..32];
    public static string TitleBlockId(string pageId) => "title-" + BuildPipeline.Hash(Encoding.UTF8.GetBytes(pageId))[..32];

    public static CompositionWebsite Convert(ContentSnapshot legacy, ImmutableArray<Block> sections,
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
