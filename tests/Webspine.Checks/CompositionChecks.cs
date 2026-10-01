using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Webspine.Core;
using Webspine.Core.Composition;

static class CompositionChecks
{
    internal static JsonElement Fields<T>(T value) => JsonSerializer.SerializeToElement(value, CompositionJson.Options);
    private static Block Text(string id, BlockOwner owner, string heading = "Heading") =>
        new(id, owner, "text", 1, Fields(new TextFields(heading, "Body")), []);
    private static Block Group(string id, BlockOwner owner, params Placement[] children) =>
        new(id, owner, "group", 1, Fields(new GroupFields("stack", "start", "medium", 1)), children.ToImmutableArray());
    private static readonly BlockOwner Home = new(OwnerKind.Page, "home");
    private static readonly BlockOwner Branding = new(OwnerKind.Shared, "branding");
    internal static CapturedComposition Example()
    {
        ImmutableArray<string> types = ["text", "image", "cta", "cards", "group"];
        var design = new CompositionDesign("sample-design", "design-r1", new("standard",
            [new("header", types, 0, 10), new("main", types, 1, 10), new("footer", types, 0, 10)]),
            new(types, ["stack", "row", "grid"], ["start", "center"], ["small", "medium"], 4));
        var page = new CompositionPage("home", "/", "Home", "Description",
            [new("header", [new("header-placement", TargetKind.Shared, "branding")]),
             new("main", [new("main-placement", TargetKind.Block, "body-group")]),
             new("footer", [new("footer-placement", TargetKind.Shared, "branding")])]);
        var site = new CompositionWebsite("sample", "Website", "en", "standard", [page],
            [Text("branding-text", Branding, "Shared branding"),
             Group("body-group", Home, new Placement("intro-placement", TargetKind.Block, "intro")), Text("intro", Home)],
            [new("branding", "branding-text")], []);
        return new(new(2, new("sample-source", "fixture"), "r1", site), design,
            ImmutableDictionary<string, ImmutableArray<byte>>.Empty);
    }
    private static CapturedComposition Site(CapturedComposition capture, CompositionWebsite site) =>
        capture with { Content = capture.Content with { Website = site } };
    private static CapturedComposition Blocks(CapturedComposition capture, ImmutableArray<Block> blocks) => Site(capture, capture.Content.Website with { Blocks = blocks });
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (ContentValidationException) { return; }
        throw new Exception("Expected composition validation failure.");
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        var registry = new BlockRegistry(StandardBlocks.Registrations);
        var pipeline = new CompositionBuildPipeline(registry);
        var example = Example();
        Task Test(Action action) { action(); return Task.CompletedTask; }
        await check("Published composition/design examples build through the registered contract", async () =>
        {
            var content = CompositionJson.Read(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "composition-v2.json")));
            var design = JsonSerializer.Deserialize<CompositionDesign>(await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "composition-design-v2.json")), CompositionJson.Options)!;
            var built = pipeline.Build(new(content, design, ImmutableDictionary<string, ImmutableArray<byte>>.Empty));
            Assert(built.Files.Any(f => f.Path == "assets/composition.css" && f.Bytes.Length > 0), "Design stylesheet was not captured.");
        });
        await check("v2 round trips shared Regions and nested Blocks without changing v1", () => Test(() =>
        {
            var serialized = JsonSerializer.Serialize(example.Content, CompositionJson.Options);
            var restored = example with { Content = CompositionJson.Read(serialized) };
            CompositionContract.Validate(restored.Content, restored.Design, registry);
            var first = pipeline.Build(example); var second = pipeline.Build(restored);
            Assert(first.Digest == second.Digest && first.ContractVersion == 2 && ContentContract.Version == 1, "Contract compatibility/determinism failed.");
            var html = Encoding.UTF8.GetString(first.Files.Single(f => f.Path == "index.html").Bytes.AsSpan());
            Assert(html.Split("Shared branding").Length == 3, "Shared references did not expand twice.");
            Assert(first.Files.Any(f => f.Path == "composition-manifest.json"), "No provenance manifest.");
        }));
        await check("Composition rejects ambiguous JSON and unapproved fields", () => Test(() =>
        {
            Reject(() => CompositionJson.Read("{\"contractVersion\":2,\"contractVersion\":2}"));
            var block = example.Content.Website.Blocks[2];
            foreach (var fields in new[] { "{\"heading\":\"A\",\"text\":\"B\",\"css\":\"body{}\"}",
                "{\"heading\":\"A\"}", "{\"heading\":\"A\",\"heading\":\"B\",\"text\":\"C\"}",
                "{\"Heading\":\"A\",\"text\":\"B\"}" })
            {
                var changed = block with { Fields = JsonDocument.Parse(fields).RootElement.Clone() };
                Reject(() => pipeline.Build(Blocks(example, example.Content.Website.Blocks.SetItem(2, changed))));
            }
        }));
        await check("Missing references, duplicate placements and ownership violations fail before rendering", () => Test(() =>
        {
            var blocks = example.Content.Website.Blocks;
            Reject(() => pipeline.Build(Blocks(example, blocks.SetItem(2, blocks[2] with { Owner = Branding }))));
            Reject(() => pipeline.Build(Blocks(example, blocks.SetItem(1, blocks[1] with { Children = [new("x", TargetKind.Block, "absent")] }))));
            Reject(() => pipeline.Build(Blocks(example, blocks.SetItem(1, blocks[1] with { Children = [new("header-placement", TargetKind.Block, "intro")] }))));
            Reject(() => pipeline.Build(Blocks(example, blocks.Add(Text("orphan", Home)))));
            Reject(() => pipeline.Build(Blocks(example, blocks.SetItem(1, blocks[1] with { Children = [new("x", TargetKind.Block, "intro"), new("y", TargetKind.Block, "intro")] }))));
            Reject(() => pipeline.Build(Site(example, example.Content.Website with { SharedBlocks = [new("branding", "absent")] })));
            Reject(() => pipeline.Build(Blocks(example, blocks.SetItem(2, blocks[2] with { Children = [new("x", TargetKind.Shared, "branding")] }))));
        }));
        await check("Cycles through shared Groups and disconnected owned Groups are rejected", () => Test(() =>
        {
            Reject(() => pipeline.Build(Blocks(example, example.Content.Website.Blocks.SetItem(0,
                Group("branding-text", Branding, new Placement("cycle", TargetKind.Shared, "branding"))))));
            var extra = example.Content.Website.Blocks.Add(Group("cycle-a", Home, new Placement("cycle-b-place", TargetKind.Block, "cycle-b")))
                .Add(Group("cycle-b", Home, new Placement("cycle-a-place", TargetKind.Block, "cycle-a")));
            Reject(() => pipeline.Build(Blocks(example, extra)));
        }));
        await check("Eight Group levels pass and nine fail across shared references", () => Test(() =>
        {
            CapturedComposition Chain(int length)
            {
                var blocks = example.Content.Website.Blocks.RemoveAt(1);
                for (var index = 0; index < length; index++)
                    blocks = blocks.Add(Group(index == 0 ? "body-group" : "g-" + index, Home,
                        new Placement("p-" + index, TargetKind.Block, index == length - 1 ? "intro" : "g-" + (index + 1))));
                return Blocks(example, blocks);
            }
            pipeline.Build(Chain(8)); Reject(() => pipeline.Build(Chain(9)));
            CapturedComposition SharedChain(int length)
            {
                var deeper = Chain(length);
                var intro = deeper.Content.Website.Blocks.Single(b => b.Id == "intro");
                return Blocks(deeper, deeper.Content.Website.Blocks.SetItem(0, Group("branding-text", Branding)).Replace(intro,
                    Group("intro", Home, new Placement("shared-depth", TargetKind.Shared, "branding"))));
            }
            // Six local Groups + the intro Group + a shared Group = eight; seven locals = nine.
            pipeline.Build(SharedChain(6)); Reject(() => pipeline.Build(SharedChain(7)));
        }));
        await check("Expanded count includes repeated shared references at the 1,000 boundary", () => Test(() =>
        {
            CapturedComposition Many(int references)
            {
                var blocks = example.Content.Website.Blocks.RemoveAt(2);
                blocks = blocks.SetItem(1, Group("body-group", Home, Enumerable.Range(0, references)
                    .Select(i => new Placement("repeat-" + i, TargetKind.Shared, "branding")).ToArray()));
                return Blocks(example, blocks);
            }
            pipeline.Build(Many(997)); Reject(() => pipeline.Build(Many(998)));
        }));
        await check("Region/type constraints, payload versions and Group choices are mandatory", () => Test(() =>
        {
            var design = example.Design with { Layout = example.Design.Layout with
                { Regions = example.Design.Layout.Regions.SetItem(1, new("main", ["text"], 1, 10)) } };
            Reject(() => pipeline.Build(example with { Design = design }));
            var blocks = example.Content.Website.Blocks;
            Reject(() => pipeline.Build(Blocks(example, blocks.SetItem(2, blocks[2] with { TypeVersion = 99 }))));
            Reject(() => pipeline.Build(Blocks(example, blocks.SetItem(1, blocks[1] with { Fields = Fields(new GroupFields("arbitrary", "start", "medium", 1)) }))));
            Reject(() => pipeline.Build(Site(example, example.Content.Website with { LayoutId = "missing" })));
            var page = example.Content.Website.Pages[0];
            Reject(() => pipeline.Build(Site(example, example.Content.Website with { Pages = [page with { Regions = page.Regions.RemoveAt(0) }] })));
        }));
        await check("A registered custom Block renders without engine type switches", () => Test(() =>
        {
            var custom = new BlockRegistration<QuoteFields>(new("quote", 1, 2, "example-quotes", "1", "quote-fields-v1", "1", false),
                (value, _) => CompositionRules.Text(value.Quote, 1000),
                (value, _, html) => { html.Markup("<blockquote>"); html.Text(value.Quote); html.Markup("</blockquote>"); });
            var customRegistry = new BlockRegistry(StandardBlocks.Registrations.Add(custom));
            var design = example.Design with { Groups = example.Design.Groups with { AllowedTypes = example.Design.Groups.AllowedTypes.Add("quote") } };
            var block = example.Content.Website.Blocks[2] with { TypeId = "quote", Fields = Fields(new QuoteFields("<script>unsafe</script>")) };
            var changed = Blocks(example with { Design = design }, example.Content.Website.Blocks.SetItem(2, block));
            var built = new CompositionBuildPipeline(customRegistry).Build(changed);
            var html = Encoding.UTF8.GetString(built.Files.Single(f => f.Path == "index.html").Bytes.AsSpan());
            Assert(html.Contains("<blockquote>&lt;script&gt;") && !html.Contains("<script>"), "Custom content escaping failed.");
            Assert(Encoding.UTF8.GetString(built.Files.Single(f => f.Path == "composition-manifest.json").Bytes.AsSpan()).Contains("example-quotes"), "Custom module not captured.");
            Reject(() => pipeline.Build(changed));
        }));
        await check("Duplicate/incompatible registrations fail and ordering is deterministic", () => Test(() =>
        {
            Reject(() => new BlockRegistry(StandardBlocks.Registrations.Add(StandardBlocks.Registrations[0])));
            var incompatible = new BlockRegistration<TextFields>(new("bad", 1, 1, "example", "1", "fields", "1", false), (_, _) => { }, (_, _, _) => { });
            Reject(() => new BlockRegistry(StandardBlocks.Registrations.Add(incompatible)));
            var conflict = new BlockRegistration<TextFields>(new("other", 1, 2, "webspine-standard", "2", "fields", "1", false), (_, _) => { }, (_, _, _) => { });
            Reject(() => new BlockRegistry(StandardBlocks.Registrations.Add(conflict)));
            Assert(pipeline.Build(example).Digest == new CompositionBuildPipeline(new BlockRegistry(StandardBlocks.Registrations.Reverse())).Build(example).Digest,
                "Registration enumeration affects output identity.");
        }));
        await check("Captured provenance and immutable output cover design/content/assets", () => Test(() =>
        {
            var first = pipeline.Build(example);
            var changed = Blocks(example, example.Content.Website.Blocks.SetItem(0, Text("branding-text", Branding, "New branding")));
            Assert(first.Digest != pipeline.Build(changed).Digest, "Shared edits absent from digest.");
            Assert(first.Digest != pipeline.Build(example with { Design = example.Design with { Revision = "design-r2" } }).Digest, "Design absent from digest.");
            Assert(first.Digest != pipeline.Build(example with { Design = example.Design with { Stylesheet = "main { max-width: 70rem; }" } }).Digest, "Design stylesheet absent from digest.");
            Assert(!Encoding.UTF8.GetString(first.Files.Single(f => f.Path == "index.html").Bytes.AsSpan()).Contains("New branding"), "Retained bytes changed.");
            var image = example.Content.Website.Blocks[2] with { TypeId = "image", Fields = Fields(new ImageFields("photo", "A \"photo\"")) };
            var withImage = Site(Blocks(example, example.Content.Website.Blocks.SetItem(2, image)),
                example.Content.Website with { Blocks = example.Content.Website.Blocks.SetItem(2, image), Assets = [new("photo", "assets/photo.png", "image/png")] });
            Reject(() => pipeline.Build(withImage));
            var valid = withImage with { AssetFiles = withImage.AssetFiles.Add("assets/photo.png", [1, 2, 3]) };
            var artifact = pipeline.Build(valid);
            Assert(artifact.Digest != pipeline.Build(valid with { AssetFiles = valid.AssetFiles.SetItem("assets/photo.png", [4]) }).Digest, "Asset bytes absent from digest.");
            Assert(Encoding.UTF8.GetString(artifact.Files.Single(f => f.Path == "index.html").Bytes.AsSpan()).Contains("&quot;photo&quot;"), "Attribute not escaped.");
            Reject(() => pipeline.Build(valid with { AssetFiles = valid.AssetFiles.Add("../secret", [1]) }));
        }));
        await check("Unsafe links and paths cannot enter a v2 artifact", () => Test(() =>
        {
            var blocks = example.Content.Website.Blocks;
            Reject(() => pipeline.Build(Blocks(example, blocks.SetItem(2, blocks[2] with
                { TypeId = "cta", Fields = Fields(new CtaFields("Heading", "Body", "Click", "javascript:alert(1)")) }))));
            Reject(() => pipeline.Build(Site(example, example.Content.Website with { Assets = [new("image", "../secret", "image/png")] })));
        }));
        await check("Adapter capabilities reject unsupported operations/capture without fallback", async () =>
        {
            var source = new FixtureCompositionSource(example);
            var artifact = await pipeline.BuildAsync(source, example.Design);
            Assert(artifact.ContractVersion == 2 && source.Captures == 1, "Alternate source did not build.");
            void Unsupported(Action action)
            {
                try { action(); } catch (SourceOperationNotSupportedException) { return; }
                throw new Exception("Expected capability rejection.");
            }
            Unsupported(() => source.CompositionCapabilities.Require(CompositionOperation.Share));
            Unsupported(() => source.CompositionCapabilities.Require(CompositionOperation.Read, "quote"));
            Unsupported(() => (source.CompositionCapabilities with { Operations = [CompositionOperation.Read, CompositionOperation.Update], AtomicConditionalWrites = false })
                .Require(CompositionOperation.Update));
            source.CompositionCapabilities = source.CompositionCapabilities with { ConsistentCapture = false };
            try { await pipeline.BuildAsync(source, example.Design); throw new Exception("Expected capture rejection."); }
            catch (SourceOperationNotSupportedException) { }
            Assert(source.Captures == 1, "Unsupported capture reached provider.");
        });
        await check("Source/design substitutions and cancellation cannot produce a candidate", async () =>
        {
            var source = new FixtureCompositionSource(example) { Capture = example with { Design = example.Design with { Revision = "changed" } } };
            try { await pipeline.BuildAsync(source, example.Design); throw new Exception("Expected design rejection."); }
            catch (ContentValidationException) { }
            source.Capture = example with { Content = example.Content with { Source = new("another", "fixture") } };
            try { await pipeline.BuildAsync(source, example.Design); throw new Exception("Expected source rejection."); }
            catch (ContentValidationException) { }
            var cancelled = new FixtureCompositionSource(example);
            try { await pipeline.BuildAsync(cancelled, example.Design, new CancellationToken(true)); throw new Exception("Expected cancellation."); }
            catch (OperationCanceledException) { }
            Assert(cancelled.Captures == 0, "Cancelled work reached source.");
        });
    }
}

sealed record QuoteFields([property: JsonRequired] string Quote);
sealed class FixtureCompositionSource(CapturedComposition captured) : ICompositionSource
{
    public SourceIdentity Identity { get; } = captured.Content.Source;
    public CompositionCapabilities CompositionCapabilities { get; set; } = new(2,
        [new("text", 1), new("group", 1)], [CompositionOperation.Read], false, true);
    public CapturedComposition Capture { get; set; } = captured;
    public int Captures { get; private set; }
    public ValueTask<CompositionSnapshot> ReadCompositionAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Capture.Content);
    public ValueTask<CapturedComposition> CaptureCompositionAsync(CompositionDesign design, CancellationToken cancellationToken = default)
    {
        Captures++;
        return ValueTask.FromResult(Capture);
    }
}
