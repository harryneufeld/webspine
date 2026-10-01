using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webspine.Core;
using Webspine.Core.Composition;
using Webspine.Designs.Studio;

namespace Webspine.Management;

internal static partial class CompositionBoard
{
    public const string Css = """
        .shared-structure{width:100%}.page-structure{padding-top:20px}.structure-area{margin-block:24px}.structure-area h3{font-size:16px;color:var(--muted);margin-bottom:12px}.structure-list{list-style:none;margin:0;padding:0}.structure-node{border:1px solid var(--line);border-radius:10px;margin-block:10px;background:#fff}.structure-node>.structure-list,.structure-node>.area-actions{margin:12px 16px 16px;border-left:2px solid var(--line);padding-left:12px}.element-row{display:flex;flex-wrap:wrap;justify-content:space-between;gap:12px;padding:16px}.element-name{display:flex;align-items:center;gap:12px;min-width:0;flex:1 1 230px}.element-name>div{min-width:0}.element-kind{display:block;font-size:12px;color:var(--muted)}.element-select{margin:0;display:flex;align-items:center;min-height:44px;min-width:28px}.element-select input{width:18px;height:18px}.element-actions,.area-actions{display:flex;align-items:center;gap:10px;flex-wrap:wrap}.element-actions{flex:0 1 auto}.element-actions a,.area-actions a{padding-block:10px}.element-actions form,.area-actions form{margin:0}.small-button{margin:0;padding:10px 12px;border-radius:8px;font-size:13px;min-height:44px}.small-button:disabled{opacity:.45;cursor:default}.element-more{max-width:100%}.element-more summary{font-size:14px;font-weight:500;padding:10px;min-height:44px}.element-menu{display:flex;flex-direction:column;align-items:flex-start;gap:8px;padding:12px;background:var(--paper);border-radius:8px}.element-menu a{padding:4px}.element-choices{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:16px}.element-choice{display:flex;flex-direction:column;padding:24px;border:1px solid var(--line);border-radius:12px;background:#fffdf8;text-decoration:none}.element-choice span{font-size:14px;color:var(--muted);margin-top:6px}.visually-hidden{position:absolute;width:1px;height:1px;margin:-1px;padding:0;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}@media(max-width:700px){.element-row{padding:12px}.element-actions{width:100%}.structure-node>.structure-list,.structure-node>.area-actions{margin-inline:8px;padding-left:8px}.element-choices{grid-template-columns:1fr}.composition-page{padding:16px}.element-menu{width:100%}}
        """;
    private sealed record PlacementContext(CompositionLocation Location, Placement Placement, BlockOwner Owner, ImmutableArray<Placement> Siblings, int Index);
    private static string Hidden(string name, string value) => $"<input type=\"hidden\" name=\"{E(name)}\" value=\"{E(value)}\">";
    private static string LocationKey(CompositionLocation l) => l.ParentBlockId is not null ? "block:" + l.ParentBlockId : "page:" + l.PageId + ":" + l.RegionId;
    private static string Q(string value) => Uri.EscapeDataString(value);
    private static string BoardUrl(CompositionWebsite site, string page) => "/manage/composition" + (site.Pages.Any(p => p.Id == page) ? "?page=" + Q(page) : "");
    private static bool Supported(CompositionCapabilities caps, CompositionOperation op, string? type = null, int version = 1)
    { try { caps.Require(op, type, version); return true; } catch (SourceOperationNotSupportedException) { return false; } }
    private static bool Editable(HttpContext c, BlockOwner owner) => Permissions.Has(c.User, "content:write") && (owner.Kind == OwnerKind.Page || Permissions.Has(c.User, "content:shared:write"));
    private static Block Target(CompositionWebsite site, Placement p) => site.Blocks.Single(b => b.Id == (p.Kind == TargetKind.Block ? p.TargetId : site.SharedBlocks.Single(s => s.Id == p.TargetId).RootBlockId));
    private static PlacementContext Locate(CompositionWebsite site, string id)
    {
        foreach (var page in site.Pages)
            foreach (var region in page.Regions)
            {
                var index = Array.FindIndex(region.Placements.ToArray(), p => p.Id == id);
                if (index >= 0) return new(new(page.Id, region.Id, null), region.Placements[index], new(OwnerKind.Page, page.Id), region.Placements, index);
            }
        foreach (var b in site.Blocks)
        {
            var index = Array.FindIndex(b.Children.ToArray(), p => p.Id == id);
            if (index >= 0) return new(new(null, null, b.Id), b.Children[index], b.Owner, b.Children, index);
        }
        throw new ContentValidationException("This element no longer exists. Reopen the page structure.");
    }
    private static (BlockOwner Owner, ImmutableArray<Placement> Items, ImmutableArray<string> Types, int Minimum, int Maximum) Destination(HttpContext c, CompositionWebsite site, CompositionDesign design, CompositionLocation l)
    {
        if (l.ParentBlockId is not null)
        {
            var b = site.Blocks.FirstOrDefault(b => b.Id == l.ParentBlockId) ?? throw new ContentValidationException("Group does not exist.");
            if (!SelectedDefinitions(c).Resolve(b.TypeId, b.TypeVersion).Descriptor.Container) throw new ContentValidationException("This element cannot contain other elements.");
            return (b.Owner, b.Children, design.Groups.AllowedTypes, 0, 1000);
        }
        var page = site.Pages.FirstOrDefault(p => p.Id == l.PageId) ?? throw new ContentValidationException("Page does not exist.");
        var region = page.Regions.FirstOrDefault(r => r.Id == l.RegionId) ?? throw new ContentValidationException("Area does not exist.");
        var definition = design.Layout.Regions.Single(r => r.Id == region.Id);
        return (new(OwnerKind.Page, page.Id), region.Placements, definition.AllowedTypes, definition.Minimum, definition.Maximum);
    }
    private static int InsertionIndex(ImmutableArray<Placement> items, string after)
    {
        if (after.Length == 0) return items.Length;
        var index = Array.FindIndex(items.ToArray(), p => p.Id == after);
        return index < 0 ? throw new ContentValidationException("The insertion point no longer exists.") : index + 1;
    }
    private static void Current(CompositionSnapshot snapshot, IFormCollection f)
    { if (snapshot.Revision != f["revision"].ToString()) throw new CompositionRevisionException(); }
    private static string ContextFields(string page, CompositionLocation location, string after = "") => Hidden("page", page) + Hidden("location", LocationKey(location)) + Hidden("after", after);
    private static string ContextQuery(string page, CompositionLocation location, string after = "") => "?page=" + Q(page) + "&location=" + Q(LocationKey(location)) + "&after=" + Q(after);
    private static string ScreenHeader(CompositionWebsite site, string page, string title, string text) => $"<a href=\"{E(BoardUrl(site, page))}\">← Page structure</a><div class=\"intro\"><h1>{E(title)}</h1><p>{E(text)}</p></div>";

    private static void MapStructureRoutes(RouteGroupBuilder board)
    {
        board.MapGet("/add", async (CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted); var design = SelectedDesign(c);
            var page = c.Request.Query["page"].ToString(); var location = ParseLocation(c.Request.Query["location"].ToString()); var after = c.Request.Query["after"].ToString();
            var destination = Destination(c, snapshot.Website, design, location); InsertionIndex(destination.Items, after);
            if (!Editable(c, destination.Owner)) throw new CompositionPermissionException("You cannot add content to this area.");
            operations.Source.CompositionCapabilities.Require(CompositionOperation.Create);
            var types = operations.Source.CompositionCapabilities.Types.Where(t => destination.Types.Contains(t.Id) && CanCreate(c, t.Id, t.Version, snapshot.Website)).ToArray();
            var selected = c.Request.Query["type"].ToString();
            var body = ScreenHeader(snapshot.Website, page, "Add an element", "Choose content for this area. Only types allowed by the design are shown.");
            if (selected.Length == 0)
            {
                body += "<div class=\"element-choices\">" + string.Join("", types.Select(t => $"<a class=\"element-choice\" href=\"/manage/composition/add{E(ContextQuery(page, location, after))}&amp;type={Q(t.Id)}\"><strong>{E(TypeLabel(c, t.Id))}</strong><span>{E(SelectedEditor(c, t.Id, t.Version).Description)}</span></a>")) + "</div>";
                if (types.Length == 0) body += "<p>No new element types are available here. Images require an approved image in the library.</p>";
                return ManagementUi.Html("Add element", body);
            }
            var type = types.FirstOrDefault(t => t.Id == selected) ?? throw new ContentValidationException("This element type is not available in the selected area.");
            if (destination.Items.Length >= destination.Maximum) throw new ContentValidationException("This area has reached its element limit.");
            var fields = ContentFieldForms.Render(SelectedEditor(c, type.Id, type.Version), SeedFields(c, type.Id, snapshot.Website), snapshot.Website, design);
            body = ScreenHeader(snapshot.Website, page, "Add " + TypeLabel(c, type.Id).ToLowerInvariant(), "Set up the element before adding it to your page.");
            return ManagementUi.Html("Add " + TypeLabel(c, type.Id), body + Form(c, snapshot.Revision, "/manage/composition/add", ContextFields(page, location, after) + Hidden("type", type.Id) + fields, "Add " + TypeLabel(c, type.Id).ToLowerInvariant()));
        });
        board.MapPost("/add", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
            var location = ParseLocation(f["location"].ToString()); var destination = Destination(c, snapshot.Website, SelectedDesign(c), location);
            var type = f["type"].ToString(); if (!destination.Types.Contains(type) || !CanCreate(c, type, SelectedDefinitions(c).Descriptors.SingleOrDefault(d => d.Id == type)?.Version ?? 0, snapshot.Website)) throw new ContentValidationException("This element type is not allowed here.");
            var fields = ContentFieldForms.Apply(SelectedEditor(c, type), SeedFields(c, type, snapshot.Website), f, snapshot.Website, SelectedDesign(c));
            var change = new CreateBlock(location, InsertionIndex(destination.Items, f["after"].ToString()), type, SelectedDefinitions(c).Descriptors.Single(d => d.Id == type).Version, fields);
            return await ApplyFromBoard(operations, c, snapshot, change, f);
        });
        board.MapGet("/reference", async (CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted); var page = c.Request.Query["page"].ToString(); var location = ParseLocation(c.Request.Query["location"].ToString()); var after = c.Request.Query["after"].ToString();
            var destination = Destination(c, snapshot.Website, SelectedDesign(c), location); InsertionIndex(destination.Items, after);
            if (!Editable(c, destination.Owner)) throw new CompositionPermissionException("You cannot add a shared reference here.");
            operations.Source.CompositionCapabilities.Require(CompositionOperation.Share);
            var body = ScreenHeader(snapshot.Website, page, "Reuse shared content", "This adds a reference. Edits to the shared content affect every page that uses it.");
            foreach (var shared in snapshot.Website.SharedBlocks.Where(s => destination.Types.Contains(snapshot.Website.Blocks.Single(b => b.Id == s.RootBlockId).TypeId)))
                body += Form(c, snapshot.Revision, "/manage/composition/reference", ContextFields(page, location, after) + Hidden("shared", shared.Id) + "<h2>" + E(BlockLabel(c, snapshot.Website.Blocks.Single(b => b.Id == shared.RootBlockId))) + "</h2>", "Use this Shared Block");
            return ManagementUi.Html("Reuse shared content", body);
        });
        board.MapPost("/reference", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
            var l = ParseLocation(f["location"].ToString()); var d = Destination(c, snapshot.Website, SelectedDesign(c), l);
            return await ApplyFromBoard(operations, c, snapshot, new ReferenceShared(f["shared"].ToString(), l, InsertionIndex(d.Items, f["after"].ToString())), f);
        });
        board.MapPost("/placements/{id}/reorder", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f); var element = Locate(snapshot.Website, id);
            var index = f["direction"].ToString() switch { "up" => element.Index - 1, "down" => element.Index + 1, _ => throw new ContentValidationException("Choose move up or move down.") };
            if (index < 0 || index >= element.Siblings.Length) throw new ContentValidationException("This element is already at the edge of its area.");
            return await ApplyFromBoard(operations, c, snapshot, new MovePlacement(id, element.Location, index), f);
        });
        board.MapGet("/placements/{id}/move", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted); var element = Locate(snapshot.Website, id); var page = c.Request.Query["page"].ToString();
            if (!Editable(c, element.Owner)) throw new CompositionPermissionException("You cannot move this element.");
            operations.Source.CompositionCapabilities.Require(CompositionOperation.Move);
            var target = Target(snapshot.Website, element.Placement); var design = SelectedDesign(c);
            var excluded = new HashSet<string>();
            void Subtree(Block b) { if (!excluded.Add(b.Id)) return; foreach (var p in b.Children.Where(p => p.Kind == TargetKind.Block)) Subtree(Target(snapshot.Website, p)); }
            if (element.Placement.Kind == TargetKind.Block) Subtree(target);
            var choices = new List<(string Value, string Label)>();
            if (element.Owner.Kind == OwnerKind.Page)
            {
                var ownerPage = snapshot.Website.Pages.Single(p => p.Id == element.Owner.Id);
                foreach (var r in ownerPage.Regions.Where(r => design.Layout.Regions.Single(d => d.Id == r.Id).AllowedTypes.Contains(target.TypeId))) choices.Add((LocationKey(new(ownerPage.Id, r.Id, null)), "End of " + TypeLabel(c, r.Id)));
            }
            if (design.Groups.AllowedTypes.Contains(target.TypeId))
                foreach (var b in snapshot.Website.Blocks.Where(b => b.Owner == element.Owner && !excluded.Contains(b.Id) && SelectedDefinitions(c).Resolve(b.TypeId, b.TypeVersion).Descriptor.Container)) choices.Add(("block:" + b.Id, "End of " + GroupLabel(snapshot.Website, b)));
            var body = ScreenHeader(snapshot.Website, page, "Move " + TypeLabel(c, target.TypeId).ToLowerInvariant(), "Choose an area in the same page or shared content. Move up/down adjusts its position afterward.");
            return ManagementUi.Html("Move element", body + Form(c, snapshot.Revision, "/manage/composition/placements/" + id + "/move", Hidden("page", page) + Select("location", "Move to", choices), "Move element"));
        });
        board.MapPost("/placements/{id}/move", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f); var element = Locate(snapshot.Website, id);
            var l = ParseLocation(f["location"].ToString()); var d = Destination(c, snapshot.Website, SelectedDesign(c), l);
            return await ApplyFromBoard(operations, c, snapshot, new MovePlacement(id, l, d.Items.Length - (l == element.Location ? 1 : 0)), f);
        });
        foreach (var action in new[] { "promote", "detach", "remove" })
        {
            var operation = action;
            board.MapPost("/placements/{id}/" + operation, async (string id, CompositionOperations operations, HttpContext c) =>
            {
                var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
                CompositionEdit change = operation switch { "promote" => new PromoteShared(id), "detach" => new DetachPlacement(id), _ => new DeletePlacement(id) };
                return await ApplyFromBoard(operations, c, snapshot, change, f, confirmRemoval: operation == "remove");
            });
        }
        board.MapPost("/group", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
            var l = ParseLocation(f["location"].ToString()); var design = SelectedDesign(c); var d = Destination(c, snapshot.Website, design, l);
            if (!Editable(c, d.Owner)) throw new CompositionPermissionException("You cannot group content in this area.");
            operations.Source.CompositionCapabilities.Require(CompositionOperation.Group, "group");
            var selected = f["selection"].Select(s => s!).ToImmutableArray();
            if (selected.IsEmpty || selected.Any(id => !d.Items.Any(p => p.Id == id))) throw new ContentValidationException("Select elements from this one area to group.");
            var fields = ContentFieldForms.Render(SelectedEditor(c, "group"), SeedFields(c, "group", snapshot.Website), snapshot.Website, design);
            var body = ScreenHeader(snapshot.Website, f["page"].ToString(), "Group selected elements", "The selected elements keep their content and order inside this Group.");
            body += "<ul>" + string.Join("", d.Items.Where(p => selected.Contains(p.Id)).Select(p => "<li>" + E(BlockLabel(c, Target(snapshot.Website, p))) + "</li>")) + "</ul>";
            return ManagementUi.Html("Group elements", body + Form(c, snapshot.Revision, "/manage/composition/group/commit", ContextFields(f["page"].ToString(), l) + string.Join("", selected.Select(id => Hidden("selection", id))) + fields, "Create Group"));
        });
        board.MapPost("/group/commit", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
            var fields = ContentFieldForms.Apply(SelectedEditor(c, "group"), SeedFields(c, "group", snapshot.Website), f, snapshot.Website, SelectedDesign(c));
            return await ApplyFromBoard(operations, c, snapshot, new GroupPlacements(ParseLocation(f["location"].ToString()), f["selection"].Select(s => s!).ToImmutableArray(), fields), f);
        });
        board.MapPost("/confirm", async (CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
            using var json = JsonDocument.Parse(f["change"].ToString()); CompositionJson.RejectDuplicateProperties(json.RootElement);
            var change = json.RootElement.Deserialize<CompositionEdit>(CompositionJson.Options) ?? throw new ContentValidationException("The action is missing.");
            await operations.EditAsync(snapshot.Revision, change, c.User, Acknowledged(f), c.RequestAborted);
            return Results.Redirect(BoardUrl(snapshot.Website, f["page"].ToString()));
        });
        board.MapPost("/shared/{id}/remove", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
            return await ApplyFromBoard(operations, c, snapshot, new DeleteShared(id), f, confirmRemoval: true);
        });
        board.MapGet("/page/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted); var page = snapshot.Website.Pages.FirstOrDefault(p => p.Id == id);
            if (page is null) return Results.NotFound();
            if (!Permissions.Has(c.User, "content:write")) return ManagementUi.Html("Page details", ScreenHeader(snapshot.Website, id, page.Title, page.Description) + "<p>You have read-only access.</p>");
            return ManagementUi.Html("Page details", ScreenHeader(snapshot.Website, id, "Edit " + page.Title, "These details are reused by the page title and navigation.") + Form(c, snapshot.Revision, "/manage/composition/page/" + id, Input("title", "Page name", page.Title) + Input("description", "Page headline", page.Description), "Save page details"));
        });
    }

    private static async Task<IResult> ApplyFromBoard(CompositionOperations operations, HttpContext c, CompositionSnapshot snapshot, CompositionEdit change, IFormCollection f, bool confirmRemoval = false)
    {
        var impact = ArrangementImpact(snapshot.Website, change);
        if (!Permissions.Has(c.User, "content:write") || (impact.SharedChange && !Permissions.Has(c.User, "content:shared:write"))) throw new CompositionPermissionException("You do not have permission to change this content.");
        var op = change switch { CreateBlock => CompositionOperation.Create, MovePlacement => CompositionOperation.Move, GroupPlacements => CompositionOperation.Group, ReferenceShared or PromoteShared => CompositionOperation.Share, DetachPlacement => CompositionOperation.Detach, _ => CompositionOperation.Delete };
        operations.Source.CompositionCapabilities.Require(op);
        if (impact.SharedChange || confirmRemoval)
        {
            var body = ScreenHeader(snapshot.Website, f["page"].ToString(), confirmRemoval ? "Remove this content?" : "Review shared change", confirmRemoval ? "This changes the saved draft. Existing previews keep their content." : "This content is shared. Review the affected pages before saving.");
            body += "<p class=\"notice\">" + E(ChangeDescription(c, snapshot.Website, change)) + "</p>";
            var ack = impact.SharedChange ? "<fieldset><legend>Affected pages</legend><p>" + E(impact.Pages.Length == 0 ? "No pages currently use this Shared Block." : string.Join(", ", impact.Pages.Select(id => snapshot.Website.Pages.Single(p => p.Id == id).Title))) + "</p>" + string.Join("", impact.Pages.Select(id => Hidden("affected", id))) + "<label class=\"scope\"><input type=\"checkbox\" name=\"acknowledge\" value=\"true\" required>I reviewed the affected pages.</label></fieldset>" : "";
            return ManagementUi.Html("Confirm change", body + Form(c, snapshot.Revision, "/manage/composition/confirm", Hidden("page", f["page"].ToString()) + Hidden("change", JsonSerializer.Serialize<CompositionEdit>(change, CompositionJson.Options)) + ack, confirmRemoval ? "Confirm removal" : "Confirm shared change"));
        }
        await operations.EditAsync(snapshot.Revision, change, c.User, [], c.RequestAborted);
        return Results.Redirect(BoardUrl(snapshot.Website, f["page"].ToString()));
    }
    private static string ChangeDescription(HttpContext c, CompositionWebsite site, CompositionEdit edit) => edit switch
    {
        CreateBlock e => "Add " + TypeLabel(c, e.TypeId).ToLowerInvariant() + " to shared content.",
        MovePlacement e => "Move “" + BlockLabel(c, Target(site, Locate(site, e.PlacementId).Placement)) + "” within shared content.",
        PromoteShared e => "Make “" + BlockLabel(c, Target(site, Locate(site, e.PlacementId).Placement)) + "” a reusable Shared Block.",
        DeletePlacement e => "Remove “" + BlockLabel(c, Target(site, Locate(site, e.PlacementId).Placement)) + "” from this area" + (Target(site, Locate(site, e.PlacementId).Placement).Children.IsEmpty ? "." : ", including its nested placements."),
        DeleteShared => "Delete the unused Shared Block and its owned content.",
        GroupPlacements => "Group the selected elements within shared content.", ReferenceShared => "Add a shared reference within shared content.", DetachPlacement => "Replace this shared reference with an independent copy.", _ => "Update this draft."
    };
    private static string GroupLabel(CompositionWebsite site, Block b) => "Group " + (site.Blocks.Where(x => x.TypeId == "group" && x.Owner == b.Owner).ToList().FindIndex(x => x.Id == b.Id) + 1);

    private static IResult Overview(HttpContext c, CompositionSnapshot snapshot, CompositionCapabilities caps, CompositionDesign design)
    {
        var site = snapshot.Website;
        var selectedPage = c.Request.Query["page"].ToString(); if (!site.Pages.Any(p => p.Id == selectedPage)) selectedPage = site.Pages[0].Id;
        var html = new StringBuilder("<nav class=\"actions\" aria-label=\"Workspace\">");
        if (Permissions.Has(c.User, "integrations:manage")) html.Append("<a href=\"/manage/integrations\">Connected apps</a>");
        if (Permissions.Has(c.User, "accounts:manage")) html.Append("<a href=\"/manage/users\">People and access</a>");
        html.Append("<a href=\"/manage/composition/export\">Download content</a><a href=\"/manage/account/password\">Your account</a></nav><div class=\"intro\"><p class=\"eyebrow\">Your website</p><h1>" + E(site.Title) + "</h1><p>Open a page to edit its content. Add and arrange elements directly in each area.</p></div>");
        if (Permissions.Has(c.User, "preview:build")) html.Append(Form(c, snapshot.Revision, "/manage/composition/preview", "<h2>Review your draft</h2><p>Build a preview of the saved pages. Later edits keep this preview unchanged.</p>", "Build preview"));
        var registry = SelectedDefinitions(c);
        string Quick(Placement p, string action, string label, string page, string extra = "", bool disabled = false) => $"<form method=\"post\" action=\"/manage/composition/placements/{E(p.Id)}/{E(action)}\">{ManagementUi.Token(c)}{Hidden("revision", snapshot.Revision)}{Hidden("page", page)}{extra}<button class=\"small-button\"{(disabled ? " disabled" : "")}>{E(label)}</button></form>";
        void List(CompositionLocation location, string page, string occurrence)
        {
            var d = Destination(c, site, design, location); var groupForm = "group-" + BuildPipeline.Hash(Encoding.UTF8.GetBytes(occurrence))[..16];
            var canEdit = Editable(c, d.Owner); var canGroup = canEdit && d.Types.Contains("group") && Supported(caps, CompositionOperation.Group, "group");
            html.Append("<ol class=\"structure-list\">");
            for (var i = 0; i < d.Items.Length; i++)
            {
                var p = d.Items[i]; var block = Target(site, p); var container = registry.Resolve(block.TypeId, block.TypeVersion).Descriptor.Container;
                var sharedReadOnly = !Editable(c, block.Owner) || !Supported(caps, CompositionOperation.Update, block.TypeId, block.TypeVersion) || !ContentEditorContract.Generic(registry.Resolve(block.TypeId, block.TypeVersion).Editor);
                var editUrl = block.TypeId == "page-title" ? "/manage/composition/page/" + page : "/manage/composition/blocks/" + block.Id + "?page=" + Q(page);
                html.Append("<li class=\"structure-node\"><div class=\"element-row\"><div class=\"element-name\">");
                if (canGroup) html.Append($"<label class=\"element-select\"><input type=\"checkbox\" form=\"{groupForm}\" name=\"selection\" value=\"{E(p.Id)}\"><span class=\"visually-hidden\">Select {E(BlockLabel(c, block))} for grouping</span></label>");
                html.Append("<div><strong>" + E(BlockLabel(c, block)) + "</strong><span class=\"element-kind\">" + E(TypeLabel(c, block.TypeId)) + (p.Kind == TargetKind.Shared ? " · Shared across pages" : "") + "</span></div></div><div class=\"element-actions\"><a href=\"" + E(editUrl) + "\">" + (sharedReadOnly || !Permissions.Has(c.User, "content:write") ? "View" : "Edit") + "</a>");
                if (canEdit && Supported(caps, CompositionOperation.Move))
                {
                    html.Append(Quick(p, "reorder", "Move up", page, Hidden("direction", "up"), i == 0));
                    html.Append(Quick(p, "reorder", "Move down", page, Hidden("direction", "down"), i == d.Items.Length - 1));
                }
                if (canEdit)
                {
                    html.Append("<details class=\"element-more\"><summary>More</summary><div class=\"element-menu\">");
                    if (d.Items.Length > d.Minimum && Supported(caps, CompositionOperation.Move)) html.Append("<a href=\"/manage/composition/placements/" + E(p.Id) + "/move?page=" + Q(page) + "\">Move to another area</a>");
                    if (d.Items.Length < d.Maximum && Supported(caps, CompositionOperation.Create)) html.Append("<a href=\"/manage/composition/add" + E(ContextQuery(page, location, p.Id)) + "\">Add after this element</a>");
                    if (p.Kind == TargetKind.Shared && Supported(caps, CompositionOperation.Detach)) html.Append(Quick(p, "detach", d.Owner.Kind == OwnerKind.Page ? "Detach for this page" : "Detach independent copy", page));
                    if (p.Kind == TargetKind.Block && d.Owner.Kind == OwnerKind.Page && Permissions.Has(c.User, "content:shared:write") && Supported(caps, CompositionOperation.Share)) html.Append(Quick(p, "promote", "Make shared", page));
                    if (d.Items.Length > d.Minimum && Supported(caps, CompositionOperation.Delete)) html.Append(Quick(p, "remove", "Remove element", page));
                    html.Append("</div></details>");
                }
                html.Append("</div></div>");
                if (container) List(new(null, null, block.Id), page, occurrence + "/" + p.Id);
                html.Append("</li>");
            }
            html.Append("</ol>");
            if (canEdit)
            {
                html.Append("<div class=\"area-actions\">");
                if (d.Items.Length < d.Maximum && Supported(caps, CompositionOperation.Create)) html.Append("<a class=\"add-element\" href=\"/manage/composition/add" + E(ContextQuery(page, location)) + "\">+ Add element</a>");
                if (d.Items.Length < d.Maximum && Supported(caps, CompositionOperation.Share) && site.SharedBlocks.Any(s => d.Types.Contains(site.Blocks.Single(b => b.Id == s.RootBlockId).TypeId))) html.Append("<a href=\"/manage/composition/reference" + E(ContextQuery(page, location)) + "\">Reuse shared content</a>");
                if (canGroup && !d.Items.IsEmpty) html.Append($"<form id=\"{groupForm}\" method=\"post\" action=\"/manage/composition/group\">{ManagementUi.Token(c)}{Hidden("revision", snapshot.Revision)}{ContextFields(page, location)}<button class=\"small-button\">Group selected</button></form>");
                html.Append("</div>");
            }
        }
        foreach (var page in site.Pages)
        {
            html.Append("<details class=\"panel composition-page\"" + (page.Id == selectedPage ? " open" : "") + "><summary>" + E(page.Title) + " <small>" + E(page.Path) + "</small></summary><div class=\"page-structure\">");
            if (Permissions.Has(c.User, "content:write")) html.Append("<a class=\"page-details\" href=\"/manage/composition/page/" + E(page.Id) + "\">Edit page details</a>");
            foreach (var region in page.Regions)
            {
                html.Append("<section class=\"structure-area\"><h3>" + E(TypeLabel(c, region.Id)) + "</h3>"); List(new(page.Id, region.Id, null), page.Id, page.Id + "/" + region.Id); html.Append("</section>");
            }
            html.Append("</div></details>");
        }
        html.Append("<details class=\"panel composition-page\"><summary>Shared content library</summary><p>Edit shared content once for all its pages. Detach a reference to keep changes on one page.</p><ul class=\"pages\">");
        foreach (var shared in site.SharedBlocks)
        {
            var root = site.Blocks.Single(b => b.Id == shared.RootBlockId); var used = site.Pages.SelectMany(p => p.Regions).SelectMany(r => r.Placements).Concat(site.Blocks.SelectMany(b => b.Children)).Any(p => p.Kind == TargetKind.Shared && p.TargetId == shared.Id);
            html.Append("<li><div><strong>" + E(BlockLabel(c, root)) + "</strong><span>" + (used ? "Referenced shared content" : "Not currently referenced") + "</span></div><a href=\"/manage/composition/blocks/" + E(root.Id) + "?page=" + Q(selectedPage) + "\">" + (Editable(c, root.Owner) ? "Edit" : "View") + "</a>");
            if (!used && Editable(c, root.Owner) && Supported(caps, CompositionOperation.Delete)) html.Append(Form(c, snapshot.Revision, "/manage/composition/shared/" + shared.Id + "/remove", Hidden("page", selectedPage), "Remove unused Shared Block"));
            if (registry.Resolve(root.TypeId, root.TypeVersion).Descriptor.Container)
            {
                html.Append("<div class=\"shared-structure\">"); List(new(null, null, root.Id), selectedPage, "library/" + shared.Id); html.Append("</div>");
            }
            html.Append("</li>");
        }
        html.Append("</ul></details><details class=\"panel composition-page\"><summary>Images</summary><div class=\"media-library\">" + string.Join("", site.Assets.Select((a, i) => "<figure><img src=\"/manage/composition/media/" + E(a.Id) + "\" alt=\"Image " + (i + 1) + "\" loading=\"lazy\"><figcaption>Image " + (i + 1) + "</figcaption></figure>")) + "</div>");
        if (Permissions.Has(c.User, "content:write") && Supported(caps, CompositionOperation.Media)) html.Append($"<form class=\"editor\" enctype=\"multipart/form-data\" method=\"post\" action=\"/manage/composition/media\">{ManagementUi.Token(c)}{Hidden("revision", snapshot.Revision)}<p>PNG images up to 2 MiB and 4 million pixels.</p><label>Choose image<input name=\"image\" type=\"file\" accept=\"image/png\" required></label><button>Upload image</button></form>");
        html.Append("</details>");
        if (Permissions.Has(c.User, "settings:write")) html.Append("<details class=\"panel composition-page\"><summary>Website settings</summary>" + Form(c, snapshot.Revision, "/manage/composition/settings", Input("title", "Website name / title", site.Title) + Input("language", "Language", site.Language), "Save website settings") + "</details>");
        return ManagementUi.Html(site.Title, html.ToString());
    }
}
