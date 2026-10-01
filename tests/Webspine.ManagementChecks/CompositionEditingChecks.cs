using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Demo;

static class CompositionEditingChecks
{
    private static void Require(bool test, string message) { if (!test) throw new Exception(message); }
    private static async Task Fails<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static Dictionary<string, string> Form(string html) => Regex.Matches(html, "<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"").GroupBy(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
    private static JsonElement Fields(object value) => JsonSerializer.SerializeToElement(value, value.GetType(), CompositionJson.Options);
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "webspine-editing-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var registry = DemoComposition.Registry(); var design = await DemoComposition.DesignAsync();
            var store = new SqliteContentSource(Path.Combine(directory, "typed.db"), registry); await store.InitializeSchemaAsync();
            var legacy = await new DemoContentSource(await File.ReadAllTextAsync(Path.Combine(DemoSite.FixtureDirectory, "site.json"))).ReadAsync();
            var mappings = LegacyCompositionMapping.Identities(legacy).ToDictionary(m => (m.PageId, m.SectionId));
            var site = DemoComposition.Convert(legacy, LegacyCompositionMapping.Blocks(legacy), (p, s) => { var m = mappings[(p, s)]; return (m.BlockId, m.PlacementId); });
            var assets = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>();
            foreach (var a in site.Assets) assets.Add(a.File, (await File.ReadAllBytesAsync(Path.Combine(DemoSite.FixtureDirectory, a.File))).ToImmutableArray());
            var snapshot = await store.CreateCompositionAsync(site, design, assets.ToImmutable());
            var editor = new CompositionEditor(store, design, registry); var admin = new CompositionAuthority(true, true, true); var ordinary = new CompositionAuthority(true, false, false);
            var home = new CompositionLocation("home", "main", null);
            snapshot = await editor.ApplyAsync(snapshot.Revision, new CreateBlock(home, 1, "text", 1, Fields(new TextFields("New", "Editable"))), ordinary, []);
            var added = snapshot.Website.Blocks.Single(b => b.TypeId == "text" && b.Fields.GetProperty("heading").GetString() == "New");
            var placement = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main").Placements.Single(p => p.TargetId == added.Id);
            var previous = snapshot.Revision;
            snapshot = await editor.ApplyAsync(snapshot.Revision, new UpdateBlock(added.Id, Fields(new TextFields("Saved", "Editable"))), ordinary, []);
            await Fails<CompositionRevisionException>(() => editor.ApplyAsync(previous, new DeletePlacement(placement.Id), admin, []));
            await Fails<CompositionPermissionException>(() => editor.ApplyAsync(snapshot.Revision, new UpdateBlock(added.Id, Fields(new TextFields("No", "No"))), new(false, false, false), []));
            await Fails<ContentValidationException>(() => editor.ApplyAsync(snapshot.Revision, new UpdateBlock(added.Id, Fields(new { heading = "No", text = "No", css = "body{}" })), admin, []));
            Require((await store.ReadCompositionAsync()).Revision == snapshot.Revision, "Rejected edits committed.");
            Console.WriteLine("PASS: Typed create/update enforce authorization, registered fields and revision conflicts without partial writes.");

            snapshot = await editor.ApplyAsync(snapshot.Revision, new GroupPlacements(home, [placement.Id], Fields(new GroupFields("stack", "start", "medium", 1))), ordinary, []);
            var group = snapshot.Website.Blocks.Single(b => b.TypeId == "group");
            var outer = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main").Placements.Single(p => p.TargetId == group.Id);
            await Fails<ContentValidationException>(() => editor.ApplyAsync(snapshot.Revision, new MovePlacement(outer.Id, new(null, null, group.Id), 0), admin, []));
            await Fails<ContentValidationException>(() => editor.ApplyAsync(snapshot.Revision, new MovePlacement(outer.Id, new("services", "main", null), 1), admin, []));
            snapshot = await editor.ApplyAsync(snapshot.Revision, new MovePlacement(placement.Id, home, 1), ordinary, []);
            Require(snapshot.Website.Blocks.Single(b => b.Id == added.Id).Owner == new BlockOwner(OwnerKind.Page, "home"), "Move changed ownership.");
            Console.WriteLine("PASS: Group/move preserve IDs and ownership; descendant moves and cross-owner moves fail.");

            await Fails<CompositionPermissionException>(() => editor.ApplyAsync(snapshot.Revision, new PromoteShared(placement.Id), ordinary, ["home"]));
            await Fails<ContentValidationException>(() => editor.ApplyAsync(snapshot.Revision, new PromoteShared(placement.Id), admin, []));
            snapshot = await editor.ApplyAsync(snapshot.Revision, new PromoteShared(placement.Id), admin, ["home"]);
            var sharedId = snapshot.Website.Blocks.Single(b => b.Id == added.Id).Owner.Id;
            snapshot = await editor.ApplyAsync(snapshot.Revision, new ReferenceShared(sharedId, new("services", "main", null), 1), ordinary, []);
            var impact = CompositionEditor.AffectedPages(snapshot.Website, [sharedId]); Require(impact.SequenceEqual(new[] { "home", "services" }), "Shared impact incorrect.");
            await Fails<CompositionPermissionException>(() => editor.ApplyAsync(snapshot.Revision, new UpdateBlock(added.Id, Fields(new TextFields("Denied", "Denied"))), ordinary, impact));
            await Fails<ContentValidationException>(() => editor.ApplyAsync(snapshot.Revision, new UpdateBlock(added.Id, Fields(new TextFields("Missing page", "No"))), admin, ["home"]));
            snapshot = await editor.ApplyAsync(snapshot.Revision, new UpdateBlock(added.Id, Fields(new TextFields("Shared", "Across pages"))), admin, impact);
            await Fails<ContentValidationException>(() => editor.ApplyAsync(snapshot.Revision, new DeleteShared(sharedId), admin, impact));
            snapshot = await editor.ApplyAsync(snapshot.Revision, new DetachPlacement(placement.Id), ordinary, []);
            var detached = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main").Placements.Single(p => p.Id == placement.Id);
            Require(detached.Kind == TargetKind.Block && detached.TargetId != added.Id, "Detach reused the shared Block ID.");
            Console.WriteLine("PASS: Sharing needs separate permission and exact affected-page acknowledgement; referencing/detaching stay page-local.");

            var readOnly = new LimitedSource(store);
            await Fails<SourceOperationNotSupportedException>(() => new CompositionEditor(readOnly, design, registry).ApplyAsync(snapshot.Revision, new DeletePlacement(detached.Id), admin, []));
            Require(readOnly.Commits == 0 && (await store.ReadCompositionAsync()).Revision == snapshot.Revision, "Unsupported adapter operation fell back or committed.");
            Console.WriteLine("PASS: An independent adapter rejects unsupported writes without hidden spinecms fallback.");

            var png = Png(); var media = CompositionMedia.Png(png); Require(media.Asset.ContentType == "image/png" && CompositionMedia.Png(png).Asset == media.Asset, "Media IDs are not content-addressed.");
            var bad = png.ToArray(); bad[^1] ^= 1;
            await Fails<ContentValidationException>(() => Task.Run(() => CompositionMedia.Png(bad)));
            await Fails<ContentValidationException>(() => Task.Run(() => CompositionMedia.Png(Png(truncateCompression: true))));
            await Fails<ContentValidationException>(() => Task.Run(() => CompositionMedia.Png("<svg onload='evil()'/>"u8.ToArray())));
            await Fails<ContentValidationException>(() => Task.Run(() => CompositionMedia.Png(new byte[CompositionMedia.MaximumBytes + 1])));
            Console.WriteLine("PASS: Media validates PNG structure/CRC/decompression and rejects corrupt, executable and oversized uploads.");

            using var browser = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new() });
            using var api = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            var httpDirectory = Path.Combine(directory, "http");
            await using var host = CheckHost.Start("Development", false, dataDirectory: httpDirectory, managementEnabled: true); await host.WaitHealthyAsync(browser);
            await AccountChecks.BootstrapAsync(browser, host.Url);
            var form = Form(await browser.GetStringAsync(host.Url + "/manage")); form["mode"] = "demo"; form["title"] = "Composition studio";
            using var setup = await browser.PostAsync(host.Url + "/manage/setup", new FormUrlEncodedContent(form)); Require(setup.StatusCode == HttpStatusCode.Found, "HTTP setup failed.");
            var ordinaryToken = await AccountChecks.Issue(browser, host.Url, "Existing content writer", ["content:read", "content:write", "preview:build", "preview:read"]);
            var fullToken = await AccountChecks.Issue(browser, host.Url, "Shared writer", ["content:read", "content:write", "content:shared:write", "settings:write", "preview:build", "preview:read"]);
            form = Form(await browser.GetStringAsync(host.Url + "/manage"));
            using var upgrade = await browser.PostAsync(host.Url + "/manage/upgrade-composition", new FormUrlEncodedContent(form)); Require(upgrade.StatusCode == HttpStatusCode.Found, "HTTP migration failed: " + await upgrade.Content.ReadAsStringAsync());
            api.DefaultRequestHeaders.Authorization = new("Bearer", ordinaryToken);
            async Task<CompositionSnapshot> Read() => JsonSerializer.Deserialize<CompositionSnapshot>(await api.GetStringAsync(host.Url + "/api/v2/site"), CompositionJson.Options)!;
            async Task<HttpResponseMessage> Change(CompositionSnapshot s, CompositionEdit edit, ImmutableArray<string> pages) => await api.PostAsync(host.Url + "/api/v2/changes", new StringContent(JsonSerializer.Serialize(new { expectedRevision = s.Revision, change = edit, acknowledgedPages = pages }, CompositionJson.Options), Encoding.UTF8, "application/json"));
            snapshot = await Read();
            using var legacyApi = await api.GetAsync(host.Url + "/api/v1/site"); Require(legacyApi.StatusCode == HttpStatusCode.Conflict, "Legacy API did not refuse v2.");
            var footer = snapshot.Website.Blocks.Single(b => b.TypeId == "site-footer"); impact = CompositionEditor.AffectedPages(snapshot.Website, [footer.Owner.Id]);
            using var denied = await Change(snapshot, new UpdateBlock(footer.Id, Fields(new FooterFields("Forbidden"))), impact); Require(denied.StatusCode == HttpStatusCode.Forbidden, "Old scoped token gained shared authority.");
            using var cookieApi = await browser.GetAsync(host.Url + "/api/v2/site"); Require(cookieApi.StatusCode == HttpStatusCode.Unauthorized, "v2 accepted cookie authentication.");
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fullToken);
            using var saved = await Change(snapshot, new UpdateBlock(footer.Id, Fields(new FooterFields("Changed through API"))), impact); Require(saved.IsSuccessStatusCode, "Typed API edit failed: " + await saved.Content.ReadAsStringAsync());
            using var stale = await Change(snapshot, new UpdateBlock(footer.Id, Fields(new FooterFields("Stale"))), impact); Require(stale.StatusCode == HttpStatusCode.Conflict, "API accepted stale edit.");
            snapshot = await Read();
            var html = await browser.GetStringAsync(host.Url + "/manage/composition/blocks/" + footer.Id); Require(html.Contains("Changed through API") && html.Contains("Shared content impact"), "Board omitted API content/shared impact.");
            form = Form(html); form["field.message"] = "Changed through board"; form["acknowledge"] = "true";
            var entries = form.Where(p => p.Key != "affected").ToList(); entries.AddRange(impact.Select(id => KeyValuePair.Create("affected", id)));
            using var boardSave = await browser.PostAsync(host.Url + "/manage/composition/blocks/" + footer.Id, new FormUrlEncodedContent(entries)); Require(boardSave.StatusCode == HttpStatusCode.Found, "Board shared save failed: " + await boardSave.Content.ReadAsStringAsync());
            snapshot = await Read(); Require(snapshot.Website.Blocks.Single(b => b.Id == footer.Id).Fields.GetProperty("message").GetString() == "Changed through board", "Board and API disagree.");
            using var noCsrf = await browser.PostAsync(host.Url + "/manage/composition/add", new FormUrlEncodedContent(new Dictionary<string, string> { ["revision"] = snapshot.Revision })); Require(noCsrf.StatusCode == HttpStatusCode.BadRequest, "Composition board lost CSRF protection.");
            Console.WriteLine("PASS: Board and bearer API use equivalent edits, exact shared impact, scoped credentials, CSRF and conditional revisions.");

            var cardBlock = snapshot.Website.Blocks.First(b => b.TypeId == "cards"); var oldCount = cardBlock.Fields.GetProperty("items").GetArrayLength();
            form = Form(await browser.GetStringAsync(host.Url + "/manage/composition/blocks/" + cardBlock.Id));
            foreach (var key in form.Keys.Where(k => k.StartsWith("remove.", StringComparison.Ordinal) || k.StartsWith("append.", StringComparison.Ordinal)).ToArray()) form.Remove(key);
            form["append.field.items"] = "true";
            using var cardAdded = await browser.PostAsync(host.Url + "/manage/composition/blocks/" + cardBlock.Id, new FormUrlEncodedContent(form)); Require(cardAdded.StatusCode == HttpStatusCode.Found, "Board could not append a card.");
            snapshot = await Read(); Require(snapshot.Website.Blocks.Single(b => b.Id == cardBlock.Id).Fields.GetProperty("items").GetArrayLength() == oldCount + 1, "Card append did not persist.");
            var selected = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main").Placements.First(p => p.Kind == TargetKind.Block && snapshot.Website.Blocks.Single(b => b.Id == p.TargetId).TypeId == "text");
            var overviewForm = Form(await browser.GetStringAsync(host.Url + "/manage/composition"));
            form = new() { ["__RequestVerificationToken"] = overviewForm["__RequestVerificationToken"], ["revision"] = snapshot.Revision, ["operation"] = "promote", ["placement"] = selected.Id, ["location"] = "page:home:main", ["index"] = "1" };
            using var confirmation = await browser.PostAsync(host.Url + "/manage/composition/placements/" + selected.Id + "/promote", new FormUrlEncodedContent(form));
            Require(confirmation.StatusCode == HttpStatusCode.OK && (await confirmation.Content.ReadAsStringAsync()).Contains("Review shared change") && (await Read()).Revision == snapshot.Revision, "Shared arrangement committed before review.");
            form = Form(await confirmation.Content.ReadAsStringAsync()); form["acknowledge"] = "true";
            using var confirmed = await browser.PostAsync(host.Url + "/manage/composition/confirm", new FormUrlEncodedContent(form)); Require(confirmed.StatusCode == HttpStatusCode.Found, "Shared arrangement confirmation failed.");
            snapshot = await Read(); Require(snapshot.Website.Blocks.Single(b => b.Id == selected.TargetId).Owner.Kind == OwnerKind.Shared, "Confirmed promotion did not persist.");
            using var export = await browser.GetAsync(host.Url + "/manage/composition/export");
            using var zip = new ZipArchive(new MemoryStream(await export.Content.ReadAsByteArrayAsync())); Require(zip.GetEntry("content.json") is not null && zip.GetEntry("assets/studio.svg") is not null, "Composition export omitted content/media.");
            Console.WriteLine("PASS: Board list editing, pre-save shared-arrangement review and composition export use validated drafts.");

            html = await browser.GetStringAsync(host.Url + "/manage/composition?page=home");
            Require(html.Contains("Move up") && html.Contains("Move down") && html.Contains("Add after this element") && html.Contains("Group selected") && !html.Contains("Arrange content"), "Page structure did not expose contextual controls.");
            var addUrl = host.Url + "/manage/composition/add?page=home&location=page%3Ahome%3Amain&type=text&after=" + selected.Id;
            html = await browser.GetStringAsync(addUrl);
            Require(html.Contains("field.heading") && html.Contains("field.text") && !html.Contains("field.assetId") && !html.Contains("field.mode"), "Text creation exposed fields belonging to other types.");
            form = Form(html); form["field.heading"] = "Contextual addition"; form["field.text"] = "Created beside the selected element";
            using var contextualAdd = await browser.PostAsync(host.Url + "/manage/composition/add", new FormUrlEncodedContent(form));
            Require(contextualAdd.StatusCode == HttpStatusCode.Found && contextualAdd.Headers.Location!.ToString().EndsWith("?page=home"), "Contextual addition lost the current page.");
            snapshot = await Read();
            var uiBlock = snapshot.Website.Blocks.Single(b => b.TypeId == "text" && b.Fields.GetProperty("heading").GetString() == "Contextual addition");
            var uiMain = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main");
            var uiPlacement = uiMain.Placements.Single(p => p.TargetId == uiBlock.Id);
            var uiIndex = Array.FindIndex(uiMain.Placements.ToArray(), p => p.Id == uiPlacement.Id);
            Require(uiIndex > 0 && uiMain.Placements[uiIndex - 1].Id == selected.Id, "Add-after ignored its insertion point.");
            form = new() { ["__RequestVerificationToken"] = overviewForm["__RequestVerificationToken"], ["revision"] = snapshot.Revision, ["page"] = "home", ["direction"] = "up" };
            using var movedUp = await browser.PostAsync(host.Url + "/manage/composition/placements/" + uiPlacement.Id + "/reorder", new FormUrlEncodedContent(form));
            Require(movedUp.StatusCode == HttpStatusCode.Found, "Inline move up failed.");
            using var staleMove = await browser.PostAsync(host.Url + "/manage/composition/placements/" + uiPlacement.Id + "/reorder", new FormUrlEncodedContent(form));
            Require(staleMove.StatusCode == HttpStatusCode.Conflict, "Inline reorder accepted a stale revision.");
            snapshot = await Read(); uiMain = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main");
            Require(uiMain.Placements[uiIndex - 1].Id == uiPlacement.Id && uiMain.Placements[uiIndex].Id == selected.Id, "Move up changed identity or order incorrectly.");
            form["revision"] = snapshot.Revision; form["direction"] = "down";
            using var movedDown = await browser.PostAsync(host.Url + "/manage/composition/placements/" + uiPlacement.Id + "/reorder", new FormUrlEncodedContent(form));
            Require(movedDown.StatusCode == HttpStatusCode.Found, "Inline move down failed."); snapshot = await Read();
            using var invalidType = await browser.GetAsync(host.Url + "/manage/composition/add?page=home&location=page%3Ahome%3Aheader&type=text");
            Require(invalidType.StatusCode == HttpStatusCode.UnprocessableEntity, "Creation offered a type forbidden in this area.");
            Console.WriteLine("PASS: Contextual add-after and inline reorder preserve placement identity/current page and reject stale or forbidden changes.");

            form = new() { ["__RequestVerificationToken"] = overviewForm["__RequestVerificationToken"], ["revision"] = snapshot.Revision, ["page"] = "home", ["location"] = "page:home:main", ["selection"] = uiPlacement.Id };
            using var groupScreen = await browser.PostAsync(host.Url + "/manage/composition/group", new FormUrlEncodedContent(form));
            html = await groupScreen.Content.ReadAsStringAsync();
            Require(groupScreen.IsSuccessStatusCode && html.Contains("<select name=\"field.mode\"") && !html.Contains("field.text") && (await Read()).Revision == snapshot.Revision, "Grouping did not offer focused approved fields before saving.");
            form = Form(html); form["field.mode"] = "stack"; form["field.alignment"] = "start"; form["field.spacing"] = "medium"; form["field.columns"] = "1";
            using var boardGroup = await browser.PostAsync(host.Url + "/manage/composition/group/commit", new FormUrlEncodedContent(form)); Require(boardGroup.StatusCode == HttpStatusCode.Found, "Contextual grouping failed.");
            snapshot = await Read(); var uiGroup = snapshot.Website.Blocks.Single(b => b.TypeId == "group");
            Require(uiGroup.Children.Single().Id == uiPlacement.Id, "Grouping lost selected placement identity.");
            html = await browser.GetStringAsync(host.Url + "/manage/composition/placements/" + uiPlacement.Id + "/move?page=home");
            Require(html.Contains("page:home:main") && !html.Contains("page:services:main") && !html.Contains("field.text"), "Move dialog offered another owner or unrelated fields.");
            form = Form(html); form["location"] = "page:home:main";
            using var outOfGroup = await browser.PostAsync(host.Url + "/manage/composition/placements/" + uiPlacement.Id + "/move", new FormUrlEncodedContent(form)); Require(outOfGroup.StatusCode == HttpStatusCode.Found, "Move from nested Group failed."); snapshot = await Read();
            foreach (var removeId in new[] { uiPlacement.Id, snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main").Placements.Single(p => p.TargetId == uiGroup.Id).Id })
            {
                form = new() { ["__RequestVerificationToken"] = overviewForm["__RequestVerificationToken"], ["revision"] = snapshot.Revision, ["page"] = "home" };
                using var removeReview = await browser.PostAsync(host.Url + "/manage/composition/placements/" + removeId + "/remove", new FormUrlEncodedContent(form));
                html = await removeReview.Content.ReadAsStringAsync(); Require(removeReview.IsSuccessStatusCode && html.Contains("Confirm removal") && (await Read()).Revision == snapshot.Revision, "Remove committed before its focused confirmation.");
                using var remove = await browser.PostAsync(host.Url + "/manage/composition/confirm", new FormUrlEncodedContent(Form(html))); Require(remove.StatusCode == HttpStatusCode.Found, "Confirmed removal failed."); snapshot = await Read();
            }
            Require(!snapshot.Website.Blocks.Any(b => b.Id == uiBlock.Id || b.Id == uiGroup.Id), "Board removal left owned content behind.");
            Console.WriteLine("PASS: Focused grouping, same-owner movement and removal confirmation persist through the board without exposing unrelated options.");

            using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, host.Url + "/api/v2/media") { Content = new ByteArrayContent(png) }; uploadRequest.Content.Headers.ContentType = new("image/png"); uploadRequest.Headers.IfMatch.Add(new EntityTagHeaderValue('"' + snapshot.Revision + '"'));
            using var uploaded = await api.SendAsync(uploadRequest); Require(uploaded.IsSuccessStatusCode, "API media upload failed: " + (int)uploaded.StatusCode + " " + await uploaded.Content.ReadAsStringAsync()); snapshot = await Read();
            using var gallery = await browser.GetAsync(host.Url + "/manage/composition/media/" + media.Asset.Id); Require(gallery.IsSuccessStatusCode && (await gallery.Content.ReadAsByteArrayAsync()).SequenceEqual(media.Bytes), "Gallery did not use captured image bytes.");
            using var addImage = await Change(snapshot, new CreateBlock(home, 1, "image", 1, Fields(new ImageFields(media.Asset.Id, "Uploaded image"))), []); Require(addImage.IsSuccessStatusCode, "Uploaded image selection failed."); snapshot = await Read();
            var main = snapshot.Website.Pages.Single(p => p.Id == "home").Regions.Single(r => r.Id == "main"); var groupedIds = main.Placements.Take(2).Select(p => p.Id).ToImmutableArray();
            using var grouped = await Change(snapshot, new GroupPlacements(home, groupedIds, Fields(new GroupFields("stack", "start", "medium", 1))), []); Require(grouped.IsSuccessStatusCode, "API could not group demo content."); snapshot = await Read();
            var outerGroup = snapshot.Website.Blocks.Single(b => b.TypeId == "group");
            using var nested = await Change(snapshot, new GroupPlacements(new(null, null, outerGroup.Id), [groupedIds[1]], Fields(new GroupFields("stack", "start", "small", 1))), []); Require(nested.IsSuccessStatusCode, "API could not nest demo content."); snapshot = await Read();
            Require(snapshot.Website.Pages.Length == 5 && snapshot.Website.SharedBlocks.Count(s => s.Id is "site-header" or "site-footer") == 2 && snapshot.Website.Blocks.Count(b => b.TypeId == "group") == 2, "Five-page nested shared-shell demonstration is incomplete.");
            using var preview = await api.PostAsJsonAsync(host.Url + "/api/v2/previews", new { expectedRevision = snapshot.Revision }); Require(preview.StatusCode == HttpStatusCode.Created, "Composition preview failed: " + await preview.Content.ReadAsStringAsync());
            using var info = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()); var previewUrl = info.RootElement.GetProperty("url").GetString()!;
            var before = await api.GetByteArrayAsync(host.Url + previewUrl); html = Encoding.UTF8.GetString(before);
            Require(html.Contains(previewUrl + "assets/composition.css") && html.Contains(previewUrl + "services/") && html.Contains(previewUrl + media.Asset.File), "Private preview assets/navigation escaped their prefix.");
            foreach (var route in new[] { "", "services/", "products/", "about/", "contact/", "assets/composition.css", media.Asset.File })
            { using var response = await api.GetAsync(host.Url + previewUrl + route); Require(response.IsSuccessStatusCode, "Preview missing " + route); }
            using var later = await Change(snapshot, new EditCompositionSettings("Changed after capture", "en"), []); Require(later.IsSuccessStatusCode, "Composition settings update failed.");
            var retained = await api.GetByteArrayAsync(host.Url + previewUrl);
            Require(before.SequenceEqual(retained), "Later composition edit changed retained bytes.");
            using var privateDenied = await browser.GetAsync(host.Url + previewUrl); Require(privateDenied.StatusCode == HttpStatusCode.Unauthorized, "API preview lost authentication separation.");
            Console.WriteLine("PASS: API upload/selection and five-page composition preview retain exact bytes with private navigation/assets.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static byte[] Png(bool truncateCompression = false)
    {
        using var stream = new MemoryStream(); stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            var length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length); stream.Write(length);
            var bytes = Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); stream.Write(bytes);
            var crc = uint.MaxValue; foreach (var b in bytes) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); stream.Write(length);
        }
        var header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, 1); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1); header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        using var pixels = new MemoryStream(); using (var z = new ZLibStream(pixels, CompressionLevel.SmallestSize, true)) z.Write(new byte[] { 0, 20, 50, 80, 255 });
        var compressed = pixels.ToArray(); Chunk("IDAT", truncateCompression ? compressed[..^4] : compressed); Chunk("IEND", []); return stream.ToArray();
    }
    private sealed class LimitedSource(ICompositionDraftPersistence inner) : ICompositionDraftPersistence
    {
        public int Commits { get; private set; }
        public SourceIdentity Identity => inner.Identity;
        public CompositionCapabilities CompositionCapabilities => inner.CompositionCapabilities with { Operations = [CompositionOperation.Read] };
        public ValueTask<CompositionSnapshot> ReadCompositionAsync(CancellationToken cancellationToken = default) => inner.ReadCompositionAsync(cancellationToken);
        public ValueTask<CapturedComposition> CaptureCompositionAsync(CompositionDesign design, CancellationToken cancellationToken = default) => inner.CaptureCompositionAsync(design, cancellationToken);
        public Task<CompositionSnapshot> CreateCompositionAsync(CompositionWebsite website, CompositionDesign design, ImmutableDictionary<string, ImmutableArray<byte>> assets, CancellationToken cancellationToken = default) => throw new SourceOperationNotSupportedException("Read-only CMS.");
        public Task<CompositionSnapshot> CommitCompositionAsync(string expectedRevision, CompositionWebsite proposed, CompositionDesign design, ImmutableDictionary<string, ImmutableArray<byte>>? newAssets = null, CancellationToken cancellationToken = default) { Commits++; throw new Exception("Unexpected commit."); }
    }
}
