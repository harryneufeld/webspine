using System.Text.Json;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

internal static partial class CompositionBoard
{
    private static ContentEditorMetadata SelectedEditor(HttpContext c, string type, int? version = null)
    {
        var registry = SelectedDefinitions(c);
        var actual = version ?? registry.Descriptors.Single(d => d.Id == type).Version;
        return ContentFieldForms.Require(registry.Resolve(type, actual));
    }
    private static JsonElement SeedFields(HttpContext c, string type, CompositionWebsite site) =>
        ContentEditorContract.Defaults(SelectedEditor(c, type), site, SelectedDesign(c));
    private static bool CanCreate(HttpContext c, string type, int version, CompositionWebsite site)
    {
        try { ContentEditorContract.Defaults(SelectedDefinitions(c).Resolve(type, version).Editor, site, SelectedDesign(c)); return true; }
        catch (Exception e) when (e is ContentValidationException or SourceOperationNotSupportedException) { return false; }
    }
    private static string TypeLabel(HttpContext c, string type) => SelectedDefinitions(c).Descriptors.FirstOrDefault(d => d.Id == type) is { } definition
        ? SelectedDefinitions(c).Resolve(type, definition.Version).Editor?.Label ?? HumanLabel(type) : HumanLabel(type);
    private static string HumanLabel(string text) => text.Length == 0 ? "Content" : char.ToUpperInvariant(text[0]) + text[1..].Replace('-', ' ');
    private static string BlockLabel(HttpContext c, Block block, CompositionWebsite site)
    {
        var editor = SelectedDefinitions(c).Resolve(block.TypeId, block.TypeVersion).Editor;
        var label = TypeLabel(c, block.TypeId);
        if (editor?.SummaryField is { } name && block.Fields.TryGetProperty(name, out var summary) && summary.ValueKind == JsonValueKind.String)
            label += " · " + summary.GetString();
        else if (editor is not null)
            foreach (var field in editor.Fields.Where(f => f.ChoiceSource == EditorChoiceSource.Records))
                if (block.Fields.TryGetProperty(field.Name, out var reference) && reference.ValueKind == JsonValueKind.String &&
                    site.Records.FirstOrDefault(r => r.Id == reference.GetString()) is { } record)
                    label += " · " + RecordLabel(record, SelectedDefinitions(c).Records.Resolve(record.SchemaId, record.SchemaVersion));
        return label.Length <= 100 ? label : label[..97] + "…";
    }
    private static IResult Edit(HttpContext c, CompositionSnapshot snapshot, Block block, CompositionDesign design, CompositionCapabilities caps)
    {
        var page = c.Request.Query["page"].ToString(); var label = TypeLabel(c, block.TypeId);
        var body = ScreenHeader(snapshot.Website, page, "Edit " + label.ToLowerInvariant(), "Change this element's content. Your saved draft changes only when you save.");
        var definition = SelectedDefinitions(c).Resolve(block.TypeId, block.TypeVersion);
        if (!ContentEditorContract.Generic(definition.Editor))
            return ManagementUi.Html("Specialized content", body + "<p class=\"notice\">This type requires a specialized editor that is not installed in this board. Use a client with its typed schema. Existing values are preserved.</p>");
        var fields = ContentFieldForms.Render(definition.Editor!, block.Fields, snapshot.Website, design);
        if (!Editable(c, block.Owner) || !Supported(caps, CompositionOperation.Update, block.TypeId, block.TypeVersion))
            return ManagementUi.Html("View content", body + "<p>This content is read-only for your account or content source. Shared content needs shared-content permission; detach a page reference to edit an independent copy.</p><fieldset disabled>" + fields + "</fieldset>");
        var impact = block.Owner.Kind == OwnerKind.Shared ? Acknowledgement(snapshot, [block.Owner.Id]) : "";
        return ManagementUi.Html("Edit " + label.ToLowerInvariant(), body + Form(c, snapshot.Revision, "/manage/composition/blocks/" + block.Id,
            Hidden("page", page) + fields + impact, "Save changes"));
    }
}
