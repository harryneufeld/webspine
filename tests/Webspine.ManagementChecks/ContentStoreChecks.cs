using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Demo;
using Webspine.Examples;

static class ContentStoreChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Dictionary<string, string> Form(string html) => Regex.Matches(html, "<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"").GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
    private static HttpClient Browser() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new() });
    public static async Task RunAsync()
    {
        foreach (var package in new[] { "studio", "fieldwork" })
        foreach (var mode in new[] { "blank", "demo" })
        {
            var directory = Path.Combine(Path.GetTempPath(), "webspine-native-" + Guid.NewGuid().ToString("N")); using var browser = Browser();
            try
            {
                await using var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true, designPackage: package);
                await host.WaitHealthyAsync(browser); await AccountChecks.BootstrapAsync(browser, host.Url);
                var form = Form(await browser.GetStringAsync(host.Url + "/manage")); form["title"] = "Native starter"; form["mode"] = mode;
                using var setup = await browser.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(form)); Require(setup.StatusCode == HttpStatusCode.Found, "Native setup failed.");
                var installed = InstalledDesigns.Select(package, "1"); var store = new SqliteContentSource(Path.Combine(directory, "webspine.db"), installed.Package.ContentTypes);
                var snapshot = await store.ReadCompositionAsync();
                Require(snapshot.ContractVersion == 2 && (await store.HistoryAsync()).Length == 1 && snapshot.Website.Pages.Length == (mode == "blank" ? 1 : package == "studio" ? 5 : 4), "Setup created legacy content or unexpected pages.");
                var board = await browser.GetStringAsync(host.Url + "/manage"); Require(board.Contains("Open a page") && !board.Contains("Enable composition editing") && !board.Contains("/manage/pages/"), "Default management is not v2.");
                form = Form(board); form["title"] = "News"; form["path"] = "/news/"; form["description"] = "News headline";
                try { await new CompositionEditor(store, installed.Package.Design, installed.Package.ContentTypes).ApplyAsync(snapshot.Revision, new AddCompositionPage("Denied", "/denied/", "Denied"), new(false, false, false), []); throw new Exception("Reader created a page."); }
                catch (CompositionPermissionException) { }
                using var created = await browser.PostAsync(host.Url + "/manage/composition/pages", new FormUrlEncodedContent(form)); Require(created.StatusCode == HttpStatusCode.Found, "Design-driven page creation failed: " + await created.Content.ReadAsStringAsync());
                var current = await store.ReadCompositionAsync(); var page = current.Website.Pages[^1];
                Require(page.Path == "/news/" && page.Regions.Select(r => r.Id).SequenceEqual(installed.Package.Design.Layout.Regions.Select(r => r.Id)) && snapshot.Website.SharedBlocks.SequenceEqual(current.Website.SharedBlocks) && snapshot.Website.Blocks.Where(b => b.Owner.Kind == OwnerKind.Shared).All(b => JsonElement.DeepEquals(b.Fields, current.Website.Blocks.Single(c => c.Id == b.Id).Fields)), "Creating a page changed shared values or assumed fixed regions.");
                using var stale = await browser.PostAsync(host.Url + "/manage/composition/pages", new FormUrlEncodedContent(form)); Require(stale.StatusCode == HttpStatusCode.Conflict, "Stale page creation succeeded.");
                form = Form(await browser.GetStringAsync(host.Url + "/manage")); form["title"] = "Duplicate"; form["path"] = "/news/"; form["description"] = "Duplicate";
                using var duplicate = await browser.PostAsync(host.Url + "/manage/composition/pages", new FormUrlEncodedContent(form)); Require(duplicate.StatusCode == HttpStatusCode.UnprocessableEntity && (await store.HeadAsync())!.Revision == current.Revision, "Duplicate route committed a partial page.");
                form = Form(await browser.GetStringAsync(host.Url + "/manage")); using var preview = await browser.PostAsync(host.Url + "/manage/composition/preview", new FormUrlEncodedContent(form));
                Require(preview.StatusCode == HttpStatusCode.Found && (await browser.GetStringAsync(host.Url + preview.Headers.Location + "news/")).Contains("News headline"), "Native starter/new page preview failed.");
                Console.WriteLine($"PASS: {package} {mode} setup starts natively on v2; page creation respects design, authority, revisions, routes and shared values.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        await VerifyLegacyRetirement();
    }
    private static async Task VerifyLegacyRetirement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-retired-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "webspine.db"); using var browser = Browser(); using var api = Browser();
        async Task<object?> Sql(string sql, params (string, object)[] parameters)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); await connection.OpenAsync();
            using var command = connection.CreateCommand(); command.CommandText = sql; foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
            return await command.ExecuteScalarAsync();
        }
        try
        {
            var fixture = await new DemoContentSource(await File.ReadAllTextAsync(Path.Combine(DemoSite.FixtureDirectory, "site.json"))).ReadAsync(); var store = new SqliteContentSource(path);
            var legacy = fixture with { Source = store.Identity, Revision = Guid.NewGuid().ToString("N") };
            var assets = legacy.Website.Assets.ToImmutableDictionary(a => a.File, a => File.ReadAllBytes(Path.Combine(DemoSite.FixtureDirectory, a.File)).ToImmutableArray());
            var previewId = Guid.NewGuid().ToString("N"); var artifact = await DemoSite.BuildSnapshotAsync(legacy, assets, "/manage/preview/" + previewId);
            var json = JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)); var previewJson = JsonSerializer.Serialize(artifact, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            // Historical database fixture, without a production legacy create operation.
            await Sql("""
                CREATE TABLE revisions(revision TEXT PRIMARY KEY,created_utc TEXT NOT NULL,snapshot TEXT NOT NULL);
                CREATE TABLE site(id INTEGER PRIMARY KEY CHECK(id=1),revision TEXT NOT NULL REFERENCES revisions(revision));
                CREATE TABLE assets(file TEXT PRIMARY KEY,bytes BLOB NOT NULL);
                CREATE TABLE previews(id TEXT PRIMARY KEY,source_revision TEXT NOT NULL REFERENCES revisions(revision),artifact TEXT NOT NULL);
                INSERT INTO revisions VALUES($revision,'historical',$json); INSERT INTO site VALUES(1,$revision);
                INSERT INTO assets VALUES($file,$bytes); INSERT INTO previews VALUES($preview,$revision,$artifact); PRAGMA user_version=1;
                """, ("$revision", legacy.Revision), ("$json", json), ("$file", assets.Single().Key), ("$bytes", assets.Single().Value.ToArray()), ("$preview", previewId), ("$artifact", previewJson));
            await store.InitializeSchemaAsync(); await store.InitializeSchemaAsync();
            Require((await store.HeadAsync()) == new ContentHead(legacy.Revision, 1) && !store.Capabilities.ConditionalDraftWrite, "Initialization reinterpreted legacy content.");
            try { await store.UpdateDraftAsync(new(legacy.Revision, "home", "introduction", "heading", "No")); throw new Exception("Legacy adapter accepted a write."); } catch (SourceOperationNotSupportedException) { }
            await using (var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true, designPackage: "fieldwork"))
            {
                await host.WaitHealthyAsync(browser); await AccountChecks.BootstrapAsync(browser, host.Url);
                using var home = await browser.GetAsync(host.Url + "/manage"); var html = await home.Content.ReadAsStringAsync(); Require(home.StatusCode == HttpStatusCode.Conflict && html.Contains("separate empty folder") && !html.Contains("Enable composition editing"), "Legacy workspace lacks fresh setup guidance.");
                api.DefaultRequestHeaders.Authorization = new("Bearer", await AccountChecks.Issue(browser, host.Url, "Retained previews", ["content:read", "preview:read"]));
                foreach (var operation in new[] { (HttpMethod.Get, "/api/v1/site"), (HttpMethod.Put, "/api/v1/site"), (HttpMethod.Post, "/api/v1/pages"), (HttpMethod.Put, "/api/v1/pages/home"), (HttpMethod.Post, "/api/v1/previews") })
                {
                    using var request = new HttpRequestMessage(operation.Item1, host.Url + operation.Item2) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                    using var response = await api.SendAsync(request); Require(response.StatusCode == HttpStatusCode.Gone, "Legacy API did not retire explicitly.");
                }
                var form = Form(await browser.GetStringAsync(host.Url + "/manage/integrations"));
                foreach (var route in new[] { "/manage/pages/home", "/manage/pages", "/manage/settings", "/manage/preview", "/manage/upgrade-composition" })
                { using var response = await browser.PostAsync(host.Url + route, new FormUrlEncodedContent(form)); Require(response.StatusCode == HttpStatusCode.Gone, "Legacy UI operation did not retire explicitly."); }
                using var v2 = await api.GetAsync(host.Url + "/api/v2/site"); Require(v2.StatusCode == HttpStatusCode.NotImplemented, "v2 read silently converted old data.");
                var bytes = artifact.Files.Single(f => f.Path == "index.html").Bytes;
                Require(bytes.SequenceEqual(await browser.GetByteArrayAsync(host.Url + "/manage/preview/" + previewId + "/")) && bytes.SequenceEqual(await api.GetByteArrayAsync(host.Url + "/api/v1/previews/" + previewId + "/")), "Retirement changed preview bytes.");
                using var head = await api.SendAsync(new HttpRequestMessage(HttpMethod.Head, host.Url + "/api/v1/previews/" + previewId + "/")); Require(head.IsSuccessStatusCode && (await head.Content.ReadAsByteArrayAsync()).Length == 0, "Retained preview HEAD failed.");
            }
            Require((string)(await Sql("SELECT snapshot FROM revisions WHERE revision=$revision", ("$revision", legacy.Revision)))! == json && (string)(await Sql("SELECT artifact FROM previews WHERE id=$preview", ("$preview", previewId)))! == previewJson && (await store.HistoryAsync()).Length == 1 && (await store.HeadAsync())!.Revision == legacy.Revision, "Retirement changed source/history/preview JSON.");
            Console.WriteLine("PASS: Old schema/content stays intact; retired editor/API reject writes while exact historical previews remain available under a different selected design.");
            foreach (var action in new[] { "inspect", "migrate", "restore" })
            {
                var before = BuildPipeline.Hash(await File.ReadAllBytesAsync(path)); await using var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true, contentAction: action);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await host.Process.WaitForExitAsync(timeout.Token);
                Require(host.Process.ExitCode != 0 && (await host.Errors).Contains("tooling has been removed") && BuildPipeline.Hash(await File.ReadAllBytesAsync(path)) == before, "Removed offline command touched content.");
            }
            Console.WriteLine("PASS: Removed inspect/migrate/restore commands fail before opening or modifying content.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
