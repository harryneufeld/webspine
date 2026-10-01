using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

// The board consumes data-only metadata. It never infers a field's meaning from a type/property name.
internal static class ContentFieldForms
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    public static ContentEditorMetadata Require(IBlockDefinition definition) => ContentEditorContract.Generic(definition.Editor)
        ? definition.Editor! : throw new SourceOperationNotSupportedException("This type needs a specialized editor that is not installed in the basic board. Use a client with its typed schema; existing values are preserved.");
    public static string Render(ContentEditorMetadata editor, JsonElement values, CompositionWebsite site, CompositionDesign design)
    {
        ContentEditorContract.ValidateValues(editor, values, site, design);
        var output = new StringBuilder();
        void Fields(ImmutableArray<EditorField> fields, JsonObject obj, string path)
        { foreach (var field in fields) Field(field, obj[field.Name], path + "." + field.Name); }
        void Field(EditorField field, JsonNode? value, string path)
        {
            if (field.Kind == EditorFieldKind.Repeat)
            {
                var array = (JsonArray)value!;
                output.Append("<fieldset class=\"repeated-fields\"><legend>" + E(field.Label) + "</legend><p class=\"hint\">" + field.Minimum + "–" + field.Maximum + " items. Changes are saved with this element.</p>");
                for (var i = 0; i < array.Count; i++)
                {
                    output.Append("<fieldset class=\"repeated-item\"><legend>" + E(field.ItemLabel) + " " + (i + 1) + "</legend>");
                    if (field.Item is null) Fields(field.ItemFields, (JsonObject)array[i]!, path + "." + i);
                    else Field(field.Item, array[i], path + "." + i);
                    if (array.Count > field.Minimum) output.Append($"<label class=\"scope\"><input type=\"checkbox\" name=\"remove.{E(path)}\" value=\"{i}\">Remove {E(field.ItemLabel!.ToLowerInvariant())} {i + 1}</label>");
                    output.Append("</fieldset>");
                }
                if (array.Count < field.Maximum)
                {
                    if (field.Item is not null && !ContentEditorContract.Choices(field.Item, site, design).IsEmpty)
                        Select("append." + path, "Add " + field.ItemLabel!.ToLowerInvariant(), "", ContentEditorContract.Choices(field.Item, site, design), true, "Keep current list");
                    else output.Append($"<label class=\"scope\"><input type=\"checkbox\" name=\"append.{E(path)}\" value=\"true\">Add another {E(field.ItemLabel!.ToLowerInvariant())}</label><p class=\"hint\">Save to add it, then fill in its fields.</p>");
                }
                output.Append("</fieldset>"); return;
            }
            var choices = ContentEditorContract.Choices(field, site, design); var current = value?.ToString() ?? "";
            if (field.ChoiceSource != EditorChoiceSource.None || !choices.IsEmpty)
            { Select(path, field.Label, current, choices, !field.Required, "No selection"); return; }
            var required = field.Required ? " required" : ""; var id = "editor-" + path;
            output.Append($"<label for=\"{E(id)}\">{E(field.Label)}</label>");
            if (field.Multiline) output.Append($"<textarea id=\"{E(id)}\" name=\"{E(path)}\" rows=\"4\" maxlength=\"{field.MaximumLength}\"{required}>{E(current)}</textarea>");
            else if (field.Kind == EditorFieldKind.Integer) output.Append($"<input id=\"{E(id)}\" name=\"{E(path)}\" type=\"number\" min=\"{field.Minimum}\" max=\"{field.Maximum}\" value=\"{E(current)}\"{required}>");
            else output.Append($"<input id=\"{E(id)}\" name=\"{E(path)}\" maxlength=\"{field.MaximumLength}\" value=\"{E(current)}\"{required}>");
            if (field.Kind == EditorFieldKind.Destination) output.Append("<p class=\"hint\">Use a page path such as /, or a complete HTTPS address.</p>");
        }
        void Select(string path, string label, string current, ImmutableArray<EditorChoice> choices, bool optional, string emptyLabel)
        {
            var id = "editor-" + path;
            output.Append($"<label for=\"{E(id)}\">{E(label)}</label><select name=\"{E(path)}\" id=\"{E(id)}\"{(optional ? "" : " required")}>");
            if (optional) output.Append("<option value=\"\">" + E(emptyLabel) + "</option>");
            foreach (var choice in choices) output.Append($"<option value=\"{E(choice.Value)}\"{(choice.Value == current ? " selected" : "")}>{E(choice.Label)}</option>");
            output.Append("</select>");
        }
        Fields(editor.Fields, (JsonObject)JsonNode.Parse(values.GetRawText())!, "field");
        return output.ToString();
    }

    public static JsonElement Apply(ContentEditorMetadata editor, JsonElement previous, IFormCollection form,
        CompositionWebsite site, CompositionDesign design)
    {
        ContentEditorContract.ValidateValues(editor, previous, site, design);
        var known = new HashSet<string>(StringComparer.Ordinal);
        string? Scalar(string path)
        {
            known.Add(path); if (!form.TryGetValue(path, out var values)) return null;
            if (values.Count != 1) throw new ContentValidationException("A field must have one value."); return values[0] ?? "";
        }
        JsonNode? Field(EditorField field, JsonNode? node, string path)
        {
            if (field.Kind == EditorFieldKind.Repeat)
            {
                var array = (JsonArray)node!;
                for (var i = 0; i < array.Count; i++)
                    if (field.Item is null) Fields(field.ItemFields, (JsonObject)array[i]!, path + "." + i);
                    else { var updated = Field(field.Item, array[i], path + "." + i); if (!ReferenceEquals(updated, array[i])) array[i] = updated; }
                known.Add("remove." + path);
                var removals = form["remove." + path].Select(v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 && n < array.Count
                    ? n : throw new ContentValidationException("Unknown list item.")).Distinct().OrderDescending().ToArray();
                foreach (var index in removals) array.RemoveAt(index);
                var append = Scalar("append." + path);
                if (!string.IsNullOrEmpty(append))
                {
                    if (array.Count >= field.Maximum) throw new ContentValidationException(field.Label + " has reached its item limit.");
                    if (field.Item is not null && !ContentEditorContract.Choices(field.Item, site, design).IsEmpty)
                    {
                        if (!ContentEditorContract.Choices(field.Item, site, design).Any(c => c.Value == append)) throw new ContentValidationException("Choose an available list item.");
                        array.Add(field.Item.Kind == EditorFieldKind.Integer
                            ? JsonValue.Create(int.Parse(append, CultureInfo.InvariantCulture)) : JsonValue.Create(append));
                    }
                    else if (append == "true") array.Add(ContentEditorContract.ItemDefault(field, site, design));
                    else throw new ContentValidationException("Unknown list action.");
                }
                return array;
            }
            var value = Scalar(path); if (value is null) return node;
            if (!field.Required && value.Length == 0) return null;
            if (field.Kind == EditorFieldKind.Integer) return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? JsonValue.Create(n) : throw new ContentValidationException(field.Label + " requires a whole number.");
            return JsonValue.Create(value);
        }
        void Fields(ImmutableArray<EditorField> fields, JsonObject obj, string path)
        {
            foreach (var field in fields)
            {
                var key = path + "." + field.Name; var updated = Field(field, obj[field.Name], key);
                if (!ReferenceEquals(obj[field.Name], updated)) obj[field.Name] = updated;
            }
        }
        var result = (JsonObject)JsonNode.Parse(previous.GetRawText())!; Fields(editor.Fields, result, "field");
        if (form.Keys.Any(k => (k.StartsWith("field.", StringComparison.Ordinal) || k.StartsWith("append.", StringComparison.Ordinal) || k.StartsWith("remove.", StringComparison.Ordinal)) && !known.Contains(k)))
            throw new ContentValidationException("The form contains an unknown registered field or list action.");
        var values = JsonSerializer.SerializeToElement(result, CompositionJson.Options);
        ContentEditorContract.ValidateValues(editor, values, site, design); return values;
    }
}
