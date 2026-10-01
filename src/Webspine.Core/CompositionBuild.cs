using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Webspine.Core.Composition;

// An opt-in v2 build path; existing management/demo builds continue using v1.
public sealed class CompositionBuildPipeline(BlockRegistry registry)
{
    public async ValueTask<BuiltArtifact> BuildAsync(ICompositionSource source, CompositionDesign design,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        source.CompositionCapabilities.RequireCapture();
        var captured = await source.CaptureCompositionAsync(design, cancellationToken);
        if (captured is null || captured.Content is null || captured.Design is null)
            throw new ContentValidationException("Source omitted required capture inputs.");
        if (captured.Content.Source != source.Identity ||
            !JsonSerializer.SerializeToUtf8Bytes(captured.Design, CompositionJson.Options).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(design, CompositionJson.Options)))
            throw new ContentValidationException("Capture changed source identity or selected design.");
        CompositionContract.Validate(captured.Content, captured.Design, registry);
        foreach (var block in captured.Content.Website.Blocks)
            source.CompositionCapabilities.Require(CompositionOperation.Read, block.TypeId, block.TypeVersion);
        return Build(captured, cancellationToken);
    }

    public BuiltArtifact Build(CapturedComposition captured, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CompositionContract.Validate(captured.Content, captured.Design, registry);
        var site = captured.Content.Website;
        var assets = site.Assets.ToDictionary(a => a.Id, StringComparer.Ordinal);
        if (captured.AssetFiles is null || captured.AssetFiles.Count != site.Assets.Length)
            throw new ContentValidationException("Capture must contain exactly the declared asset files.");
        var output = new ArtifactBuilder();
        output.AddText("assets/composition.css", captured.Design.Stylesheet);
        foreach (var asset in site.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!captured.AssetFiles.TryGetValue(asset.File, out var bytes) || bytes.IsDefaultOrEmpty)
                throw new ContentValidationException("Capture omitted asset bytes.");
            output.Add(asset.File, bytes.AsSpan());
        }
        var blocks = site.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var shared = site.SharedBlocks.ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var page in site.Pages)
        {
            var html = new HtmlOutput();
            void RenderPlacement(Placement placement)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var block = blocks[placement.Kind == TargetKind.Shared ? shared[placement.TargetId].RootBlockId : placement.TargetId];
                registry.Resolve(block.TypeId, block.TypeVersion).Render(block,
                    new(() => { foreach (var child in block.Children) RenderPlacement(child); }, id => "/" + assets[id].File), html);
            }
            html.Markup("<!doctype html><html lang=\""); html.Text(site.Language); html.Markup("\"><head><meta charset=\"utf-8\"><title>");
            html.Text(page.Title + " | " + site.Title); html.Markup("</title><meta name=\"description\" content=\"");
            html.Text(page.Description); html.Markup("\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><link rel=\"stylesheet\" href=\"/assets/composition.css\"></head><body>");
            foreach (var region in captured.Design.Layout.Regions)
            {
                // Region IDs are validated tokens, not user-supplied element names.
                var tag = region.Id is "header" or "main" or "footer" ? region.Id : "section";
                html.Markup("<" + tag + " id=\""); html.Text(region.Id); html.Markup("\">");
                foreach (var placement in page.Regions.Single(r => r.Id == region.Id).Placements) RenderPlacement(placement);
                html.Markup("</" + tag + ">");
            }
            html.Markup("</body></html>");
            output.AddText(BuildPipeline.PageFile(page.Path), html.ToString());
        }
        var registrations = registry.Descriptors.Select(d => new
        {
            type = d, implementationDigest = registry.Resolve(d.Id, d.Version).ImplementationDigest
        }).ToArray();
        var designRevision = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(new { captured.Design, registrations }, CompositionJson.Options));
        output.AddText("composition-manifest.json", JsonSerializer.Serialize(new
        {
            contractVersion = CompositionContract.Version, captured.Content.Source, captured.Content.Revision,
            contentDigest = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(captured.Content, CompositionJson.Options)),
            designRevision, design = captured.Design, registrations
        }, CompositionJson.Options));
        cancellationToken.ThrowIfCancellationRequested();
        var files = output.Freeze().OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ArtifactFile(pair.Key, pair.Value, BuildPipeline.Hash(pair.Value.AsSpan()))).ToImmutableArray();
        var digest = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            contractVersion = CompositionContract.Version, captured.Content.Source, captured.Content.Revision, designRevision,
            files = files.Select(f => new { f.Path, length = f.Bytes.Length, f.Digest }).ToArray()
        }, CompositionJson.Options));
        return new(captured.Content.Source, captured.Content.Revision, CompositionContract.Version, designRevision, files, digest);
    }
}
