using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Webspine.Core;

public static partial class ArtifactPaths
{
    [GeneratedRegex(@"^[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*(?:/[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePathPattern();

    public static void Validate(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || !SafePathPattern().IsMatch(path))
            throw new ContentValidationException("Artifact paths must be relative paths without traversal or special characters.");
    }
}

public sealed class ArtifactBuilder
{
    private readonly Dictionary<string, ImmutableArray<byte>> files = new(StringComparer.OrdinalIgnoreCase);
    private bool sealedArtifact;

    public void AddText(string path, string content) => Add(path, Encoding.UTF8.GetBytes(content));

    public void Add(string path, ReadOnlySpan<byte> bytes)
    {
        if (sealedArtifact) throw new InvalidOperationException("The artifact has already been finalized.");
        ArtifactPaths.Validate(path);
        if (!files.TryAdd(path, ImmutableArray.Create(bytes.ToArray())))
            throw new ContentValidationException($"Artifact path already exists: {path}");
    }

    internal ImmutableDictionary<string, ImmutableArray<byte>> Freeze()
    {
        sealedArtifact = true;
        return files.ToImmutableDictionary(StringComparer.Ordinal);
    }
}

public sealed record ArtifactFile(string Path, ImmutableArray<byte> Bytes, string Digest);
public sealed record BuiltArtifact(SourceIdentity Source, string SourceRevision, int ContractVersion,
    string DesignRevision, ImmutableArray<ArtifactFile> Files, string Digest);

public sealed record BuildInputs(ContentSnapshot Content, string DesignRevision);

public interface IContentValidator
{
    string Id { get; }
    ValueTask ValidateAsync(ContentSnapshot content, CancellationToken cancellationToken = default);
}

public interface IWebsiteRenderer
{
    ValueTask RenderAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default);
}

// Contributors write only to a staging artifact. Final hashing always follows all contributions.
public interface IArtifactContributor
{
    string Id { get; }
    ValueTask ContributeAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default);
}

public sealed class BuildPipeline
{
    private readonly IWebsiteRenderer renderer;
    private readonly ImmutableArray<IContentValidator> validators;
    private readonly ImmutableArray<IArtifactContributor> contributors;

    public BuildPipeline(IWebsiteRenderer renderer, IEnumerable<IContentValidator>? validators = null,
        IEnumerable<IArtifactContributor>? contributors = null)
    {
        this.renderer = renderer;
        this.validators = validators?.ToImmutableArray() ?? [];
        this.contributors = contributors?.ToImmutableArray() ?? [];
        EnsureUnique(this.validators.Select(v => v.Id));
        EnsureUnique(this.contributors.Select(c => c.Id));
    }

    public async ValueTask<BuiltArtifact> BuildAsync(BuildInputs inputs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(inputs.DesignRevision)) throw new ContentValidationException("A design revision is required.");
        ContentContract.Validate(inputs.Content);
        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await validator.ValidateAsync(inputs.Content, cancellationToken);
        }
        // Core validation remains mandatory regardless of which extension validators are installed.
        ContentContract.Validate(inputs.Content);
        var output = new ArtifactBuilder();
        await renderer.RenderAsync(inputs, output, cancellationToken);
        foreach (var contributor in contributors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await contributor.ContributeAsync(inputs, output, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var frozen = output.Freeze();
        foreach (var page in inputs.Content.Website.Pages)
            if (!frozen.ContainsKey(PageFile(page.Path))) throw new ContentValidationException("Build omitted a website page.");
        foreach (var asset in inputs.Content.Website.Assets)
            if (!frozen.ContainsKey(asset.File)) throw new ContentValidationException("Build omitted a referenced asset.");
        var files = frozen.OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new ArtifactFile(f.Key, f.Value, Hash(f.Value.AsSpan()))).ToImmutableArray();
        using var manifest = new MemoryStream();
        using (var writer = new BinaryWriter(manifest, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(ContentContract.Version);
            writer.Write(inputs.Content.Source.Id); writer.Write(inputs.Content.Source.Kind);
            writer.Write(inputs.Content.Revision); writer.Write(inputs.DesignRevision);
            foreach (var file in files) { writer.Write(file.Path); writer.Write(file.Bytes.Length); writer.Write(file.Digest); }
        }
        return new(inputs.Content.Source, inputs.Content.Revision, ContentContract.Version,
            inputs.DesignRevision, files, Hash(manifest.ToArray()));
    }

    public static string PageFile(string pagePath) => pagePath == "/" ? "index.html" : pagePath.Trim('/') + "/index.html";
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void EnsureUnique(IEnumerable<string> identifiers)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in identifiers)
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new ArgumentException("Extension identifiers must be nonempty and unique within their extension point.");
    }
}
