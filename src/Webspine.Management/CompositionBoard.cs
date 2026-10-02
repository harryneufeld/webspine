using System.Collections.Immutable;
using System.Net;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webspine.Content.Sqlite;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

internal static partial class CompositionBoard
{
    private static CompositionDesign SelectedDesign(HttpContext c) => c.RequestServices.GetRequiredService<CompositionOperations>().Design;
    private static BlockRegistry SelectedDefinitions(HttpContext c) => c.RequestServices.GetRequiredService<CompositionOperations>().Registry;
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
        MapRecordRoutes(board);
        board.MapGet("", (Delegate)HomeAsync);
        board.MapGet("/export", async (CompositionOperations operations, HttpContext c) =>
        {
            operations.Source.CompositionCapabilities.RequireCapture();
            var capture = await operations.Source.CaptureCompositionAsync(SelectedDesign(c), c.RequestAborted);
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
            var capture = await operations.Source.CaptureCompositionAsync(SelectedDesign(c), c.RequestAborted);
            var asset = capture.Content.Website.Assets.FirstOrDefault(a => a.Id == id);
            return asset is null ? Results.NotFound() : Results.File(capture.AssetFiles[asset.File].ToArray(), asset.ContentType);
        });
        board.MapGet("/blocks/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted);
            var block = snapshot.Website.Blocks.FirstOrDefault(b => b.Id == id);
            return block is null ? Results.NotFound() : Edit(c, snapshot, block, SelectedDesign(c), operations.Source.CompositionCapabilities);
        });
        board.MapPost("/blocks/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var form = await c.Request.ReadFormAsync(c.RequestAborted);
            var snapshot = await operations.ReadAsync(c.RequestAborted);
            Current(snapshot, form);
            var block = snapshot.Website.Blocks.FirstOrDefault(b => b.Id == id) ?? throw new ContentValidationException("Block does not exist.");
            var fields = ContentFieldForms.Apply(SelectedEditor(c, block.TypeId, block.TypeVersion), block.Fields, form, snapshot.Website, SelectedDesign(c));
            await operations.EditAsync(form["revision"].ToString(), new UpdateBlock(id, fields), c.User, Acknowledged(form), c.RequestAborted);
            return Results.Redirect(BoardUrl(snapshot.Website, form["page"].ToString()));
        });
        board.MapPost("/page/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            await operations.EditAsync(f["revision"].ToString(), new EditCompositionPage(id, f["title"].ToString(), f["description"].ToString()), c.User, [], c.RequestAborted);
            return Results.Redirect("/manage/composition?page=" + Q(id));
        });
        board.MapPost("/pages", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            var snapshot = await operations.EditAsync(f["revision"].ToString(), new AddCompositionPage(f["title"].ToString(), f["path"].ToString(), f["description"].ToString()), c.User, [], c.RequestAborted);
            return Results.Redirect("/manage/composition?page=" + Q(snapshot.Website.Pages[^1].Id));
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
    }
    internal static async Task<IResult> HomeAsync(HttpContext c)
    {
        var store = c.RequestServices.GetRequiredService<SqliteContentSource>();
        var head = await store.HeadAsync(c.RequestAborted);
        if (head is null) return Permissions.Has(c.User, "settings:write") ? ManagementUi.Setup(c) : ManagementUi.Problem("An operator must create the website first.", 403);
        if (head.Version != 2) return ManagementUi.LegacyWorkspace();
        var operations = c.RequestServices.GetRequiredService<CompositionOperations>();
        try { return Overview(c, await operations.ReadAsync(c.RequestAborted), operations.Source.CompositionCapabilities, SelectedDesign(c)); }
        catch (Exception e) when (CompositionApi.ExpectedError(e)) { return ManagementUi.Problem(e.Message, CompositionApi.ErrorStatus(e)); }
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
}
