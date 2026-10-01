using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.RazorPrototype;

public sealed record ImplementationInput(string Assembly, string Version, string Digest);
public sealed record PrototypeInputs(CapturedComposition Composition,
    ImmutableDictionary<string, ImmutableArray<byte>> DesignAssets,
    ImmutableArray<ImplementationInput> Implementations, string Runtime, Type? DocumentComponent = null)
{
    public static ImmutableArray<ImplementationInput> CurrentImplementations(Type? documentComponent = null) => new[]
        { typeof(PrototypeInputs).Assembly, typeof(CompositionContract).Assembly, typeof(ComponentBase).Assembly,
          typeof(HtmlRenderer).Assembly, typeof(JsonSerializer).Assembly,
          (documentComponent ?? typeof(Components.StudioDocument)).Assembly }
        .Distinct().OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
        .Select(a => new ImplementationInput(a.GetName().Name!, a.GetName().Version!.ToString(),
            BuildPipeline.Hash(File.ReadAllBytes(a.Location)))).ToImmutableArray();

    public static async Task<PrototypeInputs> CaptureAsync(CancellationToken cancellationToken = default)
    {
        async Task<ImmutableArray<byte>> Read(string file)
        {
            await using var stream = typeof(PrototypeInputs).Assembly.GetManifestResourceStream("design/" + file)
                ?? throw new InvalidOperationException("Missing embedded design asset: " + file);
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes, cancellationToken);
            return ImmutableArray.Create(bytes.ToArray());
        }
        var css = await Read("prototype.css");
        var javascript = await Read("prototype.js");
        var image = await Read("room.svg");
        var registry = PrototypeContent.Registry();
        var types = registry.Descriptors.Select(d => d.Id).ToImmutableArray();
        var design = new CompositionDesign("alder-prototype", "prototype-v1", new("studio",
            [new("header", ["brand"], 1, 1), new("main", types, 1, 20), new("footer", ["brand"], 1, 1)]),
            new(types, ["stack", "grid"], ["start", "center"], ["medium"], 2), Encoding.UTF8.GetString(css.AsSpan()));
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(PrototypeContent.Fixture(), design,
                ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("assets/room.svg", image)),
            ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("assets/prototype.css", css)
                .Add("assets/prototype.js", javascript), CurrentImplementations(), RuntimeInformation.FrameworkDescription);
    }
}

public sealed class PrototypePageContext(CapturedComposition capture, CompositionPage page, string pathBase)
{
    public CompositionPage Page { get; } = page;
    public CompositionWebsite Website => capture.Content.Website;
    private readonly Dictionary<string, Block> blocks = capture.Content.Website.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
    private readonly Dictionary<string, SharedBlock> shared = capture.Content.Website.SharedBlocks.ToDictionary(b => b.Id, StringComparer.Ordinal);
    public Block Resolve(Placement placement) => blocks[placement.Kind == TargetKind.Shared
        ? shared[placement.TargetId].RootBlockId : placement.TargetId];
    public string Link(string destination) => destination.StartsWith('/') ? pathBase + destination : destination;
    public string Asset(string id) => Link("/" + Website.Assets.Single(a => a.Id == id).File);
    public IEnumerable<Placement> Region(string id) => Page.Regions.Single(r => r.Id == id).Placements;
}
