using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Designs.Studio;
using Webspine.Rendering.Legacy;

namespace Webspine.Examples;

public static class StudioExample
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static ImmutableArray<byte> Resource(string path)
    {
        using var input = typeof(StudioExample).Assembly.GetManifestResourceStream("example/" + path.Replace('\\', '/'))
            ?? throw new ContentValidationException("An installed example asset is missing.");
        using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray().ToImmutableArray();
    }
    public static WebsiteStarter Start(string title, bool example)
    {
        var site = JsonSerializer.Deserialize<CompositionWebsite>(Resource("studio-site.json").AsSpan(), CompositionJson.Options)
            ?? throw new ContentValidationException("Empty installed Studio example.");
        if (!example)
        {
            var home = site.Pages.Single(p => p.Id == "home");
            var main = home.Regions.Single(r => r.Id == "main");
            var text = site.Blocks.Single(b => b.Id == main.Placements[1].TargetId);
            var header = site.Blocks.Single(b => b.Id == "site-header-root");
            site = site with { Pages = [home with { Description = "Welcome to your website", Regions = home.Regions.Replace(main, main with { Placements = main.Placements[..2] }) }],
                Blocks = site.Blocks.Where(b => b.Id == main.Placements[0].TargetId || b.Id == text.Id || b.Owner.Kind == OwnerKind.Shared).ToImmutableArray(), Assets = [] };
            site = site with { Blocks = site.Blocks.Replace(text, text with { Fields = JsonSerializer.SerializeToElement(new TextFields("Tell your story", "Add your first words here."), CompositionJson.Options) })
                .Replace(header, header with { Fields = JsonSerializer.SerializeToElement(new HeaderFields("Independent studio", ["home"]), CompositionJson.Options) }) };
        }
        site = site with { Id = "site", Title = title };
        CompositionContract.Validate(new(2, new("starter", "installed"), "starter", site), StudioPackage.Create().Design, StudioContent.Definitions());
        return new(site, site.Assets.ToImmutableDictionary(a => a.File, a => Resource(a.File), StringComparer.Ordinal));
    }
    private static ContentSnapshot Snapshot()
    {
        var bytes = Resource("site.json");
        var snapshot = JsonSerializer.Deserialize<ContentSnapshot>(bytes.AsSpan(), Json)
            ?? throw new ContentValidationException("Empty installed Studio example.");
        ContentContract.Validate(snapshot);
        return snapshot with { Revision = "fixture-" + BuildPipeline.Hash(bytes.AsSpan()) };
    }
    public static async ValueTask<BuiltArtifact> BuildReadOnlyAsync(string prefix, CancellationToken ct = default)
    {
        var snapshot = Snapshot();
        var assets = snapshot.Website.Assets.ToImmutableDictionary(a => a.File, a => Resource(a.File), StringComparer.Ordinal);
        var css = Encoding.UTF8.GetString(Resource("design/site.css").AsSpan());
        return await new BuildPipeline(new LegacyWebsiteRenderer(css, assets, prefix), contributors: [new LegacySiteIndexContributor()])
            .BuildAsync(new(snapshot, BuildPipeline.Hash(Encoding.UTF8.GetBytes(css + prefix))), ct);
    }
}
