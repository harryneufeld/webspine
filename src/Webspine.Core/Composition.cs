using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webspine.Core.Composition;

// The active authoring contract. Historical v1 data and retained artifacts remain separate.
public sealed record CompositionSnapshot(int ContractVersion, SourceIdentity Source, string Revision, CompositionWebsite Website);
public sealed record CompositionWebsite(string Id, string Title, string Language, string LayoutId,
    ImmutableArray<CompositionPage> Pages, ImmutableArray<Block> Blocks,
    ImmutableArray<SharedBlock> SharedBlocks, ImmutableArray<AssetContent> Assets)
{
    // Additive v2 field: older native snapshots have no Records.
    public ImmutableArray<ContentRecord> Records { get; init; } = [];
}
public sealed record CompositionPage(string Id, string Path, string Title, string Description, ImmutableArray<RegionContent> Regions);
public sealed record RegionContent(string Id, ImmutableArray<Placement> Placements);
public enum OwnerKind { Page, Shared }
public enum TargetKind { Block, Shared }
public sealed record BlockOwner(OwnerKind Kind, string Id);
public sealed record Placement(string Id, TargetKind Kind, string TargetId);
public sealed record SharedBlock(string Id, string RootBlockId);
public sealed record Block(string Id, BlockOwner Owner, string TypeId, int TypeVersion,
    JsonElement Fields, ImmutableArray<Placement> Children);

public sealed record RegionDefinition(string Id, ImmutableArray<string> AllowedTypes, int Minimum, int Maximum);
public sealed record LayoutDefinition(string Id, ImmutableArray<RegionDefinition> Regions);
public sealed record GroupRules(ImmutableArray<string> AllowedTypes, ImmutableArray<string> Modes, ImmutableArray<string> Alignments,
    ImmutableArray<string> Spacing, int MaximumColumns);
public sealed record CompositionDesign(string Id, string Revision, LayoutDefinition Layout, GroupRules Groups, string Stylesheet = "");
public sealed record CapturedComposition(CompositionSnapshot Content, CompositionDesign Design,
    ImmutableDictionary<string, ImmutableArray<byte>> AssetFiles);

public enum CompositionOperation { Read, Create, Update, Move, Group, Share, Detach, Delete, Media, RecordCreate, RecordUpdate, RecordDelete }
public sealed record SupportedBlockType(string Id, int Version);
public sealed record CompositionCapabilities(int ContractVersion, ImmutableArray<SupportedBlockType> Types,
    ImmutableArray<CompositionOperation> Operations, bool AtomicConditionalWrites, bool ConsistentCapture)
{
    public ImmutableArray<RecordSchemaDescriptor> RecordSchemas { get; init; } = [];
    public void RequireRecord(CompositionOperation operation, string schemaId, int version)
    {
        Require(operation);
        if (RecordSchemas.IsDefaultOrEmpty || !RecordSchemas.Any(s => s.Id == schemaId && s.Version == version))
            throw new SourceOperationNotSupportedException("The selected CMS does not support this Record schema/version.");
    }
    public void Require(CompositionOperation operation, string? typeId = null, int typeVersion = 1)
    {
        if (!Enum.IsDefined(operation) || ContractVersion != CompositionContract.Version || Operations.IsDefault || !Operations.Contains(operation) ||
            (operation != CompositionOperation.Read && !AtomicConditionalWrites) ||
            (typeId is not null && (Types.IsDefault || !Types.Contains(new(typeId, typeVersion)))))
            throw new SourceOperationNotSupportedException("The selected CMS does not support this composition operation/type safely.");
    }

    public void RequireCapture()
    {
        Require(CompositionOperation.Read);
        if (!ConsistentCapture) throw new SourceOperationNotSupportedException("The selected CMS cannot capture consistent composition inputs.");
    }
}

// Read/capture boundary. Typed edits use CompositionEditor; no unvalidated graph-replacement HTTP API.
public interface ICompositionSource
{
    SourceIdentity Identity { get; }
    CompositionCapabilities CompositionCapabilities { get; }
    ValueTask<CompositionSnapshot> ReadCompositionAsync(CancellationToken cancellationToken = default);
    ValueTask<CapturedComposition> CaptureCompositionAsync(CompositionDesign design, CancellationToken cancellationToken = default);
}

// Trusted persistence boundary. This is not an HTTP graph-replacement operation or an authorization boundary.
// Application operations must authorize their specific changes before committing a validated proposed draft.
public interface ICompositionDraftPersistence : ICompositionSource
{
    Task<CompositionSnapshot> CreateCompositionAsync(CompositionWebsite website, CompositionDesign design,
        ImmutableDictionary<string, ImmutableArray<byte>> assets, CancellationToken cancellationToken = default);
    Task<CompositionSnapshot> CommitCompositionAsync(string expectedRevision, CompositionWebsite proposed, CompositionDesign design,
        ImmutableDictionary<string, ImmutableArray<byte>>? newAssets = null, CancellationToken cancellationToken = default);
}

public static class CompositionJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            PropertyNameCaseInsensitive = false,
            RespectRequiredConstructorParameters = true,
            MaxDepth = 32
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static CompositionSnapshot Read(string json)
    {
        if (json is null || json.Length > 16 * 1024 * 1024) throw new ContentValidationException("Composition JSON exceeds its 16 MiB character limit.");
        // Reject duplicate properties rather than accepting an ambiguous last value.
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicateProperties(document.RootElement);
        return JsonSerializer.Deserialize<CompositionSnapshot>(json, Options)
            ?? throw new ContentValidationException("Composition content is required.");
    }

    public static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ContentValidationException("Duplicate JSON property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
