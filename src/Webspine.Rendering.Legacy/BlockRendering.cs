using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using Webspine.Core;

namespace Webspine.Core.Composition;

public interface IBlockRegistration : IBlockDefinition
{
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
    public object DecodeFields(Block block) => Decode(block)!;
    public void Validate(Block block, BlockValidationContext context) => validate(Decode(block), context);
    public void Render(Block block, BlockRenderContext context, HtmlOutput output) => render(Decode(block), context, output);
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
