using System.Collections.Immutable;
using System.Text.Json;

namespace Webspine.Core.Composition;

// Shared v2 finalization after validation/rendering. Preserve the existing digest format.
public static class CompositionArtifactFinalizer
{
    public static BuiltArtifact Complete(ArtifactBuilder output, CompositionSnapshot content, string designRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.ContractVersion != CompositionContract.Version)
            throw new ContentValidationException("Unsupported composition artifact contract.");
        CompositionRules.Text(designRevision, 256);
        var frozen = output.Freeze();
        foreach (var page in content.Website.Pages)
            if (!frozen.ContainsKey(BuildPipeline.PageFile(page.Path)))
                throw new ContentValidationException("Build omitted a website page.");
        foreach (var asset in content.Website.Assets)
            if (!frozen.ContainsKey(asset.File))
                throw new ContentValidationException("Build omitted a referenced asset.");
        var files = frozen.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ArtifactFile(pair.Key, pair.Value, BuildPipeline.Hash(pair.Value.AsSpan()))).ToImmutableArray();
        var digest = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            contractVersion = CompositionContract.Version, content.Source, content.Revision, designRevision,
            files = files.Select(f => new { f.Path, length = f.Bytes.Length, f.Digest }).ToArray()
        }, CompositionJson.Options));
        return new(content.Source, content.Revision, CompositionContract.Version, designRevision, files, digest);
    }
}
