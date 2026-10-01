using System.Collections.Immutable;




namespace Webspine.Core.Composition;

public sealed record BlockTypeDescriptor(string Id, int Version, int ContractVersion,
    string ModuleId, string ModuleVersion, string SchemaId, string RendererVersion, bool Container);

public sealed class BlockRegistry
{
    private readonly ImmutableDictionary<string, IBlockDefinition> registrations;
    public ImmutableArray<BlockTypeDescriptor> Descriptors { get; }

    public BlockRegistry(IEnumerable<IBlockDefinition> registrations)
    {
        var items = registrations.OrderBy(r => r.Descriptor.Id, StringComparer.Ordinal).ToArray();
        var map = ImmutableDictionary.CreateBuilder<string, IBlockDefinition>(StringComparer.Ordinal);
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var descriptor = item.Descriptor;
            if (item.Editor is not null) ContentEditorContract.ValidateRegistration(item.Editor, item.PayloadType);
            CompositionRules.Identifier(descriptor.Id); CompositionRules.Identifier(descriptor.ModuleId);
            CompositionRules.Text(descriptor.ModuleVersion, 80); CompositionRules.Text(descriptor.SchemaId, 120);
            CompositionRules.Text(descriptor.RendererVersion, 80);
            if (descriptor.Version < 1 || descriptor.ContractVersion != CompositionContract.Version || !map.TryAdd(descriptor.Id, item))
                throw new ContentValidationException("Duplicate or incompatible Block registration.");
            if (modules.TryGetValue(descriptor.ModuleId, out var version) && version != descriptor.ModuleVersion)
                throw new ContentValidationException("Conflicting module versions.");
            modules[descriptor.ModuleId] = descriptor.ModuleVersion;
        }
        if (map.Count == 0) throw new ContentValidationException("A Block registry is required.");
        this.registrations = map.ToImmutable();
        Descriptors = items.Select(i => i.Descriptor).ToImmutableArray();
    }

    public IBlockDefinition Resolve(string id, int version)
    {
        CompositionRules.Identifier(id);
        if (!registrations.TryGetValue(id, out var registration) || registration.Descriptor.Version != version)
            throw new ContentValidationException("Unregistered or incompatible Block type/version.");
        return registration;
    }
}

public sealed class BlockValidationContext(CompositionWebsite website, CompositionDesign design)
{
    public CompositionWebsite Website => website;
    public CompositionDesign Design { get; } = design;
    public void Asset(string id)
    {
        if (!website.Assets.Any(a => a.Id == id)) throw new ContentValidationException("Block references a missing asset.");
    }
    public void Destination(string value) => CompositionRules.Destination(value, website.Pages.Select(p => p.Path).ToHashSet(StringComparer.Ordinal));
    public void Page(string id)
    {
        if (!website.Pages.Any(p => p.Id == id)) throw new ContentValidationException("Block references a missing page.");
    }
}
