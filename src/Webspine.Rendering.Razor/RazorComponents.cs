using Microsoft.AspNetCore.Components;
using Webspine.Core.Composition;

namespace Webspine.Rendering.Razor;

public abstract class RazorDocumentComponent : ComponentBase
{
    [Parameter, EditorRequired] public RazorPageContext Context { get; set; } = default!;
    [Parameter] public CancellationToken CancellationToken { get; set; }
    protected override void OnParametersSet() => CancellationToken.ThrowIfCancellationRequested();
}

public abstract class RazorContentComponent<T> : ComponentBase
{
    [Parameter, EditorRequired] public T Fields { get; set; } = default!;
    [Parameter, EditorRequired] public RazorPageContext Context { get; set; } = default!;
    [Parameter, EditorRequired] public Block Block { get; set; } = default!;
    protected override void OnParametersSet() => Context.CancellationToken.ThrowIfCancellationRequested();
}

public sealed class RazorPageContext(RazorDesignPackage package, CapturedComposition captured,
    FrozenDesignPackage design, CompositionPage page, string prefix, CancellationToken cancellationToken)
{
    public CompositionPage Page => page;
    public CompositionWebsite Website => captured.Content.Website;
    public FrozenDesignPackage Design => design;
    public CancellationToken CancellationToken => cancellationToken;
    public string Link(string destination) => destination.StartsWith('/') ? prefix + destination : destination;
    public string Asset(string id) => Link("/" + Website.Assets.Single(a => a.Id == id).File);
    public string DesignAsset(string path) => design.Assets.ContainsKey(path) ? Link("/" + path) :
        throw new InvalidOperationException("The component requested an undeclared design asset.");
    public IEnumerable<Placement> Region(string id) => page.Regions.Single(r => r.Id == id).Placements;
    public Block Resolve(Placement placement) => Website.Blocks.Single(b => b.Id == (placement.Kind == TargetKind.Shared
        ? Website.SharedBlocks.Single(s => s.Id == placement.TargetId).RootBlockId : placement.TargetId));
    public Type Component(Block block) => package.Bindings[block.TypeId].Component;
    public IDictionary<string, object> Parameters(Block block) => new Dictionary<string, object>
    {
        ["Fields"] = package.ContentTypes.Resolve(block.TypeId, block.TypeVersion).DecodeFields(block),
        ["Context"] = this, ["Block"] = block
    };
}
