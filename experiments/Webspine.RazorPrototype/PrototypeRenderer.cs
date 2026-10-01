using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.RazorPrototype.Components;

namespace Webspine.RazorPrototype;

// Bounded experiment, not a production package loader or replacement management renderer.
public sealed class PrototypeRenderer
{
    public async Task<BuiltArtifact> BuildAsync(PrototypeInputs inputs, CancellationToken cancellationToken = default,
        string pathBase = "")
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capture = inputs.Composition;
        var registry = PrototypeContent.Registry();
        CompositionContract.Validate(capture.Content, capture.Design, registry);
        if (pathBase.Length > 0 && (!pathBase.StartsWith('/') || pathBase.Contains("..") ||
            pathBase.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-'))))
            throw new ContentValidationException("Invalid preview path prefix.");
        pathBase = pathBase.TrimEnd('/');
        var documentComponent = inputs.DocumentComponent ?? typeof(StudioDocument);
        if (!inputs.Implementations.SequenceEqual(PrototypeInputs.CurrentImplementations(documentComponent)) ||
            inputs.Runtime != RuntimeInformation.FrameworkDescription)
            throw new ContentValidationException("Captured rendering implementation/runtime no longer matches.");
        if (capture.AssetFiles.Count != capture.Content.Website.Assets.Length ||
            capture.Content.Website.Assets.Any(a => !capture.AssetFiles.TryGetValue(a.File, out var bytes) || bytes.IsDefaultOrEmpty))
            throw new ContentValidationException("Capture must include exactly the referenced image bytes.");
        if (inputs.DesignAssets.Count != 2 || !inputs.DesignAssets.TryGetValue("assets/prototype.css", out var css) ||
            css.IsDefaultOrEmpty || Encoding.UTF8.GetString(css.AsSpan()) != capture.Design.Stylesheet ||
            !inputs.DesignAssets.TryGetValue("assets/prototype.js", out var js) || js.IsDefaultOrEmpty)
            throw new ContentValidationException("Capture must include the selected CSS and JavaScript.");
        var provenance = new
        {
            capture.Design, inputs.Implementations, inputs.Runtime, pathBase, documentComponent = documentComponent.FullName,
            registrations = registry.Descriptors,
            assets = inputs.DesignAssets.Concat(capture.AssetFiles).OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new { path = p.Key, digest = BuildPipeline.Hash(p.Value.AsSpan()) }).ToArray()
        };
        var designRevision = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(provenance, CompositionJson.Options));
        var output = new ArtifactBuilder();
        foreach (var (path, bytes) in inputs.DesignAssets.Concat(capture.AssetFiles)) output.Add(path, bytes.AsSpan());
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        // Await async lifecycles before disposal. Trusted components must cooperate with the token.
        foreach (var page in capture.Content.Website.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(StudioDocument.Context)] = new PrototypePageContext(capture, page, pathBase),
                    [nameof(StudioDocument.CancellationToken)] = cancellationToken
                });
                var root = await renderer.RenderComponentAsync(documentComponent, parameters);
                cancellationToken.ThrowIfCancellationRequested();
                return root.ToHtmlString();
            });
            output.AddText(BuildPipeline.PageFile(page.Path), html);
        }
        output.AddText("razor-prototype-manifest.json", JsonSerializer.Serialize(new
        {
            contractVersion = 2, capture.Content.Source, capture.Content.Revision,
            contentDigest = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(capture.Content, CompositionJson.Options)),
            designRevision, provenance
        }, CompositionJson.Options));
        return CompositionArtifactFinalizer.Complete(output, capture.Content, designRevision, cancellationToken);
    }
}
