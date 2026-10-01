using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Webspine.Core.Composition;

public sealed record BlockTypeDescriptor(string Id, int Version, int ContractVersion,
    string ModuleId, string ModuleVersion, string SchemaId, string RendererVersion, bool Container);

public interface IBlockRegistration
{
    BlockTypeDescriptor Descriptor { get; }
    Type PayloadType { get; }
    string ImplementationDigest { get; }
    void Validate(Block block, BlockValidationContext context);
    void Render(Block block, BlockRenderContext context, HtmlOutput output);
}

// Registration is trusted code. Core ownership/reference validation is never delegated to it.
public sealed class BlockRegistration<T>(BlockTypeDescriptor descriptor,
    Action<T, BlockValidationContext> validate, Action<T, BlockRenderContext, HtmlOutput> render) : IBlockRegistration
{
    public BlockTypeDescriptor Descriptor { get; } = descriptor;
    public Type PayloadType => typeof(T);
    public string ImplementationDigest { get; } = BuildPipeline.Hash(Encoding.UTF8.GetBytes(string.Join("\n",
        new[] { typeof(T).Assembly, validate.Method.Module.Assembly, render.Method.Module.Assembly }
            .Distinct().OrderBy(a => a.FullName, StringComparer.Ordinal)
            .Select(a => BuildPipeline.Hash(File.ReadAllBytes(a.Location))))));
    private static T Decode(Block block)
    {
        try { return block.Fields.Deserialize<T>(CompositionJson.Options) ?? throw new ContentValidationException("Block fields are required."); }
        catch (JsonException exception) { throw new ContentValidationException("Invalid registered Block fields: " + exception.Message); }
    }
    public void Validate(Block block, BlockValidationContext context) => validate(Decode(block), context);
    public void Render(Block block, BlockRenderContext context, HtmlOutput output) => render(Decode(block), context, output);
}

public sealed class BlockRegistry
{
    private readonly ImmutableDictionary<string, IBlockRegistration> registrations;
    public ImmutableArray<BlockTypeDescriptor> Descriptors { get; }

    public BlockRegistry(IEnumerable<IBlockRegistration> registrations)
    {
        var items = registrations.OrderBy(r => r.Descriptor.Id, StringComparer.Ordinal).ToArray();
        var map = ImmutableDictionary.CreateBuilder<string, IBlockRegistration>(StringComparer.Ordinal);
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var descriptor = item.Descriptor;
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

    public IBlockRegistration Resolve(string id, int version)
    {
        CompositionRules.Identifier(id);
        if (!registrations.TryGetValue(id, out var registration) || registration.Descriptor.Version != version)
            throw new ContentValidationException("Unregistered or incompatible Block type/version.");
        return registration;
    }
}

public sealed class BlockValidationContext(CompositionWebsite website, CompositionDesign design)
{
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

public sealed class BlockRenderContext(Action renderChildren, Func<string, string> assetPath, CompositionWebsite website, CompositionPage page, Func<string, string>? link = null)
{
    public string Link(string destination) => link is null ? destination : link(destination);
    public void RenderChildren() => renderChildren();
    public string AssetPath(string id) => assetPath(id);
    public string WebsiteTitle => website.Title;
    public CompositionPage Page { get; } = page;
    public CompositionPage PageById(string id) => website.Pages.Single(p => p.Id == id);
}

// Escape all content values. Raw markup belongs to installed, trusted rendering code only.
public sealed class HtmlOutput
{
    private readonly StringBuilder text = new();
    public void Text(string? value) => text.Append(WebUtility.HtmlEncode(value));
    public void Markup(string trustedMarkup) => text.Append(trustedMarkup);
    public override string ToString() => text.ToString();
}
