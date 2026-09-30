using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

static partial class AuthoringChecks
{
    [GeneratedRegex("<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"")]
    private static partial Regex Inputs();
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-http-" + Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            string previewRoute;
            byte[] previewBytes;
            await using (var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await host.WaitHealthyAsync(client);
                var setup = await client.GetStringAsync(host.Url + "/manage");
                Require(setup.Contains("Start blank") && setup.Contains("Use demo"), "Setup choices missing.");
                using var unprotected = await client.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(new Dictionary<string, string> { ["title"] = "Unsafe", ["mode"] = "demo" }));
                Require(unprotected.StatusCode == HttpStatusCode.BadRequest, "Write without antiforgery token was allowed.");
                using var rebound = new HttpRequestMessage(HttpMethod.Get, host.Url + "/manage");
                rebound.Headers.Host = "untrusted.example";
                using var refused = await client.SendAsync(rebound);
                Require(refused.StatusCode == HttpStatusCode.Forbidden, "Untrusted Host header was accepted.");
                var values = Form(setup);
                values["mode"] = "demo"; values["title"] = "My saved studio";
                using var created = await client.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(values));
                Require(created.StatusCode == HttpStatusCode.Found, "Demo setup failed.");
                using var duplicate = await client.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(values));
                Require(duplicate.StatusCode == HttpStatusCode.Conflict, "Repeated setup replaced the site.");

                var edit = await client.GetStringAsync(host.Url + "/manage/pages/home");
                values = Form(edit);
                values["field.introduction.heading"] = "A heading saved in webspine";
                using var saved = await client.PostAsync(host.Url + "/manage/pages/home", new FormUrlEncodedContent(values));
                Require(saved.StatusCode == HttpStatusCode.Found, "Heading save failed.");
                values["field.introduction.heading"] = "Keep my unsaved words";
                using var stale = await client.PostAsync(host.Url + "/manage/pages/home", new FormUrlEncodedContent(values));
                Require(stale.StatusCode == HttpStatusCode.Conflict && (await stale.Content.ReadAsStringAsync()).Contains("Keep my unsaved words"), "Conflict did not preserve submitted text.");
                var overview = await client.GetStringAsync(host.Url + "/manage");
                values = Form(overview);
                using var built = await client.PostAsync(host.Url + "/manage/preview", new FormUrlEncodedContent(values));
                Require(built.StatusCode == HttpStatusCode.Found, "Preview failed.");
                previewRoute = built.Headers.Location!.ToString();
                using var preview = await client.GetAsync(host.Url + previewRoute);
                previewBytes = await preview.Content.ReadAsByteArrayAsync();
                Require(System.Text.Encoding.UTF8.GetString(previewBytes).Contains("A heading saved in webspine") && preview.Headers.CacheControl?.NoStore == true && !preview.Headers.Contains("X-Webspine-Cache"), "Preview did not show saved content privately.");
                foreach (var page in new[] { "services/", "products/", "about/", "contact/", "assets/studio.svg" })
                { using var response = await client.GetAsync(host.Url + previewRoute + page); Require(response.IsSuccessStatusCode, "Missing preview page/asset."); }
                using var export = await client.GetAsync(host.Url + "/manage/export");
                using var archive = new ZipArchive(new MemoryStream(await export.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
                Require(archive.GetEntry("content.json") is not null && archive.GetEntry("assets/studio.svg") is not null, "Portable export omitted content/media.");
                edit = await client.GetStringAsync(host.Url + "/manage/pages/home");
                values = Form(edit); values["field.introduction.heading"] = "Edited after preview";
                using var after = await client.PostAsync(host.Url + "/manage/pages/home", new FormUrlEncodedContent(values));
                Require(after.IsSuccessStatusCode || after.StatusCode == HttpStatusCode.Found, "Later save failed.");
            }
            await using (var restarted = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await restarted.WaitHealthyAsync(client);
                var overview = await client.GetStringAsync(restarted.Url + "/manage");
                var edit = await client.GetStringAsync(restarted.Url + "/manage/pages/home");
                Require(overview.Contains("My saved studio") && edit.Contains("Edited after preview"), "Process restart lost site/edit.");
                var retained = await client.GetByteArrayAsync(restarted.Url + previewRoute);
                Require(retained.SequenceEqual(previewBytes), "Process restart changed retained preview bytes.");
                var values = Form(overview); values["title"] = "News"; values["path"] = "/news/"; values["description"] = "Our news";
                using var added = await client.PostAsync(restarted.Url + "/manage/pages", new FormUrlEncodedContent(values));
                Require(added.StatusCode == HttpStatusCode.Found && (await client.GetStringAsync(restarted.Url + "/manage")).Contains("Edit News"), "Page creation failed.");
            }
            Console.WriteLine("PASS: Actual management setup/edit/preview/export/restart, antiforgery, Host checks and conflict recovery.");

            var blankDirectory = Path.Combine(directory, "blank");
            await using (var blank = CheckHost.Start("Development", false, dataDirectory: blankDirectory, managementEnabled: true))
            {
                await blank.WaitHealthyAsync(client);
                var values = Form(await client.GetStringAsync(blank.Url + "/manage")); values["mode"] = "blank"; values["title"] = "Blank project";
                using var created = await client.PostAsync(blank.Url + "/manage/setup", new FormUrlEncodedContent(values));
                var overview = await client.GetStringAsync(blank.Url + "/manage");
                Require(created.StatusCode == HttpStatusCode.Found && overview.Contains("1 total") && !overview.Contains("Edit Services"), "Blank path seeded the demo.");
            }
            await using (var production = CheckHost.Start("Production", false, dataDirectory: directory, managementEnabled: true))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await production.Process.WaitForExitAsync(timeout.Token);
                Require(production.Process.ExitCode != 0 && (await production.Errors).Contains("available only in Development"), "Local management was allowed outside Development.");
            }
            Console.WriteLine("PASS: Blank setup has one page and production management opt-in is rejected.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    private static Dictionary<string, string> Form(string html) => Inputs().Matches(html).Cast<Match>().GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
