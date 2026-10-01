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

internal static partial class CompositionBoard
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
        MapStructureRoutes(board);
        board.MapGet("", async (CompositionOperations operations, HttpContext c) => Overview(c, await operations.ReadAsync(c.RequestAborted), operations.Source.CompositionCapabilities, await DemoComposition.DesignAsync(c.RequestAborted)));
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
            return block is null ? Results.NotFound() : Edit(c, snapshot, block, await DemoComposition.DesignAsync(c.RequestAborted), operations.Source.CompositionCapabilities);
        });
        board.MapPost("/blocks/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var form = await c.Request.ReadFormAsync(c.RequestAborted);
            var snapshot = await operations.ReadAsync(c.RequestAborted);
            Current(snapshot, form);
            var block = snapshot.Website.Blocks.FirstOrDefault(b => b.Id == id) ?? throw new ContentValidationException("Block does not exist.");
            var fields = JsonNode.Parse(block.Fields.GetRawText())!;
            SetFields(fields, "field", form);
            await operations.EditAsync(form["revision"].ToString(), new UpdateBlock(id, JsonSerializer.SerializeToElement(fields, CompositionJson.Options)), c.User, Acknowledged(form), c.RequestAborted);
            return Results.Redirect(BoardUrl(snapshot.Website, form["page"].ToString()));
        });
        board.MapPost("/page/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            await operations.EditAsync(f["revision"].ToString(), new EditCompositionPage(id, f["title"].ToString(), f["description"].ToString()), c.User, [], c.RequestAborted);
            return Results.Redirect("/manage/composition?page=" + Q(id));
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
    private static IResult Edit(HttpContext c, CompositionSnapshot snapshot, Block b, CompositionDesign design, CompositionCapabilities caps)
    {
        var fields = new StringBuilder(); DescribeFields(JsonNode.Parse(b.Fields.GetRawText())!, "field", fields, snapshot.Website, design);
        var page = c.Request.Query["page"].ToString();
        var body = ScreenHeader(snapshot.Website, page, "Edit " + TypeLabel(b.TypeId).ToLowerInvariant(), "Change the fields for this element.");
        var impact = b.Owner.Kind == OwnerKind.Shared ? Acknowledgement(snapshot, [b.Owner.Id]) : "";
        if (!Editable(c, b.Owner) || !Supported(caps, CompositionOperation.Update, b.TypeId, b.TypeVersion)) return ManagementUi.Html("View Block", body + "<p>This content is read-only for your account or content source. Shared content needs shared-content permission; detach a page reference to edit an independent copy.</p><fieldset disabled>" + fields + "</fieldset>");
        return ManagementUi.Html("Edit Block", body + Form(c, snapshot.Revision, "/manage/composition/blocks/" + b.Id, Hidden("page", page) + fields + impact, "Save Block"));
    }
    private static string BlockLabel(Block b)
    {
        var name = TypeLabel(b.TypeId);
        if (b.Fields.TryGetProperty("heading", out var heading) && heading.ValueKind == JsonValueKind.String) name += " · " + heading.GetString();
        return name.Length <= 100 ? name : name[..97] + "…";
    }
    private static void DescribeFields(JsonNode node, string path, StringBuilder output, CompositionWebsite site, CompositionDesign? design = null)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Value is not null) DescribeFields(pair.Value, path + "." + pair.Key, output, site, design);
                else if (pair.Key == "assetId") output.Append(Select(path + "." + pair.Key, "Optional image", new[] { ("", "No image") }.Concat(site.Assets.Select((a, i) => (a.Id, "Image " + (i + 1))))));
                else output.Append(Input(path + "." + pair.Key, pair.Key, ""));
            }
        }
        else if (node is JsonArray array)
        {
            output.Append("<fieldset><legend>" + (path.EndsWith("pageIds", StringComparison.Ordinal) ? "Navigation pages" : "Cards") + "</legend>");
            for (var i = 0; i < array.Count; i++) if (array[i] is { } item)
            {
                if (item is JsonObject) output.Append("<fieldset><legend>Card " + (i + 1) + "</legend>");
                DescribeFields(item, path + "." + i, output, site, design);
                output.Append($"<label class=\"scope\"><input type=\"checkbox\" name=\"remove.{E(path)}\" value=\"{i}\">Remove item {i + 1}</label>");
                if (item is JsonObject) output.Append("</fieldset>");
            }
            if (path.EndsWith("pageIds", StringComparison.Ordinal)) output.Append(Select("append." + path, "Add navigation page", new[] { ("", "Keep current list") }.Concat(site.Pages.Select(p => (p.Id, p.Title)))));
            else if (path.EndsWith("items", StringComparison.Ordinal)) output.Append($"<label class=\"scope\"><input type=\"checkbox\" name=\"append.{E(path)}\" value=\"true\">Add a card (save, then edit its fields)</label>");
            output.Append("</fieldset>");
        }
        else if (path.EndsWith("assetId", StringComparison.Ordinal)) output.Append(Select(path, "Image", new[] { (node.ToString(), "Current image") }.Concat(site.Assets.Select((a, i) => (a.Id, "Image " + (i + 1))))));
        else if (path.Contains(".pageIds.", StringComparison.Ordinal)) output.Append(Select(path, "Navigation page", new[] { (node.ToString(), site.Pages.Single(p => p.Id == node.ToString()).Title) }.Concat(site.Pages.Select(p => (p.Id, p.Title)))));
        else
        {
            var key = path.Split('.').Last();
            var label = key switch { "alternativeText" => "Image description", "destination" => "Link destination", "label" => "Button label", "text" => "Text", "mode" => "Layout", "alignment" => "Alignment", "spacing" => "Spacing", "columns" => "Columns", _ => char.ToUpperInvariant(key[0]) + key[1..] };
            IEnumerable<string>? choices = key switch { "mode" => design?.Groups.Modes, "alignment" => design?.Groups.Alignments, "spacing" => design?.Groups.Spacing, _ => null };
            if (choices is not null) output.Append(Select(path, label, new[] { node.ToString() }.Concat(choices).Distinct().Select(v => (v, TypeLabel(v)))));
            else if (key == "columns" && design is not null) output.Append(Select(path, label, new[] { node.ToString() }.Concat(Enumerable.Range(1, design.Groups.MaximumColumns).Select(n => n.ToString())).Distinct().Select(v => (v, v))));
            else if (key is "text" or "description" or "message") output.Append($"<label>{E(label)}<textarea name=\"{E(path)}\" rows=\"4\">{E(node.ToString())}</textarea></label>");
            else output.Append(Input(path, label, node.ToString()));
        }
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
