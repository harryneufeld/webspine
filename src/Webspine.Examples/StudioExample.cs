using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Webspine.Core;
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
        if (!example) return new(new("site", title, "en", [new("home", "/", "Home", "Welcome to your website", [new TextSection("introduction", "Tell your story", "Add your first words here.")])], []), null,
            ImmutableDictionary<string, ImmutableArray<byte>>.Empty);
        var snapshot = Snapshot();
        return new(snapshot.Website with { Id = "site", Title = title }, null,
            snapshot.Website.Assets.ToImmutableDictionary(a => a.File, a => Resource(a.File), StringComparer.Ordinal));
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
