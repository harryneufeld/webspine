using System.Collections.Immutable;

namespace Webspine.Core.Composition;

public sealed record DesignPackageDescriptor(string Id, string Version, int ContractVersion, string RendererId, string RendererVersion);
public sealed record ComponentBinding(string TypeId, int TypeVersion, string ComponentId, string Version);
public sealed record ContentDefinitionIdentity(BlockTypeDescriptor Descriptor, string ImplementationDigest,
    ContentEditorMetadata? Editor = null, PatternDescriptor? Pattern = null);
public sealed record RecordDefinitionIdentity(RecordSchemaDescriptor Descriptor, string ImplementationDigest, ContentEditorMetadata Editor);
public sealed record ExecutableFile(string Path, string Digest, long Length);
public sealed record ExecutableEnvironmentSnapshot(string Runtime, string RuntimeIdentifier, string OperatingSystem,
    ImmutableArray<ExecutableFile> Files, ImmutableArray<byte> Archive, string Digest);
public sealed record FrozenDesignPackage(DesignPackageDescriptor Descriptor, CompositionDesign Design,
    ImmutableArray<ComponentBinding> Components, ImmutableArray<ContentDefinitionIdentity> ContentTypes,
    ImmutableDictionary<string, ImmutableArray<byte>> Assets,
    ExecutableEnvironmentSnapshot Executable, string Digest)
{
    public ImmutableArray<RecordDefinitionIdentity> RecordSchemas { get; init; } = [];
}

public interface IDesignPackage
{
    DesignPackageDescriptor Descriptor { get; }
    BlockRegistry ContentTypes { get; }
    CompositionDesign Design { get; }
    ValueTask<FrozenDesignPackage> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface ICompositionRenderer
{
    ValueTask<BuiltArtifact> BuildAsync(CapturedComposition content, FrozenDesignPackage design,
        string pathBase = "", CancellationToken cancellationToken = default);
}
