using System.Collections.Immutable;
using System.Text.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Rendering.Razor;

public sealed record RazorComponentBinding(string TypeId, int TypeVersion, Type Component, string Version = "1");

public sealed class RazorDesignPackage : IDesignPackage, ICompositionRenderer
{
    public DesignPackageDescriptor Descriptor { get; }
    public BlockRegistry ContentTypes { get; }
    public CompositionDesign Design { get; }
    public Type DocumentComponent { get; }
    public ImmutableDictionary<string, RazorComponentBinding> Bindings { get; }
    public ImmutableDictionary<string, ImmutableArray<byte>> Assets { get; }
    private FrozenDesignPackage? frozen;
    private readonly SemaphoreSlim captureLock = new(1, 1);

    public RazorDesignPackage(DesignPackageDescriptor descriptor, BlockRegistry definitions, CompositionDesign design,
        Type document, IEnumerable<RazorComponentBinding> components,
        ImmutableDictionary<string, ImmutableArray<byte>> assets)
    {
        CompositionRules.Identifier(descriptor.Id); CompositionRules.Identifier(descriptor.RendererId);
        CompositionRules.Text(descriptor.Version, 80); CompositionRules.Text(descriptor.RendererVersion, 80);
        if (descriptor.ContractVersion != 2 || descriptor.RendererId != "webspine-razor" || descriptor.RendererVersion != "1")
            throw new ContentValidationException("Unsupported design package/renderer version.");
        CompositionContract.ValidateDesign(design, definitions);
        if (!typeof(RazorDocumentComponent).IsAssignableFrom(document) || document.IsAbstract)
            throw new ContentValidationException("The package document must be a concrete Razor document component.");
        var map = ImmutableDictionary.CreateBuilder<string, RazorComponentBinding>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            var definition = definitions.Resolve(component.TypeId, component.TypeVersion);
            CompositionRules.Text(component.Version, 80);
            if (!map.TryAdd(component.TypeId, component) || !typeof(IComponent).IsAssignableFrom(component.Component) || component.Component.IsAbstract ||
                !Parameter(component.Component, "Fields", definition.PayloadType) ||
                !Parameter(component.Component, "Context", typeof(RazorPageContext)) ||
                !Parameter(component.Component, "Block", typeof(Block)))
                throw new ContentValidationException("Duplicate or incompatible typed component mapping.");
        }
        if (map.Count != definitions.Descriptors.Length || definitions.Descriptors.Any(d => !map.ContainsKey(d.Id)))
            throw new ContentValidationException("Every registered content type requires a compatible component mapping.");
        foreach (var (path, bytes) in assets)
        {
            ArtifactPaths.Validate(path);
            if (!path.StartsWith("assets/", StringComparison.Ordinal) || bytes.IsDefaultOrEmpty)
                throw new ContentValidationException("Design assets require safe paths and immutable nonempty bytes.");
        }
        if (!assets.Keys.Any(p => p.EndsWith(".css", StringComparison.Ordinal)))
            throw new ContentValidationException("A package requires a captured stylesheet.");
        Descriptor = descriptor; ContentTypes = definitions; Design = design; DocumentComponent = document;
        Bindings = map.ToImmutable(); Assets = assets;
        // All explicitly registered executable types must be in the captured application closure.
        foreach (var assembly in Bindings.Values.Select(b => b.Component.Assembly).Append(document.Assembly)
            .Concat(definitions.Descriptors.Select(d => definitions.Resolve(d.Id, d.Version).PayloadType.Assembly)).Distinct())
            if (!Path.GetFullPath(assembly.Location).StartsWith(Path.GetFullPath(AppContext.BaseDirectory),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ContentValidationException("Package assemblies must be installed with the application.");
    }

    private static bool Parameter(Type component, string name, Type expected)
    {
        var property = component.GetProperty(name);
        return property?.PropertyType == expected && property.SetMethod?.IsPublic == true &&
            property.GetCustomAttribute<ParameterAttribute>() is not null;
    }

    public async ValueTask<FrozenDesignPackage> CaptureAsync(CancellationToken cancellationToken = default)
    {
        await captureLock.WaitAsync(cancellationToken);
        try
        {
            if (frozen is not null) return frozen;
            var executable = await ExecutableCapture.ReadAsync(cancellationToken);
            var mappings = Bindings.Values.OrderBy(b => b.TypeId, StringComparer.Ordinal)
                .Select(b => new ComponentBinding(b.TypeId, b.TypeVersion, b.Component.FullName!, b.Version)).ToImmutableArray();
            var definitions = ContentTypes.Descriptors.Select(d => new ContentDefinitionIdentity(d,
                ContentTypes.Resolve(d.Id, d.Version).ImplementationDigest, ContentTypes.Resolve(d.Id, d.Version).Editor)).ToImmutableArray();
            var digest = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Descriptor, Design, document = DocumentComponent.FullName, components = mappings,
                definitions,
                executable = new { executable.Digest, executable.Runtime, executable.RuntimeIdentifier, executable.OperatingSystem },
                assets = Assets.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new { path = a.Key, digest = BuildPipeline.Hash(a.Value.AsSpan()) })
            }, CompositionJson.Options));
            return frozen = new(Descriptor, Design, mappings, definitions, Assets, executable, digest);
        }
        finally { captureLock.Release(); }
    }

    public ValueTask<BuiltArtifact> BuildAsync(CapturedComposition content, FrozenDesignPackage design,
        string pathBase = "", CancellationToken cancellationToken = default) =>
        RazorCompositionRenderer.BuildAsync(this, content, design, pathBase, cancellationToken);
}

public sealed class DesignPackageCatalog
{
    private readonly ImmutableDictionary<string, RazorDesignPackage> packages;
    public DesignPackageCatalog(IEnumerable<RazorDesignPackage> registrations)
    {
        var map = ImmutableDictionary.CreateBuilder<string, RazorDesignPackage>(StringComparer.Ordinal);
        foreach (var package in registrations)
            if (!map.TryAdd(package.Descriptor.Id, package)) throw new ContentValidationException("Duplicate design package ID.");
        if (map.Count == 0) throw new ContentValidationException("Register at least one design package.");
        packages = map.ToImmutable();
    }
    public RazorDesignPackage Select(string id, string? version = null) => packages.TryGetValue(id, out var package) &&
        (version is null || version == package.Descriptor.Version) ? package :
        throw new ContentValidationException("The configured design package/version is not installed.");
}
