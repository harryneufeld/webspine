using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Webspine.Core;
using Webspine.Demo;

var json = await File.ReadAllTextAsync(Path.Combine(DemoSite.FixtureDirectory, "site.json"));
var source = new DemoContentSource(json);
var snapshot = await source.ReadAsync();
var passed = 0;

void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

async Task Check(string name, Func<Task> check)
{
    await check();
    passed++;
    Console.WriteLine("PASS " + name);
}

async Task Reject<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}

ContentSnapshot WithFirstSection(ContentSection section) => snapshot with
{
    Website = snapshot.Website with
    {
        Pages = snapshot.Website.Pages.SetItem(0, snapshot.Website.Pages[0] with
        {
            Sections = snapshot.Website.Pages[0].Sections.SetItem(0, section)
        })
    }
};

await Check("Fixture contains five separate pages and real asset references", () =>
{
    Assert(snapshot.Website.Pages.Select(p => p.Id).SequenceEqual(new[] { "home", "services", "products", "about", "contact" }), "Unexpected pages.");
    Assert(snapshot.Website.Pages.Select(p => p.Path).Distinct().Count() == 5, "Page paths collide.");
    return Task.CompletedTask;
});

await Check("Read-only adapter rejects writes without fallback", async () =>
{
    Assert(!source.Capabilities.ConditionalDraftWrite, "Fixture incorrectly advertises writable drafts.");
    await Reject<SourceOperationNotSupportedException>(async () => await source.UpdateDraftAsync(new(snapshot.Revision, "home", "introduction", "heading", "Changed")));
    Assert((await source.ReadAsync()).Revision == snapshot.Revision, "Rejected write changed the source.");
});

await Check("Changed fixture content changes its opaque revision", async () =>
{
    var changed = new DemoContentSource(json.Replace("Good ideas deserve a clear direction.", "A different introduction."));
    Assert((await changed.ReadAsync()).Revision != snapshot.Revision, "Revision ignores fixture changes.");
});

await Check("Unexpected content fields are rejected", async () =>
{
    await Reject<JsonException>(() => { _ = new DemoContentSource(json.Replace("\"kind\": \"text\"", "\"kind\": \"text\", \"css\": \"body{}\"")); return Task.CompletedTask; });
});

await Check("Duplicate pages, missing images and broken internal links are rejected", async () =>
{
    var duplicate = snapshot with { Website = snapshot.Website with { Pages = snapshot.Website.Pages.Add(snapshot.Website.Pages[0]) } };
    await Reject<ContentValidationException>(() => { ContentContract.Validate(duplicate); return Task.CompletedTask; });
    await Reject<ContentValidationException>(() => { ContentContract.Validate(WithFirstSection(new ImageSection("image", "missing", "An image"))); return Task.CompletedTask; });
    await Reject<ContentValidationException>(() => { ContentContract.Validate(WithFirstSection(new CtaSection("cta", "Heading", "Text", "Click", "/missing/"))); return Task.CompletedTask; });
});

await Check("Unsafe destinations and artifact paths are rejected", async () =>
{
    foreach (var link in new[] { "javascript:alert(1)", "//evil.example", "/../private/", "http://example.com/" })
        await Reject<ContentValidationException>(() => { ContentContract.Validate(WithFirstSection(new CtaSection("cta", "Heading", "Text", "Click", link))); return Task.CompletedTask; });
    foreach (var path in new[] { "../secret", "/absolute", "assets/../../secret", "assets\\secret", "assets/.hidden" })
        await Reject<ContentValidationException>(() => { new ArtifactBuilder().AddText(path, "test"); return Task.CompletedTask; });
    var output = new ArtifactBuilder(); output.AddText("index.html", "first");
    await Reject<ContentValidationException>(() => { output.AddText("INDEX.html", "second"); return Task.CompletedTask; });
});

await Check("CMS text is HTML-escaped by the renderer", async () =>
{
    var escaped = WithFirstSection(new TextSection("intro", "<script>alert(1)</script>", "Text <b>content</b>"));
    var renderer = new DemoRenderer("", ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("assets/studio.svg", [1, 2, 3]));
    var built = await new BuildPipeline(renderer).BuildAsync(new(escaped, "test-design"));
    var html = Encoding.UTF8.GetString(built.Files.Single(f => f.Path == "index.html").Bytes.AsSpan());
    Assert(html.Contains("&lt;script&gt;") && !html.Contains("<script>"), "Heading was not escaped.");
    Assert(html.Contains("&lt;b&gt;"), "Body text was not escaped.");
});

await Check("Complete demo builds deterministically including extension output", async () =>
{
    var first = await DemoSite.BuildAsync(DemoSite.FixtureDirectory);
    var second = await DemoSite.BuildAsync(DemoSite.FixtureDirectory);
    Assert(first.Digest == second.Digest, "Identical inputs produce different artifacts.");
    Assert(first.Files.Count(f => f.Path.EndsWith("index.html", StringComparison.Ordinal)) == 5, "Missing page output.");
    Assert(first.Files.Any(f => f.Path == "assets/studio.svg") && first.Files.Any(f => f.Path == "site-index.json"), "Missing asset or contributor output.");
    Assert(first.Files.All(f => f.Digest == BuildPipeline.Hash(f.Bytes.AsSpan())), "File integrity mismatch.");
});

await Check("Contributors execute in order and their bytes change the final digest", async () =>
{
    var trace = new List<string>();
    var first = await new BuildPipeline(new StubRenderer(), contributors: [new TestContributor("first", "A", trace), new TestContributor("second", "B", trace)])
        .BuildAsync(new(snapshot, "design"));
    Assert(trace.SequenceEqual(new[] { "first", "second" }), "Contribution order changed.");
    var second = await new BuildPipeline(new StubRenderer(), contributors: [new TestContributor("first", "changed", []), new TestContributor("second", "B", [])])
        .BuildAsync(new(snapshot, "design"));
    Assert(first.Digest != second.Digest, "Contributed bytes are not covered by the digest.");
});

await Check("Extensions cannot skip validation or change finalized output", async () =>
{
    var renderer = new StubRenderer();
    var pipeline = new BuildPipeline(renderer);
    await Reject<ContentValidationException>(async () => await pipeline.BuildAsync(new(WithFirstSection(new CtaSection("cta", "Title", "Text", "Click", "javascript:x")), "design")));
    Assert(renderer.LastOutput is null, "Invalid content reached the renderer.");
    var artifact = await pipeline.BuildAsync(new(snapshot, "design"));
    await Reject<InvalidOperationException>(() => { renderer.LastOutput!.AddText("late.txt", "Late change"); return Task.CompletedTask; });
    Assert(!artifact.Files.Any(f => f.Path == "late.txt"), "Finalized output was modified.");
    await Reject<ContentValidationException>(async () => await new BuildPipeline(new StubRenderer(), validators: [new RejectingValidator()]).BuildAsync(new(snapshot, "design")));
});

await Check("Extension failures and missing output fail the build", async () =>
{
    await Reject<InvalidOperationException>(async () => await new BuildPipeline(new StubRenderer(), contributors: [new FailingContributor()]).BuildAsync(new(snapshot, "design")));
    await Reject<ContentValidationException>(async () => await new BuildPipeline(new EmptyRenderer()).BuildAsync(new(snapshot, "design")));
    await Reject<ArgumentException>(() => { _ = new BuildPipeline(new StubRenderer(), contributors: [new SiteIndexContributor(), new SiteIndexContributor()]); return Task.CompletedTask; });
});

await Check("Cancelled work does not start rendering", async () =>
{
    var renderer = new StubRenderer();
    await Reject<OperationCanceledException>(async () => await new BuildPipeline(renderer).BuildAsync(new(snapshot, "design"), new CancellationToken(true)));
    Assert(renderer.LastOutput is null, "Cancelled work reached the renderer.");
});

await CompositionChecks.RunAsync(Check);
Console.WriteLine($"All {passed} MVP checks passed.");

sealed class StubRenderer : IWebsiteRenderer
{
    public ArtifactBuilder? LastOutput { get; private set; }
    public ValueTask RenderAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default)
    {
        LastOutput = output;
        foreach (var page in inputs.Content.Website.Pages) output.AddText(BuildPipeline.PageFile(page.Path), "<h1>Page</h1>");
        foreach (var asset in inputs.Content.Website.Assets) output.Add(asset.File, [1, 2, 3]);
        return ValueTask.CompletedTask;
    }
}

sealed class EmptyRenderer : IWebsiteRenderer
{
    public ValueTask RenderAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

sealed class TestContributor(string id, string content, List<string> trace) : IArtifactContributor
{
    public string Id => id;
    public ValueTask ContributeAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default)
    {
        trace.Add(Id); output.AddText(Id + ".txt", content); return ValueTask.CompletedTask;
    }
}

sealed class FailingContributor : IArtifactContributor
{
    public string Id => "failing";
    public ValueTask ContributeAsync(BuildInputs inputs, ArtifactBuilder output, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Extension failed.");
}

sealed class RejectingValidator : IContentValidator
{
    public string Id => "deny";
    public ValueTask ValidateAsync(ContentSnapshot content, CancellationToken cancellationToken = default) => throw new ContentValidationException("Additional policy rejected this content.");
}
