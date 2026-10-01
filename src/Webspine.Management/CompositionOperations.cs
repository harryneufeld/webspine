using System.Collections.Immutable;
using System.Security.Claims;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

internal sealed class CompositionOperations(ICompositionDraftPersistence source, SqliteContentSource previews,
    IDesignPackage package, ICompositionRenderer renderer, ILegacyCompositionConverter converter,
    FrozenInputStore inputs, ILogger<CompositionOperations> logger)
{
    public ICompositionDraftPersistence Source => source;
    public CompositionDesign Design => package.Design;
    public BlockRegistry Registry => package.ContentTypes;
    public CompositionWebsite ConvertLegacy(ContentSnapshot legacy, ImmutableArray<Block> sections,
        Func<string, string, (string BlockId, string PlacementId)> identities) => converter.Convert(legacy, sections, identities);
    public async ValueTask<BuiltArtifact> RehearseAsync(CapturedComposition captured, CancellationToken ct) =>
        await renderer.BuildAsync(captured, await package.CaptureAsync(ct), cancellationToken: ct);
    public async Task<CompositionSnapshot> ReadAsync(CancellationToken ct)
    {
        source.CompositionCapabilities.Require(CompositionOperation.Read);
        var snapshot = await source.ReadCompositionAsync(ct);
        if (snapshot.Source != source.Identity) throw new ContentValidationException("The selected CMS returned a different source identity.");
        CompositionContract.Validate(snapshot, Design, Registry);
        return snapshot;
    }
    public static CompositionAuthority Authority(ClaimsPrincipal user) => new(Permissions.Has(user, "content:write"), Permissions.Has(user, "content:shared:write"), Permissions.Has(user, "settings:write"));
    public async Task<CompositionSnapshot> EditAsync(string revision, CompositionEdit edit, ClaimsPrincipal user, ImmutableArray<string> acknowledged, CancellationToken ct)
    {
        var result = await new CompositionEditor(source, Design, Registry).ApplyAsync(revision, edit, Authority(user), acknowledged, ct);
        logger.LogInformation("Composition operation {Operation} by account {Account} credential {Credential}: {Revision}", edit.GetType().Name,
            user.FindFirstValue(ClaimTypes.NameIdentifier), user.FindFirst("webspine:credential")?.Value, result.Revision);
        return result;
    }
    public async Task<CompositionSnapshot> MediaAsync(string revision, byte[] bytes, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Permissions.Has(user, "content:write")) throw new CompositionPermissionException("Content editing permission is required.");
        source.CompositionCapabilities.Require(CompositionOperation.Media);
        var snapshot = await ReadAsync(ct);
        if (snapshot.Revision != revision) throw new CompositionRevisionException();
        var media = CompositionMedia.Png(bytes);
        var existing = snapshot.Website.Assets.FirstOrDefault(a => a.Id == media.Asset.Id);
        if (existing is not null)
        {
            if (existing != media.Asset) throw new ContentValidationException("Media identifier collision.");
            return snapshot;
        }
        return await source.CommitCompositionAsync(revision, snapshot.Website with { Assets = snapshot.Website.Assets.Add(media.Asset) },
            Design, ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add(media.Asset.File, media.Bytes), ct);
    }
    public async Task<(string Id, BuiltArtifact Artifact)> PreviewAsync(string revision, string prefix, CancellationToken ct)
    {
        source.CompositionCapabilities.RequireCapture();
        var frozen = await package.CaptureAsync(ct);
        var captured = await source.CaptureCompositionAsync(frozen.Design, ct);
        foreach (var block in captured.Content.Website.Blocks) source.CompositionCapabilities.Require(CompositionOperation.Read, block.TypeId, block.TypeVersion);
        if (captured.Content.Revision != revision) throw new CompositionRevisionException();
        var id = Guid.NewGuid().ToString("N");
        var artifact = await renderer.BuildAsync(captured, frozen, prefix + id, ct);
        await inputs.SaveAsync(id, captured, frozen, ct);
        await previews.SavePreviewAsync(id, artifact, ct);
        return (id, artifact);
    }
}
