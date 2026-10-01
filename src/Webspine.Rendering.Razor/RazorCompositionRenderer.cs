using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Rendering.Razor;

public static class RazorCompositionRenderer
{
    public static async ValueTask<BuiltArtifact> BuildAsync(RazorDesignPackage package, CapturedComposition content,
        FrozenDesignPackage design, string pathBase = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = await package.CaptureAsync(cancellationToken);
        if (!ReferenceEquals(installed, design) ||
            !JsonSerializer.SerializeToUtf8Bytes(content.Design, CompositionJson.Options).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(design.Design, CompositionJson.Options)))
            throw new ContentValidationException("Capture does not match the selected immutable package/design.");
        CompositionContract.Validate(content.Content, design.Design, package.ContentTypes);
        if (pathBase.Length > 0 && (!pathBase.StartsWith('/') || pathBase.Contains("..") ||
            pathBase.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-'))))
            throw new ContentValidationException("Invalid preview path prefix.");
        pathBase = pathBase.TrimEnd('/');
        var site = content.Content.Website;
        if (content.AssetFiles.Count != site.Assets.Length || site.Assets.Any(a =>
            !content.AssetFiles.TryGetValue(a.File, out var bytes) || bytes.IsDefaultOrEmpty))
            throw new ContentValidationException("Capture omitted required immutable image bytes.");
        var output = new ArtifactBuilder();
        foreach (var (path, bytes) in design.Assets.Concat(content.AssetFiles)) output.Add(path, bytes.AsSpan());
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        foreach (var page in site.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync(package.DocumentComponent,
                    ParameterView.FromDictionary(new Dictionary<string, object?>
                    {
                        ["Context"] = new RazorPageContext(package, content, design, page, pathBase, cancellationToken),
                        ["CancellationToken"] = cancellationToken
                    }));
                cancellationToken.ThrowIfCancellationRequested();
                return root.ToHtmlString();
            });
            output.AddText(BuildPipeline.PageFile(page.Path), html);
        }
        var scripts = design.Assets.Where(a => a.Key.EndsWith(".js", StringComparison.Ordinal))
            .OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new { path = a.Key, digest = BuildPipeline.Hash(a.Value.AsSpan()) }).ToArray();
        output.AddText("design-package-manifest.json", JsonSerializer.Serialize(new
        {
            contractVersion = 2, content.Content.Source, content.Content.Revision,
            contentDigest = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(content.Content, CompositionJson.Options)),
            designRevision = design.Digest, package = design.Descriptor, design.Design, design.Components, design.ContentTypes,
            executable = new { design.Executable.Digest, design.Executable.Runtime, design.Executable.RuntimeIdentifier, design.Executable.OperatingSystem,
                files = design.Executable.Files }, scripts,
            assets = design.Assets.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new { path = a.Key, digest = BuildPipeline.Hash(a.Value.AsSpan()) })
        }, CompositionJson.Options));
        return CompositionArtifactFinalizer.Complete(output, content.Content, design.Digest, cancellationToken);
    }
}
