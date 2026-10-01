using System.Collections.Immutable;
using System.Security.Claims;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Demo;

namespace Webspine.Management;

internal sealed class CompositionOperations(ICompositionDraftPersistence source, SqliteContentSource previews, ILogger<CompositionOperations> logger)
{
    public ICompositionDraftPersistence Source => source;
    public async Task<CompositionSnapshot> ReadAsync(CancellationToken ct)
    {
        source.CompositionCapabilities.Require(CompositionOperation.Read);
        var snapshot = await source.ReadCompositionAsync(ct);
        if (snapshot.Source != source.Identity) throw new ContentValidationException("The selected CMS returned a different source identity.");
        CompositionContract.Validate(snapshot, await DemoComposition.DesignAsync(ct), DemoComposition.Registry());
        return snapshot;
    }
    public static CompositionAuthority Authority(ClaimsPrincipal user) => new(Permissions.Has(user, "content:write"), Permissions.Has(user, "content:shared:write"), Permissions.Has(user, "settings:write"));
    public async Task<CompositionSnapshot> EditAsync(string revision, CompositionEdit edit, ClaimsPrincipal user, ImmutableArray<string> acknowledged, CancellationToken ct)
    {
        var result = await new CompositionEditor(source, await DemoComposition.DesignAsync(ct), DemoComposition.Registry()).ApplyAsync(revision, edit, Authority(user), acknowledged, ct);
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
            await DemoComposition.DesignAsync(ct), ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add(media.Asset.File, media.Bytes), ct);
    }
    public async Task<(string Id, BuiltArtifact Artifact)> PreviewAsync(string revision, string prefix, CancellationToken ct)
    {
        source.CompositionCapabilities.RequireCapture();
        var captured = await source.CaptureCompositionAsync(await DemoComposition.DesignAsync(ct), ct);
        foreach (var block in captured.Content.Website.Blocks) source.CompositionCapabilities.Require(CompositionOperation.Read, block.TypeId, block.TypeVersion);
        if (captured.Content.Revision != revision) throw new CompositionRevisionException();
        var id = Guid.NewGuid().ToString("N");
        var artifact = new CompositionBuildPipeline(DemoComposition.Registry()).Build(captured, ct, prefix + id);
        await previews.SavePreviewAsync(id, artifact, ct);
        return (id, artifact);
    }
}
