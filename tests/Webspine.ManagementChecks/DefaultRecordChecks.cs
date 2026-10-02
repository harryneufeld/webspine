using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Webspine.Content.Sqlite;
using Webspine.Core.Composition;
using Webspine.Designs.Studio;

static class DefaultRecordChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static JsonElement Fields(object value) => JsonSerializer.SerializeToElement(value, CompositionJson.Options);
    private static Dictionary<string, string> Form(string html) => Regex.Matches(html, "<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"")
        .GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));

    public static async Task RunAsync(bool browserReview = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-neutral-records-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true);
            using var browser = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new() });
            using var api = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            await host.WaitHealthyAsync(browser); await AccountChecks.BootstrapAsync(browser, host.Url);
            var form = Form(await browser.GetStringAsync(host.Url + "/manage")); form["title"] = "Community website"; form["mode"] = "blank";
            using var setup = await browser.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(form)); Require(setup.StatusCode == HttpStatusCode.Found, "Neutral Record setup failed.");
            var token = await AccountChecks.Issue(browser, host.Url, "Neutral Record writer", ["content:read", "content:write", "content:shared:write", "preview:build", "preview:read"]);
            api.DefaultRequestHeaders.Authorization = new("Bearer", token);
            async Task<CompositionSnapshot> Read() => JsonSerializer.Deserialize<CompositionSnapshot>(await api.GetStringAsync(host.Url + "/api/v2/site"), CompositionJson.Options)!;
            async Task<HttpResponseMessage> Change(CompositionSnapshot snapshot, CompositionEdit edit, ImmutableArray<string> affected = default) =>
                await api.PostAsync(host.Url + "/api/v2/changes", new StringContent(JsonSerializer.Serialize(new { expectedRevision = snapshot.Revision, change = edit,
                    acknowledgedPages = affected.IsDefault ? [] : affected }, CompositionJson.Options), Encoding.UTF8, "application/json"));

            var chooser = await browser.GetStringAsync(host.Url + "/manage/composition/records/new");
            Require(chooser.Contains("Content entry") && !chooser.Contains("Product"), "Default Record chooser exposed Product example.");
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition/records/new/content-entry"));
            form["field.title"] = "Repair workshop"; form["field.description"] = "Bring something you would like to repair.";
            using var created = await browser.PostAsync(host.Url + "/manage/composition/records/new/content-entry", new FormUrlEncodedContent(form));
            Require(created.StatusCode == HttpStatusCode.Found, "Neutral Record human form failed: " + await created.Content.ReadAsStringAsync());
            var snapshot = await Read(); var entry = snapshot.Website.Records.Single(); var page = snapshot.Website.Pages[0];
            var location = new CompositionLocation(page.Id, "main", null);
            var addUrl = host.Url + "/manage/composition/add?page=" + page.Id + "&location=" + Uri.EscapeDataString("page:" + page.Id + ":main");
            var elements = await browser.GetStringAsync(addUrl);
            Require(elements.Contains("Record card") && !elements.Contains("Product card"), "Default element chooser exposed Product card.");
            using var card = await Change(snapshot, new CreateBlock(location, 0, "record-card", 1, Fields(new RecordCardFields(entry.Id, "Everyone welcome"))));
            Require(card.IsSuccessStatusCode, "Neutral Record card failed: " + await card.Content.ReadAsStringAsync());
            snapshot = await Read();
            using var deniedRecord = await Change(snapshot, new CreateRecord("product", 1, Fields(new { name = "Not a default type" })));
            using var deniedCard = await Change(snapshot, new CreateBlock(location, 0, "product-card", 1, Fields(new ProductCardFields(entry.Id))));
            Require(deniedRecord.StatusCode == HttpStatusCode.UnprocessableEntity && deniedCard.StatusCode == HttpStatusCode.UnprocessableEntity &&
                (await Read()).Revision == snapshot.Revision, "Retired creation was bypassed through typed API or changed head.");
            using var discovery = JsonDocument.Parse(await api.GetStringAsync(host.Url + "/api/v2/schema"));
            var product = discovery.RootElement.GetProperty("recordSchemas").EnumerateArray().Single(s => s.GetProperty("descriptor").GetProperty("id").GetString() == "product");
            Require(!product.GetProperty("canCreate").GetBoolean() && product.GetProperty("canUpdate").GetBoolean(), "Compatibility schema discovery does not distinguish create/update.");

            // Seed an existing Product draft to prove the new default preserves old records, bindings and editing.
            var package = StudioPackage.Create(); var store = new SqliteContentSource(Path.Combine(directory, "webspine.db"), package.ContentTypes);
            var legacy = new ContentRecord("existing-product", "product", 1, "existing-revision", Fields(new { name = "Existing content" }));
            var oldCard = new Block("existing-card", new(OwnerKind.Page, page.Id), "product-card", 1, Fields(new ProductCardFields(legacy.Id)), []);
            var regions = page.Regions.Select(r => r.Id == "main" ? r with { Placements = snapshot.Website.Pages[0].Regions.Single(a => a.Id == "main").Placements.Add(new("existing-placement", TargetKind.Block, oldCard.Id)) } : r).ToImmutableArray();
            var originalRevision = snapshot.Revision;
            await store.CommitCompositionAsync(snapshot.Revision, snapshot.Website with { Records = snapshot.Website.Records.Add(legacy), Blocks = snapshot.Website.Blocks.Add(oldCard),
                Pages = snapshot.Website.Pages.SetItem(0, snapshot.Website.Pages[0] with { Regions = regions }) }, package.Design);
            snapshot = await Read();
            using var saved = await Change(snapshot, new UpdateRecord(legacy.Id, legacy.Revision, Fields(new { name = "Preserved content" })), [page.Id]);
            Require(saved.IsSuccessStatusCode, "Existing Product lost editability.");
            snapshot = await Read();
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition"));
            using var preview = await browser.PostAsync(host.Url + "/manage/composition/preview", new FormUrlEncodedContent(form));
            Require(preview.StatusCode == HttpStatusCode.Found, "Neutral/compatibility preview failed.");
            var html = await browser.GetStringAsync(host.Url + preview.Headers.Location);
            Require(html.Contains("Repair workshop") && html.Contains("Everyone welcome") && html.Contains("Preserved content"), "Neutral and retained Product bindings did not render together.");
            Require((await store.ReadCompositionRevisionAsync(originalRevision))!.Website.Records.Single().Id == entry.Id, "Correction rewrote retained history.");
            Console.WriteLine("PASS: Default board uses neutral Records/card and registered type selection; Product creation is opt-in with server enforcement; existing Product data remains editable and renderable.");
            if (browserReview)
            {
                Console.WriteLine("Default Records browser fixture: " + host.Url + "/manage/composition/records"); Console.WriteLine("Owned fixture process: " + host.Process.Id);
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stopped.TrySetResult(); }; Console.CancelKeyPress += cancel;
                try { await stopped.Task; } finally { Console.CancelKeyPress -= cancel; }
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
