using System.Collections.Immutable;
using System.Text.Json;
using Webspine.Core;

namespace Webspine.Delivery;

// Opt-in from a retained, trusted package build. Default delivery remains script-free.
public sealed class DesignScriptPolicy
{
    private readonly ImmutableDictionary<string, string> scripts;
    private readonly string artifactDigest;
    private DesignScriptPolicy(string digest, ImmutableDictionary<string, string> scripts)
    { artifactDigest = digest; this.scripts = scripts; }
    public bool Allows(BuiltArtifact artifact, string path) => artifact.Digest == artifactDigest && scripts.ContainsKey(path);
    public bool Enabled(BuiltArtifact artifact) => artifact.Digest == artifactDigest && scripts.Count > 0;
    public static DesignScriptPolicy? FromArtifact(BuiltArtifact artifact)
    {
        var file = artifact.Files.FirstOrDefault(f => f.Path == "design-package-manifest.json");
        if (file is null) return null;
        if (file.Digest != BuildPipeline.Hash(file.Bytes.AsSpan())) throw new ContentValidationException("Invalid design manifest integrity.");
        using var manifest = JsonDocument.Parse(file.Bytes.ToArray());
        var list = manifest.RootElement.GetProperty("scripts");
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 100) throw new ContentValidationException("Invalid package script declaration.");
        var scripts = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var entry in list.EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString()!; var digest = entry.GetProperty("digest").GetString()!;
            ArtifactPaths.Validate(path);
            var asset = artifact.Files.SingleOrDefault(f => f.Path == path);
            if (!path.StartsWith("assets/", StringComparison.Ordinal) || !path.EndsWith(".js", StringComparison.Ordinal) ||
                asset is null || asset.Digest != digest || BuildPipeline.Hash(asset.Bytes.AsSpan()) != digest || !scripts.TryAdd(path, digest))
                throw new ContentValidationException("Missing, duplicate or changed declared package script.");
        }
        return new(artifact.Digest, scripts.ToImmutable());
    }
}
