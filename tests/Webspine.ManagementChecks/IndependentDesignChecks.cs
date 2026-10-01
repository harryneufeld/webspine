using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Webspine.Content.Sqlite;
using Webspine.Content.Faq;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Designs.Fieldwork;
using Webspine.Examples;
using Webspine.Rendering.Razor;

static class IndependentDesignChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static JsonElement Fields<T>(T fields) => JsonSerializer.SerializeToElement(fields, CompositionJson.Options);
    private static string Text(BuiltArtifact artifact, string path = "index.html") => Encoding.UTF8.GetString(artifact.Files.Single(f => f.Path == path).Bytes.AsSpan());
    private static Dictionary<string, string> Form(string html) => Regex.Matches(html, "<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"")
        .GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
    private static HttpClient Browser() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new() });
    private static async Task<CompositionSnapshot> Read(HttpClient api, string url) => CompositionJson.Read(await api.GetStringAsync(url + "/api/v2/site"));
    private static Task<HttpResponseMessage> Change(HttpClient api, string url, string revision, CompositionEdit edit, ImmutableArray<string> acknowledged = default) =>
        api.PostAsync(url + "/api/v2/changes", new StringContent(JsonSerializer.Serialize(new { expectedRevision = revision, change = edit,
            acknowledgedPages = acknowledged.IsDefault ? [] : acknowledged }, CompositionJson.Options), Encoding.UTF8, "application/json"));
    private static async Task Setup(HttpClient browser, string url, string title, string mode = "demo")
    {
        var form = Form(await browser.GetStringAsync(url + "/manage")); form["title"] = title; form["mode"] = mode;
        using var response = await browser.PostAsync(url + "/manage/setup", new FormUrlEncodedContent(form));
        Require(response.StatusCode == HttpStatusCode.Found, "Independent setup failed: " + await response.Content.ReadAsStringAsync());
    }
    public static async Task RunAsync(bool browserReview = false)
    {
        var studio = InstalledDesigns.Select("studio", "1"); var fieldwork = InstalledDesigns.Select("fieldwork", "1");
        var studioSeed = studio.Start("Studio proof", true); var serviceSeed = fieldwork.Start("Oak & Hearth", true);
        var legacy = new ContentSnapshot(new("proof", "fixture"), "studio-fixture", studioSeed.Legacy!);
        var identities = LegacyCompositionMapping.Identities(legacy).ToDictionary(m => (m.PageId, m.SectionId));
        var studioSite = studio.LegacyConverter.Convert(legacy, LegacyCompositionMapping.Blocks(legacy), (p, s) => { var identity = identities[(p, s)]; return (identity.BlockId, identity.PlacementId); });
        Require(studioSite.Pages.Length == 5 && serviceSeed.Composition!.Pages.Length == 4 && fieldwork.Start("Blank service", false).Composition!.Pages.Length == 1, "Installed starters lost independent page structures.");
        Require(!Assembly.Load("Webspine.Management").GetReferencedAssemblies().Any(a => a.Name == "Webspine.Demo") &&
            !typeof(FieldworkPackage).Assembly.GetReferencedAssemblies().Any(a => a.Name is "Webspine.Demo" or "Webspine.Designs.Studio"), "The second package or management depends on demo/presentation code.");
        Require(studio.Package.Design.Layout.Regions.Select(r => r.Id).SequenceEqual(new[] { "header", "main", "footer" }) &&
            fieldwork.Package.Design.Layout.Regions.Select(r => r.Id).SequenceEqual(new[] { "brand", "content", "contact" }), "Packages merely share a recolored document structure.");
        Console.WriteLine("PASS: Studio and Fieldwork have independent layouts/starters and no Demo dependency in management or the second package.");

        var fieldCapture = new CapturedComposition(new(2, new("proof", "fixture"), "service-fixture", serviceSeed.Composition!), fieldwork.Package.Design, serviceSeed.Assets);
        var studioCapture = new CapturedComposition(new(2, legacy.Source, legacy.Revision, studioSite), studio.Package.Design, studioSeed.Assets);
        var frozen = await fieldwork.Package.CaptureAsync();
        var serviceArtifact = await fieldwork.Package.BuildAsync(fieldCapture, frozen, "/proof/service");
        var repeat = await fieldwork.Package.BuildAsync(fieldCapture, frozen, "/proof/service");
        var studioArtifact = await studio.Package.BuildAsync(studioCapture, await studio.Package.CaptureAsync(), "/proof/studio");
        Require(serviceArtifact.Digest == repeat.Digest && Text(serviceArtifact).Contains("business-rail") && Text(serviceArtifact).Contains("contact-rail") &&
            Text(studioArtifact).Contains("site-container") && !Text(studioArtifact).Contains("business-rail"), "Independent static documents or deterministic build failed.");
        Require(Text(serviceArtifact).Contains("/proof/service/assets/questions.js") && Text(serviceArtifact).Contains("/proof/service/assets/home-illustration.svg") &&
            Text(serviceArtifact).Contains("service-group") && Text(serviceArtifact, "services/index.html").Contains("business-rail"), "Nested/shared content or captured prefixes/assets missing.");
        var changedPackage = new RazorDesignPackage(fieldwork.Package.Descriptor, fieldwork.Package.ContentTypes, fieldwork.Package.Design, fieldwork.Package.DocumentComponent,
            fieldwork.Package.Bindings.Values, fieldwork.Package.Assets.SetItem("assets/questions.js", Encoding.UTF8.GetBytes("// deliberate script revision\n").ToImmutableArray()));
        var originalScript = Text(serviceArtifact, "assets/questions.js");
        var changedArtifact = await changedPackage.BuildAsync(fieldCapture, await changedPackage.CaptureAsync());
        Require(changedArtifact.DesignRevision != serviceArtifact.DesignRevision && Text(serviceArtifact, "assets/questions.js") == originalScript &&
            originalScript.Contains("addEventListener"), "Design edits changed retained output or failed to change new candidate identity.");
        Console.WriteLine("PASS: Both packages render frozen content through the same pipeline with different structure; nested/shared content and changed designs preserve retained artifacts.");

        var directory = Path.Combine(Path.GetTempPath(), "webspine-independent-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var host = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true, designPackage: "fieldwork", designVersion: "1");
            using var browser = Browser(); using var api = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            await host.WaitHealthyAsync(browser); await AccountChecks.BootstrapAsync(browser, host.Url);
            Require((await browser.GetStringAsync(host.Url + "/manage")).Contains("Use service business example"), "Management did not consume installed setup labels.");
            await Setup(browser, host.Url, "Oak & Hearth");
            var writer = await AccountChecks.Issue(browser, host.Url, "Service writer", ["content:read", "content:write", "preview:build", "preview:read"]);
            var shared = await AccountChecks.Issue(browser, host.Url, "Shared service writer", ["content:read", "content:write", "content:shared:write", "preview:read"]);
            api.DefaultRequestHeaders.Authorization = new("Bearer", writer);
            var snapshot = await Read(api, host.Url); var firstRevision = snapshot.Revision;
            Require(snapshot.Website.LayoutId == "service" && snapshot.Website.Pages.Length == 4, "Native service starter was reinterpreted as legacy content.");
            var source = new SqliteContentSource(Path.Combine(directory, "webspine.db"), fieldwork.Package.ContentTypes);
            Require((await source.HeadAsync())?.Version == 2, "Service starter did not commit native v2.");
            var add = await browser.GetStringAsync(host.Url + "/manage/composition/add?page=home&location=page%3Ahome%3Acontent&type=faq"); var form = Form(add);
            form["field.heading"] = "Planning your project"; form["field.items.0.question"] = "Do I need a long list?"; form["field.items.0.answer"] = "A single job is a good place to start.";
            form.Remove("append.field.items");
            using var added = await browser.PostAsync(host.Url + "/manage/composition/add", new FormUrlEncodedContent(form)); Require(added.StatusCode == HttpStatusCode.Found, "Service board FAQ creation failed.");
            snapshot = await Read(api, host.Url); var faq = snapshot.Website.Blocks.Single(b => b.TypeId == "faq" && b.Fields.GetProperty("heading").GetString() == "Planning your project");
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition/blocks/" + faq.Id + "?page=home"));
            form["field.heading"] = "Your project questions"; form["field.items.0.question"] = "Can we start small?"; form["field.items.0.answer"] = "Yes. One room or one repair is a useful first step.";
            form.Remove("append.field.items"); using var saved = await browser.PostAsync(host.Url + "/manage/composition/blocks/" + faq.Id, new FormUrlEncodedContent(form));
            Require(saved.StatusCode == HttpStatusCode.Found && (await browser.GetStringAsync(host.Url + "/manage/composition?page=home")).Contains("Your project questions"), "Metadata board edit did not reach service content.");
            Console.WriteLine("PASS: Fieldwork setup creates native v2; generic board creation/editing of the independent FAQ requires no type switches.");

            snapshot = await Read(api, host.Url);
            using var apiCreated = await Change(api, host.Url, snapshot.Revision, new CreateBlock(new("services", "content", null), snapshot.Website.Pages.Single(p => p.Id == "services").Regions.Single(r => r.Id == "content").Placements.Length,
                "faq", 1, Fields(new FaqFields("Service questions", [new("Will you explain the work?", "We agree the plan before we begin.")])))); Require(apiCreated.IsSuccessStatusCode, "Service API custom creation failed.");
            snapshot = await Read(api, host.Url);
            using var stale = await Change(api, host.Url, firstRevision, new UpdateBlock(faq.Id, faq.Fields)); Require(stale.StatusCode == HttpStatusCode.Conflict, "Service API accepted stale revision.");
            using var invalid = await Change(api, host.Url, snapshot.Revision, new UpdateBlock(faq.Id, Fields(new FaqFields("Invalid questions", [new("Same?", "One"), new("same?", "Two")])))); Require(invalid.StatusCode == HttpStatusCode.UnprocessableEntity, "Service API bypassed custom typed validation.");
            var contact = snapshot.Website.Blocks.Single(b => b.Id == "business-contact"); var contactFields = Fields(new ContactFields("A clear next step", "Tell us what you have in mind. We will help you make a plan.", "Plan a visit", "/contact/"));
            using var forbidden = await Change(api, host.Url, snapshot.Revision, new UpdateBlock(contact.Id, contactFields)); Require(forbidden.StatusCode == HttpStatusCode.Forbidden, "Ordinary writer changed shared service contact.");
            api.DefaultRequestHeaders.Authorization = new("Bearer", shared);
            using var missingImpact = await Change(api, host.Url, snapshot.Revision, new UpdateBlock(contact.Id, contactFields), ["home"]); Require(missingImpact.StatusCode == HttpStatusCode.UnprocessableEntity, "Shared service update accepted wrong impact.");
            using var sharedSave = await Change(api, host.Url, snapshot.Revision, new UpdateBlock(contact.Id, contactFields), snapshot.Website.Pages.Select(p => p.Id).Order(StringComparer.Ordinal).ToImmutableArray()); Require(sharedSave.IsSuccessStatusCode, "Shared service contact edit failed.");
            snapshot = await Read(api, host.Url);
            var escapedFields = Fields(new FaqFields("Your project questions", [new("Can we start small?", "Yes. One room or one repair is a useful first step."), new("<script>question</script>", "Plain <b>answer</b>")]));
            using var safeEdit = await Change(api, host.Url, snapshot.Revision, new UpdateBlock(faq.Id, escapedFields)); Require(safeEdit.IsSuccessStatusCode, "Service API update failed.");
            Console.WriteLine("PASS: Service API creation/update enforces semantic validation, conditional revisions and exact shared authority/impact across four pages.");

            api.DefaultRequestHeaders.Authorization = new("Bearer", writer); snapshot = await Read(api, host.Url);
            using var preview = await api.PostAsync(host.Url + "/api/v2/previews", new StringContent(JsonSerializer.Serialize(new { expectedRevision = snapshot.Revision }), Encoding.UTF8, "application/json"));
            Require(preview.StatusCode == HttpStatusCode.Created, "Service preview failed: " + await preview.Content.ReadAsStringAsync());
            using var info = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()); var previewId = info.RootElement.GetProperty("id").GetString()!; var previewUrl = info.RootElement.GetProperty("url").GetString()!;
            var before = await api.GetByteArrayAsync(host.Url + previewUrl); var servicesBefore = await api.GetByteArrayAsync(host.Url + previewUrl + "services/");
            var html = Encoding.UTF8.GetString(before); Require(html.Contains("&lt;script&gt;question&lt;/script&gt;") && html.Contains("&lt;b&gt;answer&lt;/b&gt;") && !html.Contains("<script>question"), "Service content entered executable markup.");
            using var script = await api.GetAsync(host.Url + previewUrl + "assets/questions.js"); Require(script.IsSuccessStatusCode && script.Content.Headers.ContentType?.MediaType == "text/javascript" && script.Headers.GetValues("Content-Security-Policy").Single().Contains("script-src 'self'"), "Service script delivery omitted the declared MIME/CSP policy.");
            Require((await api.GetByteArrayAsync(host.Url + previewUrl + "assets/home-illustration.svg")).Length > 100, "Service preview omitted captured image.");
            using var manifest = JsonDocument.Parse(await api.GetStringAsync(host.Url + previewUrl + "design-package-manifest.json")); var root = manifest.RootElement;
            Require(root.GetProperty("package").GetProperty("id").GetString() == "fieldwork" && root.GetProperty("contentTypes").EnumerateArray().Any(t => t.GetProperty("descriptor").GetProperty("id").GetString() == "faq" && t.GetProperty("editor").GetProperty("label").GetString() == "Questions and answers"), "Service provenance omitted selected package/content metadata.");
            using (var inputs = ZipFile.OpenRead(Path.Combine(directory, "build-inputs", previewId + ".zip"))) Require(inputs.GetEntry("content.json") is not null && inputs.GetEntry("design.json") is not null && inputs.GetEntry("assets/questions.js") is not null && inputs.GetEntry("assets/home-illustration.svg") is not null, "Private service inputs omitted content/design/assets.");
            using (var runtime = ZipFile.OpenRead(Path.Combine(directory, "build-inputs", root.GetProperty("executable").GetProperty("digest").GetString() + ".runtime.zip"))) Require(runtime.GetEntry("application/Webspine.Designs.Fieldwork.dll") is not null && runtime.GetEntry("application/Webspine.Content.Faq.dll") is not null, "Service executable dependency bytes were not retained.");
            snapshot = await Read(api, host.Url); using var later = await Change(api, host.Url, snapshot.Revision, new UpdateBlock(faq.Id, Fields(new FaqFields("Your project questions", [new("Can we start small?", "Yes. One room or one repair is a useful first step.")])))); Require(later.IsSuccessStatusCode, "Later service edit failed.");
            var after = await api.GetByteArrayAsync(host.Url + previewUrl); var servicesAfter = await api.GetByteArrayAsync(host.Url + previewUrl + "services/");
            Require(before.SequenceEqual(after) && servicesBefore.SequenceEqual(servicesAfter), "Service edits rewrote retained website bytes.");
            var reopened = await new SqliteContentSource(Path.Combine(directory, "webspine.db"), fieldwork.Package.ContentTypes).ReadPreviewAsync(previewId);
            Require(reopened is not null && before.SequenceEqual(reopened.Files.Single(f => f.Path == "index.html").Bytes), "Reopened service artifact lost exact bytes.");
            Console.WriteLine("PASS: Service previews retain escaped content, package/metadata/executable/asset provenance and exact protected bytes after edits and storage reopening.");
            await VerifyCompatibility();
            if (browserReview)
            {
                Console.WriteLine("FIELDWORK REVIEW: " + host.Url + "/manage | PID " + host.Process.Id + " | data " + directory);
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stopped.TrySetResult(); }; Console.CancelKeyPress += cancel;
                try { await stopped.Task; } finally { Console.CancelKeyPress -= cancel; }
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static async Task VerifyCompatibility()
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-design-compatibility-" + Guid.NewGuid().ToString("N"));
        using var browser = Browser(); byte[] oldBytes; string oldPreview; string legacyRevision;
        try
        {
            await using (var studio = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await studio.WaitHealthyAsync(browser); await AccountChecks.BootstrapAsync(browser, studio.Url); await Setup(browser, studio.Url, "Retained studio");
                var source = new SqliteContentSource(Path.Combine(directory, "webspine.db")); legacyRevision = (await source.ReadAsync()).Revision;
                var form = Form(await browser.GetStringAsync(studio.Url + "/manage")); using var preview = await browser.PostAsync(studio.Url + "/manage/preview", new FormUrlEncodedContent(form));
                Require(preview.StatusCode == HttpStatusCode.Found, "Compatibility v1 preview failed."); oldPreview = preview.Headers.Location!.ToString(); oldBytes = await browser.GetByteArrayAsync(studio.Url + oldPreview);
            }
            await using (var incompatible = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true, designPackage: "fieldwork"))
            {
                await incompatible.WaitHealthyAsync(browser); Require(Enumerable.SequenceEqual(oldBytes, await browser.GetByteArrayAsync(incompatible.Url + oldPreview)), "Package selection changed historical v1 preview.");
                var form = Form(await browser.GetStringAsync(incompatible.Url + "/manage")); using var migration = await browser.PostAsync(incompatible.Url + "/manage/upgrade-composition", new FormUrlEncodedContent(form));
                Require(migration.StatusCode == HttpStatusCode.NotImplemented, "Unsupported legacy mapping did not fail explicitly.");
                using var newPreview = await browser.PostAsync(incompatible.Url + "/manage/preview", new FormUrlEncodedContent(form)); Require(newPreview.StatusCode == HttpStatusCode.NotImplemented, "Incompatible design silently rendered v1 with different styles.");
                Require((await new SqliteContentSource(Path.Combine(directory, "webspine.db")).ReadAsync()).Revision == legacyRevision, "Unsupported design mutated legacy head.");
            }
            byte[] compositionBytes; string compositionPreview; string compositionRevision;
            await using (var studio = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true))
            {
                await studio.WaitHealthyAsync(browser); var form = Form(await browser.GetStringAsync(studio.Url + "/manage"));
                using var migration = await browser.PostAsync(studio.Url + "/manage/upgrade-composition", new FormUrlEncodedContent(form)); Require(migration.StatusCode == HttpStatusCode.Found, "Original design could not migrate retained v1.");
                var source = new SqliteContentSource(Path.Combine(directory, "webspine.db"), InstalledDesigns.Select("studio", "1").Package.ContentTypes); compositionRevision = (await source.ReadCompositionAsync()).Revision;
                form = Form(await browser.GetStringAsync(studio.Url + "/manage/composition")); using var preview = await browser.PostAsync(studio.Url + "/manage/composition/preview", new FormUrlEncodedContent(form));
                Require(preview.StatusCode == HttpStatusCode.Found, "Compatibility v2 preview failed."); compositionPreview = preview.Headers.Location!.ToString(); compositionBytes = await browser.GetByteArrayAsync(studio.Url + compositionPreview);
            }
            await using (var incompatible = CheckHost.Start("Development", false, dataDirectory: directory, managementEnabled: true, designPackage: "fieldwork"))
            {
                await incompatible.WaitHealthyAsync(browser); using var board = await browser.GetAsync(incompatible.Url + "/manage/composition"); Require(board.StatusCode == HttpStatusCode.UnprocessableEntity, "Incompatible package silently reinterpreted v2 draft.");
                Require(Enumerable.SequenceEqual(compositionBytes, await browser.GetByteArrayAsync(incompatible.Url + compositionPreview)), "Wrong selected design altered retained v2 preview.");
                var source = new SqliteContentSource(Path.Combine(directory, "webspine.db")); Require((await source.HeadAsync())!.Revision == compositionRevision, "Incompatible package changed v2 head.");
                var restored = await source.RestoreLegacyAsync(compositionRevision, legacyRevision); Require(restored.Website.Title == "Retained studio" && restored.Revision != legacyRevision &&
                    Enumerable.SequenceEqual(oldBytes, await browser.GetByteArrayAsync(incompatible.Url + oldPreview)), "Legacy recovery lost source or historical output.");
            }
            Console.WriteLine("PASS: Wrong-package v1/v2 authoring fails without reinterpretation; historical previews and explicit fresh-revision recovery remain intact.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
