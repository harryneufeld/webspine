using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

static partial class AccountChecks
{
    private const string Password = "Disposable-Test!123";
    [GeneratedRegex("name=\"([^\"]+)\" value=\"([^\"]*)\"")]
    private static partial Regex Inputs();
    [GeneratedRegex("wsp_[a-f0-9]{64}")]
    private static partial Regex Secrets();
    private static Dictionary<string, string> Form(string html) => Inputs().Matches(html).Cast<Match>().GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
    public static async Task BootstrapAsync(HttpClient browser, string url)
    {
        var form = Form(await browser.GetStringAsync(url + "/manage/account/start"));
        form["username"] = "owner"; form["password"] = Password;
        using var created = await browser.PostAsync(url + "/manage/account/start", new FormUrlEncodedContent(form));
        Require(created.StatusCode == HttpStatusCode.Found, "Owner bootstrap failed: " + await created.Content.ReadAsStringAsync());
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-accounts-" + Guid.NewGuid().ToString("N"));
        using var browser = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new() });
        using var api = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        try
        {
            string token;
            string previewUrl;
            byte[] previewBytes;
            await using (var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await host.WaitHealthyAsync(browser);
                using var denied = await browser.GetAsync(host.Url + "/manage");
                Require(denied.StatusCode == HttpStatusCode.Found && denied.Headers.Location!.ToString().Contains("login"), "Anonymous management exposed drafts.");
                using var anonymousApi = await api.GetAsync(host.Url + "/api/v1/site"); Require(anonymousApi.StatusCode == HttpStatusCode.Unauthorized, "Anonymous API allowed.");
                await BootstrapAsync(browser, host.Url);
                var form = new Dictionary<string, string>();
                // GET start now redirects; obtain a fresh authenticated form instead.
                form = Form(await browser.GetStringAsync(host.Url + "/manage")); form["username"] = "second-owner"; form["password"] = Password;
                using var repeated = await browser.PostAsync(host.Url + "/manage/account/start", new FormUrlEncodedContent(form)); Require(repeated.StatusCode == HttpStatusCode.Conflict, "Bootstrap replaced owner.");
                form = Form(await browser.GetStringAsync(host.Url + "/manage")); form["title"] = "Original studio"; form["mode"] = "demo";
                using var setup = await browser.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(form)); Require(setup.StatusCode == HttpStatusCode.Found, "Site setup failed.");
                using var cookieApi = await browser.GetAsync(host.Url + "/api/v1/site"); Require(cookieApi.StatusCode == HttpStatusCode.Unauthorized, "Cookie authenticated the integration API.");
                var settings = Form(await browser.GetStringAsync(host.Url + "/manage/settings")); settings["title"] = "Renamed & Co"; settings["language"] = "de";
                using var saved = await browser.PostAsync(host.Url + "/manage/settings", new FormUrlEncodedContent(settings)); Require(saved.StatusCode == HttpStatusCode.Found, "UI settings failed.");
                settings["title"] = "Preserve my settings";
                using var stale = await browser.PostAsync(host.Url + "/manage/settings", new FormUrlEncodedContent(settings)); Require(stale.StatusCode == HttpStatusCode.Conflict && (await stale.Content.ReadAsStringAsync()).Contains("Preserve my settings"), "Settings conflict lost values.");
                Require((await browser.GetStringAsync(host.Url + "/manage")).Contains("Renamed &amp; Co"), "Overview did not reuse title.");
                form = Form(await browser.GetStringAsync(host.Url + "/manage"));
                using var built = await browser.PostAsync(host.Url + "/manage/preview", new FormUrlEncodedContent(form));
                previewUrl = built.Headers.Location!.ToString(); previewBytes = await browser.GetByteArrayAsync(host.Url + previewUrl);
                var previewHtml = System.Text.Encoding.UTF8.GetString(previewBytes);
                Require(previewHtml.Contains("Home · Renamed &amp; Co") && previewHtml.Contains("lang=\"de\"") && previewHtml.Contains("<span>Renamed &amp; Co</span>"), "Preview metadata did not reuse saved settings.");
                token = await Issue(browser, host.Url, "Writer", ["content:read", "content:write", "settings:write", "preview:build", "preview:read"]);
                api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var snapshot = await Read(api, host.Url);
                var revision = snapshot["revision"]!.GetValue<string>();
                using var changed = await api.PutAsJsonAsync(host.Url + "/api/v1/site", new { expectedRevision = revision, title = "Agent studio", language = "en-GB" });
                Require(changed.IsSuccessStatusCode, "Scoped API settings failed: " + await changed.Content.ReadAsStringAsync());
                using var conflict = await api.PutAsJsonAsync(host.Url + "/api/v1/site", new { expectedRevision = revision, title = "Stale agent", language = "en" }); Require(conflict.StatusCode == HttpStatusCode.Conflict, "API stale settings overwrote content.");
                snapshot = await Read(api, host.Url); revision = snapshot["revision"]!.GetValue<string>();
                using var unknown = await api.PutAsJsonAsync(host.Url + "/api/v1/site", new { expectedRevision = revision, title = "Ignore", language = "en", css = "body{}" }); Require(unknown.StatusCode == HttpStatusCode.BadRequest, "API ignored unknown fields.");
                using var invalid = await api.PutAsJsonAsync(host.Url + "/api/v1/site", new { expectedRevision = revision, title = "", language = "en" }); Require(invalid.StatusCode == HttpStatusCode.UnprocessableEntity, "API saved invalid settings.");
                using var page = await api.PostAsJsonAsync(host.Url + "/api/v1/pages", new { expectedRevision = revision, title = "News", path = "/news/", description = "News from the agent" }); Require(page.IsSuccessStatusCode, "Agent page creation failed.");
                snapshot = await Read(api, host.Url); revision = snapshot["revision"]!.GetValue<string>();
                var news = snapshot["website"]!["pages"]!.AsArray().Single(p => p!["path"]!.GetValue<string>() == "/news/")!;
                using var edited = await api.PutAsJsonAsync(host.Url + "/api/v1/pages/" + news["id"]!.GetValue<string>(), new { expectedRevision = revision, title = "News", description = "Agent-managed news", fields = new Dictionary<string, string> { ["introduction.heading"] = "Created by an external app" } }); Require(edited.IsSuccessStatusCode, "Agent page edit failed.");
                snapshot = await Read(api, host.Url); revision = snapshot["revision"]!.GetValue<string>();
                using var apiBuild = await api.PostAsJsonAsync(host.Url + "/api/v1/previews", new { expectedRevision = revision }); Require(apiBuild.StatusCode == HttpStatusCode.Created, "Agent preview build failed.");
                var apiPreviewUrl = (await apiBuild.Content.ReadFromJsonAsync<JsonObject>())!["url"]!.GetValue<string>();
                var apiHtml = await api.GetStringAsync(host.Url + apiPreviewUrl + "news/"); Require(apiHtml.Contains("Created by an external app") && apiHtml.Contains("Agent studio"), "Agent preview content wrong.");
                using var privatePreview = await browser.GetAsync(host.Url + apiPreviewUrl); Require(privatePreview.StatusCode == HttpStatusCode.Unauthorized, "API preview accepted browser cookie.");
                var contentOnly = await Issue(browser, host.Url, "Content only", ["content:read", "content:write"]);
                api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contentOnly);
                using var deniedSettings = await api.PutAsJsonAsync(host.Url + "/api/v1/site", new { expectedRevision = revision, title = "Unapproved settings", language = "en" }); Require(deniedSettings.StatusCode == HttpStatusCode.Forbidden, "Content permission implicitly granted settings permission.");
                using var tokenInUi = await api.GetAsync(host.Url + "/manage/settings"); Require(tokenInUi.StatusCode == HttpStatusCode.Found, "Integration token authenticated browser management.");
                var reader = await Issue(browser, host.Url, "Reader", ["content:read"]);
                api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reader);
                await Read(api, host.Url);
                using var forbidden = await api.PutAsJsonAsync(host.Url + "/api/v1/site", new { expectedRevision = revision, title = "Forbidden", language = "en" }); Require(forbidden.StatusCode == HttpStatusCode.Forbidden, "Read-only app changed settings.");
                using var forbiddenPage = await api.PostAsJsonAsync(host.Url + "/api/v1/pages", new { expectedRevision = revision, title = "No", path = "/no/", description = "Denied" }); Require(forbiddenPage.StatusCode == HttpStatusCode.Forbidden, "Read-only app created page.");
                using var forbiddenPreview = await api.GetAsync(host.Url + apiPreviewUrl); Require(forbiddenPreview.StatusCode == HttpStatusCode.Forbidden, "Read-only app viewed preview without scope.");
                form = Form(await browser.GetStringAsync(host.Url + "/manage/integrations"));
                var listHtml = await browser.GetStringAsync(host.Url + "/manage/integrations");
                Require(!listHtml.Contains(token) && !listHtml.Contains(reader), "Raw credentials were redisplayed.");
                // Resolve the Reader row, rather than assuming credential order.
                var readerRow = Regex.Match(listHtml, "<li><div><strong>Reader</strong>.*?</li>", RegexOptions.Singleline).Value;
                var route = Regex.Match(readerRow, "action=\"([^\"]+)\"").Groups[1].Value;
                using var revoked = await browser.PostAsync(host.Url + route, new FormUrlEncodedContent(form)); Require(revoked.StatusCode == HttpStatusCode.Found, "Credential revocation failed.");
                using var refused = await api.GetAsync(host.Url + "/api/v1/site"); Require(refused.StatusCode == HttpStatusCode.Unauthorized, "Revoked token remained active.");
                form = Form(await browser.GetStringAsync(host.Url + "/manage"));
                using var logout = await browser.PostAsync(host.Url + "/manage/account/logout", new FormUrlEncodedContent(form)); Require(logout.StatusCode == HttpStatusCode.Found, "Logout failed.");
                using var loggedOut = await browser.GetAsync(host.Url + previewUrl); Require(loggedOut.StatusCode == HttpStatusCode.Found, "Logged out browser viewed draft preview.");
                form = Form(await browser.GetStringAsync(host.Url + "/manage/account/login")); form["username"] = "owner"; form["password"] = "Wrong-password!123";
                using var bad = await browser.PostAsync(host.Url + "/manage/account/login", new FormUrlEncodedContent(form)); Require(bad.StatusCode == HttpStatusCode.Unauthorized, "Invalid login succeeded.");
                form = Form(await browser.GetStringAsync(host.Url + "/manage/account/login")); form["username"] = "owner"; form["password"] = Password;
                using var login = await browser.PostAsync(host.Url + "/manage/account/login", new FormUrlEncodedContent(form)); Require(login.StatusCode == HttpStatusCode.Found, "Login failed.");
                Console.WriteLine("PASS: Owner bootstrap/login/logout, protected UI, settings conflict/reuse, scoped API page/settings/preview operations and revocation.");
            }
            await using (var restart = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await restart.WaitHealthyAsync(browser);
                api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var snapshot = await Read(api, restart.Url); Require(snapshot["website"]!["title"]!.GetValue<string>() == "Agent studio", "Restart lost metadata or token.");
                var bytes = await browser.GetByteArrayAsync(restart.Url + previewUrl); Require(bytes.SequenceEqual(previewBytes), "Old preview metadata mutated after edits/restart.");
                await using var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "accounts.db"), Pooling = false }.ToString());
                await database.OpenAsync();
                using var query = database.CreateCommand(); query.CommandText = "SELECT Digest FROM Credentials";
                using var rows = await query.ExecuteReaderAsync(); while (await rows.ReadAsync()) Require(rows.GetString(0).Length == 64 && !rows.GetString(0).Contains("wsp_"), "Credential was not digest-only.");
                rows.Close();
                using var expire = database.CreateCommand(); expire.CommandText = "UPDATE Credentials SET ExpiresUtcTicks=0"; await expire.ExecuteNonQueryAsync();
                using var expired = await api.GetAsync(restart.Url + "/api/v1/site"); Require(expired.StatusCode == HttpStatusCode.Unauthorized, "Expired token authenticated.");
                var freshToken = await Issue(browser, restart.Url, "Stamp test", ["content:read"]);
                api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
                await Read(api, restart.Url);
                using var invalidate = database.CreateCommand(); invalidate.CommandText = "UPDATE AspNetUsers SET SecurityStamp='changed-stamp'"; await invalidate.ExecuteNonQueryAsync();
                using var invalidSession = await browser.GetAsync(restart.Url + "/manage"); Require(invalidSession.StatusCode == HttpStatusCode.Found, "Changed security stamp did not invalidate browser session.");
                using var invalidToken = await api.GetAsync(restart.Url + "/api/v1/site"); Require(invalidToken.StatusCode == HttpStatusCode.Unauthorized, "Changed security stamp did not invalidate integration token.");
                Console.WriteLine("PASS: Accounts/sessions/tokens/settings persist after restart; preview bytes stay fixed; token storage is digest-only and expiry is enforced.");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    private static async Task<JsonObject> Read(HttpClient api, string url)
    {
        using var response = await api.GetAsync(url + "/api/v1/site"); Require(response.IsSuccessStatusCode, "API read failed: " + await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    private static async Task<string> Issue(HttpClient browser, string url, string label, string[] scopes)
    {
        var form = Form(await browser.GetStringAsync(url + "/manage/integrations"));
        form.Remove("scope");
        form["label"] = label; form["days"] = "7";
        var values = form.ToList(); values.AddRange(scopes.Select(scope => new KeyValuePair<string, string>("scope", scope)));
        using var issued = await browser.PostAsync(url + "/manage/integrations", new FormUrlEncodedContent(values));
        var body = await issued.Content.ReadAsStringAsync(); Require(issued.IsSuccessStatusCode && Secrets().IsMatch(body), "Credential issuance failed: " + body);
        return Secrets().Match(body).Value;
    }
}
