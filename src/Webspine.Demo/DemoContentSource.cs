using System.Text.Json;
using System.Text.Json.Serialization;
using Webspine.Core;

namespace Webspine.Demo;

// A read-only fixture adapter for discovery. This is not the persistent built-in CMS.
public sealed class DemoContentSource : IContentSource
{
    private readonly ContentSnapshot snapshot;
    public SourceIdentity Identity => snapshot.Source;
    public SourceCapabilities Capabilities { get; } = new(true, false, false);
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public DemoContentSource(string json)
    {
        snapshot = JsonSerializer.Deserialize<ContentSnapshot>(json, Json)
            ?? throw new ContentValidationException("The demo fixture is empty.");
        ContentContract.Validate(snapshot);
        snapshot = snapshot with { Revision = "fixture-" + BuildPipeline.Hash(System.Text.Encoding.UTF8.GetBytes(json)) };
    }

    public ValueTask<ContentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(snapshot);
    }

    public ValueTask<ContentSnapshot> UpdateDraftAsync(DraftChange change, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new SourceOperationNotSupportedException("The example fixture is read-only. A writable CMS adapter must enforce conditional updates.");
    }
}
