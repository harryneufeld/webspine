using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Webspine.Content.Product;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Designs.Studio;
using Webspine.Rendering.Razor;

static class RecordPatternChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static JsonElement Fields(object value) => JsonSerializer.SerializeToElement(value, value.GetType(), CompositionJson.Options);
    private static Dictionary<string, string> Form(string html) => Regex.Matches(html, "<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"")
        .GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
    public static async Task RunAsync(bool browserReview = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-records-" + Guid.NewGuid().ToString("N"));
        using var browser = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new() });
        using var api = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        try
        {
            await using var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true);
            await host.WaitHealthyAsync(browser); await AccountChecks.BootstrapAsync(browser, host.Url);
            var form = Form(await browser.GetStringAsync(host.Url + "/manage")); form["title"] = "Product collection"; form["mode"] = "demo";
            using var setup = await browser.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(form)); Require(setup.StatusCode == HttpStatusCode.Found, "Record setup failed.");
            var writer = await AccountChecks.Issue(browser, host.Url, "Record writer", ["content:read", "content:write", "preview:build", "preview:read"]);
            var sharedWriter = await AccountChecks.Issue(browser, host.Url, "Record shared writer", ["content:read", "content:write", "content:shared:write", "preview:build", "preview:read"]);
            var reader = await AccountChecks.Issue(browser, host.Url, "Record reader", ["content:read"]);
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            async Task<CompositionSnapshot> Read() => JsonSerializer.Deserialize<CompositionSnapshot>(await api.GetStringAsync(host.Url + "/api/v2/site"), CompositionJson.Options)!;
            async Task<HttpResponseMessage> Change(CompositionSnapshot snapshot, CompositionEdit edit, ImmutableArray<string> affected = default) =>
                await api.PostAsync(host.Url + "/api/v2/changes", new StringContent(JsonSerializer.Serialize(new { expectedRevision = snapshot.Revision, change = edit,
                    acknowledgedPages = affected.IsDefault ? [] : affected }, CompositionJson.Options), Encoding.UTF8, "application/json"));
            var snapshot = await Read();
            using var created = await Change(snapshot, new CreateRecord("product", 1, Fields(new { name = "Oak chair" })));
            Require(created.IsSuccessStatusCode, "Product Record creation failed: " + await created.Content.ReadAsStringAsync());
            snapshot = await Read(); var a = snapshot.Website.Records.Single();
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition/records/new/product"));
            form["field.name"] = "Linen lamp"; form["field.description"] = "A softer light."; form["field.image"] = "studio-illustration";
            using var boardCreated = await browser.PostAsync(host.Url + "/manage/composition/records/new/product", new FormUrlEncodedContent(form));
            Require(boardCreated.StatusCode == HttpStatusCode.Found, "Product board create failed: " + await boardCreated.Content.ReadAsStringAsync());
            snapshot = await Read(); var b = snapshot.Website.Records.Single(r => r.Id != a.Id);
            foreach (var (page, record) in new[] { ("home", a), ("home", b), ("products", a) })
            {
                var count = snapshot.Website.Pages.Single(p => p.Id == page).Regions.Single(r => r.Id == "main").Placements.Length;
                using var card = await Change(snapshot, new CreateBlock(new(page, "main", null), count, "product-card", 1, Fields(new ProductCardFields(record.Id, "Made for everyday use"))));
                Require(card.IsSuccessStatusCode, "Pattern instance failed: " + await card.Content.ReadAsStringAsync()); snapshot = await Read();
            }
            var cards = snapshot.Website.Blocks.Where(block => block.TypeId == "product-card").ToArray();
            Require(cards.Length == 3 && snapshot.Website.Records.Length == 2, "Record reuse duplicated content.");
            using var discovery = JsonDocument.Parse(await api.GetStringAsync(host.Url + "/api/v2/schema"));
            Require(discovery.RootElement.GetProperty("recordSchemas")[0].GetProperty("descriptor").GetProperty("version").GetInt32() == 1 &&
                discovery.RootElement.GetProperty("types").EnumerateArray().Single(t => t.GetProperty("id").GetString() == "product-card").GetProperty("pattern").GetProperty("overrideSlots")[0].GetString() == "caption", "Schema omitted Record/Pattern versions or slots.");
            using var impact = JsonDocument.Parse(await api.GetStringAsync(host.Url + "/api/v2/records/" + a.Id + "/impact"));
            Require(impact.RootElement.GetProperty("affectedPages").EnumerateArray().Select(p => p.GetString()).SequenceEqual(new[] { "home", "products" }), "Record impact is incomplete.");
            Console.WriteLine("PASS: Product Records are presentation-independent; typed Pattern discovery and three independent cards reuse two Records across pages.");

            async Task Status(CompositionEdit edit, HttpStatusCode expected, ImmutableArray<string> ack = default)
            {
                var prior = await Read(); using var response = await Change(prior, edit, ack);
                Require(response.StatusCode == expected && (await Read()).Revision == prior.Revision, "Rejected Record/Pattern edit changed the draft: " + await response.Content.ReadAsStringAsync());
            }
            await Status(new UpdateRecord(a.Id, a.Revision, Fields(new { name = "Denied" })), HttpStatusCode.Forbidden, ["home", "products"]);
            api.DefaultRequestHeaders.Authorization = new("Bearer", sharedWriter);
            await Status(new UpdateRecord(a.Id, a.Revision, Fields(new { name = "Incomplete" })), HttpStatusCode.UnprocessableEntity, ["home"]);
            await Status(new UpdateRecord(a.Id, "old", Fields(new { name = "Stale" })), HttpStatusCode.Conflict, ["home", "products"]);
            await Status(new DeleteRecord(a.Id, a.Revision), HttpStatusCode.UnprocessableEntity);
            await Status(new CreateRecord("product", 1, Fields(new { name = "Unsafe", css = "body{}" })), HttpStatusCode.UnprocessableEntity);
            await Status(new CreateRecord("product", 2, Fields(new { name = "Wrong version" })), HttpStatusCode.NotImplemented);
            await Status(new UpdateBlock(cards[0].Id, Fields(new { productId = a.Id, caption = "Caption", css = "body{}" })), HttpStatusCode.UnprocessableEntity);
            await Status(new UpdateBlock(cards[0].Id, Fields(new ProductCardFields("missing"))), HttpStatusCode.UnprocessableEntity);
            await Status(new UpdateBlock(cards[0].Id, Fields(new { productId = 123, caption = "Wrong type" })), HttpStatusCode.UnprocessableEntity);
            api.DefaultRequestHeaders.Authorization = new("Bearer", reader);
            await Status(new CreateRecord("product", 1, Fields(new { name = "Denied" })), HttpStatusCode.Forbidden);
            api.DefaultRequestHeaders.Authorization = new("Bearer", sharedWriter);
            using var preview = await api.PostAsJsonAsync(host.Url + "/api/v2/previews", new { expectedRevision = snapshot.Revision });
            Require(preview.StatusCode == HttpStatusCode.Created, "Record preview failed: " + await preview.Content.ReadAsStringAsync());
            using var info = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()); var url = info.RootElement.GetProperty("url").GetString()!;
            var before = await api.GetByteArrayAsync(host.Url + url); var html = Encoding.UTF8.GetString(before);
            Require(html.Contains("Oak chair") && html.Contains("Linen lamp") && html.Contains("product-card"), "Products were not resolved from captured Records.");
            var secondPage = await api.GetStringAsync(host.Url + url + "products/"); Require(secondPage.Contains("Oak chair"), "Reused Product omitted on second page.");
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition/records/" + a.Id));
            form["field.name"] = "<script>Renamed oak chair</script>"; form["field.description"] = "Solid oak."; form["field.image"] = ""; form["acknowledge"] = "true";
            var entries = form.Where(p => p.Key != "affected").ToList(); entries.Add(KeyValuePair.Create("affected", "home")); entries.Add(KeyValuePair.Create("affected", "products"));
            using var boardEdit = await browser.PostAsync(host.Url + "/manage/composition/records/" + a.Id, new FormUrlEncodedContent(entries));
            Require(boardEdit.StatusCode == HttpStatusCode.Found, "Record board save failed: " + await boardEdit.Content.ReadAsStringAsync());
            using var staleBoard = await browser.PostAsync(host.Url + "/manage/composition/records/" + a.Id, new FormUrlEncodedContent(entries)); Require(staleBoard.StatusCode == HttpStatusCode.Conflict, "Record board accepted stale revision.");
            snapshot = await Read(); Require(snapshot.Website.Records.Single(r => r.Id == a.Id).Revision != a.Revision && snapshot.Website.Records.Single(r => r.Id == b.Id).Revision == b.Revision, "Record revisions are not independent.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            using var rebind = await Change(snapshot, new UpdateBlock(cards[0].Id, Fields(new ProductCardFields(b.Id)))); Require(rebind.IsSuccessStatusCode, "Independent Pattern rebinding failed.");
            snapshot = await Read(); Require(snapshot.Website.Blocks.Single(block => block.Id == cards[2].Id).Fields.GetProperty("productId").GetString() == a.Id, "Rebinding changed a different Pattern instance.");
            using var newPreview = await api.PostAsJsonAsync(host.Url + "/api/v2/previews", new { expectedRevision = snapshot.Revision });
            using var newInfo = JsonDocument.Parse(await newPreview.Content.ReadAsStringAsync());
            var newHtml = await api.GetStringAsync(host.Url + newInfo.RootElement.GetProperty("url").GetString()! + "products/");
            Require(newHtml.Contains("&lt;script&gt;Renamed oak chair&lt;/script&gt;") && !newHtml.Contains("<script>Renamed"), "Record markup was executed or not updated.");
            var after = await api.GetByteArrayAsync(host.Url + url);
            Require(before.SequenceEqual(after), "Later Record edit/rebinding changed retained bytes.");
            Console.WriteLine("PASS: Board/API enforce Record permissions, exact impact, both revisions, safe fields and protected references; rebinding/editing leave retained output unchanged.");

            var package = StudioPackage.Create(); var store = new SqliteContentSource(Path.Combine(directory, "webspine.db"), package.ContentTypes);
            var unsupported = new UnsupportedRecords(store);
            await Reject<SourceOperationNotSupportedException>(() => new CompositionEditor(unsupported, package.Design, package.ContentTypes)
                .ApplyAsync(snapshot.Revision, new CreateRecord("product", 1, Fields(new { name = "Unsupported" })), new(true, true, true), []));
            Require(!unsupported.CommitCalled && (await store.ReadCompositionAsync()).Revision == snapshot.Revision, "Unsupported adapter wrote through a fallback.");
            var captured = await store.CaptureCompositionAsync(package.Design); var frozen = await package.CaptureAsync();
            Require(frozen.RecordSchemas.Single().Descriptor.Version == 1 && frozen.ContentTypes.Single(d => d.Descriptor.Id == "product-card").Pattern?.Version == 1, "Frozen schema/Pattern provenance omitted.");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(directory, "build-inputs", info.RootElement.GetProperty("id").GetString() + ".zip")))
            {
                using var contentReader = new StreamReader(zip.GetEntry("content.json")!.Open());
                var retainedContent = CompositionJson.Read(await contentReader.ReadToEndAsync());
                Require(retainedContent.Website.Records.Single(r => r.Id == a.Id).Revision == a.Revision &&
                    retainedContent.Website.Records.Single(r => r.Id == a.Id).Fields.GetProperty("name").GetString() == "Oak chair" && zip.GetEntry("assets/studio.svg") is not null,
                    "Private frozen inputs lost original Record values/revisions or media.");
                using var designReader = new StreamReader(zip.GetEntry("design.json")!.Open()); using var retainedDesign = JsonDocument.Parse(await designReader.ReadToEndAsync());
                Require(retainedDesign.RootElement.GetProperty("recordSchemas")[0].GetProperty("descriptor").GetProperty("version").GetInt32() == 1, "Private capture lost schema provenance.");
            }
            var missing = captured with { Content = captured.Content with { Website = captured.Content.Website with { Records = captured.Content.Website.Records.RemoveAt(0) } } };
            await Reject<ContentValidationException>(async () => await package.BuildAsync(missing, frozen));
            await Reject<ContentValidationException>(async () => await store.CommitCompositionAsync(snapshot.Revision, missing.Content.Website, package.Design));
            await Reject<ContentValidationException>(async () => await package.BuildAsync(captured with { AssetFiles = ImmutableDictionary<string, ImmutableArray<byte>>.Empty }, frozen));
            var unchanged = snapshot.Website.Records.Single(r => r.Id == b.Id);
            await Reject<ContentValidationException>(async () => await store.CommitCompositionAsync(snapshot.Revision, snapshot.Website with
                { Records = snapshot.Website.Records.Replace(unchanged, unchanged with { Fields = Fields(new { name = "Changed without revision" }) }) }, package.Design));
            using var unused = await Change(snapshot, new CreateRecord("product", 1, Fields(new { name = "Unused product" }))); Require(unused.IsSuccessStatusCode, "Unused Record creation failed.");
            snapshot = await Read(); var unusedRecord = snapshot.Website.Records[^1];
            using var deleted = await Change(snapshot, new DeleteRecord(unusedRecord.Id, unusedRecord.Revision)); Require(deleted.IsSuccessStatusCode, "Unused Record could not be deleted.");
            snapshot = await Read();
            // Simulate an external/native edit that removed required data; capture must revalidate independently of API edits.
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "webspine.db"), Pooling = false }.ToString()))
            {
                await connection.OpenAsync(); using var command = connection.CreateCommand();
                command.CommandText = "UPDATE revisions SET snapshot=$json WHERE revision=$revision";
                command.Parameters.AddWithValue("$revision", snapshot.Revision);
                var original = JsonSerializer.Serialize(snapshot, CompositionJson.Options);
                command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(snapshot with { Website = snapshot.Website with { Records = snapshot.Website.Records.RemoveAt(0) } }, CompositionJson.Options));
                await command.ExecuteNonQueryAsync();
                try { await Reject<ContentValidationException>(async () => await store.CaptureCompositionAsync(package.Design)); }
                finally { command.Parameters["$json"].Value = original; await command.ExecuteNonQueryAsync(); }
            }
            // Capture values/revisions are in the content snapshot and exact media bytes in the immutable capture.
            Require(captured.Content.Website.Records.All(r => r.Revision.Length > 0) && captured.AssetFiles.ContainsKey("assets/studio.svg"), "Capture omitted Record revisions/media.");
            var compatible = new RazorDesignPackage(package.Descriptor with { Version = "1.1" }, package.ContentTypes, package.Design with { Revision = "1.1" },
                package.DocumentComponent, package.Bindings.Values, package.Assets.SetItem("assets/composition.css", Encoding.UTF8.GetBytes("/* compatible */\n" + package.Design.Stylesheet).ToImmutableArray()));
            var updatedArtifact = await compatible.BuildAsync(captured with { Design = compatible.Design }, await compatible.CaptureAsync());
            var afterDesign = await api.GetByteArrayAsync(host.Url + url);
            Require(updatedArtifact.DesignRevision != frozen.Digest && before.SequenceEqual(afterDesign), "Compatible package selection altered retained preview.");
            Console.WriteLine("PASS: Capture/build reject missing required Records/media; optional defaults resolve; frozen schema, Pattern, values and revisions survive compatible new-package candidates.");
            await MigrationAsync(directory, captured, package);
            if (browserReview)
            {
                Console.WriteLine("Records browser fixture: " + host.Url + "/manage/composition/records"); Console.WriteLine("Owned fixture process: " + host.Process.Id);
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stopped.TrySetResult(); }; Console.CancelKeyPress += cancel;
                try { await stopped.Task; } finally { Console.CancelKeyPress -= cancel; }
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed record ProductV2(string Name, string? Description, string? Image, string Summary);
    private sealed class UnsupportedRecords(ICompositionDraftPersistence inner) : ICompositionDraftPersistence
    {
        public bool CommitCalled { get; private set; }
        public SourceIdentity Identity => inner.Identity;
        public CompositionCapabilities CompositionCapabilities => inner.CompositionCapabilities with { RecordSchemas = [] };
        public ValueTask<CompositionSnapshot> ReadCompositionAsync(CancellationToken ct = default) => inner.ReadCompositionAsync(ct);
        public ValueTask<CapturedComposition> CaptureCompositionAsync(CompositionDesign design, CancellationToken ct = default) => inner.CaptureCompositionAsync(design, ct);
        public Task<CompositionSnapshot> CreateCompositionAsync(CompositionWebsite site, CompositionDesign design, ImmutableDictionary<string, ImmutableArray<byte>> assets, CancellationToken ct = default)
            => throw new SourceOperationNotSupportedException("Unsupported Records.");
        public Task<CompositionSnapshot> CommitCompositionAsync(string revision, CompositionWebsite site, CompositionDesign design, ImmutableDictionary<string, ImmutableArray<byte>>? assets = null, CancellationToken cancellationToken = default)
        { CommitCalled = true; throw new Exception("Unsupported adapter must not write."); }
    }
    private sealed record CardV2(string ProductId, string? Caption, string Heading);
    private static async Task MigrationAsync(string directory, CapturedComposition captured, RazorDesignPackage old)
    {
        var schema = new RecordDefinition<ProductV2>(new("product", 2, "webspine-product-content", "2"),
            new("Product", "New explicit schema", [EditorField.Text("name", "Name", "Product", 160), EditorField.Text("description", "Description", "", 3000, true) with { Required = false },
                EditorField.Image("image", "Image", false), EditorField.Text("summary", "Summary", "Product summary", 160)], "name"),
            (fields, context) => { CompositionRules.Text(fields.Summary, 160); if (fields.Image is not null) context.Asset(fields.Image); });
        var card = new BlockDefinition<CardV2>(old.ContentTypes.Resolve("product-card", 1).Descriptor with { Version = 2, SchemaId = "product-card-v2" }, (_, _) => { },
            new("Product card", "New explicit input contract", [EditorField.Record("productId", "Product", "product", 2), EditorField.Text("caption", "Caption", "", 160) with { Required = false },
                EditorField.Text("heading", "Heading", "Product", 160)]), new("product-card", 2, [new("productId", "product", 2)], ["caption", "heading"]));
        var registry = new BlockRegistry(old.ContentTypes.Descriptors.Where(d => d.Id != "product-card").Select(d => old.ContentTypes.Resolve(d.Id, d.Version)).Append(card), new([schema]));
        var target = old.Design with { Revision = "2" };
        var path = Path.Combine(directory, "migration.db"); var initial = new SqliteContentSource(path, old.ContentTypes); await initial.InitializeSchemaAsync();
        var libraryRecord = new ContentRecord("library-product", "product", 1, "library-revision", Fields(new { name = "Library product" }));
        var site = captured.Content.Website with { Records = captured.Content.Website.Records.Add(libraryRecord),
            Blocks = captured.Content.Website.Blocks.Add(new("library-card", new(OwnerKind.Shared, "library-pattern"), "product-card", 1, Fields(new ProductCardFields(libraryRecord.Id)), [])),
            SharedBlocks = captured.Content.Website.SharedBlocks.Add(new("library-pattern", "library-card")) };
        var current = await initial.CreateCompositionAsync(site, old.Design, captured.AssetFiles);
        var editor = new CompositionEditor(initial, old.Design, old.ContentTypes);
        await Reject<CompositionPermissionException>(() => editor.ApplyAsync(current.Revision, new UpdateRecord(libraryRecord.Id, libraryRecord.Revision, Fields(new { name = "Denied" })), new(true, false, false), []));
        await Reject<ContentValidationException>(() => editor.ApplyAsync(current.Revision, new DeleteRecord(libraryRecord.Id, libraryRecord.Revision), new(true, true, false), []));
        var source = new SqliteContentSource(path, registry);
        var plan = new CompositionUpgradePlan("product-v2", "1", "2", [], []); var authority = new CompositionAuthority(true, true, true);
        await Reject<ContentValidationException>(async () => await source.CaptureCompositionAsync(target));
        await Reject<CompositionPermissionException>(() => CompositionUpgrade.ApplyAsync(source, current.Revision, old.Design, old.ContentTypes, target, registry, plan, new(true, false, true)));
        await Reject<CompositionRevisionException>(() => CompositionUpgrade.ApplyAsync(source, "stale", old.Design, old.ContentTypes, target, registry, plan, authority));
        await Reject<ContentValidationException>(() => CompositionUpgrade.ApplyAsync(source, current.Revision, old.Design, old.ContentTypes, target, registry, plan, authority));
        Require((await source.ReadCompositionAsync()).Revision == current.Revision, "Failed migration changed source.");
        plan = plan with
        {
            RecordSchemas = [new("product", 1, 2, fields => { var p = fields.Deserialize<ProductFields>(CompositionJson.Options)!; return Fields(new ProductV2(p.Name, p.Description, p.Image, "Migrated explicitly")); })],
            Patterns = [new("product-card", 1, 2, fields => { var p = fields.Deserialize<ProductCardFields>(CompositionJson.Options)!; return Fields(new CardV2(p.ProductId, p.Caption, "Our product")); })]
        };
        var saved = await CompositionUpgrade.ApplyAsync(source, current.Revision, old.Design, old.ContentTypes, target, registry, plan, authority);
        Require(saved.Website.Records.Select(r => r.Id).SequenceEqual(current.Website.Records.Select(r => r.Id)) && saved.Website.Records.All(r => r.SchemaVersion == 2) &&
            saved.Website.Blocks.Where(b => b.TypeId == "product-card").All(b => b.TypeVersion == 2) && (await source.ReadCompositionRevisionAsync(current.Revision))!.Website.Records.All(r => r.SchemaVersion == 1), "Explicit migration lost identities/versions/history.");
        _ = await new SqliteContentSource(path, registry).CaptureCompositionAsync(target);
        await Reject<ContentValidationException>(() => Task.Run(() => new BlockRegistry([card], old.ContentTypes.Records)));
        var existingCard = old.ContentTypes.Resolve("product-card", 1);
        await Reject<ContentValidationException>(() => Task.Run(() => new BlockRegistry(
            [new BlockDefinition<ProductCardFields>(existingCard.Descriptor, (_, _) => { }, existingCard.Editor)], old.ContentTypes.Records)));
        Console.WriteLine("PASS: Breaking Record/Pattern versions fail safely until an authorized, conditional, explicit migration validates and commits atomically; identities/history persist after reopening.");
    }
}
