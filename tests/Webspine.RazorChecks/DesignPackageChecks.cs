using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Webspine.Caching.Memory;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Delivery;
using Webspine.Demo;
using Webspine.Designs.Studio;
using Webspine.Rendering.Razor;

namespace Webspine.RazorChecks;

public sealed record QuoteFields([property: JsonRequired] string Quote);
public sealed class InvalidParameterComponent : ComponentBase
{
    public TextFields Fields { get; set; } = default!;
    public RazorPageContext Context { get; set; } = default!;
    public Block Block { get; set; } = default!;
}

static class DesignPackageChecks
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static string Text(BuiltArtifact artifact, string path = "index.html") =>
        Encoding.UTF8.GetString(artifact.Files.Single(f => f.Path == path).Bytes.AsSpan());
    public static async Task RunAsync(bool servePreview = false)
    {
        var package = StudioPackage.Create();
        RazorDesignPackage Copy(Type? document = null, IEnumerable<RazorComponentBinding>? bindings = null,
            ImmutableDictionary<string, ImmutableArray<byte>>? assets = null) => new(package.Descriptor,
                package.ContentTypes, package.Design, document ?? package.DocumentComponent,
                bindings ?? package.Bindings.Values, assets ?? package.Assets);
        await Reject<ContentValidationException>(() => { new DesignPackageCatalog([package]).Select("missing"); return Task.CompletedTask; });
        await Reject<ContentValidationException>(() => { new DesignPackageCatalog([package]).Select("studio", "2"); return Task.CompletedTask; });
        await Reject<ContentValidationException>(() => { new DesignPackageCatalog([package, package]); return Task.CompletedTask; });
        await Reject<ContentValidationException>(() => { Copy(bindings: package.Bindings.Values.Skip(1)); return Task.CompletedTask; });
        await Reject<ContentValidationException>(() => { Copy(bindings: package.Bindings.Values.Append(package.Bindings.Values.First())); return Task.CompletedTask; });
        await Reject<ContentValidationException>(() => { Copy(bindings: package.Bindings.Values.Select(b => b.TypeId == "text" ? b with { Component = package.Bindings["image"].Component } : b)); return Task.CompletedTask; });
        await Reject<ContentValidationException>(() => { Copy(bindings: package.Bindings.Values.Select(b => b.TypeId == "text" ? b with { Component = typeof(InvalidParameterComponent) } : b)); return Task.CompletedTask; });
        await Reject<ContentValidationException>(() => { Copy(assets: package.Assets.Remove("assets/composition.css")); return Task.CompletedTask; });
        Assert(package.ContentTypes.Descriptors.All(d => package.ContentTypes.Resolve(d.Id, d.Version).GetType().GetMethods().All(m => m.Name != "Render")), "Content definition contains rendering.");
        Console.WriteLine("PASS Configured packages reject unknown versions and missing/duplicate/incompatible typed mappings; definitions have no rendering");

        var legacy = await new DemoContentSource(await File.ReadAllTextAsync(Path.Combine(DemoSite.FixtureDirectory, "site.json"))).ReadAsync();
        var map = LegacyCompositionMapping.Identities(legacy).ToDictionary(m => (m.PageId, m.SectionId));
        var website = new StudioLegacyConverter().Convert(legacy, LegacyCompositionMapping.Blocks(legacy),
            (p, s) => { var m = map[(p, s)]; return (m.BlockId, m.PlacementId); });
        var media = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>();
        foreach (var asset in website.Assets) media.Add(asset.File, (await File.ReadAllBytesAsync(Path.Combine(DemoSite.FixtureDirectory, asset.File))).ToImmutableArray());
        var capture = new CapturedComposition(new(2, legacy.Source, legacy.Revision, website), package.Design, media.ToImmutable());
        var frozen = await package.CaptureAsync();
        var first = await package.BuildAsync(capture, frozen, "/preview/fixed");
        var second = await package.BuildAsync(capture, frozen, "/preview/fixed");
        Assert(first.Digest == second.Digest && first.Files.Zip(second.Files).All(p => p.First.Bytes.AsSpan().SequenceEqual(p.Second.Bytes.AsSpan())), "Package build is not repeatable.");
        Assert(Text(first).Contains("/preview/fixed/assets/site.js") && Text(first).Contains("/preview/fixed/services/"), "Package shell ignored prefix.");
        Assert(website.Pages.All(p => first.Files.Any(f => f.Path == BuildPipeline.PageFile(p.Path))), "Package omitted a page.");
        var text = website.Blocks.First(b => b.TypeId == "text");
        var escaped = capture with { Content = capture.Content with { Website = website with { Blocks = website.Blocks.Replace(text,
            text with { Fields = JsonSerializer.SerializeToElement(new TextFields("<script>bad()</script>", "Plain <b>text</b>"), CompositionJson.Options) }) } } };
        var html = Text(await package.BuildAsync(escaped, frozen));
        Assert(html.Contains("&lt;script&gt;bad()&lt;/script&gt;") && !html.Contains("<script>bad()"), "Production component did not escape content.");
        Console.WriteLine("PASS Production package renders all five pages deterministically with shared shell, escaped content and prefixed assets/navigation");

        using (var zip = new ZipArchive(new MemoryStream(frozen.Executable.Archive.ToArray()), ZipArchiveMode.Read))
        {
            Assert(zip.Entries.Count == frozen.Executable.Files.Length && frozen.Executable.Digest == BuildPipeline.Hash(frozen.Executable.Archive.AsSpan()), "Executable archive identity/inventory is incomplete.");
            foreach (var file in frozen.Executable.Files)
            {
                using var input = zip.GetEntry(file.Path)!.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes);
                Assert(bytes.Length == file.Length && BuildPipeline.Hash(bytes.ToArray()) == file.Digest, "Executable archive changed " + file.Path);
            }
            Assert(zip.Entries.Any(e => e.FullName == "application/Webspine.Designs.Studio.dll") &&
                zip.Entries.Any(e => e.FullName.StartsWith("dotnet/") && e.FullName.Contains("coreclr", StringComparison.OrdinalIgnoreCase)) &&
                zip.Entries.Any(e => e.FullName.StartsWith("aspnet/")) && zip.Entries.Any(e => e.FullName.StartsWith("host-fxr/")) &&
                zip.Entries.Any(e => e.FullName.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase)), "Managed/native runtime or application dependency bytes missing.");
        }
        Assert(first.Files.All(f => !f.Path.EndsWith(".dll") && !f.Path.EndsWith(".zip")), "Private executables entered website output.");
        var changed = Copy(assets: package.Assets.SetItem("assets/site.js", Encoding.UTF8.GetBytes("// changed package script\n").ToImmutableArray()));
        var next = await changed.BuildAsync(capture, await changed.CaptureAsync());
        Assert(next.DesignRevision != first.DesignRevision && next.Digest != first.Digest && Text(first, "assets/site.js").Contains("addEventListener"), "Package asset change did not produce a distinct retained candidate.");
        Console.WriteLine("PASS Executable capture retains exact application/framework/native bytes privately; package assets affect new candidates without changing old output");

        await Reject<ContentValidationException>(async () => await package.BuildAsync(capture, frozen with { Digest = "substitution" }));
        await Reject<ContentValidationException>(async () => await package.BuildAsync(capture with { AssetFiles = ImmutableDictionary<string, ImmutableArray<byte>>.Empty }, frozen));
        await Reject<ContentValidationException>(async () => await package.BuildAsync(capture with { Design = package.Design with { Revision = "changed" } }, frozen));
        await Reject<ContentValidationException>(async () => await package.BuildAsync(capture, frozen, "/../private"));
        var missingScript = Copy(assets: package.Assets.Remove("assets/site.js"));
        await Reject<InvalidOperationException>(async () => await missingScript.BuildAsync(capture, await missingScript.CaptureAsync()));
        var colliding = Copy(assets: package.Assets.Add(website.Assets[0].File, [1]));
        await Reject<ContentValidationException>(async () => await colliding.BuildAsync(capture, await colliding.CaptureAsync()));
        Console.WriteLine("PASS Package substitution, mismatched capture, missing media, unsafe prefix and asset collision fail before a candidate can be returned");

        var delayed = Copy(document: typeof(PackageDelayedDocument)); var delayedFrozen = await delayed.CaptureAsync();
        LifecycleSignals.Reset();
        var pending = delayed.BuildAsync(capture, delayedFrozen).AsTask();
        await LifecycleSignals.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert(!pending.IsCompleted, "Package lifecycle was not awaited.");
        LifecycleSignals.Continue.TrySetResult(); Assert(Text(await pending).Contains("Package lifecycle completed"), "Async result missing.");
        using var cancellation = new CancellationTokenSource(); LifecycleSignals.Reset();
        pending = delayed.BuildAsync(capture, delayedFrozen, cancellationToken: cancellation.Token).AsTask();
        await LifecycleSignals.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
        await Reject<OperationCanceledException>(async () => await pending);
        await Reject<OperationCanceledException>(async () => await package.BuildAsync(capture, frozen, cancellationToken: cancellation.Token));
        var broken = Copy(document: typeof(PackageThrowingDocument));
        await Reject<InvalidOperationException>(async () => await broken.BuildAsync(capture, await broken.CaptureAsync()));
        Assert((await package.BuildAsync(capture, frozen)).Files.Length > 0, "Failure poisoned later builds.");
        Console.WriteLine("PASS Production rendering awaits async lifecycles, handles cancellation/failure and recovers without returning partial candidates");

        var source = new FixedArtifactSource(first); var policy = DesignScriptPolicy.FromArtifact(first);
        var delivery = new PrerenderedDelivery(source, new NoDeliveryCache(), scripts: policy);
        var script = await Request(delivery, "assets/site.js");
        Assert(script.Response.StatusCode == 200 && script.Response.ContentType == "text/javascript; charset=utf-8", "Package JavaScript MIME/delivery missing.");
        Assert((await Request(delivery, "")).Response.Headers.ContentSecurityPolicy.ToString().Contains("script-src 'self'"), "Package script CSP missing.");
        Assert((await Request(new PrerenderedDelivery(source, new NoDeliveryCache()), "assets/site.js")).Response.StatusCode == 404, "Default delivery enabled script execution.");
        var tampered = first with { Files = first.Files.Select(f => f.Path == "assets/site.js" ? f with { Bytes = [1] } : f).ToImmutableArray() };
        await Reject<ContentValidationException>(() => { DesignScriptPolicy.FromArtifact(tampered); return Task.CompletedTask; });
        Assert((await Request(new PrerenderedDelivery(new FixedArtifactSource(next), new NoDeliveryCache(), scripts: policy), "assets/site.js")).Response.StatusCode == 404, "Script policy authorized a different candidate.");
        Assert((await Request(delivery, "application/Webspine.Designs.Studio.dll")).Response.StatusCode == 404, "Private executable bytes exposed.");
        Console.WriteLine("PASS Only declared intact package scripts receive executable MIME/CSP; default, tampered and different candidates remain blocked");
        var definitions = new BlockRegistry(package.ContentTypes.Descriptors.Select(d => package.ContentTypes.Resolve(d.Id, d.Version))
            .Append(new BlockDefinition<QuoteFields>(new("quote", 1, 2, "quote-content", "1", "quote-fields-v1", "none", false),
                (v, _) => CompositionRules.Text(v.Quote, 1000))));
        var main = package.Design.Layout.Regions.Single(r => r.Id == "main");
        var customDesign = package.Design with { Layout = package.Design.Layout with { Regions = package.Design.Layout.Regions.Replace(main,
            main with { AllowedTypes = main.AllowedTypes.Add("quote") }) }, Groups = package.Design.Groups with { AllowedTypes = package.Design.Groups.AllowedTypes.Add("quote") } };
        var customPackage = new RazorDesignPackage(package.Descriptor, definitions, customDesign, package.DocumentComponent,
            package.Bindings.Values.Append(new("quote", 1, typeof(PackageQuote))), package.Assets);
        var home = website.Pages.Single(p => p.Id == "home"); var homeMain = home.Regions.Single(r => r.Id == "main");
        var customCapture = capture with { Design = customDesign, Content = capture.Content with { Website = website with
        {
            Blocks = website.Blocks.Add(new("custom-quote", new(OwnerKind.Page, "home"), "quote", 1,
                JsonSerializer.SerializeToElement(new QuoteFields("Custom <words>"), CompositionJson.Options), [])),
            Pages = website.Pages.Replace(home, home with { Regions = home.Regions.Replace(homeMain,
                homeMain with { Placements = homeMain.Placements.Add(new("custom-quote-placement", TargetKind.Block, "custom-quote")) }) })
        } } };
        Assert(Text(await customPackage.BuildAsync(customCapture, await customPackage.CaptureAsync())).Contains("<blockquote>Custom &lt;words&gt;</blockquote>"), "Custom typed definition/component did not render.");
        Console.WriteLine("PASS An independent content definition and typed Razor component extend a package without engine dispatch changes");
        Console.WriteLine("7 production design package check groups passed.");
        if (servePreview)
        {
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:9095");
            await using var app = builder.Build();
            app.MapGet("/preview/fixed/{**path}", (HttpContext context, string? path) => delivery.DeliverAsync(context, path));
            await app.RunAsync();
        }
    }
    private static async Task<DefaultHttpContext> Request(PrerenderedDelivery delivery, string path)
    {
        var context = new DefaultHttpContext(); context.Request.Method = "GET"; context.Response.Body = new MemoryStream();
        await delivery.DeliverAsync(context, path); return context;
    }
}
