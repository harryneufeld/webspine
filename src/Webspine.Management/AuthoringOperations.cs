using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Demo;

namespace Webspine.Management;

internal sealed class AuthoringOperations(IWebsiteAuthoringSource source, SqliteContentSource previews)
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
        var artifact = await DemoSite.BuildSnapshotAsync(captured.Snapshot, captured.Assets, prefix + id, ct);
        await previews.SavePreviewAsync(id, artifact, ct);
        return (id, artifact);
    }
}
