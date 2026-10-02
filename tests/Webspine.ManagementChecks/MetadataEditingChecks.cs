using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Webspine.Content.Faq;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Designs.Studio;

static class MetadataEditingChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static JsonElement Fields(object value) => JsonSerializer.SerializeToElement(value, value.GetType(), CompositionJson.Options);
    private static Dictionary<string, string> Form(string html) => Regex.Matches(html, "<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"")
        .GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
    private static void ClearLists(Dictionary<string, string> form)
    { foreach (var key in form.Keys.Where(k => k.StartsWith("append.", StringComparison.Ordinal) || k.StartsWith("remove.", StringComparison.Ordinal)).ToArray()) form.Remove(key); }
    public static async Task RunAsync(bool browserReview = false)
    {
        var definition = FaqContent.Definition; var metadata = definition.Editor!;
        ContentEditorContract.ValidateRegistration(metadata, definition.PayloadType);
        foreach (var invalid in new[] { metadata with { Fields = metadata.Fields.RemoveAt(0) }, metadata with { Fields = metadata.Fields.Add(metadata.Fields[0]) },
            metadata with { Version = 99 }, metadata with { Fields = metadata.Fields.SetItem(1, metadata.Fields[1] with { Maximum = 1001 }) },
            metadata with { Fields = metadata.Fields.SetItem(1, metadata.Fields[1] with { Required = false }) },
            metadata with { Fields = metadata.Fields.SetItem(0, metadata.Fields[0] with { ChoiceSource = EditorChoiceSource.GroupColumns, Kind = EditorFieldKind.Choice }) },
            metadata with { Fields = metadata.Fields.SetItem(0, metadata.Fields[0] with { Kind = EditorFieldKind.Integer }) } })
            await Reject<ContentValidationException>(() => { ContentEditorContract.ValidateRegistration(invalid, definition.PayloadType); return Task.CompletedTask; });
        var specialized = metadata with { Fields = [], SpecializedEditor = "rich-faq", SummaryField = null };
        ContentEditorContract.ValidateRegistration(specialized, definition.PayloadType);
        Require(!ContentEditorContract.Generic(specialized), "Specialized metadata became a lossy generic editor.");
        Console.WriteLine("PASS: Metadata rejects incompatible/missing/duplicate/oversized schemas; specialized editing is explicit rather than inferred.");

        var directory = Path.Combine(Path.GetTempPath(), "webspine-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true);
            using var browser = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new() });
            using var api = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            await host.WaitHealthyAsync(browser); await AccountChecks.BootstrapAsync(browser, host.Url);
            var form = Form(await browser.GetStringAsync(host.Url + "/manage")); form["mode"] = "demo"; form["title"] = "Questions studio";
            using var setup = await browser.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(form)); Require(setup.StatusCode == HttpStatusCode.Found, "Metadata fixture setup failed.");
            var writer = await AccountChecks.Issue(browser, host.Url, "Metadata writer", ["content:read", "content:write", "preview:build", "preview:read"]);
            var sharedWriter = await AccountChecks.Issue(browser, host.Url, "Metadata shared writer", ["content:read", "content:write", "content:shared:write", "preview:build", "preview:read"]);
            var reader = await AccountChecks.Issue(browser, host.Url, "Metadata reader", ["content:read"]);
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            async Task<CompositionSnapshot> Read() => JsonSerializer.Deserialize<CompositionSnapshot>(await api.GetStringAsync(host.Url + "/api/v2/site"), CompositionJson.Options)!;
            async Task<HttpResponseMessage> Change(CompositionSnapshot snapshot, CompositionEdit edit, ImmutableArray<string> affected = default) =>
                await api.PostAsync(host.Url + "/api/v2/changes", new StringContent(JsonSerializer.Serialize(new { expectedRevision = snapshot.Revision, change = edit, acknowledgedPages = affected.IsDefault ? [] : affected }, CompositionJson.Options), Encoding.UTF8, "application/json"));
            using var noCredential = await browser.GetAsync(host.Url + "/api/v2/schema"); Require(noCredential.StatusCode == HttpStatusCode.Unauthorized, "Schema fell back to browser credentials.");
            var schemaText = await api.GetStringAsync(host.Url + "/api/v2/schema"); using var schema = JsonDocument.Parse(schemaText);
            var faqSchema = schema.RootElement.GetProperty("types").EnumerateArray().Single(t => t.GetProperty("id").GetString() == "faq");
            Require(faqSchema.GetProperty("editor").GetProperty("label").GetString() == "Questions and answers" && faqSchema.GetProperty("defaults").GetProperty("items").GetArrayLength() == 1 &&
                faqSchema.GetProperty("canCreate").GetBoolean() && !schema.RootElement.GetProperty("canWriteShared").GetBoolean(), "FAQ discovery omitted defaults/labels/effective permissions.");
            Require(!schema.RootElement.GetProperty("operations").EnumerateArray().Any(v => v.GetString() == "share"), "Schema advertised sharing without shared-write authority.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", sharedWriter);
            using var sharedSchema = JsonDocument.Parse(await api.GetStringAsync(host.Url + "/api/v2/schema"));
            Require(sharedSchema.RootElement.GetProperty("canWriteShared").GetBoolean() && sharedSchema.RootElement.GetProperty("operations").EnumerateArray().Any(v => v.GetString() == "share"), "Shared writer could not discover sharing authority.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            Require(!schemaText.Contains("ImplementationDigest") && !schemaText.Contains("wsp_") && !schemaText.Contains(".dll"), "Schema exposed code or credentials.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", reader);
            using var readSchema = JsonDocument.Parse(await api.GetStringAsync(host.Url + "/api/v2/schema"));
            Require(readSchema.RootElement.GetProperty("operations").EnumerateArray().All(v => v.GetString() == "read") && readSchema.RootElement.GetProperty("types").EnumerateArray().All(t => !t.GetProperty("canCreate").GetBoolean()), "Read-only schema advertised write permission.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            var snapshot = await Read(); var package = StudioPackage.Create();
            var defaults = ContentEditorContract.Defaults(metadata, snapshot.Website, package.Design);
            Require(defaults.GetProperty("items")[0].GetProperty("question").GetString() == "Your question", "Declared default creation failed.");
            await Reject<SourceOperationNotSupportedException>(() => { ContentEditorContract.Defaults(specialized, snapshot.Website, package.Design); return Task.CompletedTask; });
            Console.WriteLine("PASS: Authenticated data-only schema discovers FAQ fields/defaults/references and permission-filtered operations without code or secrets.");

            var addUrl = host.Url + "/manage/composition/add?page=home&location=page%3Ahome%3Amain&type=faq";
            var html = await browser.GetStringAsync(addUrl);
            Require(html.Contains("Questions and answers") || html.Contains("questions and answers"), "Custom FAQ was not offered by the board.");
            Require(html.Contains("Question 1") && html.Contains("field.items.0.answer") && html.Contains("maxlength=\"3000\"") && !html.Contains("field.assetId"), "FAQ did not receive its own readable bounded form.");
            form = Form(html); ClearLists(form); form["field.heading"] = "Before you begin"; form["field.items.0.question"] = "Can we start small?"; form["field.items.0.answer"] = "Yes. Start with one room.";
            using var created = await browser.PostAsync(host.Url + "/manage/composition/add", new FormUrlEncodedContent(form)); Require(created.StatusCode == HttpStatusCode.Found, "FAQ board creation failed: " + await created.Content.ReadAsStringAsync());
            snapshot = await Read(); var faq = snapshot.Website.Blocks.Single(b => b.TypeId == "faq");
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition/blocks/" + faq.Id + "?page=home")); ClearLists(form);
            var priorRevision = snapshot.Revision; form["field.items.0.answer"] = "A focused first step."; form["append.field.items"] = "true";
            using var appended = await browser.PostAsync(host.Url + "/manage/composition/blocks/" + faq.Id, new FormUrlEncodedContent(form)); Require(appended.StatusCode == HttpStatusCode.Found, "Generic FAQ list append failed: " + await appended.Content.ReadAsStringAsync());
            snapshot = await Read(); faq = snapshot.Website.Blocks.Single(b => b.Id == faq.Id); Require(faq.Fields.GetProperty("items").GetArrayLength() == 2, "Generic repeated FAQ values did not persist.");
            using var stale = await browser.PostAsync(host.Url + "/manage/composition/blocks/" + faq.Id, new FormUrlEncodedContent(form)); Require(stale.StatusCode == HttpStatusCode.Conflict, "FAQ board accepted stale writes.");
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition/blocks/" + faq.Id)); ClearLists(form); form["field.css"] = "body{}";
            using var unknown = await browser.PostAsync(host.Url + "/manage/composition/blocks/" + faq.Id, new FormUrlEncodedContent(form)); Require(unknown.StatusCode == HttpStatusCode.UnprocessableEntity && (await Read()).Revision == snapshot.Revision, "Unknown board fields were dropped or partially saved.");
            form.Remove("field.css"); form["remove.field.items"] = "1";
            using var removed = await browser.PostAsync(host.Url + "/manage/composition/blocks/" + faq.Id, new FormUrlEncodedContent(form)); Require(removed.StatusCode == HttpStatusCode.Found, "Generic FAQ removal failed.");
            snapshot = await Read(); Require(snapshot.Website.Blocks.Single(b => b.Id == faq.Id).Fields.GetProperty("items").GetArrayLength() == 1 && snapshot.Revision != priorRevision, "FAQ removal lost list semantics.");
            Console.WriteLine("PASS: Custom FAQ board creation/edit/append/removal use registered metadata and reject unknown/stale fields without partial writes.");

            var main = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main");
            using var apiCreated = await Change(snapshot, new CreateBlock(new("home", "main", null), main.Placements.Length, "faq", 1,
                Fields(new FaqFields("API answers", [new("Is entered HTML executed?", "<script>alert('content')</script>")])))); Require(apiCreated.IsSuccessStatusCode, "FAQ API creation failed.");
            snapshot = await Read(); var apiFaq = snapshot.Website.Blocks.Single(b => b.TypeId == "faq" && b.Fields.GetProperty("heading").GetString() == "API answers");
            using var duplicates = await Change(snapshot, new UpdateBlock(apiFaq.Id, Fields(new FaqFields("Duplicates", [new("Same question", "First answer"), new(" same question ", "Second answer")]))));
            Require(duplicates.StatusCode == HttpStatusCode.UnprocessableEntity && (await Read()).Revision == snapshot.Revision, "Typed FAQ distinct-question validator was bypassed.");
            using var excess = await Change(snapshot, new UpdateBlock(apiFaq.Id, Fields(new FaqFields("Too many", Enumerable.Range(0, 21).Select(i => new FaqItem("Question " + i, "Answer")).ToImmutableArray())))); Require(excess.StatusCode == HttpStatusCode.UnprocessableEntity, "FAQ API ignored item bounds.");
            using var unknownApi = await Change(snapshot, new UpdateBlock(apiFaq.Id, Fields(new { heading = "Unknown", items = new[] { new { question = "Q", answer = "A", script = "bad" } } }))); Require(unknownApi.StatusCode == HttpStatusCode.UnprocessableEntity, "FAQ API ignored unknown nested fields.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", reader);
            using var denied = await Change(snapshot, new UpdateBlock(apiFaq.Id, Fields(new FaqFields("Denied", [new("Question", "Answer")])))); Require(denied.StatusCode == HttpStatusCode.Forbidden, "Metadata changed authorization.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            using var updated = await Change(snapshot, new UpdateBlock(faq.Id, Fields(new FaqFields("Changed through API", [new("When can we begin?", "After our first conversation.")])))); Require(updated.IsSuccessStatusCode, "FAQ API update failed.");
            snapshot = await Read(); html = await browser.GetStringAsync(host.Url + "/manage/composition/blocks/" + faq.Id); Require(html.Contains("Changed through API") && html.Contains("When can we begin?"), "Board and API disagree about custom values.");
            Console.WriteLine("PASS: FAQ API creation/editing enforce typed semantic validators, nested unknown-field rejection, limits and account authority.");

            var placement = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main").Placements.Single(p => p.TargetId == apiFaq.Id);
            using var shareDenied = await Change(snapshot, new PromoteShared(placement.Id), ["home"]); Require(shareDenied.StatusCode == HttpStatusCode.Forbidden, "FAQ sharing bypassed separate authority.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", sharedWriter);
            using var shared = await Change(snapshot, new PromoteShared(placement.Id), ["home"]); Require(shared.IsSuccessStatusCode, "Authorized FAQ sharing failed."); snapshot = await Read(); apiFaq = snapshot.Website.Blocks.Single(b => b.Id == apiFaq.Id);
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            using var reference = await Change(snapshot, new ReferenceShared(apiFaq.Owner.Id, new("services", "main", null), 1)); Require(reference.IsSuccessStatusCode, "Page-local FAQ reference failed."); snapshot = await Read();
            using var sharedDenied = await Change(snapshot, new UpdateBlock(apiFaq.Id, apiFaq.Fields), ["home", "services"]); Require(sharedDenied.StatusCode == HttpStatusCode.Forbidden, "Ordinary writer changed shared FAQ.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", sharedWriter);
            using var badImpact = await Change(snapshot, new UpdateBlock(apiFaq.Id, apiFaq.Fields), ["home"]); Require(badImpact.StatusCode == HttpStatusCode.UnprocessableEntity, "FAQ shared update accepted incomplete impact.");
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition/blocks/" + apiFaq.Id + "?page=home")); ClearLists(form); form["field.heading"] = "Shared questions"; form["field.items.0.answer"] = "<script>alert('content')</script>"; form["acknowledge"] = "true";
            var entries = form.Where(p => p.Key != "affected").ToList(); entries.Add(KeyValuePair.Create("affected", "home")); entries.Add(KeyValuePair.Create("affected", "services"));
            using var boardShared = await browser.PostAsync(host.Url + "/manage/composition/blocks/" + apiFaq.Id, new FormUrlEncodedContent(entries)); Require(boardShared.StatusCode == HttpStatusCode.Found, "FAQ board shared-impact save failed."); snapshot = await Read();
            using var preview = await api.PostAsJsonAsync(host.Url + "/api/v2/previews", new { expectedRevision = snapshot.Revision }); Require(preview.StatusCode == HttpStatusCode.Created, "FAQ preview failed: " + await preview.Content.ReadAsStringAsync());
            using var info = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()); var previewUrl = info.RootElement.GetProperty("url").GetString()!;
            var before = await api.GetByteArrayAsync(host.Url + previewUrl); html = Encoding.UTF8.GetString(before);
            Require(html.Contains("<details>") && html.Contains("Shared questions") && html.Contains("&lt;script&gt;") && !html.Contains("<script>alert('content')"), "Custom FAQ content was missing or unsafe in retained preview.");
            var services = await api.GetStringAsync(host.Url + previewUrl + "services/"); Require(services.Contains("Shared questions"), "Shared FAQ missing from second page.");
            using var later = await Change(snapshot, new UpdateBlock(apiFaq.Id, Fields(new FaqFields("Later questions", [new("Later question", "Later answer")]))), ["home", "services"]); Require(later.IsSuccessStatusCode, "Later FAQ update failed.");
            var after = await api.GetByteArrayAsync(host.Url + previewUrl);
            Require(before.SequenceEqual(after), "Later custom content changed retained preview.");
            var store = new SqliteContentSource(Path.Combine(directory, "webspine.db")); var retained = await store.ReadPreviewAsync(info.RootElement.GetProperty("id").GetString()!);
            Require(retained is not null && retained.Files.Single(f => f.Path == "index.html").Bytes.AsSpan().SequenceEqual(before), "Reopened custom preview lost exact bytes.");
            Console.WriteLine("PASS: Shared FAQ needs separate permission/exact impact and renders escaped native disclosures in immutable previews across pages and reopening.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            snapshot = await Read();
            using var newPage = await Change(snapshot, new AddCompositionPage("Updates", "/updates/", "Studio updates"));
            Require(newPage.IsSuccessStatusCode, "Ordinary writer could not create a page: " + await newPage.Content.ReadAsStringAsync());
            var createdPage = (await Read()).Website.Pages[^1];
            var pageContent = createdPage.Regions.Single(r => r.Id == "main").Placements.Single();
            var createdSnapshot = await Read();
            Require(pageContent.Kind == TargetKind.Block && createdSnapshot.Website.Blocks.Single(b => b.Id == pageContent.TargetId).TypeId == "page-title" &&
                CompositionEditor.AffectedPages(createdSnapshot.Website, [apiFaq.Owner.Id]).SequenceEqual(new[] { "home", "services" }),
                "Page creation pulled unrelated shared FAQ content into the new page.");
            var afterPageCreation = await api.GetByteArrayAsync(host.Url + previewUrl);
            Require(before.SequenceEqual(afterPageCreation), "Page creation changed retained output.");
            Console.WriteLine("PASS: Ordinary v2 page creation follows required-area types without pulling unrelated shared library content into the page.");
            if (browserReview)
            {
                Console.WriteLine("Metadata editor browser fixture: " + host.Url + "/manage/composition");
                Console.WriteLine("Owned fixture process: " + host.Process.Id);
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stopped.TrySetResult(); };
                Console.CancelKeyPress += cancel;
                try { await stopped.Task; } finally { Console.CancelKeyPress -= cancel; }
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
