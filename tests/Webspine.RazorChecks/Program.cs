using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.RazorPrototype;
using Webspine.RazorChecks;

var inputs = await PrototypeInputs.CaptureAsync();
var renderer = new PrototypeRenderer();
var passed = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
async Task Reject<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
string Text(BuiltArtifact artifact, string path = "index.html") =>
    Encoding.UTF8.GetString(artifact.Files.Single(f => f.Path == path).Bytes.AsSpan());
PrototypeInputs WithBlocks(ImmutableArray<Block> blocks) => inputs with
{
    Composition = inputs.Composition with { Content = inputs.Composition.Content with
        { Website = inputs.Composition.Content.Website with { Blocks = blocks } } }
};
PrototypeInputs WithDocument(Type type) => inputs with
    { DocumentComponent = type, Implementations = PrototypeInputs.CurrentImplementations(type) };

await Check("Razor builds repeatably from frozen inputs with shared, nested and typed components", async () =>
{
    var first = await renderer.BuildAsync(inputs);
    var second = await renderer.BuildAsync(inputs);
    Assert(first.Digest == second.Digest && first.Files.Zip(second.Files).All(p => p.First.Bytes.AsSpan().SequenceEqual(p.Second.Bytes.AsSpan())), "Output is not repeatable.");
    var html = Text(first);
    Assert(html.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal) && html.Contains("<title>Spaces for everyday life | Alder Studio</title>"), "Document metadata missing.");
    Assert(html.Split("Interiors made for living.").Length == 3, "Shared content did not render in both regions.");
    Assert(html.Contains("id=\"approach\"") && html.Contains("id=\"principles\"") && html.Contains("01 / Understand"), "Nested Groups did not render.");
    Assert(html.Contains("<details>") && html.Contains("Can we start with just one room?"), "Custom FAQ component missing.");
    Assert(first.Files.Length == 5 && first.Files.All(f => f.Digest == BuildPipeline.Hash(f.Bytes.AsSpan())), "Artifact completeness/integrity failed.");
});

await Check("Razor escapes content in text and attributes and preserves validated preview links", async () =>
{
    var blocks = inputs.Composition.Content.Website.Blocks;
    var index = blocks.IndexOf(blocks.Single(b => b.Id == "intro"));
    var fields = JsonSerializer.SerializeToElement(new TextFields("<script>alert(1)</script>", "Words <b>remain text</b>"), CompositionJson.Options);
    var changed = WithBlocks(blocks.SetItem(index, blocks[index] with { Fields = fields }));
    var html = Text(await renderer.BuildAsync(changed, pathBase: "/preview/proof"));
    Assert(html.Contains("&lt;script&gt;") && !html.Contains("<script>alert"), "Text injection was not escaped.");
    Assert(html.Contains("&lt;b&gt;remain text&lt;/b&gt;"), "Body escaped incorrectly.");
    Assert(html.Contains("src=\"/preview/proof/assets/room.svg\"") && html.Contains("href=\"/preview/proof/\""), "Preview links ignored prefix.");
    Assert(html.Contains("src=\"/preview/proof/assets/prototype.js\""), "Script ignored prefix.");
    var imageIndex = blocks.IndexOf(blocks.Single(b => b.Id == "room"));
    var alt = JsonSerializer.SerializeToElement(new ImageFields("room-image", "\" onerror=\"alert(1)"), CompositionJson.Options);
    var escapedAttribute = Text(await renderer.BuildAsync(WithBlocks(blocks.SetItem(imageIndex, blocks[imageIndex] with { Fields = alt }))));
    Assert(!escapedAttribute.Contains("alt=\"\" onerror="), "Attribute injection was not escaped.");
    await Reject<ContentValidationException>(async () => await renderer.BuildAsync(inputs, pathBase: "/../private"));
});

await Check("CSS, JavaScript, images and implementation provenance are captured in the artifact", async () =>
{
    var artifact = await renderer.BuildAsync(inputs);
    var html = Text(artifact);
    Assert(html.Contains("href=\"/assets/prototype.css\"") && html.Contains("src=\"/assets/prototype.js\" defer"), "Design assets not referenced.");
    Assert(!html.Contains("_framework/") && !html.Contains("onclick="), "Static output assumed a Blazor event runtime.");
    Assert(Text(artifact, "assets/prototype.js").Contains("addEventListener('click'"), "Actual browser script missing.");
    using var manifest = JsonDocument.Parse(Text(artifact, "razor-prototype-manifest.json"));
    var provenance = manifest.RootElement.GetProperty("provenance");
    Assert(provenance.GetProperty("implementations").GetArrayLength() >= 5 && provenance.GetProperty("assets").GetArrayLength() == 3, "Provenance omitted dependencies/assets.");
    Assert(provenance.GetProperty("runtime").GetString() == inputs.Runtime, "Runtime not recorded.");
    var changed = inputs with { DesignAssets = inputs.DesignAssets.SetItem("assets/prototype.js", ImmutableArray.Create(Encoding.UTF8.GetBytes("// changed script\n"))) };
    var next = await renderer.BuildAsync(changed);
    Assert(next.Digest != artifact.Digest && next.DesignRevision != artifact.DesignRevision, "JavaScript does not enter artifact/design identity.");
    Assert(Text(artifact, "assets/prototype.js").Contains("addEventListener"), "Later design input changed retained output.");
});

await Check("Invalid graphs, missing assets, unsupported components and changed implementations fail", async () =>
{
    await Reject<ContentValidationException>(async () => await renderer.BuildAsync(inputs with
        { Composition = inputs.Composition with { AssetFiles = ImmutableDictionary<string, ImmutableArray<byte>>.Empty } }));
    await Reject<ContentValidationException>(async () => await renderer.BuildAsync(inputs with
        { DesignAssets = inputs.DesignAssets.Remove("assets/prototype.js") }));
    await Reject<ContentValidationException>(async () => await renderer.BuildAsync(inputs with { Implementations = [] }));
    var blocks = inputs.Composition.Content.Website.Blocks;
    var group = blocks.IndexOf(blocks.Single(b => b.Id == "hero"));
    await Reject<ContentValidationException>(async () => await renderer.BuildAsync(WithBlocks(blocks.SetItem(group,
        blocks[group] with { Children = [new("cycle", TargetKind.Block, "hero")] }))));
    var faq = blocks.IndexOf(blocks.Single(b => b.Id == "questions"));
    var cards = JsonSerializer.SerializeToElement(new CardsFields("Unsupported", [new("Card", "Description", null, "/")]), CompositionJson.Options);
    await Reject<InvalidOperationException>(async () => await renderer.BuildAsync(WithBlocks(blocks.SetItem(faq,
        blocks[faq] with { TypeId = "cards", Fields = cards }))));
});

await Check("Build awaits asynchronous component lifecycle completion", async () =>
{
    LifecycleSignals.Reset();
    var build = renderer.BuildAsync(WithDocument(typeof(DelayedDocument)));
    await LifecycleSignals.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Assert(!build.IsCompleted, "Build returned before async lifecycle completed.");
    LifecycleSignals.Continue.TrySetResult();
    Assert(Text(await build).Contains("Async lifecycle completed"), "Final output omitted async result.");
});

await Check("Pre-cancellation, lifecycle cancellation and rendering failure return no artifact", async () =>
{
    using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
    await Reject<OperationCanceledException>(async () => await renderer.BuildAsync(inputs, alreadyCancelled.Token));
    using var cancellation = new CancellationTokenSource();
    LifecycleSignals.Reset();
    BuiltArtifact? result = null;
    var pending = renderer.BuildAsync(WithDocument(typeof(DelayedDocument)), cancellation.Token);
    await LifecycleSignals.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
    cancellation.Cancel();
    await Reject<OperationCanceledException>(async () => result = await pending);
    Assert(result is null, "Cancelled render returned an artifact.");
    await Reject<InvalidOperationException>(async () => result = await renderer.BuildAsync(WithDocument(typeof(ThrowingDocument))));
    Assert(result is null, "Failed render returned an artifact.");
    var subsequent = await renderer.BuildAsync(inputs);
    Assert(subsequent.Files.Any(f => f.Path == "index.html"), "Failure poisoned a subsequent build.");
});

await Check("Shared finalizer preserves the established v2 digest and refuses incomplete output", async () =>
{
    // Use the normal v2 renderer on a separate standard-only fixture to pin its original digest algorithm.
    var site = inputs.Composition.Content.Website;
    var standard = site.Blocks.Where(b => b.TypeId is "text" or "image").ToImmutableArray();
    var pageBlocks = standard.Select(b => b with { Owner = new BlockOwner(OwnerKind.Page, "home") }).ToImmutableArray();
    var page = site.Pages[0] with { Regions = [new("header", []), new("main", pageBlocks.Select(b => new Placement("p-" + b.Id, TargetKind.Block, b.Id)).ToImmutableArray()), new("footer", [])] };
    var capture = inputs.Composition with { Content = inputs.Composition.Content with
        { Website = site with { Pages = [page], Blocks = pageBlocks, SharedBlocks = [] } },
        Design = inputs.Composition.Design with { Layout = new("studio", [new("header", ["text"], 0, 1), new("main", ["text", "image"], 1, 20), new("footer", ["text"], 0, 1)]),
            Groups = inputs.Composition.Design.Groups with { AllowedTypes = ["text", "image", "group"] } } };
    var built = new CompositionBuildPipeline(new(StandardBlocks.Registrations)).Build(capture);
    var expected = BuildPipeline.Hash(JsonSerializer.SerializeToUtf8Bytes(new
    {
        contractVersion = 2, capture.Content.Source, capture.Content.Revision, designRevision = built.DesignRevision,
        files = built.Files.Select(f => new { f.Path, length = f.Bytes.Length, f.Digest }).ToArray()
    }, CompositionJson.Options));
    Assert(expected == built.Digest, "Established v2 digest format changed.");
    await Reject<ContentValidationException>(() => { CompositionArtifactFinalizer.Complete(new(), inputs.Composition.Content, "design"); return Task.CompletedTask; });
    var missingImage = new ArtifactBuilder(); missingImage.AddText("index.html", "Document");
    await Reject<ContentValidationException>(() => { CompositionArtifactFinalizer.Complete(missingImage, inputs.Composition.Content, "design"); return Task.CompletedTask; });
    var output = new ArtifactBuilder(); output.AddText("index.html", "Document"); output.Add("assets/room.svg", [1]);
    var completed = CompositionArtifactFinalizer.Complete(output, inputs.Composition.Content, "design");
    await Reject<InvalidOperationException>(() => { output.AddText("extra.txt", "Late mutation"); return Task.CompletedTask; });
    Assert(completed.Files.Length == 2, "Finalized artifact changed.");
});
Console.WriteLine($"{passed} Razor prototype check groups passed.");
