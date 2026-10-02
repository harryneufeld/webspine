using System.Collections.Immutable;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

internal static partial class CompositionBoard
{
    private static string RecordLabel(ContentRecord record, IRecordDefinition definition) => definition.Editor.SummaryField is { } field &&
        record.Fields.TryGetProperty(field, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString()! : record.Id;
    private static string RecordHeader(string title, string description) => "<a href=\"/manage/composition\">← Website pages</a><div class=\"intro\"><h1>" + E(title) + "</h1><p>" + E(description) + "</p></div>";
    private static ImmutableArray<string> RecordPages(CompositionSnapshot snapshot, HttpContext c, string id) =>
        PatternContract.AffectedPages(snapshot.Website, SelectedDefinitions(c), id);
    private static string RecordImpact(CompositionSnapshot snapshot, ImmutableArray<string> pages, bool acknowledge)
    {
        var body = "<fieldset><legend>Where this content appears</legend><p>" + E(pages.IsEmpty ? "No pages use this content yet." :
            string.Join(", ", pages.Select(id => snapshot.Website.Pages.Single(p => p.Id == id).Title))) + "</p><p>Saved changes appear in new previews. Existing previews keep their content.</p>";
        if (acknowledge && !pages.IsEmpty) body += string.Join("", pages.Select(id => Hidden("affected", id))) +
            "<label class=\"scope\"><input type=\"checkbox\" name=\"acknowledge\" value=\"true\" required>I reviewed the affected pages.</label>";
        return body + "</fieldset>";
    }
    private static bool RecordSupported(CompositionCapabilities caps, CompositionOperation operation, RecordSchemaDescriptor schema)
    {
        try { caps.RequireRecord(operation, schema.Id, schema.Version); return true; }
        catch (SourceOperationNotSupportedException) { return false; }
    }
    private static void MapRecordRoutes(RouteGroupBuilder board)
    {
        board.MapGet("/records", async (CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted); var registry = operations.Registry.Records;
            var body = RecordHeader("Reusable content", "Keep information in one place and reuse it across your website.");
            body += "<section class=\"panel\"><h2>Your content</h2><ul class=\"pages\">";
            foreach (var record in snapshot.Website.Records)
            {
                var definition = registry.Resolve(record.SchemaId, record.SchemaVersion); var pages = RecordPages(snapshot, c, record.Id);
                body += "<li><div><strong>" + E(RecordLabel(record, definition)) + "</strong><span class=\"element-kind\">" + E(definition.Editor.Label) + " · " +
                    E(pages.IsEmpty ? (PatternContract.IsReferenced(snapshot.Website, operations.Registry, record.Id) ? "Used in the shared library" : "Not used yet") : string.Join(", ", pages.Select(id => snapshot.Website.Pages.Single(p => p.Id == id).Title))) +
                    "</span></div><a aria-label=\"Open " + E(RecordLabel(record, definition)) + "\" href=\"/manage/composition/records/" + E(record.Id) + "\">Open</a></li>";
            }
            if (snapshot.Website.Records.IsEmpty) body += "<li>No reusable content yet. Add content below, then select it in a compatible page element.</li>";
            body += "</ul></section>";
            if (Permissions.Has(c.User, "content:write"))
                foreach (var schema in registry.Descriptors.Where(d => RecordSupported(operations.Source.CompositionCapabilities, CompositionOperation.RecordCreate, d)))
                    body += "<p><a class=\"button\" href=\"/manage/composition/records/new/" + E(schema.Id) + "\">Add " + E(registry.Resolve(schema.Id, schema.Version).Editor.Label.ToLowerInvariant()) + "</a></p>";
            return ManagementUi.Html("Reusable content", body);
        });
        board.MapGet("/records/new/{schema}", async (string schema, CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted);
            var descriptor = operations.Registry.Records.Descriptors.FirstOrDefault(d => d.Id == schema) ?? throw new ContentValidationException("Record schema is not installed.");
            operations.Source.CompositionCapabilities.RequireRecord(CompositionOperation.RecordCreate, schema, descriptor.Version);
            if (!Permissions.Has(c.User, "content:write")) throw new CompositionPermissionException("Content editing permission is required.");
            var editor = operations.Registry.Records.Resolve(schema, descriptor.Version).Editor;
            var fields = ContentEditorContract.Defaults(editor, snapshot.Website, operations.Design);
            return ManagementUi.Html("Add " + editor.Label.ToLowerInvariant(), RecordHeader("Add " + editor.Label.ToLowerInvariant(), editor.Description) +
                Form(c, snapshot.Revision, "/manage/composition/records/new/" + schema, ContentFieldForms.Render(editor, fields, snapshot.Website, operations.Design), "Add " + editor.Label.ToLowerInvariant()));
        });
        board.MapPost("/records/new/{schema}", async (string schema, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
            var descriptor = operations.Registry.Records.Descriptors.FirstOrDefault(d => d.Id == schema) ?? throw new ContentValidationException("Record schema is not installed.");
            var editor = operations.Registry.Records.Resolve(schema, descriptor.Version).Editor;
            var fields = ContentFieldForms.Apply(editor, ContentEditorContract.Defaults(editor, snapshot.Website, operations.Design), f, snapshot.Website, operations.Design);
            var saved = await operations.EditAsync(snapshot.Revision, new CreateRecord(schema, descriptor.Version, fields), c.User, [], c.RequestAborted);
            return Results.Redirect("/manage/composition/records/" + saved.Website.Records[^1].Id);
        });
        board.MapGet("/records/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var snapshot = await operations.ReadAsync(c.RequestAborted); var record = snapshot.Website.Records.FirstOrDefault(r => r.Id == id);
            if (record is null) return Results.NotFound();
            var definition = operations.Registry.Records.Resolve(record.SchemaId, record.SchemaVersion); var pages = RecordPages(snapshot, c, id);
            var referenced = PatternContract.IsReferenced(snapshot.Website, operations.Registry, id);
            var canEdit = Permissions.Has(c.User, "content:write") && (!referenced || Permissions.Has(c.User, "content:shared:write")) &&
                RecordSupported(operations.Source.CompositionCapabilities, CompositionOperation.RecordUpdate, definition.Descriptor);
            var fields = ContentFieldForms.Render(definition.Editor, definition.Values(record, snapshot.Website, operations.Design), snapshot.Website, operations.Design);
            var body = RecordHeader(RecordLabel(record, definition), "Edit this information once for every place that uses it.");
            body += "<p><a href=\"/manage/composition/records\">All reusable content</a></p>";
            body += canEdit ? Form(c, snapshot.Revision, "/manage/composition/records/" + id, Hidden("recordRevision", record.Revision) + fields + RecordImpact(snapshot, pages, true), "Save changes") :
                "<p class=\"notice\">This content is read-only for your account or source. Used content requires shared-content editing permission.</p><fieldset disabled>" + fields + "</fieldset>" + RecordImpact(snapshot, pages, false);
            if (!referenced && Permissions.Has(c.User, "content:write") && RecordSupported(operations.Source.CompositionCapabilities, CompositionOperation.RecordDelete, definition.Descriptor))
                body += Form(c, snapshot.Revision, "/manage/composition/records/" + id + "/remove", Hidden("recordRevision", record.Revision) +
                    "<p>This content is unused. Removing it changes only the saved draft.</p><label class=\"scope\"><input type=\"checkbox\" name=\"confirm\" value=\"true\" required>Remove this unused content</label>", "Remove unused content");
            return ManagementUi.Html(RecordLabel(record, definition), body);
        });
        board.MapPost("/records/{id}", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted); var snapshot = await operations.ReadAsync(c.RequestAborted); Current(snapshot, f);
            var record = snapshot.Website.Records.FirstOrDefault(r => r.Id == id) ?? throw new ContentValidationException("Record does not exist.");
            var definition = operations.Registry.Records.Resolve(record.SchemaId, record.SchemaVersion);
            var fields = ContentFieldForms.Apply(definition.Editor, definition.Values(record, snapshot.Website, operations.Design), f, snapshot.Website, operations.Design);
            await operations.EditAsync(snapshot.Revision, new UpdateRecord(id, f["recordRevision"].ToString(), fields), c.User, Acknowledged(f), c.RequestAborted);
            return Results.Redirect("/manage/composition/records/" + id);
        });
        board.MapPost("/records/{id}/remove", async (string id, CompositionOperations operations, HttpContext c) =>
        {
            var f = await c.Request.ReadFormAsync(c.RequestAborted);
            if (f["confirm"] != "true") throw new ContentValidationException("Confirm removal of unused content.");
            await operations.EditAsync(f["revision"].ToString(), new DeleteRecord(id, f["recordRevision"].ToString()), c.User, [], c.RequestAborted);
            return Results.Redirect("/manage/composition/records");
        });
    }
}
