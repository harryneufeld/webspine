using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Rendering.Legacy;
using System.Text.Json;

namespace Webspine.Management;

internal sealed class AuthoringOperations(IWebsiteAuthoringSource source, SqliteContentSource previews,
    IDesignPackage package, FrozenInputStore inputStore)
{
    public ValueTask<ContentSnapshot> ReadAsync(CancellationToken ct) => source.ReadAsync(ct);
    public Task<ContentSnapshot> SettingsAsync(string revision, string title, string language, CancellationToken ct) => source.UpdateWebsiteAsync(revision, title, language, ct);
    public Task<ContentSnapshot> AddPageAsync(string revision, string title, string path, string description, CancellationToken ct) => source.AddPageAsync(revision, title, path, description, ct);
    public Task<ContentSnapshot> EditPageAsync(string revision, string id, string title, string description, IReadOnlyDictionary<string, string> fields, CancellationToken ct) => source.EditPageAsync(revision, id, title, description, fields, ct);
    public async Task<(string Id, BuiltArtifact Artifact)> PreviewAsync(string revision, string prefix, CancellationToken ct)
    {
        var captured = await previews.CaptureAsync(ct);
        if (captured.Snapshot.Revision != revision) throw new RevisionConflictException();
        var id = Guid.NewGuid().ToString("N");
        // Existing v1 sites keep their rendering/content semantics until explicit migration.
        var frozen = await package.CaptureAsync(ct);
        var artifact = await new BuildPipeline(new LegacyWebsiteRenderer(frozen.Design.Stylesheet, captured.Assets, prefix + id),
            contributors: [new LegacySiteIndexContributor(), new LegacyPackageProvenance(frozen)])
            .BuildAsync(new(captured.Snapshot, frozen.Digest), ct);
        await inputStore.SaveLegacyAsync(id, captured.Snapshot, captured.Assets, frozen, ct);
        await previews.SavePreviewAsync(id, artifact, ct);
        return (id, artifact);
    }
}

internal sealed class LegacyPackageProvenance(FrozenDesignPackage design) : IArtifactContributor
{
    public string Id => "legacy.design-package";
    public ValueTask ContributeAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.AddText("design-package-manifest.json", JsonSerializer.Serialize(new
        {
            contractVersion = 1, inputs.Content.Source, inputs.Content.Revision, package = design.Descriptor,
            designRevision = design.Digest, executable = new { design.Executable.Digest, design.Executable.Runtime,
                design.Executable.RuntimeIdentifier, design.Executable.OperatingSystem, design.Executable.Files }, scripts = Array.Empty<object>()
        }, CompositionJson.Options));
        return ValueTask.CompletedTask;
    }
}
