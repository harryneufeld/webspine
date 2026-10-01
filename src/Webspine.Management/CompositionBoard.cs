using System.Collections.Immutable;
using System.Net;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Demo;

namespace Webspine.Management;

internal static class CompositionBoard
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Input(string name, string label, string value) => $"<label>{E(label)}<input name=\"{E(name)}\" value=\"{E(value)}\"></label>";
    private static string Select(string name, string label, IEnumerable<(string Value, string Label)> items) => $"<label>{E(label)}<select name=\"{E(name)}\">{string.Join("", items.Select(i => $"<option value=\"{E(i.Value)}\">{E(i.Label)}</option>"))}</select></label>";
    private static string Form(HttpContext c, string revision, string action, string body, string button) => $"<form class=\"panel editor\" method=\"post\" action=\"{E(action)}\">{ManagementUi.Token(c)}<input type=\"hidden\" name=\"revision\" value=\"{E(revision)}\">{body}<button>{E(button)}</button></form>";
    private static string Acknowledgement(CompositionSnapshot snapshot, IEnumerable<string> sharedIds)
    {
        var ids = CompositionEditor.AffectedPages(snapshot.Website, sharedIds);
        return "<fieldset><legend>Shared content impact</legend><p>Affects: " + E(string.Join(", ", ids.Select(id => snapshot.Website.Pages.Single(p => p.Id == id).Title))) + ". Existing previews stay unchanged.</p>" +
            string.Join("", ids.Select(id => $"<input type=\"hidden\" name=\"affected\" value=\"{E(id)}\">")) + "<label class=\"scope\"><input type=\"checkbox\" name=\"acknowledge\" value=\"true\" required>I reviewed the affected pages.</label></fieldset>";
    }
    public static void MapCompositionBoard(this WebApplication app)
    {
        var board = app.MapGroup("/manage/composition");
        board.AddEndpointFilter(async (invocation, next) =>
        {
            try { return await next(invocation); }
            catch (Exception e) when (CompositionApi.ExpectedError(e)) { return ManagementUi.Problem(e.Message, CompositionApi.ErrorStatus(e)); }
        });
        board.MapGet("", async (CompositionOperations operations, HttpContext c) => Overview(c, await operations.ReadAsync(c.RequestAborted), operations.Source.CompositionCapabilities));
        board.MapGet("/export", async (CompositionOperations operations, HttpContext c) =>
        {
            operations.Source.CompositionCapabilities.RequireCapture();
            var capture = await operations.Source.CaptureCompositionAsync(await DemoComposition.DesignAsync(c.RequestAborted), c.RequestAborted);
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                await using (var target = zip.CreateEntry("content.json").Open()) await JsonSerializer.SerializeAsync(target, capture.Content, CompositionJson.Options, c.RequestAborted);
                foreach (var a in capture.AssetFiles) { await using var target = zip.CreateEntry(a.Key).Open(); await target.WriteAsync(a.Value.ToArray(), c.RequestAborted); }
            }
            return Results.File(stream.ToArray(), "application/zip", "webspine-composition.zip");
        });
        board.MapGet("/media/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            operations.Source.CompositionCapabilities.RequireCapture();
            var capture = await operations.Source.CaptureCompositionAsync(await DemoComposition.DesignAsync(c.RequestAborted), c.RequestAborted);
            var asset = capture.Content.Website.Assets.FirstOrDefault(a => a.Id == id);
            return asset is null ? Results.NotFound() : Results.File(capture.AssetFiles[asset.File].ToArray(), asset.ContentType);
        });
        board.MapGet("/blocks/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted);
            var block = snapshot.Website.Blocks.FirstOrDefault(b => b.Id == id);
            return block is null ? Results.NotFound() : Edit(c, snapshot, block);
        });
        board.MapPost("/blocks/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var form = await c.Request.ReadFormAsync(c.RequestAborted);
            var snapshot = await operations.ReadAsync(c.RequestAborted);
            var block = snapshot.Website.Blocks.FirstOrDefault(b => b.Id == id) ?? throw new ContentValidationException("Block does not exist.");
            var fields = JsonNode.Parse(block.Fields.GetRawText())!;
            SetFields(fields, "field", form);
            await operations.EditAsync(form["revision"].ToString(), new UpdateBlock(id, JsonSerializer.SerializeToElement(fields, CompositionJson.Options)), c.User, Acknowledged(form), c.RequestAborted);
            return Results.Redirect("/manage/composition");
        });
        board.MapPost("/arrange", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            var snapshot = await operations.ReadAsync(c.RequestAborted);
            var location = ParseLocation(f["location"].ToString());
            if (!int.TryParse(f["index"], out var index) || index < 1) throw new ContentValidationException("Position must be a whole number starting at 1.");
            index--;
            var placement = f["placement"].ToString(); var shared = f["shared"].ToString();
            var group = JsonSerializer.SerializeToElement(new GroupFields(f["mode"].ToString(), f["alignment"].ToString(), f["spacing"].ToString(), int.TryParse(f["columns"], out var cols) ? cols : 1), CompositionJson.Options);
            CompositionEdit change = f["operation"].ToString() switch
            {
                "create" => new CreateBlock(location, index, f["type"].ToString(), 1, f["type"] == "group" ? group : SeedFields(f["type"].ToString(), snapshot.Website)),
                "move" => new MovePlacement(placement, location, index),
                "group" => new GroupPlacements(location, f["selection"].Select(s => s!).ToImmutableArray(), group),
                "reference" => new ReferenceShared(shared, location, index),
                "promote" => new PromoteShared(placement), "detach" => new DetachPlacement(placement), "delete" => new DeletePlacement(placement),
                "deleteShared" => new DeleteShared(shared), _ => throw new ContentValidationException("Choose a supported action.")
            };
            var impact = ArrangementImpact(snapshot.Website, change);
            if (impact.SharedChange && f["acknowledge"] != "true")
            {
                var hidden = string.Join("", f.Where(p => p.Key is not "affected" and not "acknowledge" and not "revision" && !p.Key.StartsWith("__", StringComparison.Ordinal)).SelectMany(p => p.Value.Select(v => $"<input type=\"hidden\" name=\"{E(p.Key)}\" value=\"{E(v)}\">")));
                var pages = string.Join(", ", impact.Pages.Select(id => snapshot.Website.Pages.Single(p => p.Id == id).Title));
                var ack = "<h1>Review shared change</h1><p>Affects: " + E(pages) + ". Existing previews stay unchanged.</p>" + string.Join("", impact.Pages.Select(id => $"<input type=\"hidden\" name=\"affected\" value=\"{E(id)}\">")) + "<label class=\"scope\"><input type=\"checkbox\" name=\"acknowledge\" value=\"true\" required>I reviewed these pages.</label>";
                return ManagementUi.Html("Review shared change", Form(c, f["revision"].ToString(), "/manage/composition/arrange", hidden + ack, "Confirm shared change"));
            }
            await operations.EditAsync(f["revision"].ToString(), change, c.User, Acknowledged(f), c.RequestAborted);
            return Results.Redirect("/manage/composition");
        });
        board.MapPost("/page/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            await operations.EditAsync(f["revision"].ToString(), new EditCompositionPage(id, f["title"].ToString(), f["description"].ToString()), c.User, [], c.RequestAborted);
            return Results.Redirect("/manage/composition");
        });
        board.MapPost("/settings", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            await operations.EditAsync(f["revision"].ToString(), new EditCompositionSettings(f["title"].ToString(), f["language"].ToString()), c.User, [], c.RequestAborted);
            return Results.Redirect("/manage/composition");
        });
        board.MapPost("/preview", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            var preview = await operations.PreviewAsync(f["revision"].ToString(), "/manage/preview/", c.RequestAborted);
            return Results.Redirect("/manage/preview/" + preview.Id + "/");
        }).WithMetadata(new ApiPermission("preview:build"));
        board.MapPost("/media", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            if (f.Files.Count != 1 || f.Files[0].ContentType != "image/png") throw new ContentValidationException("Choose one PNG image.");
            await using var stream = f.Files[0].OpenReadStream();
            await operations.MediaAsync(f["revision"].ToString(), await CompositionApi.ReadUpload(stream, c.RequestAborted), c.User, c.RequestAborted);
            return Results.Redirect("/manage/composition");
        });
        app.MapPost("/manage/upgrade-composition", async (SqliteContentSource store, HttpContext c) =>
        {
            if (!Permissions.Has(c.User, "content:shared:write")) return ManagementUi.Problem("Shared content permission is required.", 403);
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            try
            {
                var legacy = await store.ReadAsync(c.RequestAborted);
                var mappings = LegacyCompositionMapping.Identities(legacy);
                var bySection = mappings.ToDictionary(m => (m.PageId, m.SectionId));
                await store.MigrateToCompositionAsync(f["revision"].ToString(), await DemoComposition.DesignAsync(c.RequestAborted),
                    old => DemoComposition.Convert(old, LegacyCompositionMapping.Blocks(old), (page, section) => { var m = bySection[(page, section)]; return (m.BlockId, m.PlacementId); }), mappings, c.RequestAborted);
                return Results.Redirect("/manage/composition");
            }
            catch (Exception e) when (CompositionApi.ExpectedError(e)) { return ManagementUi.Problem(e.Message, CompositionApi.ErrorStatus(e)); }
        }).WithMetadata(new ApiPermission("settings:write"));
    }
    private static ImmutableArray<string> Acknowledged(IFormCollection f) => f["acknowledge"] == "true" ? f["affected"].Select(s => s!).ToImmutableArray() : [];
    private static (bool SharedChange, ImmutableArray<string> Pages) ArrangementImpact(CompositionWebsite site, CompositionEdit edit)
    {
        BlockOwner PlacementOwner(string id)
        {
            var page = site.Pages.FirstOrDefault(p => p.Regions.SelectMany(r => r.Placements).Any(p => p.Id == id));
            return page is not null ? new(OwnerKind.Page, page.Id) : site.Blocks.FirstOrDefault(b => b.Children.Any(p => p.Id == id))?.Owner ?? throw new ContentValidationException("Placement does not exist.");
        }
        BlockOwner LocationOwner(CompositionLocation l) => l.ParentBlockId is null ? new(OwnerKind.Page, l.PageId ?? "") : site.Blocks.FirstOrDefault(b => b.Id == l.ParentBlockId)?.Owner ?? throw new ContentValidationException("Destination does not exist.");
        BlockOwner[] owners = edit switch
        {
            CreateBlock e => [LocationOwner(e.Location)], MovePlacement e => [PlacementOwner(e.PlacementId), LocationOwner(e.Location)], GroupPlacements e => [LocationOwner(e.Location)],
            ReferenceShared e => [LocationOwner(e.Location)], DetachPlacement e => [PlacementOwner(e.PlacementId)], DeletePlacement e => [PlacementOwner(e.PlacementId)], DeleteShared e => [new(OwnerKind.Shared, e.SharedId)],
            PromoteShared e => [PlacementOwner(e.PlacementId)], _ => []
        };
        if (edit is PromoteShared) return (true, [owners[0].Id]);
        var sharedIds = owners.Where(o => o.Kind == OwnerKind.Shared).Select(o => o.Id).ToArray();
        return (sharedIds.Length > 0, CompositionEditor.AffectedPages(site, sharedIds));
    }
    private static CompositionLocation ParseLocation(string s)
    {
        var parts = s.Split(':');
        if (parts.Length == 2 && parts[0] == "block") return new(null, null, parts[1]);
        if (parts.Length == 3 && parts[0] == "page") return new(parts[1], parts[2], null);
        throw new ContentValidationException("Choose a destination.");
    }
    private static JsonElement SeedFields(string type, CompositionWebsite site)
    {
        object fields = type switch
        {
            "text" => new TextFields("New heading", "Your text"), "image" => new ImageFields(site.Assets.FirstOrDefault()?.Id ?? "", "Describe this image"),
            "cta" => new CtaFields("Take the next step", "Your invitation", "Learn more", "/"),
            "cards" => new CardsFields("Explore", [new("New card", "Your description", null, "/")]),
            "group" => new GroupFields("stack", "start", "medium", 1), "page-title" => new PageTitleFields(),
            "site-header" => new HeaderFields("Your website", site.Pages.Select(p => p.Id).ToImmutableArray()), "site-footer" => new FooterFields("Get in touch"),
            _ => throw new SourceOperationNotSupportedException("This design has no board field template for that registered type. Use its typed API fields.")
        };
        return JsonSerializer.SerializeToElement(fields, fields.GetType(), CompositionJson.Options);
    }
    private static IResult Overview(HttpContext c, CompositionSnapshot snapshot, CompositionCapabilities capabilities)
    {
        var site = snapshot.Website; var html = new StringBuilder("<div class=\"actions\"><a href=\"/manage/integrations\">Connected apps</a><a href=\"/manage/users\">People and access</a><a href=\"/manage/account/password\">Change password</a><a href=\"/manage/composition/export\">Download content and images</a></div><div class=\"intro\"><h1>" + E(site.Title) + "</h1><p>Arrange Blocks in page regions and Groups. Shared Blocks reuse the same content across pages.</p></div>");
        var blocks = site.Blocks.ToDictionary(b => b.Id); var shared = site.SharedBlocks.ToDictionary(s => s.Id);
        var choices = new List<(string Value, string Label)>(); var destinations = new List<(string Value, string Label)>();
        void Tree(Placement p, string label)
        {
            var b = blocks[p.Kind == TargetKind.Shared ? shared[p.TargetId].RootBlockId : p.TargetId];
            choices.Add((p.Id, label + " / " + BlockLabel(b) + (p.Kind == TargetKind.Shared ? " (Shared)" : "")));
            html.Append("<li><a href=\"/manage/composition/blocks/" + E(b.Id) + "\">" + E(BlockLabel(b)) + "</a>" + (p.Kind == TargetKind.Shared ? " · Shared Block" : ""));
            if (!b.Children.IsEmpty) { html.Append("<ul>"); foreach (var child in b.Children) Tree(child, label + " / Group"); html.Append("</ul>"); }
            html.Append("</li>");
        }
        foreach (var p in site.Pages)
        {
            html.Append("<details class=\"panel composition-page\"" + (p == site.Pages[0] ? " open" : "") + "><summary>" + E(p.Title) + " <small>" + E(p.Path) + "</small></summary>");
            foreach (var r in p.Regions)
            {
                destinations.Add(($"page:{p.Id}:{r.Id}", p.Title + " / " + r.Id));
                html.Append("<h3>" + E(r.Id) + "</h3><ol>"); foreach (var item in r.Placements) Tree(item, p.Title + " / " + r.Id); html.Append("</ol>");
            }
            if (Permissions.Has(c.User, "content:write")) html.Append(Form(c, snapshot.Revision, "/manage/composition/page/" + p.Id, Input("title", "Page title", p.Title) + Input("description", "Page description", p.Description), "Save page details")); html.Append("</details>");
        }
        foreach (var b in site.Blocks.Where(b => DemoComposition.Registry().Resolve(b.TypeId, b.TypeVersion).Descriptor.Container)) destinations.Add(("block:" + b.Id, (b.Owner.Kind == OwnerKind.Shared ? "Shared" : site.Pages.Single(p => p.Id == b.Owner.Id).Title) + " / Group " + b.Id[^6..]));
        if (Permissions.Has(c.User, "content:write"))
        {
            var body = "<h2>Arrange content</h2><p>Choose an action, then its content and destination. Positions start at 1. Group selections must belong to the selected destination. Moves preserve page or Shared Block ownership.</p>" +
                Select("operation", "Action", new[] { ("create", "Add Block"), ("move", "Move placement"), ("group", "Group selected placements"), ("reference", "Reference Shared Block"), ("promote", "Make placement shared"), ("detach", "Detach shared placement"), ("delete", "Delete placement"), ("deleteShared", "Delete unused Shared Block") }) +
                Select("placement", "Placement to move, share, detach or delete", choices.DistinctBy(i => i.Value)) + Select("location", "Destination for add, move, group or reference", destinations) + Input("index", "Position for add, move or reference", "1") +
                Select("type", "Block type to add", capabilities.Types.Select(t => (t.Id, t.Id))) + Select("shared", "Shared Block to reference or delete", site.SharedBlocks.Select(s => (s.Id, blocks[s.RootBlockId].TypeId + " · " + s.Id))) +
                "<fieldset><legend>Placements to group</legend>" + string.Join("", choices.DistinctBy(i => i.Value).Select(i => $"<label class=\"scope\"><input type=\"checkbox\" name=\"selection\" value=\"{E(i.Value)}\">{E(i.Label)}</label>")) + "</fieldset>" +
                Select("mode", "Group layout", new[] { ("stack", "Stack"), ("row", "Row"), ("grid", "Grid") }) + Select("alignment", "Alignment", new[] { ("start", "Start"), ("center", "Center") }) + Select("spacing", "Spacing", new[] { ("small", "Small"), ("medium", "Medium") }) + Input("columns", "Columns", "1");
            body += "<p>Shared arrangements show affected pages for confirmation before saving.</p>";
            html.Append(Form(c, snapshot.Revision, "/manage/composition/arrange", body, "Apply action"));
            html.Append($"<form class=\"panel editor\" enctype=\"multipart/form-data\" method=\"post\" action=\"/manage/composition/media\">{ManagementUi.Token(c)}<input type=\"hidden\" name=\"revision\" value=\"{E(snapshot.Revision)}\"><h2>Images</h2><p>PNG upload: at most 2 MiB and 4 million pixels. Saved images remain available to older revisions.</p><label for=\"image\">PNG image</label><input id=\"image\" name=\"image\" type=\"file\" accept=\"image/png\" required><button>Upload image</button></form>");
        }
        html.Append("<section class=\"panel\"><h2>Media library</h2><div class=\"media-library\">" + string.Join("", site.Assets.Select((a, i) => "<figure><img src=\"/manage/composition/media/" + E(a.Id) + "\" alt=\"Image " + (i + 1) + "\" loading=\"lazy\"><figcaption>Image " + (i + 1) + "</figcaption></figure>")) + "</div></section>");
        html.Append("<section class=\"panel\"><h2>Shared Blocks</h2><ul>" + string.Join("", site.SharedBlocks.Select(s => "<li><a href=\"/manage/composition/blocks/" + E(s.RootBlockId) + "\">" + E(BlockLabel(blocks[s.RootBlockId])) + "</a></li>")) + "</ul></section>");
        if (Permissions.Has(c.User, "settings:write")) html.Append(Form(c, snapshot.Revision, "/manage/composition/settings", "<h2>Website settings</h2>" + Input("title", "Website name / title", site.Title) + Input("language", "Language", site.Language), "Save website settings"));
        if (Permissions.Has(c.User, "preview:build")) html.Append(Form(c, snapshot.Revision, "/manage/composition/preview", "<h2>Review your draft</h2><p>Capture this revision in a retained preview.</p>", "Build preview"));
        return ManagementUi.Html(site.Title, html.ToString());
    }
    private static IResult Edit(HttpContext c, CompositionSnapshot snapshot, Block b)
    {
        var fields = new StringBuilder(); DescribeFields(JsonNode.Parse(b.Fields.GetRawText())!, "field", fields, snapshot.Website);
        var body = "<a href=\"/manage/composition\">← Website</a><div class=\"intro\"><h1>Edit " + E(b.TypeId.Replace('-', ' ')) + "</h1>" + (b.TypeId == "page-title" ? "<p>This Block displays the page title and description. Edit those in the page's details on the board.</p>" : "") + "</div>";
        var impact = b.Owner.Kind == OwnerKind.Shared ? Acknowledgement(snapshot, [b.Owner.Id]) : "";
        if (!Permissions.Has(c.User, "content:write")) return ManagementUi.Html("View Block", body + "<p>You have read-only access.</p>" + fields + impact);
        if (b.Owner.Kind == OwnerKind.Shared && !Permissions.Has(c.User, "content:shared:write")) return ManagementUi.Html("Shared Block", body + "<p>You can view shared content but need shared-content permission to change it. Detach a page reference to edit an independent copy.</p>" + impact);
        return ManagementUi.Html("Edit Block", body + Form(c, snapshot.Revision, "/manage/composition/blocks/" + b.Id, fields + impact, "Save Block"));
    }
    private static string BlockLabel(Block b)
    {
        var name = b.TypeId.Replace('-', ' ');
        if (b.Fields.TryGetProperty("heading", out var heading) && heading.ValueKind == JsonValueKind.String) name += " · " + heading.GetString();
        return name.Length <= 100 ? name : name[..97] + "…";
    }
    private static void DescribeFields(JsonNode node, string path, StringBuilder output, CompositionWebsite site)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Value is not null) DescribeFields(pair.Value, path + "." + pair.Key, output, site);
                else if (pair.Key == "assetId") output.Append(Select(path + "." + pair.Key, "Optional image", new[] { ("", "No image") }.Concat(site.Assets.Select((a, i) => (a.Id, "Image " + (i + 1))))));
                else output.Append(Input(path + "." + pair.Key, pair.Key, ""));
            }
        }
        else if (node is JsonArray array)
        {
            output.Append("<fieldset><legend>" + (path.EndsWith("pageIds", StringComparison.Ordinal) ? "Navigation pages" : "Cards") + "</legend>");
            for (var i = 0; i < array.Count; i++) if (array[i] is { } item)
            {
                DescribeFields(item, path + "." + i, output, site);
                output.Append($"<label class=\"scope\"><input type=\"checkbox\" name=\"remove.{E(path)}\" value=\"{i}\">Remove item {i + 1}</label>");
            }
            if (path.EndsWith("pageIds", StringComparison.Ordinal)) output.Append(Select("append." + path, "Add navigation page", new[] { ("", "Keep current list") }.Concat(site.Pages.Select(p => (p.Id, p.Title)))));
            else if (path.EndsWith("items", StringComparison.Ordinal)) output.Append($"<label class=\"scope\"><input type=\"checkbox\" name=\"append.{E(path)}\" value=\"true\">Add a card (save, then edit its fields)</label>");
            output.Append("</fieldset>");
        }
        else if (path.EndsWith("assetId", StringComparison.Ordinal)) output.Append(Select(path, "Image", new[] { (node.ToString(), "Current image") }.Concat(site.Assets.Select((a, i) => (a.Id, "Image " + (i + 1))))));
        else if (path.Contains(".pageIds.", StringComparison.Ordinal)) output.Append(Select(path, "Navigation page", new[] { (node.ToString(), site.Pages.Single(p => p.Id == node.ToString()).Title) }.Concat(site.Pages.Select(p => (p.Id, p.Title)))));
        else output.Append(Input(path, path[6..].Replace('.', ' '), node.ToString()));
    }
    private static void SetFields(JsonNode node, string path, IFormCollection f)
    {
        if (node is JsonObject obj) foreach (var pair in obj.ToArray())
        {
            var key = path + "." + pair.Key;
            if (pair.Value is JsonObject or JsonArray) SetFields(pair.Value, key, f);
            else if (f.ContainsKey(key)) obj[pair.Key] = ConvertValue(pair.Value, f[key].ToString());
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonObject or JsonArray) SetFields(array[i]!, path + "." + i, f);
                else if (f.ContainsKey(path + "." + i)) array[i] = ConvertValue(array[i], f[path + "." + i].ToString());
            }
            var removals = f["remove." + path].Select(v => int.TryParse(v, out var index) && index >= 0 && index < array.Count ? index : throw new ContentValidationException("Unknown list item.")).Distinct().OrderDescending().ToArray();
            foreach (var index in removals) array.RemoveAt(index);
            var append = f["append." + path].ToString();
            if (append.Length > 0 && path.EndsWith("pageIds", StringComparison.Ordinal)) array.Add(append);
            else if (append == "true" && path.EndsWith("items", StringComparison.Ordinal)) array.Add(JsonSerializer.SerializeToNode(new CardFields("New card", "Your description", null, "/"), CompositionJson.Options));
        }
    }
    private static JsonNode? ConvertValue(JsonNode? previous, string value)
    {
        if (previous is null && value.Length == 0) return null;
        if (previous?.GetValueKind() == JsonValueKind.Number) return int.TryParse(value, out var n) ? JsonValue.Create(n) : throw new ContentValidationException("Enter a whole number.");
        return JsonValue.Create(value);
    }
}
