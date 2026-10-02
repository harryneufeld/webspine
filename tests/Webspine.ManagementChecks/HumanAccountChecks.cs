using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

static class HumanAccountChecks
{
    private const string Password = "Disposable-Test!123";
    private const string NewPassword = "Recovered-Test!456";
    private static HttpClient Browser() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new() });
    private static Dictionary<string, string> Form(string html) => Regex.Matches(html, "name=\"([^\"]+)\" value=\"([^\"]*)\"").Cast<Match>().GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Login(HttpClient browser, string url, string name, string password = Password)
    {
        var form = Form(await browser.GetStringAsync(url + "/manage/account/login")); form["username"] = name; form["password"] = password;
        using var response = await browser.PostAsync(url + "/manage/account/login", new FormUrlEncodedContent(form)); Require(response.StatusCode == HttpStatusCode.Found, "Account login failed: " + name + " HTTP " + (int)response.StatusCode);
    }
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-people-" + Guid.NewGuid().ToString("N"));
        using var owner = Browser(); using var editor = Browser(); using var reviewer = Browser(); using var helper = Browser();
        try
        {
            string previewUrl;
            string recoveryToken;
            await using (var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await host.WaitHealthyAsync(owner); await AccountChecks.BootstrapAsync(owner, host.Url);
                var form = Form(await owner.GetStringAsync(host.Url + "/manage")); form["title"] = "Keep this website"; form["mode"] = "demo";
                using var setup = await owner.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(form)); Require(setup.StatusCode == HttpStatusCode.Found, "Site creation failed.");
                foreach (var (name, role) in new[] { ("editor", "Editor"), ("reviewer", "Reviewer"), ("operator", "Operator") })
                {
                    form = Form(await owner.GetStringAsync(host.Url + "/manage/users")); form["username"] = name; form["password"] = Password; form["role"] = role;
                    using var added = await owner.PostAsync(host.Url + "/manage/users", new FormUrlEncodedContent(form)); Require(added.StatusCode == HttpStatusCode.Found, "Account creation failed.");
                }
                await Login(editor, host.Url, "editor"); await Login(reviewer, host.Url, "reviewer"); await Login(helper, host.Url, "operator");
                using var editorSettings = await editor.GetAsync(host.Url + "/manage/settings"); Require(editorSettings.StatusCode == HttpStatusCode.Forbidden, "Editor changed website settings.");
                using var editorAdmin = await editor.GetAsync(host.Url + "/manage/users"); Require(editorAdmin.StatusCode == HttpStatusCode.Forbidden, "Editor read account administration.");
                form = Form(await editor.GetStringAsync(host.Url + "/manage/composition/blocks/block-2eb6afd238dc39c89c97343021e0263a")); form["field.heading"] = "Edited by an editor";
                using var edit = await editor.PostAsync(host.Url + "/manage/composition/blocks/block-2eb6afd238dc39c89c97343021e0263a", new FormUrlEncodedContent(form)); Require(edit.StatusCode == HttpStatusCode.Found, "Editor could not edit.");
                form = Form(await editor.GetStringAsync(host.Url + "/manage"));
                using var build = await editor.PostAsync(host.Url + "/manage/composition/preview", new FormUrlEncodedContent(form)); Require(build.StatusCode == HttpStatusCode.Found, "Editor could not build preview."); previewUrl = build.Headers.Location!.ToString();
                Require((await reviewer.GetStringAsync(host.Url + previewUrl)).Contains("Edited by an editor"), "Reviewer could not inspect preview.");
                form = Form(await reviewer.GetStringAsync(host.Url + "/manage"));
                using var forbidden = await reviewer.PostAsync(host.Url + "/manage/composition/preview", new FormUrlEncodedContent(form)); Require(forbidden.StatusCode == HttpStatusCode.Forbidden, "Reviewer built preview.");
                using var forbiddenWrite = await reviewer.PostAsync(host.Url + "/manage/composition/blocks/block-2eb6afd238dc39c89c97343021e0263a", new FormUrlEncodedContent(form)); Require(forbiddenWrite.StatusCode == HttpStatusCode.Forbidden, "Reviewer edited content.");
                Require(!(await reviewer.GetStringAsync(host.Url + "/manage")).Contains("People and access"), "Reviewer sees privileged navigation.");
                var list = await helper.GetStringAsync(host.Url + "/manage/users");
                var row = Regex.Match(list, "<li><div><strong>editor</strong>.*?</li>", RegexOptions.Singleline).Value;
                var route = Regex.Match(row, "action=\"([^\"]+)\"").Groups[1].Value;
                form = Form(row); form.Remove("disabled"); form["role"] = "Reviewer";
                using var demoted = await helper.PostAsync(host.Url + route, new FormUrlEncodedContent(form)); Require(demoted.StatusCode == HttpStatusCode.Found, "Operator could not change role.");
                using var stale = await helper.PostAsync(host.Url + route, new FormUrlEncodedContent(form)); Require(stale.StatusCode == HttpStatusCode.Conflict, "Stale account changes overwrote state.");
                using var oldSession = await editor.GetAsync(host.Url + "/manage"); Require(oldSession.StatusCode == HttpStatusCode.Found, "Role change did not invalidate session.");
                await Login(editor, host.Url, "editor");
                using var nowDenied = await editor.PostAsync(host.Url + "/manage/composition/preview", new FormUrlEncodedContent(Form(await editor.GetStringAsync(host.Url + "/manage")))); Require(nowDenied.StatusCode == HttpStatusCode.Forbidden, "Demoted editor retained permission.");
                list = await owner.GetStringAsync(host.Url + "/manage/users"); row = Regex.Match(list, "<li><div><strong>editor</strong>.*?</li>", RegexOptions.Singleline).Value;
                form = Form(row); form["role"] = "Editor"; form["disabled"] = "true";
                using var disabled = await owner.PostAsync(host.Url + route, new FormUrlEncodedContent(form)); Require(disabled.StatusCode == HttpStatusCode.Found, "Disable failed.");
                using var disabledSession = await editor.GetAsync(host.Url + "/manage"); Require(disabledSession.StatusCode == HttpStatusCode.Found, "Disabled account session remained active.");
                var disabledLogin = Form(await editor.GetStringAsync(host.Url + "/manage/account/login")); disabledLogin["username"] = "editor"; disabledLogin["password"] = Password;
                using var disabledAttempt = await editor.PostAsync(host.Url + "/manage/account/login", new FormUrlEncodedContent(disabledLogin)); Require(disabledAttempt.StatusCode == HttpStatusCode.Unauthorized, "Disabled account could sign in.");
                var passwordForm = Form(await reviewer.GetStringAsync(host.Url + "/manage/account/password")); passwordForm["current"] = Password; passwordForm["password"] = NewPassword; passwordForm["confirm"] = NewPassword;
                using var ownPassword = await reviewer.PostAsync(host.Url + "/manage/account/password", new FormUrlEncodedContent(passwordForm)); Require(ownPassword.StatusCode == HttpStatusCode.Found && (await reviewer.GetAsync(host.Url + "/manage")).IsSuccessStatusCode, "Reviewer could not change own password/stay signed in.");
                await using var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "accounts.db"), Pooling = false }.ToString()); await database.OpenAsync();
                using var query = database.CreateCommand(); query.CommandText = "SELECT Id,ConcurrencyStamp FROM AspNetUsers WHERE UserName='owner'";
                using var ownerRow = await query.ExecuteReaderAsync(); Require(await ownerRow.ReadAsync(), "Owner missing."); var id = ownerRow.GetString(0); var stamp = ownerRow.GetString(1); ownerRow.Close();
                form = Form(await helper.GetStringAsync(host.Url + "/manage/users")); form["expectedStamp"] = stamp; form["role"] = "Reviewer"; form["disabled"] = "true";
                using var protectedOwner = await helper.PostAsync(host.Url + "/manage/users/" + id, new FormUrlEncodedContent(form)); Require(protectedOwner.StatusCode == HttpStatusCode.Conflict, "Original owner was disabled.");
                var credentialForm = Form(await helper.GetStringAsync(host.Url + "/manage/integrations")); credentialForm["label"] = "Operator app"; credentialForm["days"] = "7"; credentialForm["scope"] = "content:read";
                using var issued = await helper.PostAsync(host.Url + "/manage/integrations", new FormUrlEncodedContent(credentialForm)); var token = Regex.Match(await issued.Content.ReadAsStringAsync(), "wsp_[a-f0-9]{64}").Value; Require(token.Length == 68, "Operator credential issuance failed.");
                list = await owner.GetStringAsync(host.Url + "/manage/users"); row = Regex.Match(list, "<li><div><strong>operator</strong>.*?</li>", RegexOptions.Singleline).Value;
                form = Form(row); form.Remove("disabled"); form["role"] = "Reviewer";
                using var changedOperator = await owner.PostAsync(host.Url + Regex.Match(row, "action=\"([^\"]+)\"").Groups[1].Value, new FormUrlEncodedContent(form)); Require(changedOperator.StatusCode == HttpStatusCode.Found, "Operator demotion failed.");
                using var api = new HttpClient(new HttpClientHandler { UseProxy = false }); api.DefaultRequestHeaders.Authorization = new("Bearer", token);
                using var revokedByRole = await api.GetAsync(host.Url + "/api/v2/site"); Require(revokedByRole.StatusCode == HttpStatusCode.Unauthorized, "Role change kept app credential active.");
                credentialForm = Form(await owner.GetStringAsync(host.Url + "/manage/integrations")); credentialForm["label"] = "Owner recovery test"; credentialForm["days"] = "7"; credentialForm["scope"] = "content:read";
                using var ownerToken = await owner.PostAsync(host.Url + "/manage/integrations", new FormUrlEncodedContent(credentialForm)); recoveryToken = Regex.Match(await ownerToken.Content.ReadAsStringAsync(), "wsp_[a-f0-9]{64}").Value; Require(recoveryToken.Length == 68, "Owner credential failed.");
                Console.WriteLine("PASS: Operator/editor/reviewer permissions, owner protection, account conflicts, disabling, role/session invalidation and self-service password changes.");
            }
            // Simulate the previous one-owner claim layout, without changing database schema or content.
            await using (var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "accounts.db"), Pooling = false }.ToString()))
            {
                await database.OpenAsync(); using var legacy = database.CreateCommand();
                legacy.CommandText = "DELETE FROM AspNetUserClaims WHERE UserId=(SELECT Id FROM AspNetUsers WHERE UserName='owner') AND (ClaimType IN ('webspine:owner','webspine:role') OR ClaimValue IN ('accounts:manage','content:shared:write')); DELETE FROM AspNetUserClaims WHERE UserId=(SELECT Id FROM AspNetUsers WHERE UserName='operator') AND ClaimValue='integrations:manage'";
                await legacy.ExecuteNonQueryAsync();
            }
            await using (var upgraded = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await upgraded.WaitHealthyAsync(owner);
                using var oldSession = await owner.GetAsync(upgraded.Url + "/manage/users"); Require(oldSession.StatusCode == HttpStatusCode.Found, "Shared permission upgrade kept an old administrative session.");
                using var api = new HttpClient(new HttpClientHandler { UseProxy = false }); api.DefaultRequestHeaders.Authorization = new("Bearer", recoveryToken);
                using var oldCredential = await api.GetAsync(upgraded.Url + "/api/v2/site"); Require(oldCredential.StatusCode == HttpStatusCode.Unauthorized, "Shared permission upgrade retained old credentials.");
                await Login(owner, upgraded.Url, "owner");
                Require((await owner.GetStringAsync(upgraded.Url + "/manage/users")).Contains("Owner") && (await owner.GetStringAsync(upgraded.Url + "/manage/integrations")).Contains("content:shared:write"), "Legacy owner shared authority was not upgraded.");
            }
            await using (var recovery = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true, recoveryUsername: "owner"))
            {
                await recovery.Process.StandardInput.WriteLineAsync(NewPassword); await recovery.Process.StandardInput.WriteLineAsync(NewPassword); recovery.Process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await recovery.Process.WaitForExitAsync(timeout.Token);
                Require(recovery.Process.ExitCode == 0 && !(await recovery.Output).Contains(NewPassword) && !(await recovery.Errors).Contains(NewPassword), "Recovery failed or logged the password.");
            }
            await using (var unknown = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true, recoveryUsername: "missing"))
            {
                await unknown.Process.StandardInput.WriteLineAsync(NewPassword); await unknown.Process.StandardInput.WriteLineAsync(NewPassword); unknown.Process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await unknown.Process.WaitForExitAsync(timeout.Token); Require(unknown.Process.ExitCode != 0, "Recovery created an unknown account.");
            }
            await using (var restarted = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await restarted.WaitHealthyAsync(owner);
                using var oldSession = await owner.GetAsync(restarted.Url + "/manage"); Require(oldSession.StatusCode == HttpStatusCode.Found, "Recovery kept old session.");
                using var api = new HttpClient(new HttpClientHandler { UseProxy = false }); api.DefaultRequestHeaders.Authorization = new("Bearer", recoveryToken);
                using var oldToken = await api.GetAsync(restarted.Url + "/api/v2/site"); Require(oldToken.StatusCode == HttpStatusCode.Unauthorized, "Recovery retained old app credential.");
                await Login(owner, restarted.Url, "owner", NewPassword);
                Require((await owner.GetStringAsync(restarted.Url + "/manage")).Contains("Keep this website") && (await owner.GetStringAsync(restarted.Url + previewUrl)).Contains("Edited by an editor"), "Recovery lost website/preview state.");
                Console.WriteLine("PASS: Legacy owner claims upgrade safely; offline recovery accepts stdin, avoids password logs, invalidates sessions and preserves website/accounts/previews.");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
